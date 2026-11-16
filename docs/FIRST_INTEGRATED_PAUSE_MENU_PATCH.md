# First integrated T360 pause-menu patch

Status: **separate-copy patch and static IL validation passed**.

## Workflow

- Run: https://github.com/davizim140/t360modloader/actions/runs/43974917355
- Artifact: `terraria-game-exe-analysis`
- Artifact ID: `14280871371`
- Patched managed assembly: `Terraria.Managed.t360menu.dll`
- SHA-256: `4e876e03912aedb1eaf3d216b63e64c1d3cae8d97b7bda9a3060642e2e2716b9`

## Applied changes

1. Pause `uiHeight`: 7 -> 8.
2. Trial pause item count: 7 -> 8.
3. Full-game pause item count: 6 -> 7.
4. Full-game coordinate slot 6 enabled.
5. `T360 ModLoader` label inserted at relative item index 5 (`loc9 + 5)).
6. Selecting it opens the existing `STATUS_SCREEN` page with T360 text.
7. B calls `PrevMenu(-1)` and returns to the pause menu.
8. Existing Exit branch remains in its original logical path.

The coordinate array was previously extended from seven to eight entries in the separate `pausecoords` DLL. Slot 7 uses Y=497, which remains a geometric estimate until actual Xbox rendering is tested.

## Validation

- Input `Terraria.Managed.pausecoords.dll` SHA-256 unchanged: `eeefb6dd531894b8b7d7d5ecf5449dd84420ba486f575a2b2e8b149a687b5e42`.
- Output DLL reopened successfully.
- Module identity preserved.
- Branch and exception-handler targets valid.
- Independent IL stack-depth audit: 1,338 methods analyzed, zero failures.
- GitHub Actions workflow completed successfully.

## Still not done

- This is a landing page, not a .tmod file browser, package verifier, or runtime mod loader.
- No rebuilt `game.exe.xex` exists.
- No rebuilt STFS package or deployable test folder exists.
- No Xbox 360 runtime test has been performed.
- Do not replace the original game files with this DLL.
