#!/usr/bin/env python3
"""Audit Terraria's existing XNA storage path for a safe in-game T360 catalog."""
from __future__ import annotations
import argparse, json
from pathlib import Path

def require(text: str, token: str, label: str) -> bool:
    return token in text

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("decompiled", type=Path)
    ap.add_argument("--json", required=True, type=Path)
    ap.add_argument("--markdown", required=True, type=Path)
    a = ap.parse_args()
    ui_path = a.decompiled / "Terraria" / "UI.cs"
    manager_path = a.decompiled / "StorageDeviceManager.cs"
    if not ui_path.is_file() or not manager_path.is_file():
        raise SystemExit("Required ILSpy decompilation files are missing")
    ui, manager = ui_path.read_text(encoding="utf-8"), manager_path.read_text(encoding="utf-8")
    checks = {
        "storage_device_presence_check": require(ui, "public bool HasPlayerStorage()", "HasPlayerStorage"),
        "container_open_method": require(ui, "public StorageContainer OpenPlayerStorage(string name)", "OpenPlayerStorage"),
        "existing_file_enumeration": require(ui, 'GetFileNames("player?.plr")', "GetFileNames"),
        "container_file_exists": require(ui, ".FileExists(", "FileExists"),
        "container_stream_open": require(ui, ".OpenFile(", "OpenFile"),
        "container_disposal": require(ui, "((IDisposable)val)?.Dispose()", "Dispose"),
        "storage_device_selector_api": require(manager, "StorageDevice.BeginShowSelector", "BeginShowSelector"),
        "begin_open_container_api": require(ui, "BeginOpenContainer", "BeginOpenContainer"),
        "callback_driven_container_open_not_yet_proven": "BeginOpenContainer" in ui and "(AsyncCallback)null" not in ui,
        "sync_wait_in_existing_open_helper": require(ui, "asyncResult.AsyncWaitHandle.WaitOne()", "WaitOne"),
    }
    checks["all_required_storage_evidence_found"] = all(v for k,v in checks.items() if k not in ("sync_wait_in_existing_open_helper", "callback_driven_container_open_not_yet_proven"))
    checks["blocking_open_helper_detected"] = checks["sync_wait_in_existing_open_helper"]
    report = {
        "status": "storage-api-audit-pass-with-blocker" if checks["all_required_storage_evidence_found"] and checks["blocking_open_helper_detected"] and not checks["callback_driven_container_open_not_yet_proven"] else ("storage-api-audit-pass" if checks["all_required_storage_evidence_found"] else "storage-api-audit-fail"),
        "mode": "read-only-source-audit",
        "checks": checks,
        "implementation_constraints": [
            "Do not call OpenPlayerStorage from the per-frame UI Update path: it waits synchronously.",
            "The decompiled calls currently pass a null callback and wait; a callback-driven open path must be implemented and audited before catalog I/O.",
            "Reuse the currently selected StorageDevice; do not prompt for storage unexpectedly.",
            "Use a dedicated T360 container; never reuse Characters, Worlds, or Settings.",
            "The audit proves API presence only, not container creation behavior or Xbox 360 runtime compatibility."
        ],
        "safety": {"sourceFilesModified": False, "gameAssemblyModified": False, "xexProduced": False, "consoleTested": False}
    }
    a.json.parent.mkdir(parents=True, exist_ok=True)
    a.json.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [
        "# Terraria storage API audit", "",
        "**Status:** " + report["status"], "",
        "Read-only inspection of the decompiled game source.", "",
        "| Check | Result |", "|---|---|"
    ]
    lines += [f"| {k} | {'PASS' if v else 'FAIL'} |" for k,v in checks.items()]
    lines += ["", "## Implementation constraints", ""] + [f"- {x}" for x in report["implementation_constraints"]]
    lines += ["", "No game files were changed and no XEX was produced."]
    a.markdown.parent.mkdir(parents=True, exist_ok=True)
    a.markdown.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(report["status"])
    return 0 if report["status"] == "storage-api-audit-pass" else 1

if __name__ == "__main__":
    raise SystemExit(main())
