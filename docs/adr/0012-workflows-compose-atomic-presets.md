# Workflows are the multi-step concept; presets stay atomic

ADR 0011 made a [[Preset]] an ordered list of [[Step]]s so chained recipes could be expressed. Designing the multi-step editor surfaced that this overloads "preset": a preset is then sometimes one separation and sometimes a tree of separations, and the natural next want — *a step that reuses a curated built-in preset, or one of your own saved recipes* — turns into presets-referencing-presets, with recursion, cycles, and reference-integrity on a graph. This ADR splits the two ideas. It **supersedes the "a preset is an ordered list of steps" decision of ADR 0011**; the persistence lesson of 0011 (model the container now, not later) still holds, but the container moves off `Preset`.

## The decision

- **`Preset` is atomic** — one [[Separation run]] (built-in, custom ensemble, or single model + its [[Keep set]]). This is what a preset already is in practice. Presets are the reusable building blocks.
- **`Workflow` is the multi-step concept** — an ordered list of [[Step]]s ending in a single keep/naming decision. Workflows **compose** presets; presets never contain steps or other presets, so composition is one level deep with no cycles.
- **The step list is linear; inputs reference back.** A step's input is the source audio or a *specific output stem of any earlier step* (`Step N · stem`). The ordered list therefore expresses a tree without a tree-shaped UI — no node-graph canvas. The 90% case (input = the previous step's output) is the default.
- **Keep is one end-of-workflow decision over the pooled outputs of every step.** Which stems each step must emit is *derived* (a stem is produced only if a later step consumes it or the keep set retains it), so the single-stem fast path falls out for free and the user never authors per-step keep. A keep set is a property of *running* a separation, not of the atomic preset: a built-in's curated keep is the default when it runs standalone, but inside a workflow the workflow's decision governs (so a workflow can keep a stem the preset would normally hide).
- **Output identity avoids model names.** Step outputs are referenced as `Step N · stem` (stem names from the [[Model profile]]; positional fallback when unknown). Intermediate outputs are auto-named; the user names only the final kept files, with tokens `title` / `stem` / `workflow` / `step`, defaulting to the clean `{title} ({stem})` shared with presets (see ADR for #66 output naming). Collisions are disambiguated deterministically regardless.

## v1 scope

- A step's separation may be: a **built-in preset**, an **inline single model**, an **inline custom ensemble**, or a **user atomic preset** — all referenced **live by id** (editing a referenced preset propagates; deleting one a workflow uses is guarded by a reference-integrity check).
- **Deferred:** a step referencing another **workflow** (the only case that reintroduces recursion); **mixing operations** (add/subtract/combine, multi-input steps, the terminal kick+snare combine, and pure-mix presets with multiple external source inputs). The model leaves room for these as additive extensions, not migrations.

## Considered options

- **Keep the multi-step list on `Preset` (ADR 0011).** Rejected. It overloads "preset," and reusing a preset inside a multi-step recipe becomes preset-nesting with recursion and graph integrity. Two concepts make composition one-directional and shallow.
- **Built-ins are the only presets; every user recipe is a workflow.** Rejected. Cleaner two-bucket story, but a simple custom ensemble becomes a one-step workflow, existing user presets migrate to workflows, and reusing your own recipes immediately needs the deferred workflow→workflow case. Keeping atomic user presets lets workflows reference user building blocks *safely* (atomic → no recursion) with only a deletion check.
- **A node-graph / DAG editor.** Rejected for the editor UX. A linear list with back-referencing inputs expresses every tree the use cases need; a canvas is far more build and interaction cost for no added expressiveness while steps remain single-input. True joins only arrive with mixing, which is deferred.
- **Snapshot a referenced preset into the step.** Rejected in favour of live references: a snapshot never gets the preset's later improvements and silently drifts; live refs plus the deletion check are simpler and match "composition."

## Consequences

- ADR 0011's `PresetStep`/`Preset.Steps` work is repurposed: the step shape (input, models/ensemble, algorithm) moves onto `Workflow`; `Preset` reverts to atomic. The persistence container still exists, now on the workflow.
- The deferred #68 keep-set and the deferred #66 template UI are absorbed here: keep and naming are the workflow's end-of-run step, not bolt-ons to a single-step preset.
- A new persistence schema for workflows is needed (an ordered steps list + the keep/naming decision), plus reference-integrity for preset deletion. Built-in and existing user presets are unchanged.
- Mixing remains a clearly bounded future capability: multi-input steps + a mixing engine + (for pure-mix presets) multi-source jobs.
