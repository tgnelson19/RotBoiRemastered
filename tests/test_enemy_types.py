import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
from enemyTypes import BASE_ENEMY_SPEED_SCALE, ENEMY_CATALOG, ShotgunEnemy, SnakeEnemy, WanderingRangedEnemy
import variableHolster as vH


class EnemyTypeTests(unittest.TestCase):
    def setUp(self):
        self.frame_rate = vH.frameRate
        self.has_delta = vH.hasFrameDelta
        vH.frameRate = 120
        vH.hasFrameDelta = False
        self.spawn_x = bG.spawnX + vH.tileSizeGlobal * 2
        self.spawn_y = bG.spawnY

    def tearDown(self):
        vH.frameRate = self.frame_rate
        vH.hasFrameDelta = self.has_delta

    def test_every_enemy_type_is_available_from_level_zero(self):
        level_zero = {definition.key for definition in ENEMY_CATALOG.available(0)}
        self.assertIn("ranged_wanderer", level_zero)
        self.assertIn("shotgunner", level_zero)
        self.assertIn("snake", level_zero)
        self.assertEqual(level_zero, set(ENEMY_CATALOG.definitions))

    def test_wandering_ranged_enemy_fires_single_aimed_projectile(self):
        enemy = ENEMY_CATALOG.create("ranged_wanderer", self.spawn_x, self.spawn_y, 2, random.Random(2))
        self.assertIsInstance(enemy, WanderingRangedEnemy)
        enemy.attackCooldown = 0
        projectiles = []
        enemy.updateEnemy(self.spawn_x + 150, self.spawn_y, projectiles)
        self.assertEqual(len(projectiles), 1)
        self.assertEqual(projectiles[0].speed, 1.4)
        self.assertGreaterEqual(projectiles[0].size, 12)

    def test_shotgunner_fires_variable_pellet_volley(self):
        enemy = ENEMY_CATALOG.create("shotgunner", self.spawn_x, self.spawn_y, 2, random.Random(4))
        self.assertIsInstance(enemy, ShotgunEnemy)
        enemy.attackCooldown = 0
        projectiles = []
        enemy.updateEnemy(self.spawn_x + 120, self.spawn_y, projectiles)
        self.assertGreaterEqual(len(projectiles), 4)
        self.assertLessEqual(len(projectiles), 7)
        self.assertGreater(len({round(projectile.speed, 2) for projectile in projectiles}), 1)
        self.assertGreater(len({round(projectile.size, 2) for projectile in projectiles}), 1)

    def test_snake_head_is_invulnerable_until_segments_are_destroyed(self):
        enemy = ENEMY_CATALOG.create("snake", self.spawn_x, self.spawn_y, 2, random.Random(7))
        self.assertIsInstance(enemy, SnakeEnemy)
        self.assertGreater(enemy.segmentSpacing, enemy.segmentSize)
        blocked = enemy.take_damage(999, "head")
        self.assertTrue(blocked.blocked)
        self.assertFalse(blocked.applied)

        segment_ids = [segment["id"] for segment in enemy.segments]
        for segment_id in segment_ids:
            result = enemy.take_damage(999, segment_id)
            self.assertTrue(result.applied)
        self.assertEqual(enemy.segments, [])

        killed = enemy.take_damage(999, "head")
        self.assertTrue(killed.applied)
        self.assertTrue(killed.killed)

    def test_catalog_uses_reduced_global_enemy_speed(self):
        self.assertLessEqual(BASE_ENEMY_SPEED_SCALE, .66)


if __name__ == "__main__":
    unittest.main()
