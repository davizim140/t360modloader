#!/usr/bin/env python3
"""Read-only audit of the pause-menu selection branch insertion point."""
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path

def block_after(text: str, marker: str) -> tuple[int, str]:
    pos = text.find(marker)
    if pos < 0 or text.find(marker, pos + 1) >= 0:
        raise ValueError(f"Expected exactly one marker: {marker}")
    opening = text.find("{", pos)
    if opening < 0:
        raise ValueError("Opening brace not found")
    depth = 0
    for i in range(opening, len(text)):
        if text[i] == "{": depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0: return opening, text[opening + 1:i]
    raise ValueError("Unbalanced block")

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source_dir", type=Path)
    ap.add_argument("--json", required=True, type=Path)
    args = ap.parse_args()
    marker = "else if (this.menuMode == MenuMode.PAUSE)"
    candidates = []
    for candidate in args.source_dir.rglob("*.cs"):
        try:
            candidate_text = candidate.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        if marker in candidate_text:
            candidates.append((candidate, candidate_text))
    if len(candidates) != 1:
        raise ValueError(f"Expected one decompiled C# file containing pause branch; found {len(candidates)}")
    source_path, text = candidates[0]
    raw = source_path.read_bytes()
    source_hash = hashlib.sha256(raw).hexdigest()
    _, pause = block_after(text, marker)
    exit_marker = "else if (selectedMenu == num2 + 4)"
    exit_pos, exit_block = block_after(pause, exit_marker)
    new_marker = "selectedMenu == num2 + 5"
    existing_new = new_marker in pause
    exit_action = "SetMenu(MenuMode.EXIT);" in exit_block
    # The candidate insertion point is the end of the existing Exit branch,
    # before the pause-mode block closes; no source modification is made.
    following = pause[exit_pos + len(exit_marker):]
    next_branch = following.find("else if")
    pass_audit = exit_action and not existing_new and next_branch < 0
    report = {
      "status": "pause-selection-anchor-audit-pass" if pass_audit else "pause-selection-anchor-audit-fail",
      "mode": "read-only-no-patch",
      "source": {"file": str(source_path), "sha256": source_hash},
      "target": {
        "method": "Terraria.UI::UpdateMenu()",
        "pauseBranchUnique": True,
        "existingExitCondition": "selectedMenu == num2 + 4",
        "existingExitActionFound": exit_action,
        "newRelativeIndex5HandlerAlreadyPresent": existing_new,
        "candidate": "append an else-if for selectedMenu == num2 + 5 after the existing Exit handler",
        "candidateAfterExitBranch": True
      },
      "safety": {
        "sourceFileUnchanged": hashlib.sha256(source_path.read_bytes()).hexdigest() == source_hash,
        "assemblyWritten": False, "xexProduced": False,
        "runtimeXbox360TestPerformed": False
      }
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(report["status"])
    return 0 if pass_audit else 1

if __name__ == "__main__":
    raise SystemExit(main())
