#!/usr/bin/env python3
"""Audit exact pause-menu IL branch/index constraints before writing a patcher."""
import argparse, json, re
from pathlib import Path

def method_body(il, name):
    # Find the method terminator first: calls to the same method may occur earlier.
    end_marker = "} // end of method UI::" + name
    end = il.find(end_marker)
    if end < 0: raise ValueError("method end for " + name + " not found")
    start = il.rfind(".method", 0, end)
    if start < 0: raise ValueError("method declaration for " + name + " not found")
    body = il[start:end]
    if not re.search(r"\\b" + re.escape(name) + r"\\s*\\(", body):
        raise ValueError("method declaration does not match " + name)
    return body

def main():
    p=argparse.ArgumentParser()
    p.add_argument("il",type=Path)
    p.add_argument("--json",required=True,type=Path)
    p.add_argument("--markdown",required=True,type=Path)
    a=p.parse_args()
    il=a.il.read_text(encoding="utf-8",errors="replace")
    update=method_body(il,"UpdateMenu")
    setmenu=method_body(il,"SetMenu")
    cctor_start=il.find(".method private hidebysig static")
    cctor_end=il.find("} // end of method UI::.cctor",cctor_start)
    cctor=il[cctor_start:cctor_end] if cctor_start>=0 and cctor_end>=0 else ""
    pause_init=re.search(r"ldc\.i4\.7\s+newarr Terraria\.Location(?P<body>.*?)stsfld valuetype Terraria\.Location\[\] Terraria\.UI::MENU_PAUSE_COORDS",cctor,re.S)
    if not pause_init: raise SystemExit("Could not locate pause coordinate initializer")
    ys=[int(m.group(1)) for m in re.finditer(r"ldc\.i4\s+480\s+ldc\.i4\s+(\d+)\s+newobj instance void Terraria\.Location::.ctor",pause_init.group("body"))]
    pause_set=re.search(r"ldc\.i4\.7\s+stfld int8 Terraria\.UI::uiHeight\s+ldarg\.0\s+ldsfld valuetype Terraria\.Location\[\] Terraria\.UI::MENU_PAUSE_COORDS",setmenu)
    trial_count=bool(re.search(r"ldc\.i4\.7\s+stfld uint8 Terraria\.UI::numMenuItems",update))
    full_count=bool(re.search(r"ldc\.i4\.6\s+stfld uint8 Terraria\.UI::numMenuItems",update))
    selected6=bool(re.search(r"ldarg\.0\s+ldfld int8 Terraria\.UI::selectedMenu\s+ldc\.i4\.6\s+bne\.un\.s",update))
    selected5=bool(re.search(r"ldarg\.0\s+ldfld int8 Terraria\.UI::selectedMenu\s+ldc\.i4\.5\s+bne\.un\.s",update))
    constraints=[
      "Extend MENU_PAUSE_COORDS to 8 entries and update the static initializer.",
      "Set pause uiHeight to 8 in SetMenu only.",
      "Use numMenuItems 7 for full game and 8 for Trial.",
      "Populate item 6 in full game and item 7 in Trial without shifting existing entries.",
      "At selectedMenu index 6, branch on Main.isTrial: preserve Exit in Trial and open T360 in full game.",
      "At selectedMenu index 7, open T360 only in Trial.",
      "Audit controller navigation, layout, rendering, and stack/branch integrity before output."
    ]
    report={"status":"baseline-il-audited","pause_coordinate_count":len(ys),"pause_coordinate_y":ys,
      "setmenu_pause_height_is_7":bool(pause_set),"trial_pause_count_is_7":trial_count,
      "full_pause_count_is_6":full_count,"has_selected_index_5_branch":selected5,
      "has_selected_index_6_branch":selected6,
      "index_conflict":{"full_game_new_item_index":6,"trial_existing_exit_index":6,"trial_new_item_index":7,
       "required_logic":"Index 6 is Exit in Trial but the appended T360 item in full game; branch on Main.isTrial."},
      "patch_constraints":constraints,
      "limitations":["Static IL inspection only.","No IL bytes changed; no assembly or XEX rebuilt.",
       "Coordinate Y=497 remains unverified in actual rendering."]}
    if not (len(ys)==7 and pause_set and trial_count and full_count and selected5 and selected6):
      raise SystemExit("Pause-menu baseline audit failed: "+json.dumps(report))
    a.json.parent.mkdir(parents=True,exist_ok=True)
    a.json.write_text(json.dumps(report,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    lines=["# Pause menu IL baseline audit","","PASS: current IL constraints captured.","",
      "- Pause coordinate count: "+str(len(ys)),
      "- Current Y coordinates: "+", ".join(map(str,ys)),
      "- Full game item count: 6 (indices 0–5)",
      "- Trial item count: 7 (indices 0–6)",
      "- Existing selected-index 6 branch: present","",
      "## Critical index collision","",
      "Index 6 is the existing Exit action in Trial, but is the first free appended index in the full game. The patch must branch on Main.isTrial at index 6. Index 7 is the new T360 entry in Trial.","",
      "## Patch constraints"]+["- "+x for x in constraints]+["","## Limitations",""]+["- "+x for x in report["limitations"]]
    a.markdown.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print("Pause menu IL baseline audit: PASS")
if __name__=="__main__": main()
