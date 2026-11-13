#!/usr/bin/env python3
"""Report exact CLR AssemblyRef identities from a managed PE using dnfile."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import dnfile


def blob_value(value):
    raw = getattr(value, "value", value)
    if raw is None:
        return b""
    if isinstance(raw, bytes):
        return raw
    try:
        return bytes(raw)
    except Exception:
        return str(raw).encode("utf-8", "replace")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("assembly", type=Path)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    data = args.assembly.read_bytes()
    pe = dnfile.dnPE(str(args.assembly))
    if not getattr(pe, "net", None) or not getattr(pe.net, "mdtables", None):
        raise SystemExit("Input is not a readable CLR managed PE.")

    table = getattr(pe.net.mdtables, "AssemblyRef", None)
    rows = list(getattr(table, "rows", []) or [])
    if not rows:
        raise SystemExit("CLR AssemblyRef table is empty or unavailable.")

    refs = []
    for row in rows:
        name = str(row.Name)
        culture = str(row.Culture) if getattr(row, "Culture", None) else ""
        token_bytes = blob_value(getattr(row, "PublicKeyOrToken", b""))
        flags = int(getattr(row, "Flags", 0).value if hasattr(getattr(row, "Flags", 0), "value") else getattr(row, "Flags", 0))
        refs.append({
            "name": name,
            "version": f"{int(row.MajorVersion)}.{int(row.MinorVersion)}.{int(row.BuildNumber)}.{int(row.RevisionNumber)}",
            "culture": culture or "neutral",
            "public_key_or_token_hex": token_bytes.hex(),
            "flags": flags,
        })

    report = {
        "assembly": str(args.assembly),
        "sha256": hashlib.sha256(data).hexdigest(),
        "assembly_reference_count": len(refs),
        "assembly_references": refs,
        "note": "PublicKeyOrToken is reported exactly as stored in AssemblyRef metadata; it may be a full public key when the AssemblyFlags PublicKey bit is set.",
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    lines = [
        "# Terraria.Managed.dll — AssemblyRef identities",
        "",
        f"- SHA-256: `{report['sha256']}`",
        f"- References found: **{len(refs)}**",
        "",
        "| Assembly | Version | Culture | PublicKeyOrToken (hex) | Flags |",
        "|---|---:|---|---|---:|",
    ]
    for ref in refs:
        lines.append(
            f"| `{ref['name']}` | `{ref['version']}` | {ref['culture']} | "
            f"`{ref['public_key_or_token_hex'] or '(empty)'}` | `0x{ref['flags']:x}` |"
        )
    lines += ["", report["note"], ""]
    args.markdown.write_text("\n".join(lines), encoding="utf-8")

    print(f"AssemblyRef metadata: PASS ({len(refs)} references)")
    print(f"SHA-256: {report['sha256']}")
    for ref in refs:
        print(f"{ref['name']}, Version={ref['version']}, Culture={ref['culture']}, PublicKeyOrToken={ref['public_key_or_token_hex'] or '(empty)'}")


if __name__ == "__main__":
    main()
