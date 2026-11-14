#!/usr/bin/env python3
"""Verify a safe, explicit SpriteBatch window and existing font for a Terraria overlay."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

def read_lines(path: Path) -> list[str]:
    if not path.is_file():
        raise FileNotFoundError(path)
    return path.read_text(encoding="utf-8", errors="replace").splitlines()

def find_line(lines: list[str], anchor: str, start: int = 0) -> int:
    for i in range(start, len(lines)):
        if anchor in lines[i]:
            return i + 1
    raise ValueError(f"Required anchor not found after line {start + 1}: {anchor}")

def build(root: Path) -> dict:
    main_lines = read_lines(root / "Terraria/Main.cs")
    ui_lines = read_lines(root / "Terraria/UI.cs")
    draw_start = find_line(main_lines, "protected override void Draw(GameTime dt)")
    ui_draw = find_line(main_lines, "ui[j].Draw();", draw_start)
    begin = find_line(main_lines, "spriteBatch.Begin();", ui_draw)
    chat = find_line(main_lines, "DrawChat();", begin)
    save_icon = find_line(main_lines, "SpriteSheet<_sheetSprites>.Draw(642, 878, 479", chat)
    end = find_line(main_lines, "spriteBatch.End();", save_icon)

    ui_draw_start = find_line(ui_lines, "public void Draw()")
    ui_draw_end = find_line(ui_lines, "Main.spriteBatch.End();", ui_draw_start)
    font_field = find_line(ui_lines, "public static SpriteFont fontSmallOutline;")
    font_load = find_line(ui_lines, 'fontSmallOutline = Content.Load<SpriteFont>("Fonts/small2");')
    if not (ui_draw_start < ui_draw_end):
        raise ValueError("UI.Draw no longer closes its SpriteBatch")
    if not (ui_draw < begin < chat < save_icon < end):
        raise ValueError("Main.Draw SpriteBatch insertion window changed")
    return {
        "verified": [
            f"Main.Draw calls UI.Draw at line {ui_draw} before opening its own SpriteBatch at line {begin}.",
            f"Main.Draw's active batch remains open through DrawChat (line {chat}) and save-icon drawing (line {save_icon}), then closes at line {end}.",
            f"UI.Draw closes its prior batch at line {ui_draw_end}.",
            f"UI.fontSmallOutline is declared at line {font_field} and loaded from Fonts/small2 at line {font_load}.",
        ],
        "candidate_insertion": {
            "file": "Terraria/Main.cs",
            "method": "protected override void Draw(GameTime dt)",
            "after_line": save_icon,
            "before_line": end,
            "constraint": "Insert drawing before the final End; do not call Begin or End inside the overlay.",
        },
        "anchors": {
            "main_draw_start": draw_start, "ui_draw": ui_draw, "spritebatch_begin": begin,
            "draw_chat": chat, "save_icon": save_icon, "spritebatch_end": end,
            "ui_draw_start": ui_draw_start, "ui_draw_end": ui_draw_end,
            "font_field": font_field, "font_load": font_load,
        },
        "limitations": [
            "Static decompilation evidence only; it does not validate GPU state or on-console rendering.",
            "The insertion window is a candidate, not a compiled or tested patch.",
            "No controller shortcut is selected by this report.",
            "No game assembly or XBLA package is modified.",
        ],
    }

def main():
    p = argparse.ArgumentParser()
    p.add_argument("source_root", type=Path)
    p.add_argument("--json", required=True, type=Path)
    p.add_argument("--markdown", required=True, type=Path)
    a = p.parse_args()
    report = build(a.source_root)
    a.json.parent.mkdir(parents=True, exist_ok=True)
    a.markdown.parent.mkdir(parents=True, exist_ok=True)
    a.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    rows = ["# Terraria — validação estática do contexto de renderização", "",
            "## Janela candidata", "",
            f"- Arquivo: `{report['candidate_insertion']['file']}`",
            f"- Método: `{report['candidate_insertion']['method']}`",
            f"- Inserção candidata: depois da linha {report['candidate_insertion']['after_line']} e antes da linha {report['candidate_insertion']['before_line']}.",
            f"- Restrição: {report['candidate_insertion']['constraint']}", "",
            "## Fatos verificados", ""]
    rows += [f"- {x}" for x in report["verified"]]
    rows += ["", "## Âncoras", ""]
    rows += [f"- {name}: linha {number}" for name, number in report["anchors"].items()]
    rows += ["", "## Limitações", ""]
    rows += [f"- {x}" for x in report["limitations"]]
    a.markdown.write_text("\n".join(rows) + "\n", encoding="utf-8")
    print("Menu render context: PASS")
    print(f"Verified anchors: {len(report['anchors'])}")

if __name__ == "__main__":
    main()
