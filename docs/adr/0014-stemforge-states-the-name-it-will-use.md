# StemForge states the name it will use, and a name may be lossier than the metadata

A [[Resolve]] reports the [[Display name]] and [[Base name]] it would give an acquisition, not just
the raw title and artist it read. `download --list-formats --json` carries both alongside the
existing fields, so a caller deciding whether to rename a track can see the eventual name before any
audio is fetched or any [[Separation run]] is paid for.

The rule that produces the base name has exactly one definition, `YtDlpMetadata.BaseName`, and the
downloader writes its file from that same property. The reported name and the written name cannot
disagree, because they are not two implementations of one rule; they are one expression read twice.

## The decision

**The side that owns a naming rule states the answer. A caller never reconstructs it.**

The obvious alternative is to publish the parts and let the caller compose `{artist} - {title}`.
It is wrong for a reason that generalises past this feature: the name is the output of rules that
live here and will keep changing here, so any copy of them elsewhere is a copy that drifts silently.
A prediction that can go stale is worse than no prediction, because the entire value of the field is
being trustworthy enough to make a rename decision on.

That is not a hypothetical. Building this, the first implementation deduplicated the artist list
correctly, joined it to a string, and then split that string back apart to strip featured artists.
The round-trip corrupted `Simon & Garfunkel` into `Simon, Garfunkel` — a name broken in two and a
separator rewritten — because the split recognised separators the join had never produced. That was
StemForge mis-reconstructing its own data, one file away from the authoritative list, with a test
suite around it. A consumer in another repository, working from a flattened string and a guess at
the rules, has materially worse odds.

The credit list is therefore carried as a list end to end and flattened exactly once, at
`YtDlpMetadata.Artist`, where something finally needs text.

## What is promised, and what is only documented

Only the **source file's** base name is a promise. Stems are conventionally `Base name (Stem)`, but
a [[Preset]] may supply its own naming template, and `OutputNamer` suffixes on collision using the
contents of the output directory at write time. A resolve knows neither the preset nor the
directory, so predicting a stem's final name would be exactly the drifting mirror this decision
exists to avoid. The convention is documented; the source file's name is guaranteed.

A related worry turned out not to apply. Two sanitisers exist — the downloader drops characters the
filesystem rejects, `OutputNamer` replaces them with `-` — and they look like they could disagree.
They cannot, on the URL path: they run in sequence rather than in parallel, and the stem title is
derived from the already-dropped download filename, so the second sanitiser never sees a character
the first left behind. They were left as they are.

## A name is allowed to be lossier than the truth

Two shaping rules act on the credit list, and they are deliberately scoped differently.

| rule | what it does | where it applies |
| --- | --- | --- |
| collapse repeats | removes a yt-dlp artifact — one source reports 11 entries naming 3 artists, one per role held | everywhere: name, JSON, [[Provenance]] |
| drop a credit the title carries | removes a real fact so the name does not say it twice | the [[Display name]] only |

The asymmetry is the point. Repeats carry no information, so collapsing them restores the truth and
every consumer should see it. A featured artist named in the title is genuinely an artist on the
track, so omitting them shortens the *name* while the ARTIST tag keeps every credit. Provenance is
written into the file to survive being moved and re-ingested; a filename is not, and is allowed to
be a convenience.

Featured detection reads free text, which a title cannot disambiguate: `feat. Tyler, The Creator` is
either one artist or two, and nothing in the string says which. Rather than guess, both readings are
offered as candidates and matched against the credit list, which does know. A candidate that
matches no credited artist does nothing, so an unparseable title degrades to no change rather than
to a wrong one. If the rule would remove every artist, it is abandoned and the full list kept: a
track is never left nameless to avoid a repetition.

## Considered options

- **Publish the parts; let callers compose.** Rejected above. Cheapest to ship, and the failure mode
  is silent divergence discovered after a rename has already been made on bad information.
- **Report a stem name too.** Rejected: it depends on preset and collision state a resolve cannot
  observe, so it would be a promise broken by the first user preset with a template.
- **Apply featured-artist removal everywhere, including the tag.** Rejected: it writes a knowably
  incomplete ARTIST into a file whose whole purpose is to carry provenance forward.
- **Drop featured-artist removal entirely.** Genuinely defensible, and considered after the
  round-trip bug. Kept because matching against the credit list means the rule can only fire on a
  real credited artist, which bounds the worst case to "no change".
- **A dedicated `name` subcommand, or renaming the flag to `--resolve`.** Deferred. The glossary
  calls the operation a [[Resolve]] and `--list-formats` is named after one of its outputs, so the
  CLI vocabulary and the domain language disagree. Additive fields ship without breaking existing
  callers; the rename is a separate change whenever it is worth making.

## Consequences

- A consumer gets the name in the same call that already resolves formats, with no extra fetch.
- Changing a naming rule changes the reported name automatically. That is the property being bought,
  and it means a caller must treat the name as StemForge's answer rather than caching a derivation
  of it.
- `YtDlpMetadata.Artist` is now derived rather than stored. Anything constructing metadata supplies
  `Artists`, and the flattened form is computed.
- The featured-artist rule is a heuristic over free text. It is bounded — it can only remove a name
  the credit list already contains, and never all of them — but it will silently not fire on titles
  it cannot parse. That is the intended failure direction.
