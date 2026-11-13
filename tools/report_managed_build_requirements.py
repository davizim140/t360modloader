#!/usr/bin/env python3
"""Create a factual inventory of references in an ILSpy-generated project."""
from __future__ import annotations

import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def child_text(element: ET.Element, wanted: str) -> str | None:
    for child in element:
        if local_name(child.tag) == wanted and child.text:
            return child.text.strip()
    return None


def inspect_project(path: Path) -> dict:
    root = ET.parse(path).getroot()
    properties: dict[str, str] = {}
    references: list[dict[str, str | None]] = []
    packages: list[dict[str, str | None]] = []

    for element in root.iter():
        tag = local_name(element.tag)
        if tag == "PropertyGroup":
            for prop in element:
                name = local_name(prop.tag)
                if prop.text and prop.text.strip():
                    properties[name] = prop.text.strip()
        elif tag == "Reference":
            references.append({
                "include": element.attrib.get("Include", ""),
                "hint_path": child_text(element, "HintPath"),
                "private": child_text(element, "Private"),
            })
        elif tag == "PackageReference":
            packages.append({
                "include": element.attrib.get("Include", ""),
                "version": element.attrib.get("Version") or child_text(element, "Version"),
            })

    return {
        "project": str(path),
        "target_framework_version": properties.get("TargetFrameworkVersion"),
        "target_framework": properties.get("TargetFramework"),
        "output_type": properties.get("OutputType"),
        "platform_target": properties.get("PlatformTarget"),
        "references": references,
        "package_references": packages,
    }


def render_markdown(data: dict) -> str:
    lines = [
        "# Terraria managed project — reference inventory",
        "",
        f"Project: `{data['project']}`",
        "",
        "## Project properties",
        "",
        "| Property | Value |",
        "|---|---|",
    ]
    for key, label in (
        ("target_framework_version", "TargetFrameworkVersion"),
        ("target_framework", "TargetFramework"),
        ("output_type", "OutputType"),
        ("platform_target", "PlatformTarget"),
    ):
        value = data.get(key) or "(not declared)"
        lines.append(f"| {label} | `{value}` |")

    lines.extend(["", f"## Assembly references ({len(data['references'])})", ""])
    if data["references"]:
        lines.extend(["| Include | HintPath | Private |", "|---|---|---|"])
        for ref in data["references"]:
            include = (ref.get("include") or "").replace("|", "\\|")
            hint = (ref.get("hint_path") or "(not declared)").replace("|", "\\|")
            private = ref.get("private") or "(default)"
            lines.append(f"| `{include}` | `{hint}` | `{private}` |")
    else:
        lines.append("No explicit assembly references were found in the project XML.")

    lines.extend(["", f"## Package references ({len(data['package_references'])})", ""])
    if data["package_references"]:
        lines.extend(["| Package | Version |", "|---|---|"])
        for package in data["package_references"]:
            lines.append(
                f"| `{package.get('include') or ''}` | `{package.get('version') or '(not declared)'}` |"
            )
    else:
        lines.append("No PackageReference entries were found.")

    lines.extend([
        "",
        "## Interpretation limits",
        "",
        "- This report reads project XML only; it does not compile the project.",
        "- A reference without a HintPath may be resolved by the original Xbox/XNA development environment.",
        "- The presence of a reference does not prove that a compatible redistributable assembly is available in this repository or on GitHub-hosted runners.",
        "- No game executable or original package is modified by this report.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("project", type=Path, help="Path to the generated .csproj")
    parser.add_argument("--json", type=Path, required=True, help="JSON report output")
    parser.add_argument("--markdown", type=Path, required=True, help="Markdown report output")
    args = parser.parse_args()

    if not args.project.is_file():
        parser.error(f"project file not found: {args.project}")

    data = inspect_project(args.project)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    args.markdown.write_text(render_markdown(data), encoding="utf-8")
    print(f"Project: {args.project}")
    print(f"Assembly references: {len(data['references'])}")
    print(f"Package references: {len(data['package_references'])}")
    print(f"Markdown report: {args.markdown}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
