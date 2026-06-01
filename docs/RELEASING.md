# Releasing StemForge

Maintainer runbook for cutting a tagged release. Contributors who only want to build a local self-contained artifact need just the "Publish + package a Windows release" section of the [README](../README.md); the steps below are for publishing an official versioned release.

## Steps

1. Bump `<Version>` in `src/StemForge/StemForge.csproj`. This is the single source of truth; the package task names the zip from it.
2. Run `dotnet csharpier format .` and `dotnet test`, then commit the version bump and any release-prep changes.
3. Merge the release branch into `main`: `git checkout main && git merge release-prep-vX.Y.Z --no-ff`.
4. Tag and push: `git tag vX.Y.Z && git push origin main vX.Y.Z`.
5. Build the artifact: run the **"package: win-x64"** VS Code task. It reads `<Version>` from the csproj and produces `publish/StemForge-vX.Y.Z-win-x64.zip`.
6. Create a GitHub Release from the `vX.Y.Z` tag, attach the zip, and write release notes.
