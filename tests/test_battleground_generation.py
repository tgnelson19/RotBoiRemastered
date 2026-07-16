import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import background as bG


class BattlegroundGenerationTests(unittest.TestCase):
    def test_arena_has_circular_solid_boundary_and_open_center(self):
        room = bG.generate_battleground(61)
        center = len(room) // 2
        self.assertIn(room[0][0][0], bG.SOLID_TILES)
        self.assertIn(room[0][-1][0], bG.SOLID_TILES)
        self.assertEqual(room[center][center][0], 2)

    def test_arena_contains_buildings_with_two_sided_passages(self):
        room = bG.generate_battleground(97)
        tile_counts = {}
        for row in room:
            for tile, _ in row:
                tile_counts[tile] = tile_counts.get(tile, 0) + 1
        self.assertGreater(tile_counts.get(4, 0), 100)
        self.assertGreater(tile_counts.get(3, 0), 200)
        self.assertGreater(tile_counts.get(2, 0), 100)


if __name__ == "__main__":
    unittest.main()
