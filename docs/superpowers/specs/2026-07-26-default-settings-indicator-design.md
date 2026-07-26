# Default Settings indicator light — design

**Date:** 2026-07-26
**Scope:** QualityBuilder mod settings window (`QualityBuilderMod.DoSettingsWindowContents`)

## Problem

QualityBuilder has two settings scopes: a global "Default settings" page and per-map settings.
Which one actually drives the game is decided by `QualityBuilder_MapComponent.useMapSettings` —
a checkbox that is only visible while the "Current map" page is selected. A player editing the
Default settings page has no way to tell whether those edits reach their colony: if the current
map has opted into its own settings, the Default page is inert for that map.

## Solution

A labeled status light to the right of the "Selected settings" selector, plus an explanatory note.

### Semantics

The light reports what is **actually governing the current map right now**, independent of which
settings page the player is viewing.

| Current map state | Light color | Text |
| --- | --- | --- |
| `useMapSettings == false` | green | Default Settings Active |
| no current map (main menu) | green | Default Settings Active |
| `useMapSettings == true` | red | Default Settings Inactive |

This mirrors the branch in `QualityBuilderModSettings.getSettings(Map)` that selects the settings
object — the indicator and the runtime resolution must never disagree.

Deliberate choice: the light does **not** follow the selected page. Showing "Inactive" while the
player is on the Default page is exactly the situation the feature exists to surface.

### Layout

`Listing_Standard.ButtonTextLabeled` spans the full column width (label 0–50%, button 50–100%),
leaving no room to its right. The selector row is therefore drawn manually:

- `Rect row = list.GetRect(30f)`
- left ~72% — `Widgets.Label` + `Widgets.ButtonText`, preserving current behavior and the tooltip
- right ~28% — a 12×12 `Widgets.DrawBoxSolidWithOutline` swatch and the status text in the same
  color, vertically centered

A `TooltipHandler.TipRegion` over the indicator repeats the explanation on hover.

### Explanatory note

A greyed line under the existing `SelectedSettingsHint`, paired to the light's state:

- **Inactive:** "This map is using its own settings, so changes on the Default settings page will
  not affect it. Untick 'Use these map settings' to hand control back."
- **Active:** "This map is using the Default settings below. Tick 'Use these map settings' on the
  Current map page to give this map its own independent settings."

New keys are added to `Languages/English` only; RimWorld falls back to English for the other six
shipped languages.

## Testability

UI state drawn inside `OnGUI` cannot be asserted. The light's state is therefore extracted into
two statics on `QualityBuilderModSettings`, which the UI calls:

- `areDefaultSettingsActive(Map map)` → `bool`
- `getDefaultSettingsStatusLabel(Map map)` → translated label string
- `getDefaultSettingsStatusNote(Map map)` → translated note/tooltip string

A read-only bridge tool `qb/get_default_settings_indicator` (QualityBuilderBridgeTools) reflects
into these and returns `{ active, label, note }`. Tests assert against the same methods the UI
renders — not a parallel reimplementation.

## Tests

Added to `qbtest/tests/test_e_config.py` (dimension E is settings territory and already runs in
the default suite — no new pytest marker or run profile needed).

- **e6 — indicator tracks `useMapSettings`.** Flip true → false → true; each time assert `active`
  and that `label` is exactly "Default Settings Inactive" / "Default Settings Active", and that
  the note matches the paired state.
- **e7 — indicator agrees with runtime resolution.** Set a distinct `defaultMinQualitySetting` on
  each scope, spawn a blueprint in each `useMapSettings` state, and confirm the light correctly
  predicts which scope's value the blueprint received. Also confirm the indicator is unaffected by
  the `scope` argument used to read settings.

## Out of scope

- Translating the new keys into the six non-English language files.
- Any change to how settings are resolved, stored, or saved. This is a read-only indicator.
