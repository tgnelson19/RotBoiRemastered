import random
import unittest

import upgrades


class UpgradeOfferTests(unittest.TestCase):
    def test_offer_contains_three_distinct_stats(self):
        cards = upgrades.generate_offer(count=3, rng=random.Random(7))
        self.assertEqual(len(cards), 3)
        self.assertEqual(len({card.name for card in cards}), 3)

    def test_card_modifier_uses_rarity_and_math_type(self):
        definition = upgrades.DEFINITIONS_BY_NAME["Bullet Damage"]
        additive = upgrades.UpgradeCard(definition, "Rare", "additive")
        multiplicative = upgrades.UpgradeCard(definition, "Rare", "multiplicative")
        self.assertAlmostEqual(upgrades.card_modifier(additive), 40)
        self.assertAlmostEqual(upgrades.card_modifier(multiplicative), 1.256)

    def test_seeded_offer_is_reproducible(self):
        left = upgrades.generate_offer(count=3, rng=random.Random(42))
        right = upgrades.generate_offer(count=3, rng=random.Random(42))
        self.assertEqual(left, right)


if __name__ == "__main__":
    unittest.main()
