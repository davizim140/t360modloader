#!/usr/bin/env python3
"""Inventory Terraria's statically visible XNA gamepad button bindings."""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

CONSTANT_RE = re.compile(
    r"\b(?:const\s+)?Buttons\s+(\w+)\s*=\s*\(Buttons\)\s*(\d+)\b"
)
CAST_RE = re.compile(r"\(Buttons\)\s*(\d+)\b")


def build_report(root: Path) -> dict:
    constants: dict[int, list[dict[str, str]]] = defaultdict(list)
    counts: Counter[int] = Counter()
    files: dict[int, set[str]] = defaultdict(set)

    sources = sorted(root.rglob("*.cs"))
    for path in sources:
        text = path.read_text(encoding="utf-8", errors="replace")
        relative = path.relative_to(root).as_posix()

        for match in CONSTANT_RE.finditer(text):
            name, raw_value = match.groups()
            constants[int(raw_value)].append({"name": name, "file": relative})

        for match in CAST_RE.finditer(text):
            value = int(match.group(1))
            counts[value] += 1
            files[value].add(relative)

    values = sorted(set(constants) | set(counts))
    bindings = []
    for value in values:
        bindings.append({
            "value": value,
            "named_constants": sorted(
                constants.get(value, []), key=lambda item: (item["name"], item["file"])
            ),
            "numeric_cast_occurrences": counts[value],
            "source_files": sorted(files[value]),
        })

    return {
        "source_root": str(root),
        "source_files_scanned": len(sources),
        "bindings": bindings,
        "limitations": [
            "Static inventory only; it does not prove that an unlisted controller input is unused.",
            "Input may also be consumed through GamePadState, helper methods, or framework code.",
            "No button combination is declared safe or conflict-free by this report.",
        ],
    }


def render_markdown(report: dict) -> str:
    lines = [
        "# Terraria Xbox 360 — inventário estático de controles",
        "",
        f"- Arquivos C# analisados: {report['source_files_scanned']}",
        "- Método: constantes Buttons e casts numéricos (Buttons)value no código decompilado.",
        "",
        "| Valor | Constantes nomeadas no jogo | Casts numéricos | Arquivos |",
        "|---:|---|---:|---|",
    ]
    for item in report["bindings"]:
        names = ", ".join(entry["name"] for entry in item["named_constants"]) or "—"
        paths = ", ".join(item["source_files"]) or "—"
        lines.append(
            f"| {item['value']} | {names} | {item['numeric_cast_occurrences']} | {paths} |"
        )
    lines += ["", "## Limitações", ""]
    lines.extend(f"- {item}" for item in report["limitations"])
    lines += [
        "",
        "Este relatório não escolhe um atalho para o ModLoader. Antes de definir uma combinação,",
        "é necessário interceptar e consumir a entrada antes da lógica original e validar os",
        "conflitos no jogo. Ausência de uma referência literal não significa botão livre.",
        "",
    ]
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_root", type=Path)
    parser.add_argument("--json", required=True, type=Path)
    parser.add_argument("--markdown", required=True, type=Path)
    args = parser.parse_args()

    if not args.source_root.is_dir():
        raise SystemExit(f"Source directory not found: {args.source_root}")

    report = build_report(args.source_root)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    args.markdown.write_text(render_markdown(report), encoding="utf-8")

    print(f"Controller binding inventory: PASS ({len(report['bindings'])} values)")
    print(f"Scanned C# files: {report['source_files_scanned']}")
    print(f"JSON: {args.json}")
    print(f"Markdown: {args.markdown}")


if __name__ == "__main__":
    main()
