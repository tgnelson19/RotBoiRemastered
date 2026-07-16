import random
import unittest

import items
import upgrades


class ItemDropTests(unittest.TestCase):
    def test_roll_drop_count_stays_in_range(self):
        rng = random.Random(1)
        for _ in range(500):
            self.assertIn(items.roll_drop_count(rng), range(5))

    def test_roll_drop_count_is_reproducible(self):
        left = [items.roll_drop_count(random.Random(11)) for _ in range(20)]
        right = [items.roll_drop_count(random.Random(11)) for _ in range(20)]
        self.assertEqual(left, right)

    def test_generate_drop_has_valid_slot_type_and_rarity(self):
        rng = random.Random(2)
        for _ in range(200):
            drop = items.generate_drop(rng)
            self.assertIn(drop.slot_type, items.SLOT_TYPES)
            self.assertIn(drop.rarity, upgrades.RARITY_WEIGHTS)

    def test_generate_drops_is_reproducible(self):
        left = items.generate_drops(4, rng=random.Random(42))
        right = items.generate_drops(4, rng=random.Random(42))
        self.assertEqual(left, right)

    def test_generate_drops_returns_requested_count(self):
        drops = items.generate_drops(3, rng=random.Random(5))
        self.assertEqual(len(drops), 3)


if __name__ == "__main__":
    unittest.main()
