# Premium shortfall: an expectation-driven, advisory signal for degraded acquisition

StemForge classifies every URL [[Resolve]] against the user's premium expectation, where the
expectation is **inferred from the user having configured a cookie source** rather than declared
through a setting of its own. Classification happens at resolve time, before any audio bytes are
paid for, and is advisory in both front-ends (chips in the GUI, stderr in the CLI); it never
blocks. Callers needing a guarantee pin a [[Source format]] with `--format-id`, which hard-fails
the input instead.

Two definitions carry the design. An **[[Auth-gated format]]** is one YouTube offers only to an
authenticated Premium request; that is a session signal, not a quality one. A **[[Premium
format]]** is auth-gated *and* higher-bitrate than the best ungated format the same source
offers, computed per resolve rather than from a fixed list. The distinction is load-bearing:
format 250 is auth-gated at 61 kbps and loses to the freely-available 140 at 130 kbps, so calling
it premium would tell a user they received better audio than they did. Measured across a
72-video corpus, 250 was gated on 65 sources and beat the ungated baseline on **zero** of them.

Five outcomes follow, distinguished by two facts, and only two of them are the user's to fix:

| source | gated formats offered | outcome | what the user should do |
| --- | --- | --- | --- |
| any | a Premium-gated one is selected | met | nothing |
| [[Music track entity]] | none at all | **not signed in** | re-authenticate the browser session |
| [[Music track entity]] | session-gated only | **account not Premium** | check which profile/account the cookies come from |
| video upload | session-gated only | source has no Premium audio | nothing; try a song version |
| video upload | none at all | no [[Premium ladder]] | nothing; try a song version |

Two independent measurements make that table possible, both from a 552-video corpus resolved three
times over (logged out, signed-in free, signed-in Premium):

**Provisioning attaches to the track, not the video.** All 326 music-typed sources carried the full
ladder; 29% of non-music sources carried none at all despite working cookies. So "nothing gated" is
only evidence of a fault when the source is a music track. Conditioning on music-typed metadata is
not a hedge but the actual discriminator: an unconditioned rule would have blamed cookies on 65
perfectly healthy resolves.

**Gating has two tiers.** `141` and `774` are withheld even from a signed-in free account (0/552),
while `250` and the suffixed variants are shown to it. Since every music-typed source carries
`250`, its presence or absence on a music track separates a dead session from a live one on a
non-subscribing account. That distinction matters because a free signed-in account is a legitimate
state, not a malfunction, and telling such a user their cookies are broken would be a misdiagnosis.

Premium-ness remains **advisory, never authoritative**, on the same terms as the [[Model profile]]
in ADR 0010: the gated id set is empirical, has no upstream source, and will drift.

## Considered options

- **Detect the broken authentication directly, by parsing yt-dlp's stderr.** Rejected, because the failure mode that motivated this ADR is silent. yt-dlp's auth surfaces are graded: an unusable cookie *source* (malformed file, Netscape-vs-JSON, unreadable browser database) warns, and genuinely restricted content raises `ExtractorError` via `raise_login_required`. But a cookie that loads cleanly whose *session* has been invalidated (the real case: a password reset elsewhere) produces no warning and no error at all. YouTube simply serves the logged-out format set and yt-dlp succeeds normally with fewer formats. Stderr parsing would therefore have caught every case except the one that occurred. The absence of a premium format id is the only observable the silent case leaves behind, so format-set inference is the detector, and stderr parsing is retained only as a secondary surface for the cookie-source warnings that are currently swallowed.
- **Report the shortfall without trying to explain it.** Rejected once the corpus was measured,
  and this is the decision that changed most. The original reasoning was that both causes (dead
  session, source with no premium audio) warrant the same response, so naming the gap honestly
  was enough. The data disproved the premise: on 72 authenticated resolves an unconditioned rule
  produced 7 false "your session may not be signed in" warnings, a 10% rate on a signal whose
  entire purpose is to be noticed. A warning that cries wolf one time in ten trains the user to
  ignore it, which is precisely the failure this ADR exists to prevent.

  What made explanation possible was discovering *where* provisioning lives. A matched pair on
  one recording settled it: the YouTube Music track entity (`- Topic` channel, artist/track/album
  populated) carried the full ladder, while the label's own official video upload of the same
  track carried nothing at all. So an official channel is not the predictor — both official
  channels tested had no ladder. Music-typed metadata is, and it converts the false-alarm rate
  from 10% to zero on the corpus while letting StemForge say something useful about a video
  upload ("a song version may offer higher bitrate") instead of something wrong about cookies.

  The two alternatives previously considered for disambiguation remain rejected, now for a
  better reason than cost: probing a hardcoded reference video, and inferring from the provenance
  of past downloads. Both would establish session health across resolves. Neither is needed,
  because a single resolve already carries the answer once you read the metadata shape.

