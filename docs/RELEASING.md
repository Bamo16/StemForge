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

Work lands on a per-release `integration/vX.Y.Z` branch. Feature branches merge into it directly. The only pull request is `integration/vX.Y.Z` into `main`, at release time. CI runs on every push to the integration branch and again on that PR.

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

One workflow, `.github/workflows/ci.yml`, with two jobs: **Linux smoke test (linux-x64)** on `ubuntu-latest` and **Windows tests and fresh install (win-x64)** on `windows-latest`.

### When it runs

On pushes and pull requests to `main` and `integration/**`, weekly (Monday 06:00 UTC), and on demand from the Actions tab. The weekly run is there for the YouTube step: YouTube changes without StemForge changing, and that run is how a break shows up before users hit it.

The check to wait for at step 6 is the run on the release PR. v0.3.0 was merged and tagged with a failing build because that check was ignored.

### What it covers

| Step | Job | What it proves | Network |
| --- | --- | --- | --- |
| Restore, Build (Release) | both | The source compiles with the pinned SDK | no |
| Test (offline suite) | both | The full test suite, minus the env-gated classes below | no |
| Verify bundled assets | Linux | Downloads the linux-x64 yt-dlp, ffmpeg and deno binaries and checks their pinned SHA-256, exercising the ffmpeg `tar.gz` extraction path. Gated by `STEMFORGE_LIVE_ASSETS=1` | yes |
| Fresh install | both | `FreshInstallTests`: installs ffmpeg, yt-dlp and deno as setup does, into an empty bin folder, and checks detection finds each one. Gated by `STEMFORGE_LIVE_ASSETS=1` | yes |
| YouTube download on a fresh install | Windows | Resolves and downloads a real video through `YouTubeAudioService` on that install, yt-dlp update included. Also needs `STEMFORGE_LIVE_YOUTUBE=1`, and is `continue-on-error`, since YouTube sometimes refuses CI machines; read a failure anyway | yes |
| Download integration test | Linux | Fetches a small public-domain clip and verifies the file and its metadata. Gated by `STEMFORGE_INTEGRATION=1`, and `continue-on-error` | yes |

The asset and fresh-install steps catch a pinned download going dead upstream, the failure behind the v0.2.1.1 hotfix. They can pass vacuously if their `--filter-class` stops matching, as happened once after the test project was reorganised by domain. A run that matches no tests exits 8, so the step fails rather than reporting success, but the class names in `ci.yml` still have to be kept in step with the code.

### What it does not cover

- **macOS is never built or tested.**
- **The GUI is not exercised.** Headless view-model tests run; nothing renders.
- **Separation itself never runs.** No `uv`, no `audio-separator`, no models, no GPU.

So CI is a compile-and-unit gate on Linux and Windows, plus a check that a fresh install's downloads work. How separation behaves is verified by running the app.

### Local versus CI

`dotnet test StemForge.slnx` locally runs the same offline suite CI does, and skips the same env-gated classes. Set `STEMFORGE_LIVE_ASSETS=1` (plus `STEMFORGE_LIVE_YOUTUBE=1` for the YouTube download) or `STEMFORGE_INTEGRATION=1` to run those.

CI builds on both Linux and Windows, so platform-conditional code that disagrees between them now shows up there rather than after release. `ProcessRunner`'s `KillOnParentExit` guard is the standing example. It is correct on both and warns on neither today, because the API is attributed for Windows and Linux (and Android) as of the pinned SDK. It did warn once: the property landed Windows-only in .NET 11 preview 4, so the Linux leg of the guard tripped `CA1416` on CI while a local build stayed clean. macOS is not supported and is not expected to be, since the platform has neither of the primitives this is built on.

The general shape is worth remembering when bumping the SDK pin: platform attributes move between previews, and a `CA1416` that appears on one OS only will not show up until the other one builds.
