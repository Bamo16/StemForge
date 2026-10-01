# StemForge runs its own download tools, and keeps yt-dlp current

A friend's first install of v0.4.0 could not download from YouTube. ffmpeg exited with -858797304, which is `AVERROR_HTTP_FORBIDDEN`: YouTube answered the media URL with 403. Three choices behind that are reversed here.

**Bundled tools never fall back to PATH.** `AppPaths` resolved ffmpeg, yt-dlp and deno to the bundled binary when it existed and to the bare name otherwise, and the setup wizard treated a copy on PATH as installed. That machine had an old ffmpeg on PATH, so setup skipped the bundled one and enabled Next before yt-dlp and deno were installed. ffmpeg, yt-dlp and deno now resolve to the bundled binary or a Settings override and nothing else; a missing binary is reported missing, and the first detection after launch reopens setup when a required tool is. ADR 0002's rubric still decides how a tool installs (uv and audio-separator on PATH, the rest bundled); it no longer lets PATH stand in for a bundled tool. The Install step's own button installs everything ticked and moves on, so optional tools are not left behind by pressing Next.

**yt-dlp is kept current, not just pinned.** The pinned yt-dlp (2026.03.17) was six months old, and YouTube refuses the links an old yt-dlp produces: that build reproduced the 403 here, and 2026.08.19 did not. ADR 0002 already expected yt-dlp to self-update; now StemForge runs its `--update` at most once a day, at GUI launch and before resolving a URL, for the bundled copy only. `--update` stays on the installed channel, so a user who moves to nightly stays there. The pin is only where a new install starts, and moves with releases. ffmpeg and deno stay pinned, as ADR 0015 decided for ffmpeg: their output must not change under a user, and YouTube does not break them.

**yt-dlp downloads; ffmpeg converts a local file.** ADR 0003 had yt-dlp only resolve a media URL and ffmpeg fetch it, so its `yt-dlp → ffmpeg` dependency did not apply. A client fetching that URL without yt-dlp's per-request handling is what YouTube refused, once after two and a half minutes of a download. yt-dlp now downloads the chosen format into a temp directory, reusing the resolve's JSON through `--load-info-json` so there is no second extraction, and ffmpeg converts that file to the output format at 44.1 kHz. yt-dlp is given `--ffmpeg-location` because it repairs some containers after downloading; with this, the stale 2026.03.17 build also downloads successfully.

Consequences:

- A user's own ffmpeg or yt-dlp is used only through Settings → Tool paths, and an overridden yt-dlp is never updated by StemForge.
- The source is written to a temp file before conversion, where it used to stream straight into ffmpeg.
- An update check needs the network once a day; when it fails, the installed copy is used.
