# Pause menu IL patch plan

Status: static analysis and patcher validation. No Terraria assembly has been modified.

## Verified from Terraria.UI raw IL

- `UI::UpdateMenu()` handles pause mode (`menuMode == 1`).
- Full game: `numMenuItems = 6`; valid indices are 0–5 and index 5 is Exit.
- Trial: `numMenuItems = 7`; valid indices are 0–6, index 5 is Marketplace and index 6 is Exit.
- The pause menu's dynamic base local (`loc.s 9`) is 1 in full game and 2 in Trial. Existing actions compare `selectedMenu` against this base plus offsets 0–4. The existing Exit action is `selectedMenu == loc.s 9 + 4`.
- Therefore the new T360 item is index 6 in full game and index 7 in Trial. The safest selection branch is a new `selectedMenu == loc.s 9 + 5` action, leaving the existing Exit action unchanged in both modes.
- `menuString`, `menuScale`, and `menuHC` have capacity 14, so their backing arrays have room.
- `MENU_PAUSE_COORDS` is a separate static `Location[]` of length 7. Its current Y coordinates are 221, 259, 297, 334, 372, 421, 459.
- `UI::SetMenu()` sets `uiHeight = 7` and assigns `MENU_PAUSE_COORDS` for pause mode.
- `UI::UpdateMouse()` uses `uiHeight` for vertical navigation/wrap and indexes `uiCoords` to position the cursor. Extending the menu therefore requires both an eight-entry coordinate array and pause `uiHeight = 8`.
- `UI::DrawMenu()` loops to `numMenuItems` and reads `menuString`; increasing the item count makes the new item participate in the existing draw loop.
- The candidate Y coordinate 497 is only a geometric estimate. It has not been checked in runtime rendering.

## Managed assembly writer gate

- Mono.Cecil 0.11.6 synthetic branch/exception-region round-trip test passed on GitHub Actions.
- Read-only Mono.Cecil inventory of the extracted Terraria assembly passed: 168 types, 1,367 methods, 1,338 method bodies, and zero invalid branch/EH targets.
- The real Terraria assembly is now being tested with a separate-copy round-trip writer. This must preserve a fingerprint of types, fields, methods, IL instructions, branch targets, exception regions, resources, and assembly references while leaving the input SHA-256 unchanged.
- The round-trip result is not yet verified; do not begin patching until this gate passes.

## Required patch surface

1. Extend only the static `MENU_PAUSE_COORDS` initializer from 7 to 8 entries; select and verify the final coordinate before runtime testing.
2. Change only the pause-mode `SetMenu()` branch to `uiHeight = 8`; do not change title-menu coordinates or height.
3. In `UpdateMenu()`, set the pause count to 7 in full game and 8 in Trial, and populate the T360 label at index 6 or 7 respectively.
4. Extend pause-menu layout/visibility handling so the new coordinate and item are enabled in both modes without disturbing Trial Marketplace.
5. Add a new action branch for `selectedMenu == loc.s 9 + 5`; preserve the existing `loc.s 9 + 4` Exit action.
6. Validate branch targets, stack consistency, max stack, exception regions, and metadata with an IL-aware writer before emitting any modified assembly.

## Safety gates

- Never overwrite the supplied base package or the user's installed game.
- Produce a separate patched managed assembly and package copy.
- Require metadata/IL validation and binary hash comparison before calling the artifact usable.
- No XEX rebuild, runtime rendering, or console test is claimed by this document.
