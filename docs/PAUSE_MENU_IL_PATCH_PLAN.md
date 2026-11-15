# Pause menu IL patch plan

Status: static analysis only. No assembly has been modified.

## Verified from Terraria.UI raw IL

- `UI::UpdateMenu()` handles pause mode (`menuMode == 1`).
- It sets the first pause item at index 0 (Resume).
- Full game: `numMenuItems = 6`; the remaining entries are Options, Leaderboards, Achievements, Save, Exit (indices 1–5).
- Trial: `numMenuItems = 7`; Marketplace is inserted at index 1, shifting Options, Leaderboards, Achievements, Save, Exit to indices 2–6.
- Therefore an appended T360 entry must be index 6 in full game and index 7 in Trial. Existing actions must remain unchanged.
- `menuString`, `menuScale`, and `menuHC` are allocated with capacity 14, so those arrays have room.
- `MENU_PAUSE_COORDS` is a separate static `Location[]` of length 7. Its current Y coordinates are 221, 259, 297, 334, 372, 421, 459.
- `UI::SetMenu()` sets `uiHeight = 7` and assigns `MENU_PAUSE_COORDS` for pause mode.
- `UpdateMenu()` uses `MENU_PAUSE_COORDS` with dynamic indexes, so appending index 7 requires extending this coordinate array and its initialization, not merely increasing `numMenuItems`.

## Required patch surface

1. Extend the static `MENU_PAUSE_COORDS` initializer from 7 to 8 entries, adding a verified in-bounds coordinate for index 7.
2. Change only the pause-mode `SetMenu()` branch to `uiHeight = 8`.
3. In `UpdateMenu()`, add the T360 label at index 6 for full game and index 7 for Trial, after preserving the existing localized strings.
4. Extend pause-menu layout/visibility handling so the new coordinate is enabled in both modes.
5. Add a new selection branch for the appended index, preserving all original selection branches and actions.
6. Validate branch targets, stack consistency, max stack, exception regions, and metadata with an IL-aware writer before emitting any modified assembly.

## Safety gates

- Never overwrite the supplied base package or the user's installed game.
- Produce a separate patched managed assembly and package copy.
- Require metadata/IL validation and binary hash comparison before calling the artifact usable.
- No XEX rebuild, runtime rendering, or console test is claimed by this document.
