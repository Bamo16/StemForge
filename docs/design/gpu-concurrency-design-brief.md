# Design brief: cross-process GPU contention

**Status:** research only, nothing implemented, no decision taken.
**Prepared for:** a `grill-with-docs` pass, then `to-issues`.

The owner's stated lean, captured earlier: a GUI-configurable max-concurrent-separations setting
defaulting to 1, rather than a hard mutex, so the common case is protected without blocking a user
whose GPU can handle more. This brief tests that lean against the code and the platform, and
surfaces the questions worth settling before anything is built. Two findings below complicate it.

## The problem

A separation loads model weights onto the GPU. Two separations running at once on one GPU contend
for VRAM, and the failure is an out-of-memory abort partway through a run rather than a queue.

StemForge serialises separations **within a process** and not at all **between processes**:

| scope | mechanism | covers |
| --- | --- | --- |
| GUI queue | `JobQueueService._gate`, a `SemaphoreSlim(1,1)` (`src/StemForge/Services/JobQueueService.cs:18`) | one user-submitted job at a time, FIFO |
| driver | `SeparatorDriverService._runLock`, a `SemaphoreSlim(1,1)` (`src/StemForge.Core/Separation/SeparatorDriverService.cs:37`) | one driver job at a time |

Both are in-process objects. Nothing coordinates a GUI instance against a CLI invocation, or two
CLI invocations against each other. Since the CLI ships as a separate binary the owner keeps on
PATH, running one while the GUI is working is an ordinary thing to do, not an edge case.

## Finding 1: the GPU is held for five minutes after a job ends

`SeparatorDriverService` spawns the driver lazily and tears it down after `IdleTimeout`, which
defaults to **5 minutes** (`SeparatorDriverService.cs:16`). Between jobs the driver process stays
alive, which is the point: it avoids paying model-load cost per job. But a live driver holds its
CUDA context and loaded weights, so **VRAM stays occupied for up to five minutes after the last
separation finishes**.

That makes the lock's scope a real design question rather than a detail, and neither obvious answer
is right:

- **Lock only the run.** Process B acquires the moment A's run ends, while A's driver still holds
  VRAM for another five minutes. The lock would not actually prevent the contention it exists for.
- **Lock the driver's whole lifetime.** Correct on VRAM, but process B waits five minutes after A's
  last job for a GPU that is sitting idle. Unusable.

Neither works unmodified, so this needs a decision. Candidates, in rough order of appeal:

1. **Hold the lock for the run, and tear the driver down on release when someone is waiting.**
   Keeps the warm-driver optimisation for the uncontended case (the overwhelmingly common one) and
   pays the model-load cost only when a second process actually wants the GPU. Requires the lock to
   expose "is anyone waiting", which a bare mutex does not.
2. **Shorten `IdleTimeout` when not holding the lock.** Cruder, no waiter signal needed, but it
   trades away warm-start for everyone to fix a case that is rare.
3. **Accept the overlap.** Lock the run only, and treat the residual five-minute window as
   tolerable. Cheapest, and arguably fine if the second process merely needs to not start
   *simultaneously* rather than never overlap.

## Finding 2: the configurable-max design has no cheap cross-platform primitive

Verified against current .NET API documentation, not assumed:

- **Named `Semaphore` is Windows-only.** *"On Unix-based operating systems … named semaphores are
  not supported."* So the natural implementation of "at most N concurrent, N configurable" does not
  exist on Linux or macOS, which StemForge supports.
- **Named `Mutex` is cross-platform.** Backed by the filesystem on Unix. Three caveats worth
  knowing:
  - The default namespace is `Local\`, which on Unix means **per shell session**. Cross-process
    coordination needs `Global\`.
  - An abandoned mutex (process killed mid-run) throws `AbandonedMutexException` in the next
    acquirer, which *does* still receive ownership. So it self-heals, but the exception has to be
    caught or a crashed job deadlocks every later one.
  - On Unix there is no way to restrict a named mutex to its creating user.

So N=1 is cheap and portable; N>1 is not. Anything above 1 means hand-rolling a counting lock (N
named mutexes tried in turn, or a slot-file directory), which is materially more code and more
failure modes than the feature seems to warrant.

**This suggests reframing the setting.** The owner's goal was to protect the common case without
blocking a user with a strong GPU. A boolean achieves that: *serialise separations across
StemForge processes* (on by default, a single named mutex) versus *do not* (no lock at all,
today's behaviour). Both endpoints are portable and near-free. Arbitrary N buys a middle ground
that is expensive to implement and that few users could calibrate anyway, since the right number
depends on VRAM and on which models are loaded.

## Questions for the owner

1. **Is the middle ground worth its cost?** A serialise on/off toggle covers both stated goals with
   a portable primitive. Arbitrary N needs a hand-rolled counting lock. Is N=3 a case worth paying
   for, or is it hypothetical?
2. **What is the lock's scope**, given the five-minute idle driver? Options above; option 1 is the
   most correct and needs a waiter signal, option 3 is nearly free and accepts some overlap.
3. **What should a blocked process do?** Wait indefinitely, wait with a timeout then fail, or
   refuse immediately with a clear message? The CLI and GUI may want different answers: a batch
   script probably wants to wait, an interactive user probably wants to be told.
4. **Should waiting be visible?** A CLI that appears to hang because another process holds the GPU
   is indistinguishable from a hung CLI. If it waits, it should say so.
5. **Does this cover CPU-variant users?** The lock is about VRAM. A user on the `Cpu` GPU variant
   has no such constraint, and serialising them is pure loss. The [[Detected variant]] is already
   known at runtime, so the lock could apply only to GPU variants.
6. **What about a non-StemForge GPU consumer?** Nothing here helps if the user is also running a
   game or another ML tool. Worth stating as explicitly out of scope rather than implied.

## Not in scope

Anything that changes how a single process queues work. `JobQueueService`'s FIFO and the driver's
`_runLock` are correct for what they cover; this is strictly about adding coordination *between*
processes.
