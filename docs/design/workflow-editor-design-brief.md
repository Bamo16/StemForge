# Design brief: multi-step Workflow editor

For a UX/visual design pass (e.g. claude design). StemForge is a Windows-first desktop app (Avalonia, dark theme). Existing screens to match for visual language are in `docs/images/` (`screenshot-separate-presets.png`, `screenshot-models.png`, `screenshot-queue.png`, `screenshot-settings.png`). Match that dark, compact, card-and-chip aesthetic.

## What we're designing
A new **Workflow editor** (a multi-step recipe builder) plus how workflows are listed and launched. This is the main new surface; the rest of the app is unchanged.

## Concepts (just enough to design against)
- **Stem** — an isolated audio component (vocals, drums, bass, instrumental, …). A separation produces one file per stem.
- **Preset** — a *single* separation, atomic and reusable. Either built-in (curated, best-in-class) or user-made (a custom ensemble of models, or a single model). This is today's "preset" — unchanged.
- **Workflow** — the new thing: an **ordered list of Steps** ending in a keep/naming decision. Composes presets; does not nest.
- **Step** — one stage of a workflow: an **input** + a **separation** to run on it, producing that separation's stems.

## The editor — core interaction
Render the workflow as a **linear ordered list of steps** (think notebook cells / a vertical stepper). NOT a node-graph canvas. The list still expresses a branching tree because each step picks its input by reference (below).

Each **step row** has:
1. **Input picker** — choose from `the source audio` + `every earlier step's output stem`. Default selection = the previous step's output. Label outputs as **"Step 1 · drums"** (step position + stem name). **Never show model file names** in pickers (they're absurdly long).
2. **Separation picker** — what to run: a **built-in preset** (e.g. "Vocals — Balanced"), or an **inline model / ensemble** (pick one or more models + an ensemble algorithm).
3. Add / remove / reorder steps.

Worked example the editor must express (four ordered rows representing a tree):
1. Demucs on **source** → vocals / bass / drums / other
2. A vocal-split model on **Step 1 · vocals** → male / female
3. A drum-split model on **Step 1 · drums** → kick / snare / …
4. (terminal) keep bass+other from Step 1, male+female from Step 2, all drums from Step 3

## The terminal step — keep & naming (one decision at the end)
- A single **keep selection** over the *pooled* outputs of every step (shown as "Step N · stem"). The user ticks which stems to keep; everything else is discarded.
- **File naming for kept stems only.** Default to the clean convention `Title (stem)` (e.g. "Song Title (drums)"). Offer an optional name template with tokens: `title`, `stem` (and possibly `workflow`). Intermediate/unkept stems are never named by the user.

## Library / launch surface (Separate screen)
Today the Separate screen lists built-in presets in categories with a separate "My Presets" tab. With workflows added there are three kinds: built-in presets, your presets, your workflows. **Recommended:** a unified "Mine" list with a small **type chip** (`Preset` / `Workflow`) rather than three groupings — but explore alternatives.

## Hard constraints (don't violate)
- Linear list, not a canvas. Inputs reference earlier steps; that's how trees are expressed.
- No model file names in pickers — use "Step N · stem".
- Stems come from a "model profile" that resolves names without running the model. **Handle the unknown case:** some models can't have their stems named — fall back to positional labels ("Step 1 · output 1/2") and degrade gracefully.
- Keep is ONE terminal decision; the user never sets per-step keep.
- Advisory, never blocking: the app should guide, not prevent.

## Out of scope for v1 (don't design these yet)
- Mixing operations (add/subtract/combine stems) and steps with multiple inputs.
- Workflows that reference other user workflows.
- Multiple external source inputs in one recipe.

## Open questions design can help explore
- Best layout for the per-step input + separation pickers (inline vs. expandable).
- Where the terminal keep/naming lives: a final pinned section, a distinct last "step", or a separate screen.
- The unified Mine list vs. separate Presets/Workflows groupings.
- How to make the 1-step case (which equals "make a custom ensemble") feel as light as today's save-preset flow.
