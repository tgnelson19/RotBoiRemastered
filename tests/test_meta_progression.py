from copy import deepcopy
from unittest import mock
import unittest

import gameProfile
import items
import metaProgression


class MetaProgressionTests(unittest.TestCase):
    def setUp(self):
        self.original = deepcopy(gameProfile.profile)
        gameProfile.profile.clear()
        gameProfile.profile.update(deepcopy(gameProfile.DEFAULTS))

    def tearDown(self):
        gameProfile.profile.clear()
        gameProfile.profile.update(self.original)

    def test_first_path_clear_awards_one_token_and_artifact(self):
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            gameProfile.record_run(20, 80, completed=True, path_key="sound")
            gameProfile.record_run(20, 90, completed=True, path_key="sound")
        self.assertEqual(gameProfile.profile["soul_tokens"], 1)
        self.assertEqual(gameProfile.profile["path_mastery"]["sound"], 2)
        self.assertIn("sound_echo", gameProfile.profile["museum_artifacts"])

    def test_storage_round_trip_and_loadout(self):
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Epic")
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            metaProgression.store_drop(drop)
            self.assertTrue(metaProgression.equip_from_storage(0))
        loaded = metaProgression.starting_equipment()["weapon"]
        self.assertEqual(loaded, drop)

    def test_begin_run_withdraws_risked_items_until_extraction(self):
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Epic")
        gameProfile.profile["skill_nodes"] = ["deep_reserve"]
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            metaProgression.store_drop(drop)
            self.assertTrue(metaProgression.equip_from_storage(0))
            equipment = metaProgression.begin_run()
        self.assertEqual(equipment["weapon"], drop)
        self.assertEqual(gameProfile.profile["storage"], [])
        self.assertEqual(gameProfile.profile["starting_loadout"], {})
        self.assertEqual(gameProfile.profile["skill_nodes"], ["deep_reserve"])

        # Extraction is the only route that returns the surviving held copy.
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            metaProgression.extract_equipment(equipment)
        self.assertEqual(gameProfile.profile["storage"], [items.serialize(drop)])

    def test_identical_items_are_real_copies_not_an_infinite_unlock(self):
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Lucky Charm"], "Common")
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            metaProgression.store_drop(drop)
            metaProgression.store_drop(drop)
            self.assertTrue(metaProgression.equip_from_storage(0))
            self.assertTrue(metaProgression.equip_from_storage(1))
            equipment = metaProgression.begin_run()
        self.assertEqual(gameProfile.profile["storage"], [])
        self.assertEqual(equipment["accessory_1"], drop)
        self.assertEqual(equipment["accessory_2"], drop)

    def test_skill_purchase_changes_stat_adjustments(self):
        gameProfile.profile["soul_tokens"] = 1
        with mock.patch.object(gameProfile, "save_profile", return_value=True):
            self.assertTrue(gameProfile.purchase_skill("deep_reserve", 1))
        additive, _multiplicative = metaProgression.stat_adjustments()
        self.assertEqual(additive["Health"], [30])


class ItemModifierTests(unittest.TestCase):
    def test_item_serialization_round_trip(self):
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Silver Band"], "Legendary")
        self.assertEqual(items.deserialize(items.serialize(drop)), drop)

    def test_two_wayfarer_items_grant_set_bonus(self):
        equipment = {
            "weapon": items.ItemDrop(items.DEFINITIONS_BY_NAME["Iron Dagger"], "Common"),
            "armor": items.ItemDrop(items.DEFINITIONS_BY_NAME["Leather Vest"], "Common"),
        }
        additive, _multiplicative = items.equipment_adjustments(equipment)
        self.assertIn(.12, additive["Player Speed"])

    def test_description_contains_tradeoffs_and_status(self):
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        text = items.describe(drop)
        self.assertIn("Bullet Damage", text)
        self.assertIn("Attack Speed", text)
        self.assertIn("Bleed", text)


if __name__ == "__main__":
    unittest.main()
