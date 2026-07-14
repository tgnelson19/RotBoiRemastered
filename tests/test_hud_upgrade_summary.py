import unittest
import characterStats as cS


class UpgradeTrackingTests(unittest.TestCase):
    def setUp(self):
        cS.reset_upgrade_tracking()

    def test_record_upgrade_increments_type_and_rarity_counts(self):
        cS.record_upgrade("Bullet Damage", "Rare")
        cS.record_upgrade("Bullet Damage", "Common")
        cS.record_upgrade("Player Speed", "Epic")

        self.assertEqual(cS.upgradeCollection["types"]["Bullet Damage"], 2)
        self.assertEqual(cS.upgradeCollection["types"]["Player Speed"], 1)
        self.assertEqual(cS.upgradeCollection["rarities"]["Rare"], 1)
        self.assertEqual(cS.upgradeCollection["rarities"]["Epic"], 1)


if __name__ == "__main__":
    unittest.main()
