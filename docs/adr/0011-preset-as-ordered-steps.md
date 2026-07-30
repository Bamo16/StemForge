# A preset is an ordered list of steps, persisted that way from the start

A [[Preset]] used to be a flat recipe: a set of [[Model]]s plus an [[Ensemble algorithm]] and a [[Separation mode]] tag. That shape can only ever express one [[Separation run]]: a single pass over the source audio. The roadmap wants chained presets (a [[Step]] that runs on an earlier step's output stem, e.g. extract drums, then split the drum stem by kit piece), and the flat shape cannot hold a second stage. This ADR restructures the preset model **and its on-disk schema** so a preset is an ordered list of steps from day one, with exactly one step today.

The key decision is about persistence, not just the in-memory type. An earlier attempt added a computed steps *view* over the flat fields and left `user_presets.json` flat; that was rejected because it postpones the real schema change to the day chaining ships, which is exactly the migration this ADR exists to avoid. The schema itself must store the steps list now.

## The decision

- A [[Preset]] carries an ordered `Steps` list. Each [[Step]] is a `PresetStep` holding its input, the [[Model]]-or-[[Ensemble]] to run, the [[Ensemble algorithm]] (when two or more models run together), and the [[Keep set]]. Today exactly one step is ever created, and its input is always the source audio (`StepInput.Source`).
- `user_presets.json` stores the steps list directly: each persisted preset has its metadata plus a `Steps` array. This is the source of truth on disk. Because the container already exists in the schema, a future chained preset is more rows in the array, **not a migration**.
- The flat constructor parameters (`PrimaryModel`, `ExtraModels`, `EnsembleAlgorithm`, `Mode`) stay as the entry point for built-in and UI-created presets, and the flat accessors (`Mode`, `PrimaryModel`, `AllModels`, ...) stay as computed projections of the single step. Built-in catalog presets and every existing consumer (the pipeline's request builder, the preset chips) see an unchanged shape. This is what keeps "no behavior change for single-step presets" true.
- The old flat file format is still read. When a persisted preset has no `Steps` array, its flat fields (`Mode` + `PrimaryModel` + `ExtraModels` + `EnsembleAlgorithm`) are migrated into a single step on load, then re-saved in the steps schema on the next write. No data is lost: a single-model preset becomes a one-model step, and a custom ensemble becomes a primary-first multi-model step carrying its algorithm.

## The rules behind the shape

- **The container is introduced, not the capability.** There is no UI, pipeline, or driver support for more than one step. A preset with two steps cannot be created today and would not run. The point is solely that the *schema* and *type* are shaped so adding the capability later touches the executor, not the persisted format.
- **A step owns the [[Keep set]].** The glossary already places the keep set on the step (the stems a run retains, the rest discarded). Modelling it on `PresetStep` now means a chained step can keep a different subset than its predecessor without another schema change. An empty keep set means "keep everything the run emits", which is the prior default for user presets, so the field never has to enumerate stems the [[Model profile]] cannot yet name.
- **`Input` is an enum, not a stem reference, today.** The only legal value is `Source`. When chaining arrives, the input of a later step becomes a named stem from an earlier step; encoding `Input` as a small enum now (rather than a bare bool or nothing) reserves that axis without inventing the stem-reference representation before it is needed.
- **The flat fields are read-only legacy on disk.** They are still deserialized so an old file migrates, but they are omitted when writing (a freshly saved preset carries the steps list alone). The two never both appear in a file StemForge wrote.
- **The flat in-memory parameters are not legacy.** They remain the ergonomic way to declare a single-step preset in code (the built-in catalog and the "save preset" UI both use them) and are projected into the one step. Removing them would churn every call site for no behavioral gain while the only kind of preset is single-step.

## Considered options

- **A computed steps view over the flat persisted fields.** Rejected (this was the earlier attempt). It satisfies the type-level reading of the issue but leaves `user_presets.json` flat, so the schema change is merely deferred to the day chaining ships, dragging a real migration with it. The whole value of doing this now is that the persisted format already holds the list.
- **Replace the flat fields entirely with a steps list everywhere.** Rejected for now. It would rewrite the built-in catalog, the save-preset view-model, the pipeline's request builder, and the preset chips to read a one-element list, all to express the single-step case that the flat parameters already express clearly. The flat parameters are kept as a thin façade over the single step; the cost of removing them is paid (if ever) when a second step actually exists.
- **Migrate the old flat file in a one-shot rewrite tool.** Rejected as overkill. Migration stakes are low (no shipped release has written user presets that would survive to chaining), so reading the old format on load and re-saving in the new shape is enough and needs no separate tool or version stamp.

## Consequences

- `user_presets.json` is now a list of presets each carrying a `Steps` array; the flat model/algorithm fields are gone from files StemForge writes but are still accepted on read for one migration cycle.
- Chained (multi-step) presets become an executor and UI change, not a persistence migration: the on-disk and in-memory containers already hold an ordered list.
- The [[Keep set]] now has a home on the [[Step]] for when the stem picker can populate it per step; today it is empty (keep all) for user presets and owned by the driver for built-in presets, unchanged.
- The pipeline is untouched: it still consumes a [[Preset]] through the same flat accessors and builds the same `JobRequest` per [[Separation run]], so single-step presets run exactly as before.
