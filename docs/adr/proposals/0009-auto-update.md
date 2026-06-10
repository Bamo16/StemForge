# Full in-app auto-update: Velopack vs NetSparkle

**Status: Proposed (pending owner decision)**

This is a research spike, not an accepted ADR. Whether to adopt full in-app auto-update, and which framework to use, is the owner's call. This note compares the two realistic .NET desktop options (Velopack and NetSparkle) against StemForge's current release setup and records a recommendation.

## What "full auto-update" means here

StemForge already has a notify-only update check (issue #34): `UpdateCheckService` plus `GitHubReleaseFetcher` poll `https://api.github.com/repos/Bamo16/StemForge/releases/latest`, compare the running `Version` (0.2.0) against the latest tag, and surface "an update is available". The user then downloads the new zip manually. Full auto-update means the app downloads the newer release and installs it (replacing the running install), so the user does not re-download and re-extract a zip by hand. This spike builds directly on the #34 discovery mechanism.

## Current release setup (the baseline both options must change)

There is no release workflow in `.github/workflows/` (CI only builds and tests; it has no publish or release job), so release packaging today is the local VS Code tasks in `.vscode/tasks.json`:

- `publish: win-x64 GUI` and `publish: win-x64 CLI` each run `dotnet publish` for `win-x64` with `--self-contained true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `PublishReadyToRun=true`, `DebugType=embedded`, both into `publish/win-x64/`. ADR 0007 records the rationale: a clean folder containing only `StemForge.exe`, `StemForge.Cli.exe`, and the `tools/` scripts, with all managed and native dependencies embedded in each apphost.
- `package: win-x64` stages that folder under `StemForge/` and zips it to `StemForge-v{Version}-win-x64.zip`, reading `<Version>` from `src/StemForge/StemForge.csproj` (the single source of truth for the version).

So the shipped artifact is a hand-built, self-contained, single-file, ReadyToRun win-x64 zip with no installer and no update channel beyond "newer zip attached to a GitHub Release". The app is run by extracting the zip wherever the user chooses; there is no install location the updater can assume.

## Velopack

Velopack is the modern successor to Squirrel for .NET (the updater core is written in Rust). Its model is: take the `dotnet publish` output folder and run one `vpk pack` command, which produces a full release set: a `Setup.exe` installer, a self-updating portable zip, a NuGet-style full package, delta packages against the previous release, and a `RELEASES`/release-manifest feed. Those artifacts are uploaded to any static host; GitHub Releases is a first-class, documented target (`vpk upload github`, and a documented GitHub Actions path). In the app, an `UpdateManager` pointed at the GitHub Releases source checks the feed, downloads (delta when possible), applies, and relaunches in roughly ten lines of code. The same `UpdateCheckService` discovery role #34 already fills can be kept or replaced by `UpdateManager.CheckForUpdatesAsync`.

Packaging change versus the current zip: this is a real shift, not a wrapper. Velopack owns the install location and the update layout, so the deliverable becomes Velopack's `Setup.exe` (plus its portable variant) instead of the hand-zipped `StemForge/` folder. The current `dotnet publish` flags stay usable as Velopack's input, with one caveat: `PublishSingleFile=true` fights Velopack's delta and patch-in-place model (Velopack expects to see the individual files it manages, and self-extracting single-file works against per-file deltas). The likely change is to drop `PublishSingleFile`/`IncludeNativeLibrariesForSelfExtract` and let `vpk pack` produce the clean installed layout instead, which also makes the "~240 loose DLLs is an ugly artifact" objection from ADR 0007 moot, because the user never sees that folder; the installer puts it in a managed app directory. Two executables (GUI + CLI) in one release is fine, Velopack packs a directory. The `package: win-x64` zip task is replaced by a `vpk pack` + `vpk upload github` step, ideally promoted into a release workflow rather than left as a local task.

Signing: Velopack integrates code signing into the pack process and must do the signing itself, because its own `Update`/`Setup` binaries are generated mid-build and have to be signed at the right points. It supports `signtool.exe` (pass your normal sign parameters straight through) and Azure Trusted Signing, and arbitrary tools via `--signTemplate`. It signs both the app binaries and the installer. This lines up directly with sibling spike #35 (signing to clear SmartScreen): whatever certificate path #35 picks (SignPath Foundation certificate driven through signtool, or Azure Trusted Signing) feeds Velopack's signing arguments. Velopack does not remove the #35 dependency; it consumes its output. Unsigned, the `Setup.exe` hits the same SmartScreen warning the current zip does.

## NetSparkle

NetSparkle is a cross-platform .NET update framework (the maintained `NetSparkleUpdater` fork), .NET 6+, with a prebuilt Avalonia UI package (`NetSparkleUpdater.UI.Avalonia`, currently aligned to Avalonia 11). Its model is appcast-driven: you sign each release with an Ed25519 key, publish an `appcast.xml` plus the update files, and the app's `SparkleUpdater` reads the appcast, downloads the referenced installer, verifies the Ed25519 signature, and runs the installer. The `netsparkle-generate-appcast` CLI tool can build the appcast from GitHub releases.

Crucially, NetSparkle does not package your app. It downloads an installer or archive and executes it; it does not itself replace files or own an install location. "You are responsible for creating the installer" (InnoSetup, NSIS, MSI). So adopting NetSparkle still requires introducing an installer that StemForge does not have today, because the current artifact is a bare extract-anywhere zip with no install step for an updater to re-run. NetSparkle can drive a self-extracting zip/archive flow, but the clean in-place upgrade story still wants a real installer underneath.

Packaging change versus the current zip: NetSparkle adds two new obligations on top of the existing publish. First, build and host an `appcast.xml` per release (the GitHub Releases hosting works, the appcast and files are attached as release assets and referenced by URL). Second, an Ed25519 signing key for the appcast/downloads, managed via the `SPARKLE_PRIVATE_KEY`/`SPARKLE_PUBLIC_KEY` environment or key files, with the public key embedded in the app. The current single-file/self-contained publish can stay (NetSparkle is agnostic to how the payload is built), but the "extract-anywhere zip, no installer" model has to become "an installer or a self-extracting archive NetSparkle can run", which is the bulk of the work.

Signing: NetSparkle's Ed25519 signatures are update-integrity signatures (they prove the appcast and the downloaded file were not tampered with). They are not Authenticode and do nothing for SmartScreen. So NetSparkle carries two independent signing concerns: its own Ed25519 keys (which you must add and manage), plus, separately, the Authenticode code signing from #35 applied to whatever installer you hand it. Velopack folds update integrity into the signed installer it produces; NetSparkle keeps them separate and adds a key-management burden of its own.

## Comparison

| | Velopack | NetSparkle |
| --- | --- | --- |
| Packaging | One `vpk pack` produces installer + portable + deltas + feed from the publish folder | Bring-your-own installer (InnoSetup/NSIS/MSI); NetSparkle only downloads and runs it |
| Update hosting | GitHub Releases is a documented first-class target; `vpk upload github` | GitHub Releases works; you host `appcast.xml` + assets and reference by URL |
| In-app code | `UpdateManager`, ~10 lines, delta-aware, applies and relaunches | `SparkleUpdater` + prebuilt Avalonia UI (Avalonia 11) |
| Update integrity | Folded into the signed installer it builds | Separate Ed25519 key you generate, host, and embed |
| Authenticode / SmartScreen | Signs binaries and installer via signtool / Azure Trusted Signing (consumes #35) | Out of scope for NetSparkle; you sign your own installer separately (still needs #35) |
| Effect on ADR 0007 zip | Replaces the hand-zipped artifact with an installer; likely drops `PublishSingleFile` | Keeps the publish; adds an installer and an appcast alongside it |

## Code-signing dependency on #35

Both options need Authenticode signing to be useful: an unsigned `Setup.exe` (Velopack) or unsigned installer (NetSparkle) trips the same SmartScreen warning the current unsigned zip does, and auto-updating to an unsigned binary is exactly the trust problem signing exists to solve. This spike therefore depends on sibling spike #35 ("code signing to remove the SmartScreen warning", weighing SignPath Foundation versus Azure Trusted Signing) landing first. Velopack consumes #35's certificate directly through its signing arguments; NetSparkle needs #35 for the installer's Authenticode signature and additionally introduces its own Ed25519 update-integrity key. Auto-update should not ship before #35 is decided.

## Effort estimate

- **Velopack:** roughly 1 to 2 days. Add the `UpdateManager` call site and a small "update available / restart" UI hook (the #34 plumbing already covers discovery), replace the `package: win-x64` zip task with a `vpk pack` + `vpk upload github` step, fold in the #35 signing parameters, and verify a real install-then-upgrade cycle on Windows. The largest concrete change is moving off `PublishSingleFile` and accepting Velopack's installed layout, plus promoting release packaging from a local VS Code task into a repeatable workflow.
- **NetSparkle:** roughly 2 to 4 days. Same in-app integration cost, but additionally you must introduce and maintain an installer that does not exist today (InnoSetup/NSIS/MSI), stand up Ed25519 key generation and appcast publishing, and wire both into the release process, all before the first auto-update can ship. More moving parts, more bespoke glue.

Both estimates assume Windows-only first (matching the current win-x64-only release) and exclude the #35 signing work itself, which is a prerequisite for either.

## Recommendation

Adopt full in-app auto-update with **Velopack**, after #35 lands, not before.

Velopack is the better fit because it collapses packaging, hosting discovery, delta updates, install, relaunch, and update integrity into one tool that targets GitHub Releases natively and consumes the #35 signing decision directly. NetSparkle would leave StemForge owning an installer toolchain and a second signing-key system it does not have today, for no offsetting benefit on Windows. The one real cost of Velopack is giving up the ADR 0007 single-file artifact in favor of Velopack's installed layout; that trade is acceptable because the property ADR 0007 was protecting (a clean artifact the user sees) is replaced by an installer the user never has to unpack, and the loose-DLL objection disappears inside the managed install directory.

Suggested sequencing: (1) land #35 and pick the certificate path; (2) prototype `vpk pack` against the current `win-x64` publish on a throwaway branch to confirm the GUI + CLI two-exe layout and the drop of `PublishSingleFile`; (3) add the `UpdateManager` call site reusing the #34 discovery; (4) promote release packaging into a GitHub Actions workflow with signing wired in. If the owner prefers to defer the installer shift entirely and keep the bare zip, the fallback is to stay notify-only (#34 as-is) rather than take on NetSparkle, since NetSparkle does not avoid the installer problem.
