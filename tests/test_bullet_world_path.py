import os
import unittest
from math import pi

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
from bullet import Bullet
import variableHolster as vH


class BulletWorldPathTests(unittest.TestCase):
    def setUp(self):
        self.original_player_position = (bG.playerPosX, bG.playerPosY)
        self.original_frame_rate = vH.frameRate
        self.original_has_delta = vH.hasFrameDelta
        bG.playerPosX, bG.playerPosY = 500, 500
        vH.frameRate = 120
        vH.hasFrameDelta = False

    def tearDown(self):
        bG.playerPosX, bG.playerPosY = self.original_player_position
        vH.frameRate = self.original_frame_rate
        vH.hasFrameDelta = self.original_has_delta

    def make_bullet(self, direction=0):
        return Bullet(550, 550, 4, direction, 500, 10, (255, 255, 255), 1, 1, False)

    def test_world_velocity_does_not_inherit_player_motion(self):
        bullet = self.make_bullet()
        bullet.updateAndDrawBullet(vH.screen)
        first_world_x = bullet.worldX

        # Simulate the camera/player moving rapidly after the projectile was fired.
        bG.playerPosX += 100
        bullet.updateAndDrawBullet(vH.screen)

        self.assertAlmostEqual(first_world_x, 558)
        self.assertAlmostEqual(bullet.worldX, 566)
        self.assertAlmostEqual(bullet.posX, bullet.worldX - bG.playerPosX + bG.lockX)

    def test_direction_controls_world_path(self):
        bullet = self.make_bullet(pi / 2)
        bullet.updateAndDrawBullet(vH.screen)
        self.assertAlmostEqual(bullet.worldX, 550)
        self.assertAlmostEqual(bullet.worldY, 542)

    def test_range_is_measured_by_distance_travelled(self):
        bullet = Bullet(550, 550, 4, 0, 8, 10, (255, 255, 255), 1, 1, False)
        bullet.updateAndDrawBullet(vH.screen)
        self.assertTrue(bullet.remFlag)


if __name__ == "__main__":
    unittest.main()
