# Main menu insertion audit

Status: static audit passed; no main-menu patch has been applied yet.

## Verified source behavior

The inspected method is `Terraria.UI.UpdateMenu()`, inside the `MenuMode.TITLE` branch.

The original full-game title menu has six entries (indices 0-5):
1. Character Select
2. Tutorial
3. Leaderboards
4. Achievements
5. Options
6. Quit

The Trial title menu has seven entries (indices 0-6):
1. Character Select
2. Tutorial
3. Leaderboards
4. Achievements
5. Options
6. Marketplace
7. Quit

The static coordinate array `UI.MENU_TITLE_COORDS` has eight elements. The title-menu branch already uses index 6 for Trial Quit and disables index 6 in the full game. Index 7 is not used by the title-menu entries in the inspected source.

The method uses `numMenuItems` to bound menu scaling and selection. It assigns menu labels in `menuString`, and handles selected indices 0 through 6 in the same title-menu branch.

## Proposed insertion

Preserve the existing Trial Marketplace entry and put T360 ModLoader immediately before Quit:

- Full game: index 5 becomes T360 ModLoader; move Quit to index 6; count becomes 7.
- Trial: index 5 remains Marketplace; index 6 becomes T360 ModLoader; move Quit to index 7; count becomes 8.
- Use coordinate slots 5/6 in the full game and 6/7 in Trial, keeping the existing visibility rules for earlier entries.
- Preserve all existing actions and add a dedicated selection path for T360 ModLoader.

## Navigation destination

The current first-stage implementation can route the new entry to the existing `MenuMode.STATUS_SCREEN` as a temporary landing page. This is not yet a functional .tmod manager. A dedicated T360 screen and file browser are separate implementation work.

## Verification

GitHub Actions run [44107134017](https://github.com/davizim140/t360modloader/actions/runs/44107134017) completed successfully.

The read-only Mono.Cecil audit reported:
- `Terraria.UI::UpdateMenu()` inspected
- 126 relevant field-reference instruction windows captured
- 35 local variables
- source SHA-256 unchanged during the audit

No main-menu IL was modified by this audit. The existing rebuilt XEX from the workflow remains the earlier pause-menu build, not a main-menu build. It has not been tested on the Xbox 360.
