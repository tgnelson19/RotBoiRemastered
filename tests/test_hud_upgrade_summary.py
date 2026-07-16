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

    def test_record_upgrade_keeps_visual_card_history_when_math_is_known(self):
        cS.record_upgrade("Bullet Speed", "Epic", "multiplicative")
        cS.record_upgrade("Defense", "Rare", "additive")

        self.assertEqual(cS.upgradeCollection["history"], [
            {"name": "Bullet Speed", "rarity": "Epic", "math_type": "multiplicative"},
            {"name": "Defense", "rarity": "Rare", "math_type": "additive"},
        ])

    def test_expected_dps_supports_over_one_hundred_percent_crit(self):
        original = (cS.attackCooldownStat, cS.bulletDamage,
                    cS.projectileCount, cS.critChance, cS.critDamage)
        try:
            cS.attackCooldownStat = 120
            cS.bulletDamage = 10
            cS.projectileCount = 1
            cS.critChance = 1.25
            cS.critDamage = 2
            attacks, dps = cS.informationSheet._combat_values()
            self.assertEqual(attacks, 1)
            self.assertEqual(dps, 25)
        finally:
            (cS.attackCooldownStat, cS.bulletDamage,
             cS.projectileCount, cS.critChance, cS.critDamage) = original


if __name__ == "__main__":
    unittest.main()
