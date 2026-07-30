# ⓘ icon as the standard affordance for hover-help tooltips

Avalonia `ToolTip.Tip` attached to bare text or controls is invisible until hover — users have no signal that extra info is available, so most tooltips go undiscovered. We standardise on a small ⓘ icon (`TextSecondaryBrush`, ~12px, baseline-aligned, ~6px right of its anchor) as the universal hover-help affordance: every place that wants to expose a tooltip attaches it to an ⓘ icon next to the labelled element, rather than to the element itself. Considered and rejected: bare tooltips on raw text (poor discoverability) and dotted-underline on text (works only for text anchors, easy to overlook). One visual idiom keeps the surface predictable and scales to any anchor type — labels, checkboxes, icons, headings.

Refinement: the ⓘ is the *signal*, not necessarily the only *target*. Where the anchor is itself a
sizeable element (a status chip, say), attaching the same tooltip to the anchor as well as the icon
widens the hover area without weakening discoverability. Two cases were considered and rejected for
such chips: dropping the icon because the chip already carries a ⚠ glyph — a warning glyph
communicates severity, not that more text is available on hover, and those are different jobs — and
varying the rule by chip type, which forces a user to learn that warnings behave differently from
notes for no gain. The rule stays one rule. Note this matters most, not least, where the visible
label is a compressed status and the tooltip carries the only actionable instruction.

A truncated text anchor is the one standing exception: an ellipsis already signals that there is
more to see, so text with `TextTrimming` may carry its full value as a bare tooltip without an ⓘ.

Implementation note (gotcha caught post-implementation): the icon is drawn as a `Border` with `BorderThickness=1` and a small `TextBlock` glyph inside. Avalonia only hit-tests a `Border` on the border ring and its child content — the empty disc inside the ring is transparent to pointer events unless `Background` is explicitly set. Setting `Background="Transparent"` (the literal brush, not unset) makes the whole disc hit-testable so hovering anywhere over the icon fires the tooltip. Leaving `Background` unset leaves a narrow ring of valid hit area and a "dead zone" in the middle that swallows hovers; users see the icon but most attempts to point at it produce nothing.
