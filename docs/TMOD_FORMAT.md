# T360 Mod Package Format (draft 1)

A `.tmod` file is a ZIP archive. This first format is metadata-and-assets only; it does not execute code.

## Layout

```
example.tmod
├── mod.json
└── assets/
    └── icon.png
```

## Manifest

`mod.json` is UTF-8 JSON with these required fields:

- `format_version`: integer, currently `1`.
- `id`: stable identifier matching `[a-z0-9][a-z0-9._-]{1,63}`.
- `name`: display name (1–80 characters).
- `version`: version string (1–32 characters).
- `files`: object mapping every asset path to its lowercase SHA-256 hex digest.

Example:

```json
{
  "format_version": 1,
  "id": "example.firstmod",
  "name": "Example First Mod",
  "version": "0.1.0",
  "files": {
    "assets/icon.png": "REPLACE_WITH_64_CHARACTER_SHA256"
  }
}
```

The hash covers the uncompressed bytes of each listed file. The manifest itself is not hashed. Every file under `assets/` must be listed, and every listed file must exist.

## Safety and compatibility rules

- Archive paths must be relative POSIX paths. Absolute paths, parent traversal, backslashes, duplicate paths and symbolic links are rejected.
- Only `mod.json` and files below `assets/` are accepted in draft 1.
- Maximum archive entry count: 2,000.
- Maximum total uncompressed size: 64 MiB.
- No scripts, native binaries, patches or executable payloads are supported by this draft.
- The format does not yet define runtime hooks or guarantee that the Xbox 360 game can load a package.

The validator is a development-side integrity check. Runtime loading and the Xbox 360 integration must be designed and tested separately.
