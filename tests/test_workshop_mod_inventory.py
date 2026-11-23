"""Offline checks for Workshop mod discovery; no Steam/game files required."""
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/inspect_workshop_mods.py"
spec = importlib.util.spec_from_file_location("inspect_workshop_mods", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class WorkshopModInventoryTests(unittest.TestCase):
    def test_managed_and_resource_mods(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            folder = root / "123456"
            folder.mkdir()
            (folder / "manifest.json").write_text('{"id":"example"}')
            (folder / "Example.dll").write_bytes(b"dummy")
            (folder / "resources.pck").write_bytes(b"dummy")
            result = module.inspect_mods(root)
            self.assertEqual(result["schema"], 1)
            self.assertEqual(result["mods"], [{
                "folder": "123456", "manifests": ["manifest.json"],
                "managed_assemblies": ["Example.dll"],
                "resource_packs": ["resources.pck"]
            }])

    def test_rejects_missing_root(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ValueError):
                module.inspect_mods(Path(tmp) / "missing")

if __name__ == "__main__":
    unittest.main()
