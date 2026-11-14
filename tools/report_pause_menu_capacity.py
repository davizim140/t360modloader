#!/usr/bin/env python3
"""Verify capacity and a non-destructive extension plan for Terraria's pause menu."""
from __future__ import annotations
import argparse
import json
import re
from pathlib import Path

def one(pattern: str, text: str, label: str) -> re.Match[str]:
    match = re.search(pattern, text, re.S)
    if not match:
        raise ValueError(f"Required source structure not found: {label}")
    return match

def build(source_root: Path) -> dict:
    source = (source_root / "Terraria/UI.cs").read_text(encoding="utf-8", errors="replace")
    pause_h = int(one(r"MENU_PAUSE_H\s*=\s*(\d+)\s*;", source, "MENU_PAUSE_H").group(1))
    coords_match = one(
        r"MENU_PAUSE_COORDS\s*=\s*new Location\[(\d+)\]\s*\{(.*?)\n\s*\};",
        source, "MENU_PAUSE_COORDS"
    )
    coords_capacity = int(coords_match.group(1))
    ys = [int(v) for v in re.findall(r"new Location\(480,\s*(\d+)\)", coords_match.group(2))]
    arrays = {}
    for name, typ in (("menuString", "string"), ("menuScale", "float"), ("menuHC", "byte")):
        arrays[name] = int(one(
            rf"\b{name}\s*=\s*new {typ}\[(\d+)\]", source, name
        ).group(1))
    pause_case = one(r"case MenuMode\.PAUSE\s*:(.*?)(?:case MenuMode\.|$)", source, "PAUSE switch case").group(1)
    if not re.search(r"uiHeight\s*=\s*7\s*;", pause_case) or "uiCoords = MENU_PAUSE_COORDS;" not in pause_case:
        raise ValueError("Pause menu geometry setup changed")
    if pause_h != 7 or coords_capacity != 7 or len(ys) != 7:
        raise ValueError(f"Unexpected pause geometry: const={pause_h}, array={coords_capacity}, points={len(ys)}")
    if min(arrays.values()) < 8 or len(set(ys)) != len(ys):
        raise ValueError(f"Backing arrays or coordinates are incompatible: {arrays}, {ys}")
    indices = {"full_game": 6, "trial": 7}
    if max(indices.values()) >= min(arrays.values()):
        raise ValueError("Proposed appended index exceeds a backing array")
    candidate_y = 497
    if candidate_y in ys or not 0 <= candidate_y < 540:
        raise ValueError("Candidate pause-menu coordinate is invalid")
    return {
        "status": "static-capacity-pass",
        "source": "Terraria/UI.cs",
        "current": {
            "pause_height_constant": pause_h, "pause_coordinates_capacity": coords_capacity,
            "pause_coordinate_y": ys, "pause_menu_height": 7, "backing_array_capacities": arrays,
            "active_items_full_game": 6, "active_items_trial": 7
        },
        "extension_candidate": {
            "new_pause_height_constant": 8, "new_pause_coordinates_capacity": 8,
            "new_pause_menu_height": 8, "new_active_items_full_game": 7,
            "new_active_items_trial": 8, "append_expression": "num2 + 5",
            "new_indices": indices, "candidate_coordinate": {"x": 480, "y": candidate_y},
            "required_follow_up": [
                "Add the eighth MENU_PAUSE_COORDS entry and set MENU_PAUSE_H/uiHeight to 8.",
                "Increase numMenuItems from 6/7 to 7/8 only in the pause-menu branch.",
                "Populate menuString[num2 + 5] with the T360 ModLoader label.",
                "Handle selectedMenu == num2 + 5 before leaving the pause-menu branch.",
                "Keep all existing indices and original actions unchanged."
            ]
        },
        "limitations": [
            "This is a source-capacity audit, not a patch to Terraria.Managed.dll.",
            "The candidate Y coordinate is geometric only; visual clipping and focus navigation remain untested.",
            "No XNA/XDK rebuild or console execution is claimed."
        ]
    }

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("source_root", type=Path)
    p.add_argument("--json", required=True, type=Path)
    p.add_argument("--markdown", required=True, type=Path)
    a = p.parse_args()
    report = build(a.source_root)
    a.json.parent.mkdir(parents=True, exist_ok=True)
    a.markdown.parent.mkdir(parents=True, exist_ok=True)
    a.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    cur, ext = report["current"], report["extension_candidate"]
    lines = [
        "# Terraria — auditoria de capacidade do menu de pausa", "",
        "## Estado atual", "",
        f"- Altura/constante: {cur['pause_height_constant']}",
        f"- Coordenadas: {cur['pause_coordinates_capacity']} ({cur['pause_coordinate_y']})",
        f"- Arrays de suporte: {cur['backing_array_capacities']}",
        f"- Itens ativos: jogo completo={cur['active_items_full_game']}; Trial={cur['active_items_trial']}", "",
        "## Extensão candidata (não aplicada)", "",
        f"- Altura e coordenadas propostas: {ext['new_pause_menu_height']}",
        f"- Índices novos: {ext['new_indices']}",
        f"- Posição geométrica candidata: {ext['candidate_coordinate']}",
        "- Expressão de índice: num2 + 5", "", "### Alterações necessárias", ""
    ]
    lines += [f"- {x}" for x in ext["required_follow_up"]]
    lines += ["", "## Limitações", ""]
    lines += [f"- {x}" for x in report["limitations"]]
    a.markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("Pause menu capacity audit: PASS")
    print("Backing arrays: " + ", ".join(f"{k}={v}" for k, v in cur["backing_array_capacities"].items()))
    print(f"Proposed appended indices: {ext['new_indices']}")

if __name__ == "__main__":
    main()
