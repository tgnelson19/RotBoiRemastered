import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame

import background as bG
import character as game
import characterStats as cS
from enemyTypes import ENEMY_CATALOG
import variableHolster as vH


class BountyIndicatorTests(unittest.TestCase):
    def setUp(self):
        game.resetAllStats()
        self.angle = bG.cameraAngleDegrees

    def tearDown(self):
        bG.set_camera_angle(self.angle)

    def test_highest_total_value_patrol_becomes_the_bounty(self):
        low_encounter, low = ENEMY_CATALOG.spawn_patrol(2, 80, (), random.Random(2))
        high_encounter, high = ENEMY_CATALOG.spawn_patrol(12, 80, (), random.Random(4))
        for enemy in low:
            enemy.expValue = 1
        for enemy in high:
            enemy.expValue = 20
        cS.enemyHolster = low + high
        bounty = game.selectBountyTarget()
        self.assertIs(bounty["target"], high_encounter)
        self.assertEqual(bounty["label"], high_encounter.key.replace("_", " ").upper())

    def test_active_boss_always_overrides_patrol_value(self):
        _, patrol = ENEMY_CATALOG.spawn_patrol(18, 100, (), random.Random(7))
        cS.enemyHolster = patrol
        boss = ENEMY_CATALOG.create("miniboss_siege", bG.spawnX, bG.spawnY,
                                    18, random.Random(3))
        boss.bossName = "TEST BOSS"
        cS.activeBoss = boss
        cS.enemyHolster.append(boss)
        self.assertIs(game.selectBountyTarget()["target"], boss)

    def test_arrow_geometry_stays_inside_gameplay_viewport_and_points_to_target(self):
        viewport = pygame.Rect(34, 44, int(vH.sW * .75) - 68, int(vH.sH) - 86)
        target = (viewport.right + 500, bG.lockY - 120)
        points, tip, direction = game._bounty_arrow_geometry(target, viewport)
        self.assertAlmostEqual(tip[0], viewport.right)
        self.assertTrue(viewport.top <= tip[1] <= viewport.bottom)
        self.assertGreater(direction[0], 0)
        self.assertLess(direction[1], 0)
        self.assertEqual(len(points), 7)

    def test_camera_rotation_changes_arrow_direction(self):
        world_target = (bG.playerPosX + 500, bG.playerPosY)
        viewport = pygame.Rect(34, 44, int(vH.sW * .75) - 68, int(vH.sH) - 86)
        bG.set_camera_angle(0)
        direction_zero = game._bounty_arrow_geometry(
            bG.world_to_screen(*world_target), viewport)[2]
        bG.set_camera_angle(90)
        direction_ninety = game._bounty_arrow_geometry(
            bG.world_to_screen(*world_target), viewport)[2]
        self.assertGreater(direction_zero[0], .9)
        self.assertLess(direction_ninety[1], -.9)


if __name__ == "__main__":
    unittest.main()
