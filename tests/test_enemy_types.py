import os
import random
import unittest
from math import pi
import pygame

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
from enemyTypes import (BASE_ENEMY_SPEED_SCALE, ENEMY_CATALOG, ParentEnemy, PillarEnemy,
                        ShotgunEnemy, SnakeEnemy, WanderingRangedEnemy)
from enemyTypes import VolleyEnemy
from enemyTypes import LaserEnemy
from enemyTypes import BombEnemy
from enemyTypes import ArsenalMiniBoss
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

    def test_opening_pool_withholds_advanced_close_range_types(self):
        level_zero = {definition.key for definition in ENEMY_CATALOG.available(0)}
        self.assertIn("ranged_wanderer", level_zero)
        self.assertNotIn("shotgunner", level_zero)
        self.assertNotIn("snake", level_zero)
        self.assertNotIn("parent", level_zero)

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

    def test_parent_thresholds_birth_fragile_children_once(self):
        enemy = ENEMY_CATALOG.create("parent", self.spawn_x, self.spawn_y, 3, random.Random(8))
        self.assertIsInstance(enemy, ParentEnemy)
        enemy.take_damage(enemy.maxHp * .31)
        self.assertEqual(enemy.pendingChildren, 2)
        enemy.updateEnemy(self.spawn_x + 100, self.spawn_y, [])
        enemy.updateEnemy(self.spawn_x + 100, self.spawn_y, [])
        self.assertEqual(len(enemy.spawnedEnemies), 2)
        self.assertTrue(all(child.threatCost == .5 for child in enemy.spawnedEnemies))
        enemy.take_damage(0)
        self.assertEqual(enemy.pendingChildren, 0)

    def test_pillar_fires_six_alternating_four_way_volleys(self):
        enemy = ENEMY_CATALOG.create("pillar", self.spawn_x, self.spawn_y, 3, random.Random(4))
        self.assertIsInstance(enemy, PillarEnemy)
        enemy.awarenessState = "alerted"
        enemy.state = "firing"
        projectiles = []
        for _ in range(6):
            enemy.stateTimer = 0
            enemy.updateEnemy(self.spawn_x + 100, self.spawn_y, projectiles)
        self.assertEqual(len(projectiles), 24)
        self.assertEqual(enemy.volleyIndex, 6)
        first = [round(projectiles[index].direction, 3) for index in range(4)]
        second = [round(projectiles[index].direction, 3) for index in range(4, 8)]
        self.assertNotEqual(first, second)

    def test_volley_tiers_gain_pellets_but_reduce_damage_per_pellet(self):
        samples = []
        damage_fractions = []
        for level, tier in ((0, "small"), (3, "medium"), (6, "large")):
            enemy = ENEMY_CATALOG.create(f"volley_{tier}", self.spawn_x, self.spawn_y,
                                         level, random.Random(3))
            self.assertIsInstance(enemy, VolleyEnemy)
            enemy.awarenessState = "alerted"
            enemy.charging = True
            enemy.chargeRemaining = 0
            projectiles = []
            enemy.updateEnemy(self.spawn_x + 100, self.spawn_y, projectiles)
            samples.append(projectiles)
            damage_fractions.append(projectiles[0].damage / enemy.damage)
        self.assertEqual([len(sample) for sample in samples], [4, 7, 10])
        self.assertGreater(damage_fractions[0], damage_fractions[2])

    def test_laser_tiers_are_telegraphed_and_large_controls_two_directions(self):
        enemy = ENEMY_CATALOG.create("laser_large", self.spawn_x, self.spawn_y,
                                     7, random.Random(2))
        self.assertIsInstance(enemy, LaserEnemy)
        projectiles = []
        enemy._fire(self.spawn_x + 100, self.spawn_y, projectiles)
        self.assertEqual(len(projectiles), 2)
        self.assertTrue(all(projectile.path == "laser" for projectile in projectiles))
        self.assertTrue(all(projectile.telegraphDuration >= 1.0 for projectile in projectiles))
        self.assertAlmostEqual(abs(projectiles[1].direction - projectiles[0].direction), pi)

    def test_large_bomb_enemy_creates_three_visible_blast_zones(self):
        enemy = ENEMY_CATALOG.create("bomb_large", self.spawn_x, self.spawn_y,
                                     7, random.Random(2))
        self.assertIsInstance(enemy, BombEnemy)
        projectiles = []
        enemy._fire(self.spawn_x + 100, self.spawn_y, projectiles)
        self.assertEqual(len(projectiles), 3)
        self.assertTrue(all(projectile.path == "bomb" for projectile in projectiles))
        self.assertTrue(all(projectile.blastRadius > projectile.size for projectile in projectiles))
        self.assertFalse(projectiles[0].collides(pygame.Rect(self.spawn_x, self.spawn_y, 20, 20)))

    def test_miniboss_transitions_are_brief_invulnerable_and_change_attacks(self):
        enemy = ENEMY_CATALOG.create("miniboss_arsenal", self.spawn_x, self.spawn_y,
                                     8, random.Random(2))
        self.assertIsInstance(enemy, ArsenalMiniBoss)
        enemy.take_damage(enemy.maxHp * .35)
        self.assertEqual(enemy.phase, 1)
        self.assertTrue(enemy.invulnerable)
        self.assertTrue(enemy.transitionCleanupRequested)
        blocked = enemy.take_damage(10)
        self.assertTrue(blocked.blocked)
        self.assertLessEqual(enemy.transitionRemaining, vH.frameRate)
        projectiles = []
        enemy.invulnerable = False
        enemy.attackCooldown = 0
        enemy.updateEnemy(self.spawn_x + 100, self.spawn_y, projectiles)
        self.assertTrue(projectiles)
        self.assertTrue(all(projectile.path == "laser" for projectile in projectiles))

    def test_minibosses_are_level_gated_guaranteed_spawns_not_random_choices(self):
        self.assertEqual(ENEMY_CATALOG.definitions["miniboss_arsenal"].min_level, 5)
        self.assertEqual(ENEMY_CATALOG.definitions["miniboss_siege"].min_level, 15)
        self.assertTrue(ENEMY_CATALOG.definitions["miniboss_arsenal"].guaranteed_only)
        self.assertTrue(ENEMY_CATALOG.definitions["miniboss_siege"].guaranteed_only)
        rng = random.Random(11)
        choices = {ENEMY_CATALOG.choose(10, rng).key for _ in range(500)}
        self.assertNotIn("miniboss_arsenal", choices)
        self.assertNotIn("miniboss_siege", choices)


if __name__ == "__main__":
    unittest.main()
