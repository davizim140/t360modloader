import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from tools.validate_tmod import TmodError, validate_tmod


class ValidateTmodTests(unittest.TestCase):
    def make_package(self, entries, manifest=None):
        temp = tempfile.NamedTemporaryFile(suffix=".tmod", delete=False)
        temp.close()
        path = Path(temp.name)
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            for name, data in entries.items():
                archive.writestr(name, data)
        self.addCleanup(path.unlink, missing_ok=True)
        return path

    def valid_entries(self):
        asset = b"test image bytes"
        manifest = {
            "format_version": 1,
            "id": "example.firstmod",
            "name": "Example First Mod",
            "version": "0.1.0",
            "files": {
                "assets/icon.png": hashlib.sha256(asset).hexdigest()
            },
        }
        return {
            "mod.json": json.dumps(manifest),
            "assets/icon.png": asset,
        }

    def test_valid_package(self):
        path = self.make_package(self.valid_entries())
        result = validate_tmod(str(path))
        self.assertEqual(result["id"], "example.firstmod")
        self.assertEqual(result["asset_count"], 1)

    def test_rejects_hash_mismatch(self):
        entries = self.valid_entries()
        entries["assets/icon.png"] = b"changed"
        path = self.make_package(entries)
        with self.assertRaisesRegex(TmodError, "SHA-256 mismatch"):
            validate_tmod(str(path))

    def test_rejects_unlisted_asset(self):
        entries = self.valid_entries()
        entries["assets/extra.dat"] = b"extra"
        path = self.make_package(entries)
        with self.assertRaisesRegex(TmodError, "Unlisted asset"):
            validate_tmod(str(path))

    def test_rejects_path_traversal(self):
        entries = self.valid_entries()
        entries["../outside.txt"] = b"no"
        path = self.make_package(entries)
        with self.assertRaisesRegex(TmodError, "Unsafe archive path"):
            validate_tmod(str(path))

    def test_rejects_missing_manifest(self):
        path = self.make_package({"assets/file.bin": b"x"})
        with self.assertRaisesRegex(TmodError, "Missing mod.json"):
            validate_tmod(str(path))


if __name__ == "__main__":
    unittest.main()
