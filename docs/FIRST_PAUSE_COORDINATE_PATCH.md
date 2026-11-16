# First pause-coordinate IL patch: verified separate copy

Workflow: [Terraria XEX static analysis #43857199194](https://github.com/davizim140/t360modloader/actions/runs/43857199194)

## Result

- Status: `pause-coordinate-copy-patch-pass`
- Source assembly: `Terraria.Managed.roundtrip.dll`
- Patched copy: `Terraria.Managed.pausecoords.dll`
- Original source SHA-256 before and after: unchanged
- Assembly identity and MVID: preserved
- Terraria.UI method count: preserved
- Patched output reopened successfully with Mono.Cecil
- Branch and exception-handler instruction targets: valid
- Independent stack-depth audit: `maxstack-audit-pass`, 1,338 methods, 0 failures

## Only change made

`Terraria.UI::MENU_PAUSE_COORDS` was extended from 7 to 8 entries.

| Index | X | Y | Status |
|---:|---:|---:|---|
| 0 | 480 | 221 | Preserved |
| 1 | 480 | 259 | Preserved |
| 2 | 480 | 297 | Preserved |
| 3 | 480 | 334 | Preserved |
| 4 | 480 | 372 | Preserved |
| 5 | 480 | 421 | Preserved |
| 6 | 480 | 459 | Preserved |
| 7 | 480 | 497 | Added |

The new Y coordinate (497) is a geometric estimate only. It has not been validated in-game.

## What was not changed

- `SetMenu` / `uiHeight`
- Online or offline `numMenuItems`
- Menu labels or rendering
- Controller/mouse navigation
- Selection logic or Exit branch
- Original DLL or any XEX

## Artifact

The workflow artifact `terraria-game-exe-analysis` contains the patched DLL and its machine-readable report. This is a research artifact, not an installable Terraria package or XEX.

## Next stage

Before making the new slot visible, validate the Y position and UI spacing in a controlled runtime test. Then plan the separate `SetMenu` height edit and re-run the full managed assembly, branch/EH, and stack-depth checks. Do not install this DLL on the console yet.
