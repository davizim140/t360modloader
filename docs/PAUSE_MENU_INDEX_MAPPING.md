# T360 pause-menu index mapping

## Current verified mapping

The pause-menu branch in `Terraria.UI::UpdateMenu()` uses a local base index (`num2`):

- Full game: `num2 = 1`; current item count is 6.
- Trial: `num2 = 2`; current item count is 7.

Existing entries are assigned relative to `num2`:

| Meaning | Full game | Trial |
|---|---:|---:|
| Resume | 0 | 0 |
| Marketplace | — | 1 |
| Options | 1 | 2 |
| Leaderboards | 2 | 3 |
| Achievements | 3 | 4 |
| Save | 4 | 5 |
| Exit | 5 | 6 |
| T360 ModLoader (planned) | 6 | 7 |

The existing Exit handler is `selectedMenu == num2 + 4`. It must remain unchanged. The new selection must be handled separately at `num2 + 5`.

## Important coordinate detail

The current full-game path hides coordinate slot 6 (`MENU_PAUSE_COORDS[6].X = 0`) because the full-game Exit is at index 5. The Trial path uses coordinate slot 6 for its existing Exit.

Therefore, the new option cannot simply use the same coordinate slot in both editions:

- Full game: planned new item index 6 can reuse coordinate slot 6, but that slot must be made visible only in the full-game branch.
- Trial: planned new item index 7 requires the newly appended coordinate slot 7.
- The appended slot 7 currently has candidate position (480, 497), which is geometric only and not validated in game.

The array extension and the unique pause-only `uiHeight = 7` target have passed static audits. The label anchor audit also found one existing five-label assignment sequence and no current assignment for relative index 5. These audits do not prove visual layout or runtime behavior.

## Safe implementation order

1. Add the label at `menuString[num2 + 5]` and increase pause item counts to 7/8.
2. Make coordinate visibility correct for full game and Trial without changing existing Exit coordinates.
3. Add the new selection branch after the existing Exit branch, preserving Exit's current `num2 + 4` behavior.
4. Set pause `uiHeight` to 8 only after all eight-item data and action paths exist.
5. Reopen the separate patched DLL, validate branch and exception-handler targets, compare metadata, and run the independent stack-depth audit.

## Safety status

- Original Terraria assembly remains untouched.
- No new label, item count, visibility, selection branch, or height change has been applied.
- No installable managed DLL or XEX has been produced.
- No Xbox 360 runtime test has been performed.
