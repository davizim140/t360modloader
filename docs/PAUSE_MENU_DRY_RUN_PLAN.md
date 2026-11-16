# Pause-menu patch planner: dry-run verified

Workflow: [Terraria XEX static analysis #43853587559](https://github.com/davizim140/t360modloader/actions/runs/43853587559)

## Result

- Planner status: `dry-run-plan-ready`
- Mode: `read-only-dry-run`
- Source: `Terraria.Managed.roundtrip.dll`
- Source SHA-256: `f634ffe127d1e12272e9acca748d6059f0ca6bac0c3d5db30e9aedcf893603bd`
- Assembly: `game, Version=1.0.4.1`
- MVID: `d0f84b30-d7a0-41d8-8306-c72bb0d9d9cf`

## Anchors independently matched

| Check | Verified location |
|---|---|
| Unique pause coordinate array allocation | `Terraria.UI::.cctor`, IL index 127 |
| Seven existing coordinate initializers | indexes 0 through 6 |
| Unique pause branch in SetMenu | `SetMenu`, IL index 185 |
| Online pause item count | `UpdateMenu`, IL index 804, value 7 |
| Offline pause item count | `UpdateMenu`, IL index 813, value 6 |
| Online/offline visibility writes for pause slot 6 | `UpdateMenu`, indexes 807 and 816 |
| Menu drawing uses `numMenuItems` | `DrawMenu`, field reference found |
| Navigation reads `uiHeight` and `uiCoords` | `UpdateMouse`, 3 references to each |

The planner deliberately scopes the item-count checks to the following `MENU_PAUSE_COORDS` access. It rejects unrelated menu counts, including title and character-selection menus.

## Safety result

- Original source assembly was read only.
- Planner wrote only `pause-menu-patch-plan.json`.
- No patched DLL was produced.
- No XEX was produced.
- No Xbox 360 runtime test was performed.

## Next stage

Implement the first real IL edit only on a separate working copy: extend the pause coordinate array initializer. Before saving the copy, require a unique exact match and verify the added coordinate slot. Then reopen the output, compare all unrelated methods/metadata, check branch and exception-handler targets, and run the independent stack-depth audit. Do not modify `SetMenu`, item counts, labels, or selection/Exit branches in that first patch.
