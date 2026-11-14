#!/usr/bin/env python3
"""Extract raw IL method bodies for Terraria.UI menu-flow inspection."""
from __future__ import annotations
import argparse, json, re
from pathlib import Path
TARGETS = ("UpdateMenu", "SetMenu", "Update", "Draw")

def method_blocks(text):
    lines, i = text.splitlines(), 0
    while i < len(lines):
        if re.match(r"\s*\.method\b", lines[i]):
            start, header = i, [lines[i]]
            i += 1
            while i < len(lines) and "{" not in lines[i]:
                header.append(lines[i]); i += 1
            if i >= len(lines): break
            depth, body, opened = 0, [], False
            while i < len(lines):
                line = lines[i]; body.append(line)
                depth += line.count("{") - line.count("}")
                opened = opened or "{" in line
                i += 1
                if opened and depth == 0: break
            yield start + 1, "\n".join(header), "\n".join(body)
        else: i += 1

def main():
    p = argparse.ArgumentParser()
    p.add_argument("il_file", type=Path)
    p.add_argument("--json", required=True, type=Path)
    p.add_argument("--markdown", required=True, type=Path)
    a = p.parse_args()
    found = []
    for line, header, body in method_blocks(a.il_file.read_text(encoding="utf-8", errors="replace")):
        m = re.search(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(", header)
        if m and m.group(1) in TARGETS:
            found.append({"name":m.group(1),"line":line,"signature":header.strip(),"il":body})
    if not any(x["name"] == "UpdateMenu" for x in found):
        raise SystemExit("UpdateMenu method body not found in raw IL dump")
    report = {"status":"raw-il-captured","source":str(a.il_file),"methods":found,
      "limitations":["Raw IL inspection only; no IL mutation or assembly rebuild.",
      "Overloads are retained separately.","Runtime controller behavior is not established."]}
    a.json.parent.mkdir(parents=True, exist_ok=True)
    a.json.write_text(json.dumps(report,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
    lines = ["# Terraria.UI — inspeção de IL bruto","","Inspeção estática; o assembly não é alterado.",""]
    for x in found:
        lines += [f"## {x['name']} (linha {x['line']})","","~~~il",x["signature"],x["il"],"~~~",""]
    lines += ["## Limitações","", "- Nenhuma instrução IL foi modificada.",
      "- Nenhum assembly/XEX foi recompilado ou executado no console."]
    a.markdown.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print(f"Raw IL inventory: PASS ({len(found)} methods)")
if __name__ == "__main__": main()
