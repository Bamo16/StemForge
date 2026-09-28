# Bundle nomercy-ffmpeg, pinned by release tag and SHA-256

StemForge bundled ffmpeg from two sources: BtbN's builds by way of `yt-dlp/FFmpeg-Builds` on Windows and Linux, and evermeet.cx on macOS. Neither carries the `beatdetect` and `keydetect` audio filters, which the tempo and key tooling around StemForge (keytag, crateside) depends on. Those filters ship in [nomercy-ffmpeg](https://github.com/NoMercy-Entertainment/nomercy-ffmpeg), the build NoMercy Media Server uses. We now bundle nomercy-ffmpeg on every platform, and the ffmpeg a user of those tools has on PATH can be the identical build, so a tempo reading means the same thing whichever tool took it.

**Pinned, not followed.** Each platform's asset is pinned by release tag (currently `v1.0.42`, ffmpeg 9.0) and by SHA-256. NoMercy Media Server itself follows `releases/latest` and checks the release's `manifest.json`; StemForge does not, because a new release can change a detector's output, and ADRs 0002 and 0005 hold that a user gets exactly what StemForge pinned. Moving the pin is a deliberate change, reviewed like any other. `CatalogWellFormednessTests` enforces that every ffmpeg asset comes from one nomercy-ffmpeg tag and never from `latest`.

**Retention.** nomercy-ffmpeg releases are kept rather than rotated, unlike FFmpeg-Builds' dailies (the repo's owner confirmed this, and that StemForge may use them). If an asset were ever re-uploaded under the same name, the SHA-256 pin fails the install loudly instead of installing different bytes. Each asset also has a `.sha256` beside it and the release a `manifest.json`; `v1.0.42` publishes no signature for the manifest, and a signature check would add nothing to a pinned hash anyway, since the pin already names the only acceptable bytes.

**Rehosting is not a fallback as the build stands.** Republishing a chosen build from StemForge's own repo would protect against the upstream release disappearing, but the build is configured with `--enable-nonfree` (`libfdk-aac`), and `ffmpeg -L` says so: it is not legally redistributable. Fetching NoMercy's own asset at install time does not redistribute it; hosting a copy would. A rehost would need a build without the nonfree parts.

Consequences:

- The archives are static builds with `ffmpeg`, `ffprobe` and `ffplay` at the root, zip on Windows and tar.gz elsewhere. `BundledLayout.FilesAtRoot` takes the target binary plus the asset's named companions (`ffprobe`, which audio-separator's pydub looks for beside ffmpeg) and nothing else, so `ffplay` is skipped. The `/bin/` flattening layout and tar.xz support are gone, and with them ADR 0005's SharpCompress dependency: the BCL reads gzip and tar. ADR 0005's decision to bundle ffmpeg on every OS stands.
- macOS gets native arm64 and x86_64 builds with `ffprobe`, replacing evermeet's x86_64-only binary without it.
- The first-run download grows from about 100 MB to between 160 MB (macOS arm64) and 265 MB (Windows).
- An existing install keeps whatever ffmpeg it already has: a bundled fetch runs only when the binary is missing, and nothing compares an installed binary with the current pin. A new pin reaches new installs only, until a refresh path exists.
- Extracted and downloaded binaries now get their execute bits on Linux and macOS; before, a bundled fetch left them at `0644`.