- **A dedicated "I expect premium audio" setting.** Rejected as a redundant second declaration. The cookie source setting is already opt-in, already blank by default, and its own help text already frames it as *"Needed for YouTube Premium audio"*. A user who filled it in has stated the expectation; asking again invites the two to disagree, and a shortfall warning shown to someone who never configured cookies is noise about a feature they do not use. Inference from existing configuration is what scopes this signal to the users it is for.
- **Surface premium status on the format-picker toggle.** Rejected: the control disappears in precisely the degraded case. The toggle is bound to `HasFormatPicker`, which requires more than one candidate format, and the GUI's preview path swallows every resolve failure to `null` — so a thin or failed format list removes the very affordance the indicator would live on. Premium status and shortfall belong in the resolved chips row alongside codec, bitrate and sample rate, which is gated on the title resolving and therefore survives both degradations. Every outcome is shown explicitly (the YouTube Premium wordmark when confirmed, a distinct chip for each negative case) rather than letting absence carry the meaning, since an indicator you must notice is missing is the class of fix that already failed.
- **Ship `--dry-run` and `--require-premium`.** Rejected in favour of `--list-formats` and `--format-id`, which subsume both. Listing every candidate with the auto-pick marked answers everything a dry run would, plus the question a dry run cannot: which of several candidate URLs for the same track to prefer, and on what grounds. Pinning a format id with hard-fail semantics expresses "do not give me a degraded file" more precisely than a premium flag, since a reproducibility corpus needs *identical* treatment across tracks, not merely *premium* treatment. Together they also close a real asymmetry: the GUI has had a format picker since v0.2.0 and the CLI could neither see the candidate list nor choose from it.
- **Mirror yt-dlp's `--format` flag name for pinning.** Rejected: `--format` is already taken on `download` and means the **[[Output format]]** (the container ffmpeg encodes *to*), which is the opposite end of the pipeline from yt-dlp's source selection. Reusing the name would silently invert its meaning for anyone carrying yt-dlp habits across. `--format-id` is unambiguous and matches the vocabulary the JSON contract and provenance tag already use.

## Consequences

- The shortfall rule is scoped by construction: no cookie source configured means no [[Premium expectation]], which means premium-ness is never surfaced and no warning can fire. Users who do not use YouTube, or do not use Premium, see no change whatsoever.
- Because premium-ness is inferred from a hardcoded format-id set with no test behind it, and the entire signal rests on that set, it gains a test pinning current behaviour and a comment recording each id's provenance (`774` currently has none). The set stays in code rather than moving to configuration: exposing it would trade a drift problem StemForge can fix in a release for one users must diagnose and hand-edit with no way to derive the right answer.
- `--format-id` hard-fails the affected input at resolve time and lets the batch continue, matching the continue-on-failure contract `download` already documents, so exit code 2 (partial) reports it with no new exit code. A silent fallback to the auto pick is specifically not offered, since that is the behaviour this work exists to make impossible.
- The CLI JSON contract grows additively. `DownloadResult` gains `formatId`, `codec`, `bitrateKbps`, `isPremium` and `premiumShortfall`, so existing parsers keep working while a consumer can assert acquisition quality per track programmatically rather than reading provenance tags off the finished file. `--list-formats --json` carries the full candidate array per input with the auto-pick marked.
- Post-hoc verification via the [[Provenance]] comment tag is unchanged and remains the record of what was actually acquired. This work moves the same answer *earlier*, to before the download; it deliberately does not add a second provenance mechanism.
- The default [[Source format]] selection policy and the 44.1 kHz normalisation are untouched. Both remain correct for separation, which is the product's primary use; `--format-id` is an opt-in override for callers whose goal is reproducibility across a set rather than the best result per track.

## Open risk

The design assumes a [[Music track entity]] is *always* provisioned a [[Premium ladder]]. That held
on 326 of 326 music-typed sources, hunted adversarially across long-tail Topic channels, classical,
jazz, non-Western and spoken-word repertoire, and compilation tracks, with no counterexample. Two
gaps remain, neither large enough to change the design:

- **Brand-new releases are thin** (6 Art Tracks from the preceding weeks, all provisioned). A
  provisioning delay on a just-released track would surface as a spurious "not signed in" on that
  one source. The cost is one wrong advisory chip, not a bad download, and the wording enumerates
  causes rather than asserting one.
- **One region, one point in time, two accounts.** Provisioning could differ by geography.
- **The expectation is global, but the evidence is YouTube-only.** A configured cookie source is
  read as "this user expects premium audio" without asking *whose* premium. That holds while
  YouTube is the only source with a premium tier StemForge can detect. It stops holding the moment
  a second provider offers one: a user who configures cookies for that provider, and who has no
  YouTube subscription, would then be told their YouTube resolves fall short, which would be true
  but unwanted. The fix at that point is to scope the expectation per extractor rather than
  globally, which is a change to [[Premium expectation]] and not to any of the outcomes above.

If a counterexample does appear, the fallback is to require corroboration across several resolves
before implicating the session: a genuinely dead session yields no gated formats on 100% of
sources, against 12% for a healthy one, so a couple of consecutive observations separate them
decisively.
