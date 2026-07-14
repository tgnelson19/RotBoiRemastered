import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import background as bG


class BackgroundSpawnTests(unittest.TestCase):
    def setUp(self):
        bG.currRoomRects = [
            [[1, None], [1, None], [1, None]],
            [[1, None], [0, None], [1, None]],
            [[1, None], [1, None], [1, None]],
        ]
        bG.vH.tileSizeGlobal = 10

    def test_find_spawn_position_avoids_walls(self):
        spawn_rect = bG.find_spawn_rect(size=8)
        self.assertFalse(bG.rect_hits_wall(spawn_rect))

    def test_find_nearest_open_rect_escapes_wall_overlap(self):
        overlapping_rect = bG.pg.Rect(0, 10, 8, 8)
        safe_rect = bG.find_nearest_open_rect(overlapping_rect, 8)
        self.assertFalse(bG.rect_hits_wall(safe_rect))

    def test_find_nearest_open_rect_prefers_smallest_offset(self):
        overlapping_rect = bG.pg.Rect(0, 10, 8, 8)
        safe_rect = bG.find_nearest_open_rect(overlapping_rect, 8)
        self.assertLessEqual(abs(safe_rect.x - overlapping_rect.x), 10)
        self.assertLessEqual(abs(safe_rect.y - overlapping_rect.y), 10)

    def test_find_path_around_walls_returns_safe_step(self):
        world_rect = bG.pg.Rect(10, 10, 8, 8)
        safe_rect = bG.find_path_around_walls(world_rect, 0, 10, 8)
        self.assertFalse(bG.rect_hits_wall(safe_rect))


if __name__ == "__main__":
    unittest.main()
