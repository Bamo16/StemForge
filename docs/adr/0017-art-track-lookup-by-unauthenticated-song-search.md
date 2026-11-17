# An Art Track is found by an unauthenticated YouTube Music song search

A caller whose source has poor audio wants to offer the matching [[Art Track]], the audio-only song upload that carries the [[Premium format]]s. `stemforge-cli art-track <video id or link>` finds it and prints one JSON object: `found`, and when true `videoId`, `title`, `artist`, `lengthSeconds` and `lengthDifferenceSeconds` (song minus video). It never changes a source by itself; the caller decides what to do with the suggestion.

**Search, not the counterpart link.** YouTube's own video-to-song link (`next` → `counterpart`) never appears without signing in: 0 of 87 videos and 0 of 70 songs returned one unauthenticated. StemForge does not use anyone's cookies or account for this, so the lookup is two unauthenticated YouTube Music requests: `next` for the video's artist, title and length, then a Songs-filtered `search` for "artist title". A hit is the Art Track only if it is an ATV (`musicVideoType`), its length is within 2 s of the video's, and its title equals the video's once brackets, case and punctuation are ignored. The closest length wins. Without the title rule, 2 of 15 candidates in a 22-video sample were wrong ("Sweet Time" for "Cheerleader"); with it, 13 were right, none wrong and 9 found nothing.

**Nothing found is an answer, not an error.** The command exits 0 with `found: false`. Exit 1 is for an input that is not a YouTube video or a request that failed. A video that is already an ATV gets `found: false`, since there is no other half.

**Politeness.** The two requests are 1.1 s apart and the client sends a browser user agent, because YouTube Music is an API StemForge has no agreement with. The caller is expected to cache the result; StemForge keeps none.

**A video id may start with `-`**, which the command parser reads as an option, so a caller passes the link (`https://music.youtube.com/watch?v=<id>`) rather than the bare id.

Consequences:

- The lookup depends on YouTube Music's `WEB_REMIX` response shape and the Songs filter parameter; either can change without notice, and the first symptom is `found: false` for everything. The parser tests run against recorded responses, so they pass either way; the smoke test is the live check.
- It finds roughly 6 in 10 Art Tracks. Videos whose song title differs from the video's, and mixes with no song, are missed.
