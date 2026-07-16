import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import background as bG
from enemy import Enemy
import variableHolster as vH


class EnemyWallSlideTests(unittest.TestCase):
    def setUp(self):
        self.room = bG.currRoomRects
        self.tile_size = vH.tileSizeGlobal
        self.frame_rate = vH.frameRate
        self.has_delta = vH.hasFrameDelta
        vH.tileSizeGlobal = 10
        vH.frameRate = 120
        vH.hasFrameDelta = False
        bG.currRoomRects = [
            [[1, None], [1, None], [1, None], [1, None], [1, None]],
            [[1, None], [0, None], [1, None], [0, None], [1, None]],
            [[1, None], [0, None], [1, None], [0, None], [1, None]],
            [[1, None], [0, None], [1, None], [0, None], [1, None]],
            [[1, None], [1, None], [1, None], [1, None], [1, None]],
        ]

    def tearDown(self):
        bG.currRoomRects = self.room
        vH.tileSizeGlobal = self.tile_size
        vH.frameRate = self.frame_rate
        vH.hasFrameDelta = self.has_delta

    def test_blocked_enemy_keeps_wall_parallel_velocity_without_vibrating(self):
        enemy = Enemy(12, 11, 1, 6, (255, 0, 0), 1, 2, 1, 1)
        positions = []
        for _ in range(7):
            enemy.updateEnemy(36, 34)
            positions.append((enemy.worldX, enemy.worldY))

        x_positions = [position[0] for position in positions]
        y_positions = [position[1] for position in positions]
        self.assertLess(max(x_positions), 15)
        self.assertTrue(all(later >= earlier for earlier, later in zip(y_positions, y_positions[1:])))
        self.assertGreater(y_positions[-1], y_positions[0])


if __name__ == "__main__":
    unittest.main()
