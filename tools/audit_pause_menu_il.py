#!/usr/bin/env python3
"""Audit Terraria pause-menu IL and controller/render integration before patching."""
import argparse
import json
import re
from pathlib import Path

def method_body(il, name):
    marker = "} // end of method UI::" + name
    end = il.find(marker)
    if end < 0:
        raise ValueError("method end not found: " + name)
    start = il.rfind(".method", 0, end)
    if start < 0:
        raise ValueError("method declaration not found: " + name)
    body = il[start:end]
    if name not in body[:500]:
        raise ValueError("method declaration mismatch: " + name)
    return body

def instructions(body):
    result = []
    for line in body.splitlines():
        match = re.match(r"\s*IL_[0-9A-Fa-f]+:\s*(.*)", line)
        if match:
            result.append(match.group(1).strip())
    return result

def has_sequence(items, sequence):
    size = len(sequence)
    return any(items[i:i + size] == sequence for i in range(len(items) - size + 1))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("il", type=Path)
    parser.add_argument("--json", required=True, type=Path)
    parser.add_argument("--markdown", required=True, type=Path)
    args = parser.parse_args()

    il = args.il.read_text(encoding="utf-8", errors="replace")
    update = instructions(method_body(il, "UpdateMenu"))
    setmenu = instructions(method_body(il, "SetMenu"))
    mouse = instructions(method_body(il, "UpdateMouse"))
    draw = instructions(method_body(il, "DrawMenu"))
    cctor = instructions(method_body(il, ".cctor"))

    pause_store = next((i for i, x in enumerate(cctor)
                         if x == "stsfld valuetype Terraria.Location[] Terraria.UI::MENU_PAUSE_COORDS"), -1)
    if pause_store < 0:
        raise SystemExit("Pause coordinate field assignment not found")
    array_new = max((i for i in range(pause_store)
                     if cctor[i] == "newarr Terraria.Location"), default=-1)
    if array_new < 0:
        raise SystemExit("Pause coordinate array allocation not found")
    array_size = max((i for i in range(array_new)
                      if cctor[i] == "ldc.i4.7"), default=-1)
    if array_size < 0:
        raise SystemExit("Pause coordinate array length instruction not found")
    pause_init = cctor[array_size:pause_store + 1]
    ys = []
    for i, instruction in enumerate(pause_init):
        if instruction == "ldc.i4 480" and i + 2 < len(pause_init):
            y_instruction = pause_init[i + 1]
            if y_instruction.startswith("ldc.i4 ") and pause_init[i + 2].startswith("newobj instance void Terraria.Location::.ctor"):
                ys.append(int(y_instruction.split()[-1]))

    pause_set = has_sequence(setmenu, [
        "ldc.i4.7",
        "stfld int8 Terraria.UI::uiHeight",
        "ldarg.0",
        "ldsfld valuetype Terraria.Location[] Terraria.UI::MENU_PAUSE_COORDS"
    ])
    trial_count = has_sequence(update, [
        "ldc.i4.7",
        "stfld uint8 Terraria.UI::numMenuItems",
        "ldsfld valuetype Terraria.Location[] Terraria.UI::MENU_PAUSE_COORDS"
    ])
    full_count = has_sequence(update, [
        "ldc.i4.6",
        "stfld uint8 Terraria.UI::numMenuItems",
        "ldsfld valuetype Terraria.Location[] Terraria.UI::MENU_PAUSE_COORDS"
    ])
    exit_branch = has_sequence(update, [
        "ldfld int8 Terraria.UI::selectedMenu",
        "ldloc.s 9",
        "ldc.i4.4",
        "add"
    ])
    mouse_uses_height = "ldfld int8 Terraria.UI::uiHeight" in mouse
    mouse_uses_coords = "ldfld valuetype Terraria.Location[] Terraria.UI::uiCoords" in mouse
    mouse_wraps_y = "stfld int16 Terraria.UI::uiY" in mouse
    draw_uses_count = "ldfld uint8 Terraria.UI::numMenuItems" in draw
    draw_uses_strings = "ldfld string[] Terraria.UI::menuString" in draw

    constraints = [
        "Extend MENU_PAUSE_COORDS from 7 to 8 entries and update only its static initializer.",
        "Set uiHeight to 8 only in the pause branch of SetMenu.",
        "Set numMenuItems to 7 in full game and 8 in Trial after adding T360.",
        "Add T360 at index 6 in full game and index 7 in Trial.",
        "Preserve the existing exit comparison selectedMenu == loc.s 9 + 4; append T360 at loc.s 9 + 5.",
        "Update the pause menu selection/layout logic without changing title-menu coordinates.",
        "Validate IL branches, stack, metadata, and the separate output assembly before packaging."
    ]
    report = {
        "status": "baseline-il-audited",
        "pause_coordinate_count": len(ys),
        "pause_coordinate_y": ys,
        "setmenu_pause_height_is_7": pause_set,
        "trial_pause_count_is_7": trial_count,
        "full_pause_count_is_6": full_count,
        "existing_exit_branch_uses_base_plus_4": exit_branch,
        "controller_navigation": {
            "reads_ui_height": mouse_uses_height,
            "reads_ui_coordinates": mouse_uses_coords,
            "updates_wrapped_ui_y": mouse_wraps_y
        },
        "menu_rendering": {
            "loops_using_num_menu_items": draw_uses_count,
            "reads_menu_strings": draw_uses_strings
        },
        "index_plan": {
            "full_game": {"existing_indices": "0-5", "t360_index": 6, "new_item_count": 7},
            "trial": {"existing_indices": "0-6", "t360_index": 7, "new_item_count": 8},
            "note": "The existing exit is selectedMenu == loc.s 9 + 4; loc.s 9 is 1 in full game and 2 in Trial."
        },
        "patch_constraints": constraints,
        "limitations": [
            "Static IL inspection only; no assembly bytes changed.",
            "No managed DLL or XEX rebuilt.",
            "Candidate Y=497 is not validated by runtime rendering."
        ]
    }
    checks = [
        len(ys) == 7,
        pause_set, trial_count, full_count, exit_branch,
        mouse_uses_height, mouse_uses_coords, mouse_wraps_y,
        draw_uses_count, draw_uses_strings
    ]
    if not all(checks):
        raise SystemExit("Pause-menu baseline audit failed: " + json.dumps(report))

    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    lines = [
        "# Pause menu IL baseline audit",
        "",
        "PASS: pause menu, controller navigation, and draw loop inspected.",
        "",
        "- Pause coordinates: " + ", ".join(map(str, ys)),
        "- Full game: 6 existing entries; T360 will be index 6 (7 total).",
        "- Trial: 7 existing entries; T360 will be index 7 (8 total).",
        "- Existing exit action uses selectedMenu == loc.s 9 + 4.",
        "- Controller navigation reads uiHeight and uiCoords, and updates uiY.",
        "- DrawMenu iterates numMenuItems and reads menuString.",
        "",
        "## Patch constraints",
        ""
    ] + ["- " + item for item in constraints] + [
        "",
        "## Limitations",
        ""
    ] + ["- " + item for item in report["limitations"]]
    args.markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("Pause menu IL baseline audit: PASS")

if __name__ == "__main__":
    main()
