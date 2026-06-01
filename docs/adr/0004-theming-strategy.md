# ADR 0004: Theming strategy — SystemAccentColor override, not per-control explicit styles

**Status:** Accepted

## Context

StemForge uses Avalonia 12 with `FluentTheme`. The default accent colour in FluentTheme is
Windows' system accent (blue on most machines). We want StemForge orange (`#d4703a`) used
everywhere: CheckBox ticks, RadioButton fills, ToggleSwitch tracks, Slider thumbs, TextBox
focus rings, ProgressBar fills, and Button.accent backgrounds.

Two approaches were considered:

**A — Explicit per-control styles**: Write `Style Selector="CheckBox /template/ ..."`,
`Style Selector="Slider /template/ ..."`, etc. in `Controls.axaml`, overriding each
FluentTheme template individually.

**B — SystemAccentColor override** (chosen): Define `SystemAccentColor` (and its Light1-3 /
Dark1-3 family) in `Application.Resources` (via `Tokens.axaml`). FluentTheme reads these
resources for every control that uses the accent colour, so one set of values applies
globally with no per-control boilerplate.

## Decision

Use approach B. `Tokens.axaml` defines `SystemAccentColor` through `SystemAccentColorDark3`
to cover the full shade family FluentTheme references internally.

## Consequences

### What this means in practice

- **Do not fight FluentTheme.** If a control looks right in isolation, let the theme handle
  it. Reach for a custom style only when FluentTheme genuinely cannot produce the desired
  result (e.g. `Button.accent`'s idle state, which requires targeting `/template/
  ContentPresenter` because FluentTheme's template sets Background on the presenter, not the
  button element).

- **Do not strip native control styles with inline overrides.** Controls like `Button.ghost`,
  `ComboBox`, `CheckBox`, etc. have carefully designed hover, focus, and pressed states.
  Setting `Padding="0"`, `BorderThickness="0"`, or `Background="Transparent"` directly on
  the element overrides those states and produces inconsistent behaviour. If the native
  appearance is wrong, write a scoped `Style` in the view or in `Controls.axaml` rather than
  applying inline property setters.

- **AccentBrush is for decorative use only.** `AccentBrush` (a `SolidColorBrush` pointing at
  `#d4703a`) is used in places where we explicitly paint with the brand colour outside of
  FluentTheme's control pipeline (e.g. status dots, variant chips, hyperlink foregrounds).
  It is not used to theme interactive controls — that is `SystemAccentColor`'s job.

- **ProgressBar foreground inherits from SystemAccentColor.** Do not set `Foreground` on
  ProgressBar elements; the theme derives it automatically.
