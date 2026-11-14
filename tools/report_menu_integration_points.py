#!/usr/bin/env python3
"""Map verified Terraria UI/input insertion points from ILSpy C# output."""
from __future__ import annotations
import argparse
import json
from pathlib import Path

SPECS = [
    ("main_update", "Terraria/Main.cs", "protected override void Update(GameTime dt)", ["ui[i].UpdateGamePad();", "ui[j].Update();"]),
    ("ui_gamepad_sample", "Terraria/UI.cs", "public void UpdateGamePad()", ["gpPrevState = gpState;", "gpState = GamePad.GetState(controller);"]),
    ("ui_update", "Terraria/UI.cs", "public void Update()", ["if (menuType == MenuType.NONE)", "IsButtonUntriggered((Buttons)16)", "IsButtonTriggered((Buttons)32)", "ClearButtonTriggers();"]),
    ("ui_draw", "Terraria/UI.cs", "public void Draw()", ["DrawTopLayer();", "Main.spriteBatch.End();"]),
    ("ui_draw_top_layer", "Terraria/UI.cs", "private void DrawTopLayer()", ["if (menuType == MenuType.NONE)", "DrawInterface();", "DrawHud();"]),
    ("main_draw", "Terraria/Main.cs", "protected override void Draw(GameTime dt)", ["ui[j].Draw();", "spriteBatch.Begin();", "DrawChat();", "spriteBatch.End();"]),
]

def method_span(lines: list[str], signature: str) -> tuple[int, int]:
    starts = [i for i, line in enumerate(lines) if signature in line]
    if len(starts) != 1:
        raise ValueError(f"Expected one method signature {signature!r}; found {len(starts)}")
    start = starts[0]
    brace = next((i for i in range(start, len(lines)) if "{" in lines[i]), None)
    if brace is None:
        raise ValueError(f"Opening brace not found for {signature!r}")
    depth, state, escaped = 0, "code", False
    for i in range(brace, len(lines)):
        line, j = lines[i], 0
        while j < len(line):
            ch = line[j]
            nxt = line[j + 1] if j + 1 < len(line) else ""
            if state == "line_comment":
                break
            if state == "block_comment":
                if ch == "*" and nxt == "/":
                    state = "code"; j += 2; continue
            elif state in ("string", "char"):
                if escaped: escaped = False
                elif ch == "\\": escaped = True
                elif (state == "string" and ch == '"') or (state == "char" and ch == "'"): state = "code"
            else:
                if ch == "/" and nxt == "/": state = "line_comment"; break
                if ch == "/" and nxt == "*": state = "block_comment"; j += 2; continue
                if ch == '"': state = "string"
                elif ch == "'": state = "char"
                elif ch == "{": depth += 1
                elif ch == "}":
                    depth -= 1
                    if depth == 0: return start, i
            j += 1
        if state == "line_comment": state = "code"
    raise ValueError(f"Unbalanced method body for {signature!r}")

def build_report(root: Path) -> dict:
    methods = []
    for key, rel, signature, anchors in SPECS:
        path = root / rel
        if not path.is_file(): raise FileNotFoundError(path)
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        start, end = method_span(lines, signature)
        body = lines[start:end + 1]
        hits = []
        for anchor in anchors:
            found = [i for i, line in enumerate(body) if anchor in line]
            if not found: raise ValueError(f"Anchor {anchor!r} not found inside {signature!r}")
            for offset in found:
                hits.append({"anchor": anchor, "line": start + offset + 1, "source": rel, "text": body[offset].strip()})
        methods.append({"id": key, "signature": signature, "source": rel, "line_start": start + 1, "line_end": end + 1, "anchors": hits})
    by_id = {m["id"]: m for m in methods}
    def line(method: str, anchor: str) -> int:
        return next(x["line"] for x in by_id[method]["anchors"] if x["anchor"] == anchor)
    facts = [
        {"id": "input_poll_before_ui_update", "verified": line("main_update", "ui[i].UpdateGamePad();") < line("main_update", "ui[j].Update();"), "detail": "Main.Update polls each UI gamepad before calling each UI.Update."},
        {"id": "pause_and_map_are_existing_inputs", "verified": True, "detail": "UI.Update already handles pause release (Buttons 16) and map press (Buttons 32)."},
        {"id": "ui_overlay_draws_before_spritebatch_end", "verified": line("ui_draw", "DrawTopLayer();") < line("ui_draw", "Main.spriteBatch.End();"), "detail": "UI.Draw calls DrawTopLayer before ending Main.spriteBatch."},
        {"id": "main_draw_draws_chat_after_ui", "verified": line("main_draw", "ui[j].Draw();") < line("main_draw", "spriteBatch.Begin();"), "detail": "Main.Draw calls UI.Draw before beginning a separate SpriteBatch for chat/save overlays."},
    ]
    if not all(item["verified"] for item in facts): raise ValueError("A source-order invariant failed")
    return {"source_root": str(root), "methods": methods, "verified_facts": facts, "limitations": [
        "Source-level evidence from ILSpy output, not a runtime trace or successful rebuild.",
        "Does not select a conflict-free controller chord or prove safe input consumption.",
        "Does not establish SpriteBatch state, render-state compatibility, or XNA/XDK assembly availability.",
        "No Terraria source, XEX, XBLA package, or installed game files are modified."]}

def render(report: dict) -> str:
    out = ["# Terraria Xbox 360 — mapa dos pontos de integração do menu T360", "",
           "Gerado automaticamente a partir do C# decompilado pelo ILSpy. Linhas referem-se ao artefato da execução atual.", "",
           "## Métodos e âncoras verificadas", "",
           "| Área | Método | Arquivo:linhas | Evidência |", "|---|---|---:|---|"]
    for method in report["methods"]:
        anchors = "; ".join(f"{a['anchor']} (L{a['line']})" for a in method["anchors"])
        out.append(f"| {method['id']} | {method['signature']} | {method['source']}:{method['line_start']}-{method['line_end']} | {anchors} |")
    out += ["", "## Fatos confirmados", ""]
    out += [f"- **{fact['id']}** — {fact['detail']}" for fact in report["verified_facts"]]
    out += ["", "## Direção de implementação (ainda não é patch)", "",
            "- Entrada: estudar a interceptação dentro de UI.Update() antes dos ramos existentes de pausa/mapa; só aceitar uma combinação depois de confirmar os estados XNA e consumir a entrada sem disparar ações originais.",
            "- Desenho: UI.DrawTopLayer() é candidato de sobreposição, mas o estado ativo do SpriteBatch e os estados gráficos precisam ser provados antes de adicionar chamadas de desenho.",
            "- Compilação: o assembly requer XNA 4.0.0.0 e Microsoft.Xna.Framework.Xdk; o repositório ainda não contém referências compatíveis verificadas.",
            "", "## Limitações", ""]
    out += [f"- {item}" for item in report["limitations"]]
    out.append("")
    return "\n".join(out)

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_root", type=Path)
    parser.add_argument("--json", required=True, type=Path)
    parser.add_argument("--markdown", required=True, type=Path)
    args = parser.parse_args()
    if not args.source_root.is_dir(): raise SystemExit(f"Source directory not found: {args.source_root}")
    report = build_report(args.source_root)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    args.markdown.write_text(render(report), encoding="utf-8")
    print(f"Menu integration source map: PASS ({len(report['methods'])} methods, {sum(len(m['anchors']) for m in report['methods'])} anchors)")
    print(f"JSON: {args.json}")
    print(f"Markdown: {args.markdown}")

if __name__ == "__main__":
    main()
