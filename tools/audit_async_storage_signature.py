#!/usr/bin/env python3
"""Verify the exact XNA asynchronous container-open signature in Terraria IL."""
from __future__ import annotations
import argparse, json
from pathlib import Path

BEGIN = "StorageDevice::BeginOpenContainer(string, class [mscorlib]System.AsyncCallback, object)"
END = "StorageDevice::EndOpenContainer(class [mscorlib]System.IAsyncResult)"
CALLBACK = "System.AsyncCallback::.ctor(object, native int)"

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("il", type=Path)
    ap.add_argument("--json", required=True, type=Path)
    ap.add_argument("--markdown", required=True, type=Path)
    a = ap.parse_args()
    text = a.il.read_text(encoding="utf-8", errors="replace")
    checks = {
        "begin_open_container_accepts_async_callback": BEGIN in text,
        "begin_open_container_accepts_async_state": BEGIN in text and "System.AsyncCallback, object)" in BEGIN,
        "matching_end_open_container_exists": END in text,
        "async_callback_delegate_constructor_present": CALLBACK in text,
        "stock_open_helper_does_not_prove_nonblocking_use": "(AsyncCallback)null" in text,
    }
    safe = all(checks.values())
    report = {
        "status": "async-storage-signature-audit-pass" if safe else "async-storage-signature-audit-fail",
        "mode": "read-only-il-signature-audit",
        "checks": checks,
        "conclusion": (
            "The managed API exposes BeginOpenContainer(name, callback, state) and the matching "
            "EndOpenContainer(IAsyncResult). The existing helper passes a null callback and waits; "
            "a new callback-driven flow is technically expressible, but this audit does not implement "
            "it or prove Xbox runtime behavior."
        ),
        "next_implementation_constraints": [
            "Start BeginOpenContainer only from a user action, never on every frame.",
            "Use a non-null AsyncCallback and an explicit state object.",
            "Call EndOpenContainer in the callback and publish completion for the game UI thread.",
            "Enumerate *.tmod only after completion on the UI thread.",
            "Handle cancellation/errors and dispose the container when leaving the catalog.",
            "Do not call WaitOne and do not overwrite Characters, Worlds, or Settings."
        ],
        "safety": {
            "source_il_modified": False,
            "game_assembly_modified": False,
            "xex_produced": False,
            "console_tested": False
        }
    }
    a.json.parent.mkdir(parents=True, exist_ok=True)
    a.json.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = ["# Async storage signature audit", "", "**Status:** " + report["status"], "",
             "| Check | Result |", "|---|---|"]
    lines += [f"| {k} | {'PASS' if v else 'FAIL'} |" for k, v in checks.items()]
    lines += ["", "## Conclusion", "", report["conclusion"], "",
              "## Constraints", ""] + [f"- {x}" for x in report["next_implementation_constraints"]]
    lines += ["", "This is a signature audit only; no game code or XEX was changed."]
    a.markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(report["status"])
    return 0 if safe else 1

if __name__ == "__main__":
    raise SystemExit(main())
