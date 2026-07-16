import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import pygame

import background as bG
from bossTypes import (BOSS_CATALOG, Bair, Chronos, Hypno, Ishe, Kage,
                       Malady, Rot, Sting)
import character as game
import characterStats as cS
from enemyProjectile import EnemyProjectile
from enemyTypes import ENEMY_CATALOG
import gamePaths
import variableHolster as vH


class GamePathTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        pygame.init()

    def tearDown(self):
        gamePaths.select("sound")
        gamePaths.activate_selected()

    def _activate(self, key):
        gamePaths.select(key)
        gamePaths.activate_selected()

    def test_catalog_exposes_all_five_isolated_content_paths(self):
        self.assertEqual(tuple(gamePaths.PATHS), (
            "sound", "touch", "sight", "chemesthesis", "phantasia"))
        expected = {
            "sound": ("beaudis", "dissonance"),
            "touch": ("bair", "sting"),
            "sight": ("ishe", "chronos"),
            "chemesthesis": ("kage", "rot"),
            "phantasia": ("hypno", "malady"),
        }
        self.assertEqual({key: (path.mid_boss, path.final_boss)
                          for key, path in gamePaths.PATHS.items()}, expected)

    def test_map_profiles_have_distinct_structural_silhouettes(self):
        sight = bG.generate_sight_battleground()
        chem = bG.generate_chemesthesis_battleground()
        dream = bG.generate_phantasia_battleground()

        def count(room, tile):
            return sum(value == tile for row in room for value, _ in row)

        self.assertEqual(count(sight, 3) + count(sight, 4), 0)
        self.assertEqual(count(chem, 3), 0)
        self.assertGreater(count(chem, 4), 20)
        self.assertGreater(count(dream, 3), 100)
        self.assertGreater(count(dream, 4), 80)
        self.assertGreater(len(dream), len(sight))

    def test_touch_map_is_denser_and_keeps_a_safe_open_cistern(self):
        sound = bG.generate_battleground(87)
        touch = bG.generate_touch_battleground(87)
        sound_walls = sum(tile in bG.RAISED_TILES for row in sound for tile, _ in row)
        touch_walls = sum(tile in bG.RAISED_TILES for row in touch for tile, _ in row)
        center = len(touch) // 2
        self.assertGreater(touch_walls, sound_walls)
        self.assertEqual(touch[center][center][0], 2)

    def test_enemy_profiles_apply_without_mutating_shared_definitions(self):
        sound = ENEMY_CATALOG.create("ranged_wanderer", 0, 0, 8, random.Random(17))

        self._activate("touch")
        touch = gamePaths.apply_enemy_identity(
            ENEMY_CATALOG.create("ranged_wanderer", 0, 0, 8, random.Random(17)))
        self.assertLess(touch.speed, sound.speed)
        self.assertGreater(touch.size, sound.size)
        self.assertGreater(touch.maxHp, sound.maxHp)

        self._activate("sight")
        sight = gamePaths.apply_enemy_identity(
            ENEMY_CATALOG.create("ranged_wanderer", 0, 0, 8, random.Random(17)))
        self.assertGreater(sight.speed, sound.speed)
        self.assertLess(sight.size, sound.size)
        self.assertLess(sight.maxHp, sound.maxHp)
        self.assertLess(sight.attackCooldownMax, sound.attackCooldownMax)
        self.assertLess(sight.attackRangeTiles, sound.attackRangeTiles)

        self._activate("chemesthesis")
        chem = gamePaths.apply_enemy_identity(
            ENEMY_CATALOG.create("ranged_wanderer", 0, 0, 8, random.Random(17)))
        self.assertGreater(chem.maxHp, sound.maxHp * 2)
        self.assertIn("minefield", chem.interactionTags)

    def test_chemesthesis_projectiles_seed_long_lived_unaimed_fields(self):
        self._activate("chemesthesis")
        gamePaths._projectile_rng.seed(11)
        shots = [EnemyProjectile(0, 0, 0, 1, 20, 10, travel_range=100)
                 for _ in range(12)]
        gamePaths.tune_new_projectiles(shots, 0)
        self.assertTrue(all(shot.lifetime == 18 for shot in shots))
        self.assertTrue(all(shot.remainingRange == 400 for shot in shots))
        self.assertGreater(sum(abs(shot.direction) > .01 for shot in shots), 7)

    def test_placeholder_boss_rosters_are_registered_per_path(self):
        expected_types = {
            "bair": Bair, "sting": Sting, "ishe": Ishe, "chronos": Chronos,
            "kage": Kage, "rot": Rot, "hypno": Hypno, "malady": Malady,
        }
        for key, boss_type in expected_types.items():
            boss = BOSS_CATALOG.spawn(key, random.Random(2))
            self.assertIsInstance(boss, boss_type)
            self.assertEqual(boss.contentKey, key)
            self.assertEqual(len(boss.phaseLabels), 3)

    def test_shared_progression_routes_levels_ten_and_twenty_to_active_roster(self):
        for path_key in ("touch", "sight", "chemesthesis", "phantasia"):
            self._activate(path_key)
            game.resetAllStats()
            path = gamePaths.active()
            cS.currentLevel = 10
            cS.enemySpawnTimer = 999999
            game.handlingEnemyCreation()
            self.assertEqual(cS.activeBoss.contentKey, path.mid_boss)

            cS.activeBoss = None
            cS.enemyHolster.clear()
            cS.enemySpawningEnabled = True
            cS.beaudisDefeated = True
            cS.currentLevel = 20
            game.handlingEnemyCreation()
            self.assertEqual(cS.activeBoss.contentKey, path.final_boss)

    def test_sting_retains_large_slow_touch_projectile_placeholder(self):
        self._activate("touch")
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.35)
        boss = Sting(rect.x, rect.y, random.Random(4))
        boss.entranceRemaining = 0
        boss.phase = 3
        projectiles = []
        center_x, center_y = boss._center()
        boss._fire_pattern(center_x + 200, center_y, projectiles)
        self.assertGreaterEqual(len(projectiles), 11)
        self.assertTrue(all(shot.speed < 1 for shot in projectiles))
        self.assertTrue(all(shot.damage >= 300 for shot in projectiles))

    def test_exclusive_encounter_factories_are_scoped_to_one_path(self):
        calls = []

        def factory(*args, **kwargs):
            calls.append("sight")
            return None

        gamePaths.register_exclusive_encounter("sight", factory)
        try:
            self._activate("sound")
            gamePaths.ENCOUNTERS.spawn_patrol(0, 0, ())
            self.assertEqual(calls, [])
            self._activate("sight")
            gamePaths.ENCOUNTERS.spawn_patrol(0, 0, ())
            self.assertEqual(calls, ["sight"])
        finally:
            gamePaths._EXCLUSIVE_ENCOUNTER_FACTORIES["sight"].remove(factory)


if __name__ == "__main__":
    unittest.main()
