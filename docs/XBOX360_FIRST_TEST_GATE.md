# First Xbox 360 test gate

The modified game executable now exists as a separate XEX file. It has **not** been tested on the console and is not yet a deployable game package.

## Build artifacts

1. **Patched managed assembly:** complete. The pause-menu entry and landing page are integrated in `Terraria.Managed.t360menu.dll`.
2. **Rebuilt `game.exe.xex`:** complete. Managed DLL injected into a separate basefile, XEX rebuilt, page hashes recomputed and independently verified.
3. **Deployable test copy:** pending. The repository currently contains only the inner `game.exe.xex`, not the full Terraria XBLA package or its companion files.
4. **Integrity manifest:** complete for the source XEX, basefile, managed DLL, patched basefile and rebuilt XEX.

## Latest successful build

- GitHub Actions: https://github.com/davizim140/t360modloader/actions/runs/43976159733
- Artifact: `terraria-game-exe-analysis`
- Artifact ID: `14279079573`
- Rebuilt XEX: `game.exe.t360menu.xex`
- Rebuilt XEX SHA-256: `3b07d403b6314c63955422f82758dbd3b9fd91c2bc98f55c6632428f59fe8ba2`
- Patched managed DLL SHA-256: `4e876e03912aedb1eaf3d216b63e64c1d3cae8d97b7bda9a3060642e2e2716b9`
- Patched basefile SHA-256: `2a8d13384b3ea20a94523c5155dd3da24d448b677b8e2f6085ab60122a8bba5c`

## Verified results

- Embedded managed DLL was replaced at offset `0x30000` in a separate basefile copy.
- Original embedded span: 2,231,808 bytes; patched DLL: 2,227,712 bytes. Remaining bytes were zero-filled; outer basefile size stayed unchanged.
- Rebuilt XEX was extracted again; its embedded managed DLL matched the patched DLL byte-for-byte.
- Independent page-hash verification passed: 41 descriptors, 64 KiB pages, 2,686,976-byte image.
- Re-extracted rebuilt basefile matched the modified basefile byte-for-byte.
- Original source XEX hash remains `fe7dd83fa1518499477cf7faf31c1eb2ad5677d3253b0759e169c82ee3b079b6`.
- Independent IL stack-depth audit: 1,338 methods, zero failures.

## Important signature limitation

The input XEX is retail-signed. The rebuild recalculates the page-hash chain and header hash, but the output RSA signature is zeroed because the tool does not possess Microsoft's retail signing key. This output is **not** a retail-signed executable. RGH acceptance has not been verified. Do not replace the original installation or treat this file as console-safe yet.

## What is still needed before the console test

To create a separate deployable game folder or rebuilt STFS package, provide either:

- the original Terraria XBLA/STFS package; or
- the complete extracted game folder, including `default.xex`, `RuntimeHost.xex`, `HostLoader.dll`, and the other original files.

The test copy will keep the original installation untouched and will be launched from Aurora on USB/external storage only after the package/layout and XEX signature behavior are assessed.

## Console test plan

On a separate test copy:
1. Launch Terraria from Aurora.
2. Confirm the title screen loads.
3. Open pause and confirm Resume, Options, and Exit remain available alongside T360 ModLoader.
4. Open T360 ModLoader and confirm B returns to pause.
5. Confirm Exit still works.
6. If boot or UI fails, remove only the test copy.

No Xbox 360 runtime test has been performed yet.
