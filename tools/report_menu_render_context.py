#!/usr/bin/env python3
"""Verify a safe, explicit SpriteBatch window and existing font for a Terraria overlay."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

def require(path: Path, anchors: list[str]) -> list[dict]:
    if not path.is_file():
        raise FileNotFoundError(path)
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    hits = []
    for anchor in anchors:
        found = [i for i, line in enumerate(lines) if anchor in line]
        if not found:
            raise ValueError(f"{path}: required anchor not found: {anchor}")
        hits.extend({"anchor": anchor, "line": i + 1, "text": lines[i].strip()} for i in found)
    return hits

def build(root: Path) -> dict:
    main = root / "Terraria/Main.cs"
    ui = root / "Terraria/UI.cs"
    main_hits = require(main, [
        "protected override void Draw(GameTime dt)",
        "ui[j].Draw();",
        "spriteBatch.Begin();",
        "DrawChat();",
        "spriteBatch.End();",
    ])
    ui_hits = require(ui, [
        "public void Draw()",
        "Main.spriteBatch.End();",
        "public static SpriteFont fontSmallOutline;",
        'fontSmallOutline = Content.Load<SpriteFont>("Fonts/small2");',
    ])
    def line(hits, anchor):
        return next(x["line"] for x in hits if x["anchor"] == anchor)
    begin, chat, end = (line(main_hits, a) for a in ("spriteBatch.Begin();", "DrawChat();", "spriteBatch.End();"))
    if not begin < chat < end:
        raise ValueError("Main.Draw SpriteBatch Begin/DrawChat/End ordering changed")
    if line(main_hits, "ui[j].Draw();") >= begin:
        raise ValueError("UI.Draw must finish before the dedicated Main.Draw SpriteBatch begins")
    return {
        "main_draw": main_hits,
        "ui_font_and_draw": ui_hits,
        "verified": [
            "Main.Draw calls UI.Draw before opening a separate SpriteBatch.",
            "Main.Draw has a verified active SpriteBatch window: Begin -> DrawChat -> End.",
            "UI.fontSmallOutline exists and is loaded from the game's existing Fonts/small2 content asset.",
            "A T360 overlay can be investigated inside Main.Draw's active batch without adding a new XNA assembly reference.",
        ],
        "candidate_insertion": {
            "file": "Terraria/Main.cs",
            "method": "protected override void Draw(GameTime dt)",
            "after_line": begin,
            "before_line": end,
            "constraint": "The final patch must preserve the original Begin/End pairing and must not call Begin/End from inside the inserted overlay.",
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
    rows += ["", "## Âncoras de Main.Draw", ""]
    rows += [f"- L{x['line']}: `{x['text']}`" for x in report["main_draw"]]
    rows += ["", "## Âncoras de UI/fontes", ""]
    rows += [f"- L{x['line']}: `{x['text']}`" for x in report["ui_font_and_draw"]]
    rows += ["", "## Limitações", ""]
    rows += [f"- {x}" for x in report["limitations"]]
    a.markdown.write_text("\n".join(rows) + "\n", encoding="utf-8")
    print("Menu render context: PASS")
    print(f"Main.Draw anchors: {len(report['main_draw'])}; UI/font anchors: {len(report['ui_font_and_draw'])}")

if __name__ == "__main__":
    main()
