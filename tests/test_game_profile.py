import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

import gameProfile


class GameProfileTests(unittest.TestCase):
    def test_missing_profile_uses_all_defaults(self):
        with TemporaryDirectory() as folder:
            profile = gameProfile.load_profile(Path(folder) / "missing.json")
        self.assertEqual(profile, gameProfile.DEFAULTS)

    def test_unknown_fields_are_ignored_and_known_fields_are_loaded(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / "profile.json"
            path.write_text(json.dumps({"best_level": 12, "unknown": "ignored"}), encoding="utf-8")
            profile = gameProfile.load_profile(path)
        self.assertEqual(profile["best_level"], 12)
        self.assertNotIn("unknown", profile)
        self.assertTrue(profile["casual_mode"])

    def test_corrupt_profile_falls_back_safely(self):
        with TemporaryDirectory() as folder:
            path = Path(folder) / "profile.json"
            path.write_text("not json", encoding="utf-8")
            profile = gameProfile.load_profile(path)
        self.assertEqual(profile, gameProfile.DEFAULTS)


if __name__ == "__main__":
    unittest.main()
