# First Xbox 360 test gate

The project is not ready to install on the console yet.

## Required build artifacts before the first console test

1. A separate patched managed assembly containing the complete pause-menu entry (label, counts, correct full-game/Trial coordinate visibility, navigation height, and selection action).
2. A rebuilt `game.exe.xex` with that managed assembly embedded/replaced, validated by XEX inspection.
3. A test STFS package or equivalent deployable game layout that preserves the original title files and can be restored.
4. Hashes and a manifest proving the original package and source assembly were not overwritten.

## Console test method

- Keep the original Terraria installation untouched.
- Deploy only a separate test copy to USB/external storage.
- Launch the test copy from Aurora.
- First test: boot to title screen, open pause menu, verify all original entries and T360 entry, navigate with controller, open the new entry, and confirm Exit still behaves normally.
- If the game fails to boot or the menu behaves incorrectly, remove the test copy and return to the original installation.

## Current state

- Original managed assembly inventory and separate-copy round-trip passed.
- Pause coordinate array extension passed in a separate DLL copy.
- Integrated T360 pause-menu patch now passes: pause height 8, Trial count 8, full-game count 7, label at loc9+5, selection opens STATUS_SCREEN, and B returns through PrevMenu.
- Independent IL stack-depth audit passed for all 1,338 methods with zero failures.
- Branch and exception-handler targets validated; patched DLL reopened; source DLL hash unchanged.
- The T360 screen is currently a landing page, not yet a .tmod browser or loader.
- No rebuilt `game.exe.xex` or deployable STFS test package exists.
- No Xbox 360 runtime test has been performed.

The console test begins only after all four build-artifact gates above pass. No test is performed against the user's original installation.
