# Releasing StemForge

Maintainer runbook for cutting a tagged release. Contributors who only want to build a local self-contained artifact need just the "Publish + package a Windows release" section of the [README](../README.md); the steps below are for publishing an official versioned release.

## Toolchain

The projects target `net11.0`, a preview framework. `global.json` pins the exact **SDK** that builds them, with `rollForward: disable`, so `dotnet` fails loudly rather than silently compiling with a different one.

That pin is not cosmetic. `<TargetFramework>` selects the runtime the code targets; the SDK selects the compiler, analyzers, and implicit-using set that build it. Two SDKs can both produce `net11.0` output and still disagree about whether a given `using` is required. v0.3.0 shipped source that did not compile on the CI runner for exactly this reason. CI reads its SDK version from `global.json` via `global-json-file`, so the two cannot drift apart again.

When the pinned preview needs to move, change `global.json` only. Nothing else records an SDK version.

## Version

`Directory.Build.props` holds `<Version>` and is the single source of truth. It feeds:

- `scripts/package-win-x64.ps1`, which names the release zip from it
- `AppInfo`, which surfaces it in the Settings footer **and writes it into the provenance tag of every output file**

Because of that second one, the bump must happen **before** publishing. Publish first and the shipped binaries stamp every file a user produces with the previous version.

## Branching

Work lands on a per-release `integration/vX.Y.Z` branch. Feature branches merge into it directly. The only pull request is `integration/vX.Y.Z` into `main`, at release time, and that PR is what runs CI.

Issues are closed at the integration-to-main merge as one event, not when their code lands, so an issue staying open while its code is on the integration branch is correct.

## Steps

1. **Bump `<Version>`** in `Directory.Build.props`.
2. **Write the CHANGELOG entry.** `CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/): an `## [X.Y.Z] - YYYY-MM-DD` heading, `### Added` / `### Changed` / `### Fixed` sections, and a compare link at the bottom of the file. Describe what a user can see or do differently; leave out internal reorganisation and groundwork that no UI or flag reaches yet. This step is easy to skip and awkward to backfill: v0.2.1 and v0.2.1.1 were both tagged and released with no entry, and were reconstructed from their release notes months later.
3. **Format and test.** `dotnet csharpier format .` then `dotnet test StemForge.slnx`. The formatter is enforced; run it before committing.
4. **Commit and push** the branch.
5. **Open the PR** from `integration/vX.Y.Z` into `main`.
6. **Wait for CI to pass on that PR.** See below. Do not merge on a red or pending check.
7. **Merge the PR.**
8. **Publish and package.** Run the VS Code task **`package: win-x64`**. It chains `publish: win-x64 CLI`, which in turn chains `publish: win-x64 GUI`, so one task rebuilds both executables and then stages `publish/win-x64/` under `StemForge/` and writes `publish/StemForge-vX.Y.Z-win-x64.zip`.

   If you run the steps by hand instead, **publish both executables before packaging.** They write into the same `publish/win-x64/` folder and the script zips whatever is there, so publishing one and not the other silently ships a stale copy of the other. That folder also persists between releases, so a skipped publish leaves a genuinely old binary in place rather than an obviously missing one.
9. **Verify the artifact** before tagging: both `publish/win-x64/StemForge.exe` and `publish/win-x64/stemforge-cli.exe` should report the new version, and the zip name should match.
10. **Tag and push:** `git tag -a vX.Y.Z -m "StemForge vX.Y.Z" && git push origin vX.Y.Z`. Tags live on `main`, on the merge commit.
11. **Create the GitHub Release** from the tag with `gh release create`, attach the zip, and write the notes.
12. **Close the release's issues.** Ones referenced by a `Closes:` line in the PR body close themselves on merge; close the rest by hand.

There is no release automation. `.github/workflows/ci.yml` is CI only, and every step above is manual.

## Continuous integration

One workflow, `.github/workflows/ci.yml`, with a single job: **Linux smoke test (linux-x64)** on `ubuntu-latest`.

### When it runs

On pushes to `main`, and on pull requests targeting `main`.

**It does not run on `integration/*` branches.** Pushing an integration branch produces no CI signal at all; the first and only signal arrives when the release PR is opened. That is the check to wait for at step 6, and it is the one that was ignored when v0.3.0 was merged and tagged with a failing build.

### What it covers

| Step | What it proves | Network |
| --- | --- | --- |
| Restore, Build (Release) | The source compiles on Linux with the pinned SDK | no |
| Test (offline suite) | The full test suite, minus the two env-gated classes below | no |
| Verify bundled assets | Downloads the linux-x64 yt-dlp, ffmpeg, and deno binaries and checks their pinned SHA-256, exercising the ffmpeg `tar.xz` extraction path. Gated by `STEMFORGE_LIVE_ASSETS=1` | yes |
| Download integration test | Fetches a small public-domain clip and verifies the file and its metadata. Gated by `STEMFORGE_INTEGRATION=1`, and `continue-on-error` so a flaky remote host cannot block a release | yes |

The bundled-asset step is the one that earns its keep: it catches a pinned download URL going dead upstream, which is the failure that forced the v0.2.1.1 hotfix. It is worth understanding that it can pass vacuously if its `--filter-class` argument stops matching. It did exactly that after the test project was reorganised by domain, and went unnoticed because a build failure was ending the job first. A run that matches no tests exits 8, so the step fails rather than reporting success, but the class name in `ci.yml` still has to be kept in step with the code.

### What it does not cover

- **Windows and macOS are never built or tested.** The published artifact is Windows, and it is built and verified locally. A green CI run says nothing about it.
- **The GUI is not exercised.** Headless view-model tests run; nothing renders.
- **Separation itself never runs.** No `uv`, no `audio-separator`, no models, no GPU.

So CI is a compile-and-unit gate plus a check that the pinned external downloads are still good. Anything about how the app actually behaves is verified by running it.

### Local versus CI

`dotnet test StemForge.slnx` locally runs the same offline suite CI does, and skips the same two env-gated classes. Set `STEMFORGE_LIVE_ASSETS=1` or `STEMFORGE_INTEGRATION=1` to run those.

A local run is Windows and CI is Linux, so the two can still disagree on platform-conditional code. `ProcessRunner`'s `KillOnParentExit` guard is the standing example: it is correct on both, and it produces a `CA1416` warning on an SDK where the API is attributed Windows-only.
