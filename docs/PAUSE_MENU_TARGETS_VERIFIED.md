# Exact Terraria.UI pause-menu targets

Source: read-only Mono.Cecil target inventory from GitHub Actions run
[43766648307](https://github.com/davizim140/t360modloader/actions/runs/43766648307).

## Verification result

- Status: `pause-menu-targets-found`
- Assembly: `game, Version=1.0.4.1`
- Module: `game.exe`
- MVID: `d0f84b30-d7a0-41d8-8306-c72bb0d9d9cf`
- All eight required fields were found exactly once.
- All five target methods were found exactly once and contain method bodies.
- This is a read-only inventory. It did not modify or rewrite the original DLL.

## Exact fields

| Field | Type |
|---|---|
| `MENU_PAUSE_COORDS` | `Terraria.Location[]` |
| `uiCoords` | `Terraria.Location[]` |
| `uiHeight` | `System.SByte` |
| `menuString` | `System.String[]` |
| `numMenuItems` | `System.Byte` |
| `selectedMenu` | `System.SByte` |
| `menuHC` | `System.Byte[]` |
| `menuScale` | `System.Single[]` |

## Exact method bodies

| Method | IL instructions | Code size | EH regions | MaxStack |
|---|---:|---:|---:|---:|
| `.cctor` | 334 | 1274 | 0 | 4 |
| `SetMenu(MenuMode, Boolean, Boolean)` | 343 | 1064 | 0 | 7 |
| `UpdateMenu()` | 2890 | 7867 | 7 | 7 |
| `DrawMenu()` | 810 | 2414 | 0 | 10 |
| `UpdateMouse()` | 614 | 1548 | 0 | 5 |

## Confirmed pause-coordinate initializer

In `Terraria.UI::.cctor()`:

- IL 126: `ldc.i4.7`
- IL 127: `newarr Terraria.Location`
- IL 131–177: initializes indices 0 through 6.
- IL 179 (offset `0x02b2`): stores the array in `MENU_PAUSE_COORDS`.
- Existing Y positions: 221, 259, 297, 334, 372, 421, 459 (all X=480).

The array currently has seven entries. Any eighth coordinate requires a deliberate array-size and initializer edit; merely writing index 7 would be out of bounds.

## Confirmed pause branch in SetMenu

In `Terraria.UI::SetMenu(...)`:

- IL 185–186: assigns `uiHeight = 7`.
- IL 188–189: assigns `uiCoords = MENU_PAUSE_COORDS`.
- This is the pause-coordinate branch.
- The immediately following title-menu branch independently assigns `uiHeight = 7` and `MENU_TITLE_COORDS`; it must not be changed as part of the pause edit.

## Confirmed dynamic pause-menu logic in UpdateMenu

Relevant IL anchors:

- IL 771–780: accesses `MENU_PAUSE_COORDS[local9]` and updates X / `menuHC`.
- IL 805: assigns `numMenuItems = 7` on one branch.
- IL 806–810: makes pause coordinate index 6 visible at X=480.
- IL 814: assigns `numMenuItems = 6` on the alternate branch.
- IL 815–819: hides pause coordinate index 6 by setting X=0.
- The branch after IL 820 checks `HasOnline()`; additional pause-menu layout behavior follows.

This confirms that the pause menu already has separate online/offline layout paths. A future added item must account for both paths rather than simply changing a single item count.

## Rendering and input

- `DrawMenu()` draws menu entries in a loop bounded by `numMenuItems` (IL 719–726).
- `UpdateMouse()` reads `uiHeight` at multiple points (IL 524, 551, 593), confirming that the height/coordinate mapping affects mouse/controller navigation.
- The inventory confirms the fields and method bodies, not runtime behavior on an Xbox 360.

## Next safe stage

Build a dry-run patch planner against a separate copy. It must:
1. Match the exact method signatures and IL anchor sequences above, not only method names.
2. Require unique anchor matches and abort on any mismatch.
3. Describe proposed edits without writing a patched DLL.
4. Preserve the original source SHA-256 and emit a machine-readable plan.
5. Account for both online/offline branches and the existing full-game/trial exit positions.

No Terraria IL patch or replacement XEX is produced by this audit.