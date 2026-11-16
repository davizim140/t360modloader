# First rebuilt Terraria game.exe.xex

Status: **rebuild and independent integrity checks passed; console boot not tested**.

## Workflow artifact

- Workflow: https://github.com/davizim140/t360modloader/actions/runs/43976159733
- Artifact name: `terraria-game-exe-analysis`
- Artifact ID: `14279079573`
- Output file: `game.exe.t360menu.xex`
- Size: 2,592,768 bytes
- SHA-256: `3b07d403b6314c63955422f82758dbd3b9fd91c2bc98f55c6632428f59fe8ba2`

## Embedded managed assembly

- Patched file: `Terraria.Managed.t360menu.dll`
- SHA-256: `4e876e03912aedb1eaf3d216b63e64c1d3cae8d97b7bda9a3060642e2e2716b9`
- Injected offset in the XEX basefile: `0x30000`
- Original embedded span: 2,231,808 bytes
- Patched DLL size: 2,227,712 bytes
- The remaining 4,096 bytes in that reserved span were zero-filled.
- The outer basefile remained 2,686,976 bytes.

## Validation results

- Managed DLL injection report: `managed-assembly-injection-pass`.
- Rebuilt XEX was extracted again and its embedded managed DLL matched the patched DLL byte-for-byte.
- Independent page-hash chain check: `page-hash-chain-pass`.
- 41 page descriptors, 64 KiB page size, 2,686,976-byte basefile.
- Re-extracted rebuilt basefile matched the injected basefile byte-for-byte.
- The separate source XEX remained unchanged (SHA-256: `fe7dd83fa1518499477cf7faf31c1eb2ad5677d3253b0759e169c82ee3b079b6`).
- The managed IL stack-depth audit passed all 1,338 methods with zero failures.

## Signature and deployment limitations

The source XEX is retail-signed. The rebuild recalculated the page-hash chain and header hash, but zeroed the RSA signature because the retail private signing key is unavailable. This is not a retail-signed XEX, and its acceptance by the user's RGH kernel has not been tested.

This artifact is only the inner `game.exe.xex`. It is not a complete STFS package or complete deployable game folder. The repo currently does not contain `default.xex`, `RuntimeHost.xex`, `HostLoader.dll`, or all other original title files.

Do not replace the original installation with this file. The next gate is to obtain the full original package or extracted game folder, create a separate USB test copy, and assess XEX loading on RGH without touching the installed original.
