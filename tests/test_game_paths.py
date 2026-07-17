import os
import random
import unittest
from types import MappingProxyType
from math import cos, hypot, pi, sin

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import pygame

import background as bG
from bossTypes import (Ache, BOSS_CATALOG, Bair, Beaudis, Chronos, Dissonance,
                       Hypno, Ishe, Kage, Malady, Rot)
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
            "touch": ("bair", "rot"),
            "sight": ("ishe", "chronos"),
            "chemesthesis": ("kage", "ache"),
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
        self.assertGreater(chem.maxHp, sound.maxHp * 1.4)
        self.assertIn("minefield", chem.interactionTags)

    def test_chemesthesis_projectiles_seed_long_lived_unaimed_fields(self):
        self._activate("chemesthesis")
        gamePaths._projectile_rng.seed(11)
        shots = [EnemyProjectile(0, 0, 0, 1, 20, 10, travel_range=100)
                 for _ in range(12)]
        gamePaths.tune_new_projectiles(shots, 0)
        self.assertTrue(all(shot.lifetime == 12 for shot in shots))
        self.assertTrue(all(shot.remainingRange == 260 for shot in shots))
        self.assertGreater(sum(abs(shot.direction) > .01 for shot in shots), 7)

    def test_non_sound_paths_escalate_to_bounded_level_nineteen_hordes(self):
        for key in ("touch", "sight", "chemesthesis", "phantasia"):
            self._activate(key)
            early = gamePaths.tune_encounter_pacing(
                {"patrol_size": 7, "max_world_encounters": 4,
                 "spawn_interval_seconds": 5.0, "curated_chance": .3}, 10)
            late = gamePaths.tune_encounter_pacing(
                {"patrol_size": 9, "max_world_encounters": 5,
                 "spawn_interval_seconds": 4.4, "curated_chance": .4}, 19)
            self.assertGreater(late["patrol_size"], early["patrol_size"])
            self.assertGreater(late["max_world_encounters"], early["max_world_encounters"])
            self.assertLess(late["spawn_interval_seconds"], early["spawn_interval_seconds"])
            self.assertLessEqual(gamePaths.projectile_cap(19), 420)

            base = ENEMY_CATALOG.create(
                "ranged_wanderer_hard", 0, 0, 19, random.Random(17))
            tuned = gamePaths.apply_enemy_identity(
                ENEMY_CATALOG.create(
                    "ranged_wanderer_hard", 0, 0, 19, random.Random(17)), 19)
            self.assertGreater(tuned.maxHp, base.maxHp * gamePaths.active().style.health)
            self.assertLess(
                tuned.attackCooldownMax / base.attackCooldownMax,
                gamePaths.active().style.attack_cooldown,
            )

    def test_placeholder_boss_rosters_are_registered_per_path(self):
        expected_types = {
            "bair": Bair, "rot": Rot, "ishe": Ishe, "chronos": Chronos,
            "kage": Kage, "ache": Ache, "hypno": Hypno, "malady": Malady,
        }
        for key, boss_type in expected_types.items():
            boss = BOSS_CATALOG.spawn(key, random.Random(2))
            self.assertIsInstance(boss, boss_type)
            self.assertEqual(boss.contentKey, key)
            expected_phases = ({"bair": 5, "rot": 7, "chronos": 7,
                                "kage": 4, "ache": 8,
                                "hypno": 5, "malady": 10}
                               .get(key, 4))
            self.assertEqual(len(boss.phaseLabels), expected_phases)

    def test_level_ten_bosses_share_one_third_health_and_one_survival_contract(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        pairs = (
            (Beaudis, Dissonance, 4, 9),
            (Bair, Rot, 4, 7),
            (Ishe, Chronos, 3, 7),
            (Kage, Ache, 3, 8),
            (Hypno, Malady, 4, 10),
        )
        for midpoint_type, final_type, midpoint_survival, finale_survival in pairs:
            midpoint = midpoint_type(rect.x, rect.y, random.Random(31))
            final = final_type(rect.x, rect.y, random.Random(31))
            self.assertAlmostEqual(midpoint.maxHp / final.maxHp, 1 / 3,
                                   delta=.005, msg=midpoint.bossName)
            self.assertLess(midpoint.damage, final.damage * .5, midpoint.bossName)
            midpoint_phases = getattr(midpoint, "PHASE_COUNT",
                                      len(getattr(midpoint, "phaseLabels", ())))
            final_phases = getattr(final, "PHASE_COUNT",
                                   len(getattr(final, "phaseLabels", ())))
            self.assertLess(midpoint_phases, final_phases, midpoint.bossName)
            midpoint_survivals = getattr(midpoint, "SURVIVAL_PHASES")
            midpoint_survivals = (tuple(midpoint_survivals) if isinstance(midpoint_survivals, dict)
                                  else tuple(midpoint_survivals))
            final_survivals = getattr(final, "SURVIVAL_PHASES")
            final_survivals = (tuple(final_survivals) if isinstance(final_survivals, dict)
                               else tuple(final_survivals))
            self.assertEqual(midpoint_survivals, (midpoint_survival,))
            self.assertIn(finale_survival, final_survivals)
            self.assertNotEqual(midpoint_survival, finale_survival)

    def test_each_path_midpoint_runs_one_half_health_survival_then_damage_finish(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        for boss_type, expected_finish in ((Bair, 5), (Ishe, 4), (Kage, 4), (Hypno, 5)):
            boss = boss_type(rect.x, rect.y, random.Random(37))
            boss.entranceRemaining = 0
            boss.actTransitionTimer = 0
            boss.phaseProtectionTimer = 0
            for _ in range(8):
                boss.take_damage(boss.maxHp * 2)
                if boss.survivalActive:
                    break
                boss._update_phase()
                boss.actTransitionTimer = 0
                boss.phaseProtectionTimer = 0
            self.assertTrue(boss.survivalActive, boss.bossName)
            self.assertEqual(boss.hp, boss.maxHp * .5)
            self.assertTrue(boss.take_damage(1000).blocked)
            boss.survivalRemaining = 0
            boss.debugPhaseLocked = False
            boss.actTransitionTimer = 0
            boss.updateEnemy(*boss._center(), [])
            self.assertFalse(boss.survivalActive)
            self.assertTrue(boss.midpointSurvivalComplete)
            self.assertEqual(boss.phase, expected_finish)

    def test_rot_burial_is_an_invulnerable_zero_health_finale_then_collapses(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 3.15)
        boss = Rot(rect.x, rect.y, random.Random(41))
        boss.entranceRemaining = 0
        boss.actTransitionTimer = boss.phaseProtectionTimer = 0
        for _ in range(6):
            boss.take_damage(boss.maxHp * 2)
            boss.actTransitionTimer = boss.phaseProtectionTimer = 0
        self.assertEqual(boss.phase, 7)
        self.assertTrue(boss.survivalActive)
        self.assertEqual(boss.survivalRemaining, 30.0)
        self.assertTrue(boss.take_damage(1000).blocked)
        boss.survivalRemaining = 0
        boss.actTransitionTimer = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.collapsing)
        self.assertEqual(boss.collapseRemaining, 10.0)

    def test_chronos_is_the_fragile_fast_final_boss_with_only_one_static_phase(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 1.2)
        boss = Chronos(rect.x, rect.y, random.Random(13))
        self.assertEqual(boss.maxHp, 240000)
        self.assertEqual(boss.SURVIVAL_PHASES, {4: 18.0, 7: 30.0})
        self.assertEqual([index + 1 for index, mode in enumerate(boss.movementModes)
                          if mode == "static"], [4])
        self.assertGreater(boss.speed, Ishe(rect.x, rect.y, random.Random(13)).speed)

    def test_chronos_patterns_are_small_mobile_volleys_without_field_hazards(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 1.2)
        boss = Chronos(rect.x, rect.y, random.Random(7))
        for phase in range(1, 8):
            boss.debug_set_phase(phase)
            shots = []
            boss._fire_pattern(*boss._arena_center(), shots)
            self.assertGreaterEqual(len(shots), 5)
            self.assertTrue(all(shot.size <= boss.size * .11 for shot in shots))
            self.assertTrue(all(shot.path in ("linear", "sine") for shot in shots))
            self.assertTrue(all(not shot.persistentHazard for shot in shots))

    def test_chronos_final_chase_replays_exact_past_angles_then_collapses_for_ten_seconds(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 1.2)
        boss = Chronos(rect.x, rect.y, random.Random(17))
        boss._set_chronos_phase(6)
        first = []
        boss._fire_pattern(*boss._arena_center(), first)
        remembered_angles = boss.attackMemory[-1][1]
        boss._set_chronos_phase(7)
        self.assertTrue(boss.survivalActive)
        self.assertEqual(boss.movementModes[6], "chase")
        self.assertTrue(boss.echoQueue)
        echoed = []
        boss._update_echoes(echoed, 10.0)
        self.assertEqual(len(echoed), len(remembered_angles))
        self.assertTrue(all(shot.owner.endswith("past_echo_survival") for shot in echoed))
        self.assertEqual(tuple(shot.direction for shot in echoed), remembered_angles)
        boss.debugPhaseLocked = False
        boss.entranceRemaining = 0
        boss.survivalRemaining = 0
        boss.updateEnemy(*boss._arena_center(), [])
        self.assertTrue(boss.collapsing)
        self.assertEqual(boss.collapseDuration, 10.0)
        boss.collapseRemaining = 0
        boss.updateEnemy(*boss._arena_center(), [])
        self.assertTrue(boss.is_dead())

    def test_phantasia_bosses_cover_five_pairs_and_ten_commandments(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        hypno = Hypno(rect.x, rect.y, random.Random(8))
        malady = Malady(rect.x, rect.y, random.Random(8))
        self.assertEqual(len(hypno.phaseLabels), 5)
        self.assertEqual(len(malady.phaseLabels), 10)
        self.assertEqual(malady.phaseLabels[0], "THRONE")
        self.assertEqual(malady.phaseLabels[-1], "ENOUGH")
        for boss in (hypno, malady):
            for phase in range(1, len(boss.phaseLabels) + 1):
                boss.debug_set_phase(phase)
                boss.actTransitionTimer = 0
                shots = []
                boss._fire_pattern(*boss._center(), shots)
                self.assertTrue(shots, f"{boss.bossName} phase {phase} fired nothing")

    def test_bosses_hide_redundant_overhead_health_bars(self):
        for key in BOSS_CATALOG.definitions:
            boss = BOSS_CATALOG.spawn(key, random.Random(3))
            self.assertFalse(boss.showOverheadHealthBar, key)

    def test_malady_rotates_eight_damage_phases_around_two_health_gates(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        self.assertEqual(boss.actTitle, "ACT I // THE DOCTRINE")
        boss.actTransitionTimer = 0
        boss.phaseProtectionTimer = 0
        boss.take_damage(boss.maxHp * 2)
        self.assertEqual(boss.hp, boss.maxHp * .5)
        self.assertEqual(boss.phase, 5)
        self.assertEqual(boss.actTitle, "ACT II // THE COVENANT")
        boss.debugPhaseLocked = False
        boss.survivalRemaining = 0
        boss.entranceRemaining = 0
        boss.actTransitionTimer = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertIn(boss.phase, boss.DAMAGE_PHASES)
        self.assertNotEqual(boss.phase, 5)
        boss.debug_set_phase(10)
        self.assertEqual(boss.actTitle, "ACT III // THE TESTIMONY")
        self.assertEqual(set(boss.DAMAGE_PHASES), {1, 2, 3, 4, 6, 7, 8, 9})
        self.assertEqual(boss.maxHp, 320000)
        self.assertGreaterEqual(boss.damage, 900)

    def test_malady_survival_phases_seal_vitality_and_advance_on_time(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        boss.debug_set_phase(5)
        self.assertTrue(boss.survivalActive)
        self.assertTrue(boss.vitalitySuppressed)
        self.assertEqual(boss.survivalRemaining, 22.0)
        hp = boss.hp
        self.assertTrue(boss.take_damage(1000).blocked)
        self.assertEqual(boss.hp, hp)

        previous_boss, previous_health = cS.activeBoss, cS.healthPoints
        try:
            cS.activeBoss = boss
            cS.healthPoints = max(1, cS.maxHealthPoints - 100)
            game.recoverPlayerHealth()
            self.assertEqual(cS.healthPoints, cS.maxHealthPoints - 100)
        finally:
            cS.activeBoss, cS.healthPoints = previous_boss, previous_health

        timed_boss = Malady(rect.x, rect.y, random.Random(6))
        timed_boss._set_dream_phase(5)
        timed_boss.entranceRemaining = 0
        timed_boss.actTransitionTimer = 0
        timed_boss.survivalRemaining = 0
        timed_boss.updateEnemy(*timed_boss._center(), [])
        self.assertIn(timed_boss.phase, timed_boss.DAMAGE_PHASES)
        self.assertFalse(timed_boss.vitalitySuppressed)

        timed_boss.firstSurvivalComplete = True
        timed_boss._set_dream_phase(10)
        timed_boss.actTransitionTimer = 0
        timed_boss.survivalRemaining = 0
        timed_boss.updateEnemy(*timed_boss._center(), [])
        self.assertTrue(timed_boss.collapsing)
        self.assertEqual(timed_boss.collapseRemaining, 10.0)

    def test_malady_survival_patterns_create_portals_pools_and_flowing_chains(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        boss.debug_set_phase(4)
        boss.actTransitionTimer = 0
        boss.entranceRemaining = 0
        shots = []
        boss._fire_pattern(*boss._center(), shots)
        boss._update_sequences(shots, .1)
        boss._spawn_pool(shots, boss._arena_center())
        self.assertEqual(len(boss.projectilePortals), 3)
        self.assertTrue(all(hasattr(portal, "remainingLifetime")
                            for portal in boss.projectilePortals))
        self.assertTrue(any(shot.owner.endswith("flowing_chain") for shot in shots))
        moving_shot = next(shot for shot in shots
                           if shot.path not in ("pool", "laser"))
        self.assertEqual(moving_shot.remainingRange, float("inf"))
        pool = next(shot for shot in shots if shot.path == "pool")
        player = pygame.Rect(pool.worldX, pool.worldY, 20, 20)
        self.assertFalse(pool.collides(player))
        pool.age = pool.telegraphDuration
        player.center = pool.world_rect().center
        self.assertTrue(pool.collides(player))
        boss._update_transient_portals(shots, 20)
        self.assertEqual(boss.projectilePortals, [])

    def test_ache_has_two_survival_gates_and_a_ten_second_finale(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.3)
        boss = Ache(rect.x, rect.y, random.Random(15))
        self.assertEqual(boss.maxHp, 280000)
        self.assertLess(boss.maxHp, Malady(rect.x, rect.y, random.Random(5)).maxHp)
        boss.actTransitionTimer = boss.phaseProtectionTimer = 0
        for expected_phase in (2, 3, 4):
            boss.take_damage(boss.maxHp * 2)
            self.assertEqual(boss.phase, expected_phase)
            boss.actTransitionTimer = boss.phaseProtectionTimer = 0
        self.assertTrue(boss.survivalActive)
        self.assertEqual(boss.survivalRemaining, 20.0)
        self.assertTrue(boss.take_damage(1000).blocked)

        boss.entranceRemaining = boss.actTransitionTimer = 0
        boss.survivalRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertEqual(boss.phase, 5)
        self.assertTrue(boss.firstSurvivalComplete)
        boss.debugPhaseLocked = False
        for expected_phase in (6, 7, 8):
            boss.actTransitionTimer = boss.phaseProtectionTimer = 0
            boss.take_damage(boss.maxHp * 2)
            self.assertEqual(boss.phase, expected_phase)
        self.assertTrue(boss.survivalActive)
        self.assertEqual(boss.survivalRemaining, 30.0)
        boss.actTransitionTimer = 0
        boss.survivalRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.collapsing)
        self.assertEqual(boss.collapseRemaining, 10.0)
        boss.collapseRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.is_dead())

    def test_ache_pairs_opposite_sines_and_separates_chip_from_beams(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.3)
        boss = Ache(rect.x, rect.y, random.Random(9))
        for phase in range(1, 9):
            boss.debug_set_phase(phase)
            boss.actTransitionTimer = 0
            shots = []
            boss._fire_pattern(boss._center()[0] + 200, boss._center()[1], shots)
            sine = [shot for shot in shots if shot.path == "sine"]
            self.assertTrue(sine, f"Ache phase {phase} has no helix")
            amplitudes = [round(shot.amplitude, 4) for shot in sine]
            self.assertTrue(all(-amplitude in amplitudes for amplitude in amplitudes))
            micro = [shot for shot in shots if "micro" in shot.owner]
            heavy = [shot for shot in shots if "heavy" in shot.owner]
            self.assertTrue(micro)
            self.assertTrue(all(shot.damage <= 130 for shot in micro))
            if heavy:
                self.assertTrue(all(shot.damage >= 690 for shot in heavy))

    def test_ache_lightning_is_a_slow_telegraphed_multi_angle_beam(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.3)
        boss = Ache(rect.x, rect.y, random.Random(12))
        bolt = boss._lightning([], 0, 700, "test")
        self.assertEqual(bolt.path, "lightning")
        self.assertGreater(len(bolt.lightningPoints), 4)
        slopes = [round((end[1]-start[1]) / max(1e-6, end[0]-start[0]), 2)
                  for start, end in zip(bolt.lightningPoints, bolt.lightningPoints[1:])]
        self.assertGreater(len(set(slopes)), 2)
        self.assertGreater(bolt.telegraphDuration, 1.0)
        target = pygame.Rect(bolt.worldX, bolt.worldY, 20, 20)
        self.assertFalse(bolt.collides(target))
        bolt.age = bolt.telegraphDuration
        bolt.lightningTravel = vH.tileSizeGlobal * 3
        self.assertTrue(bolt.collides(target))

    def test_ache_three_cube_orbit_has_foreground_and_background_depth(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.3)
        boss = Ache(rect.x, rect.y, random.Random(3))
        boss.age = 90
        elements = boss._orbit_elements((450, 350))
        self.assertEqual(len(elements), 3)
        self.assertLess(elements[0][0], elements[-1][0])
        surface = pygame.Surface((900, 700), pygame.SRCALPHA)
        boss.posX, boss.posY = 450-boss.size/2, 350-boss.size/2
        boss._draw_ache_body(surface)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 500)

    def test_malady_puppet_has_cardinal_motion_attack_poses_and_wide_arms(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        directions = (((1, 0), "east"), ((-1, 0), "west"),
                      ((0, -1), "north"), ((0, 1), "south"))
        for (delta_x, delta_y), expected in directions:
            boss = Malady(rect.x, rect.y, random.Random(5))
            boss._update_puppet_motion(
                boss.worldX - delta_x, boss.worldY - delta_y)
            self.assertEqual(boss.puppetFacing, expected)

        boss = Malady(rect.x, rect.y, random.Random(5))
        expected_poses = {1: "burst", 2: "radial", 3: "laser", 4: "chain"}
        for phase, pose in expected_poses.items():
            boss.debug_set_phase(phase)
            boss._fire_pattern(boss._center()[0] + 100,
                               boss._center()[1], [])
            self.assertEqual(boss.attackPose, pose)

        surface = pygame.Surface((900, 700), pygame.SRCALPHA)
        boss.posX, boss.posY = 450-boss.size/2, 350-boss.size/2
        boss.survivalActive = False
        boss._draw_dream_body(surface)
        bounds = pygame.mask.from_surface(surface).get_bounding_rects()
        union = bounds[0].unionall(bounds[1:])
        self.assertGreater(union.width, boss.size * 2.25)

    def test_malady_arm_orbit_crosses_front_then_recedes_behind_core(self):
        core = (400, 300)
        right = Malady._project_cube_orbit(core, 0, 120, 50)
        front = Malady._project_cube_orbit(core, pi/2, 120, 50)
        left = Malady._project_cube_orbit(core, pi, 120, 50)
        behind = Malady._project_cube_orbit(core, 3*pi/2, 120, 50)
        self.assertAlmostEqual(right[0], 520)
        self.assertGreater(front[1], core[1])
        self.assertGreater(front[2], 0)
        self.assertAlmostEqual(left[0], 280)
        self.assertLess(behind[1], core[1])
        self.assertLess(behind[2], 0)

    def test_malady_final_survival_dance_opens_beyond_first_survival(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        widths = []
        for phase in (5, 10):
            boss = Malady(rect.x, rect.y, random.Random(5))
            boss.debug_set_phase(phase)
            boss.posX, boss.posY = 450-boss.size/2, 350-boss.size/2
            boss.age = 140
            surface = pygame.Surface((900, 700), pygame.SRCALPHA)
            boss._draw_dream_body(surface)
            rects = pygame.mask.from_surface(surface).get_bounding_rects()
            widths.append(rects[0].unionall(rects[1:]).width)
        self.assertGreater(widths[1], widths[0] * 1.12)

    def test_malady_has_larger_court_anticipation_and_delayed_collapse(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        hypno = Hypno(rect.x, rect.y, random.Random(5))
        boss = Malady(rect.x, rect.y, random.Random(5))
        self.assertAlmostEqual(boss.arenaRadius, hypno.arenaRadius * 1.25)

        boss.debug_set_phase(3)
        boss.entranceRemaining = 0
        boss.actTransitionTimer = 0
        boss.attackCooldown = vH.frameRate * .12
        center = boss._center()
        boss.updateEnemy(center[0]+100, center[1], [])
        self.assertEqual(boss.attackPose, "laser")
        self.assertGreater(boss.attackAnticipation, 0)

        boss.firstSurvivalComplete = True
        boss.debugPhaseLocked = False
        boss._set_dream_phase(9)
        boss.phaseProtectionTimer = 0
        boss.actTransitionTimer = 0
        boss.hp = 10
        result = boss.take_damage(100)
        self.assertFalse(result.killed)
        self.assertEqual(boss.phase, 10)
        self.assertTrue(boss.survivalActive)
        self.assertEqual(boss.hp, 1)
        boss.actTransitionTimer = 0
        boss.survivalRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.collapsing)
        self.assertEqual(boss.collapseDuration, 10.0)
        boss.collapseRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.is_dead())

    def test_every_path_boss_blacks_out_the_exterior_of_its_arena(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        for boss_type in (Rot, Chronos, Ache, Malady):
            boss = boss_type(rect.x, rect.y, random.Random(7))
            surface = pygame.Surface((1920, 1080))
            surface.fill((73, 91, 109))
            boss._draw_path_arena(surface)
            center = bG.world_to_screen(*boss._arena_center())
            corners = ((0, 0), (surface.get_width()-1, 0),
                       (0, surface.get_height()-1),
                       (surface.get_width()-1, surface.get_height()-1))
            outside = max(corners, key=lambda point: hypot(
                point[0]-center[0], point[1]-center[1]))
            self.assertEqual(surface.get_at(outside)[:3], (0, 0, 0),
                             boss_type.__name__)
            if surface.get_rect().collidepoint(center):
                center_pixel = (round(center[0]), round(center[1]))
                self.assertNotEqual(surface.get_at(center_pixel)[:3], (0, 0, 0),
                                    boss_type.__name__)

    def test_phantasia_illusions_are_harmless_and_truth_is_marked(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        boss.debug_set_phase(2)
        shots = []
        boss._fire_pattern(*boss._center(), shots)
        self.assertTrue(any(shot.illusory for shot in shots))
        self.assertTrue(any(shot.truthMarked for shot in shots))
        player = pygame.Rect(shots[0].worldX, shots[0].worldY, 20, 20)
        illusion = next(shot for shot in shots if shot.illusory)
        player.topleft = (illusion.worldX, illusion.worldY)
        self.assertFalse(illusion.collides(player))

    def test_belief_clarity_and_contentment_offerings_are_tracked(self):
        cS.reset_dream_state()
        cS.alter_belief(4, false_rule=True)
        cS.alter_belief(-1, truth=True)
        self.assertEqual(cS.dreamState["belief"], 3)
        self.assertEqual(cS.dreamState["false_rules"], 1)
        self.assertEqual(cS.dreamState["truths_read"], 1)
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        boss.debug_set_phase(10)
        offering = boss.offeringPositions[0]
        boss._update_special_rules(offering["x"], offering["y"], .1)
        self.assertEqual(len(boss.acceptedOfferings), 1)
        self.assertTrue(boss.challenge_results()["measured_desire"])

    def test_commandment_sigils_render_all_ten_distinct_words(self):
        surface = pygame.Surface((900, 700), pygame.SRCALPHA)
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        names = []
        for phase in range(1, 11):
            names.append(boss._draw_commandment_sigil(
                surface, (450, 350), 82, 1.0, phase=phase))
        self.assertEqual(len(set(names)), 10)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 100)

    def test_kage_and_rot_cover_composite_and_accumulation_phases(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        kage = Kage(rect.x, rect.y, random.Random(8))
        rot = Rot(rect.x, rect.y, random.Random(8))
        self.assertEqual(kage.phaseLabels,
                         ("FEAST", "PROVOCATION", "STAGNANT MIRROR", "LURE"))
        self.assertEqual(rot.phaseLabels,
                         ("SEEP", "SILT", "SLUMP", "BLOOM",
                          "SINKHOLE", "MIASMA", "BURIAL"))
        for boss in (kage, rot):
            for phase in range(1, len(boss.phaseLabels) + 1):
                boss.debug_set_phase(phase)
                shots = []
                boss._fire_pattern(*boss._center(), shots)
                self.assertTrue(shots, f"{boss.bossName} phase {phase} fired nothing")
                self.assertTrue(all(boss.ownerPrefix in str(shot.owner) for shot in shots))

    def test_rot_phases_use_persistent_hazards_and_edge_reaching_fronts(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        for phase in range(1, 8):
            rot.debug_set_phase(phase)
            shots = []
            rot._fire_pattern(*rot._center(), shots)
            self.assertTrue(any(shot.path in ("pool", "mine", "orbit", "laser")
                                for shot in shots), phase)
            moving = [shot for shot in shots if shot.path in ("linear", "sine")]
            self.assertTrue(moving, phase)
            self.assertTrue(all(shot.remainingRange == float("inf") for shot in moving))
            self.assertTrue(all(390 <= shot.damage <= 520 for shot in shots), phase)

    def test_rot_only_uses_static_or_sluggish_pathed_movement_and_three_acts(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        self.assertEqual(rot.movementModes[0], "static")
        self.assertEqual(set(rot.movementModes), {"static", "path"})
        self.assertLess(rot.speed, .05)
        self.assertEqual(rot.actTitle, "ACT I // ACCUMULATION")
        self.assertGreater(rot.actTransitionTimer, 0)
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.debug_set_phase(3)
        self.assertEqual(rot.actTitle, "ACT II // STAGNATION")
        self.assertTrue(rot.take_damage(10).blocked)
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.debug_set_phase(6)
        self.assertEqual(rot.actTitle, "ACT III // BURIAL")

    def test_rot_health_gates_require_six_damage_movements_before_burial(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.take_damage(rot.maxHp * 2)
        self.assertEqual(rot.hp, rot.maxHp * 5 / 6)
        self.assertFalse(rot.is_dead())
        rot.updateEnemy(*rot._center(), [])
        self.assertEqual(rot.phase, 2)

    def test_player_build_snapshot_is_immutable(self):
        cS.reset_upgrade_tracking()
        cS.record_upgrade("Crit Chance", "Rare", "additive")
        cS.record_upgrade("Crit Damage", "Rare", "additive")
        snapshot = cS.player_build_snapshot()
        self.assertIsInstance(snapshot, MappingProxyType)
        self.assertEqual(snapshot["dominant_offense"], "critical")
        with self.assertRaises(TypeError):
            snapshot["dominant_offense"] = "volley"

    def test_boss_afflictions_stack_exposure_but_never_immobilize_player(self):
        cS.reset_boss_afflictions()
        cS.apply_boss_affliction("slow", 2.0, .3, 4.0)
        self.assertEqual(cS.bossAfflictions["exposure"], 4.0)
        self.assertGreaterEqual(cS.boss_movement_multiplier(), .58)
        cS.apply_boss_affliction("pull", 1.0, .4, 8.0, (100, 100))
        self.assertEqual(cS.bossAfflictions["exposure"], 10.0)
        self.assertEqual(cS.bossAfflictions["pull_source"], (100, 100))
        cS.update_boss_afflictions(3.0)
        self.assertIsNone(cS.bossAfflictions["pull_source"])

    def test_kage_ache_and_rot_render_their_visual_diagrams(self):
        surface = pygame.Surface((1280, 720), pygame.SRCALPHA)
        world_x, world_y = bG.screen_to_world(640, 360)
        for boss_type in (Kage, Ache, Rot):
            boss = boss_type(world_x - vH.tileSizeGlobal,
                             world_y - vH.tileSizeGlobal, random.Random(4))
            boss.actTransitionTimer = 0
            for phase in range(1, len(boss.phaseLabels) + 1):
                boss.debug_set_phase(phase)
                boss.actTransitionTimer = 0
                boss.drawEnemy(surface)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 0)

    def test_rot_sludge_pools_are_slow_long_lived_persistent_hazards(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        shots = []
        rot._pool(shots, *rot._center(), 440, "test_pool")
        self.assertEqual(len(shots), 1)
        pool = shots[0]
        self.assertEqual(pool.path, "pool")
        self.assertTrue(pool.persistentHazard)
        self.assertGreaterEqual(pool.lifetime, 15)
        self.assertEqual(pool.affliction, "slow")

    def test_rot_lanes_follow_camera_orientation_and_report_mastery(self):
        original = bG.cameraAngleDegrees
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        try:
            rot = Rot(rect.x, rect.y, random.Random(4))
            bG.set_camera_angle(0)
            first = rot._camera_cardinal_angle()
            bG.set_camera_angle(90)
            second = rot._camera_cardinal_angle()
            self.assertNotAlmostEqual(first, second)
            self.assertTrue(rot.challenge_results()["clean_traversal"])
            rot.peakExposure = 4
            self.assertFalse(rot.challenge_results()["clean_traversal"])
        finally:
            bG.set_camera_angle(original)

    def test_rot_has_one_uniform_hitbox_and_no_stagger_weak_point(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        self.assertEqual([part for part, _ in rot.get_screen_hitboxes()], ["body"])
        result = rot.take_damage(500, "anything")
        self.assertTrue(result.applied)
        self.assertEqual(result.amount, 500)
        self.assertFalse(rot.isStaggered)
        self.assertEqual(rot.stagger, 0)
        self.assertGreater(rot.maxHp, Malady(rect.x, rect.y, random.Random(5)).maxHp)

    def test_rot_mud_banks_warn_then_take_space_without_becoming_weak_points(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        rot._raise_mud_bank(0, advancing=True)
        self.assertEqual(len(rot.mudBanks), 1)
        self.assertFalse(rot.movement_obstacles())
        rot._update_terrain(*rot._center(), 1.9)
        self.assertEqual(len(rot.movement_obstacles()), 1)
        self.assertEqual([part for part, _ in rot.get_screen_hitboxes()], ["body"])

    def test_sin_sigils_are_distinct_and_animate_across_every_phase(self):
        surface = pygame.Surface((900, 700), pygame.SRCALPHA)
        world_x, world_y = bG.screen_to_world(450, 350)
        for boss_type, expected_count in ((Kage, 4), (Rot, 7)):
            boss = boss_type(world_x, world_y, random.Random(5))
            names = [name for name, _ in boss.SIN_SIGILS]
            self.assertEqual(len(names), expected_count)
            self.assertEqual(len(set(names)), expected_count)
            for phase in range(1, expected_count + 1):
                boss.debug_set_phase(phase)
                boss._draw_sigil(surface, (450, 350), 90, .5, .2, phase=phase)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 100)

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

    def test_rot_is_the_slow_heavy_touch_finale(self):
        self._activate("touch")
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 3.15)
        boss = Rot(rect.x, rect.y, random.Random(4))
        boss.actTransitionTimer = 0
        boss.phaseProtectionTimer = 0
        boss.debug_set_phase(7)
        projectiles = []
        center_x, center_y = boss._center()
        boss._fire_pattern(center_x + 200, center_y, projectiles)
        self.assertGreaterEqual(len(projectiles), 11)
        self.assertTrue(all(shot.speed < .3 for shot in projectiles))
        self.assertTrue(all(shot.damage >= 390 for shot in projectiles))
        self.assertEqual(boss.arenaShape, "square")

    def test_touch_bosses_cover_bair_and_rots_accumulation_phases(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.7)
        bair = Bair(rect.x, rect.y, random.Random(4))
        rot = Rot(rect.x, rect.y, random.Random(4))
        self.assertEqual(len(bair.phaseLabels), 5)
        self.assertEqual(rot.phaseLabels, ("SEEP", "SILT", "SLUMP", "BLOOM",
                                           "SINKHOLE", "MIASMA", "BURIAL"))
        for phase in range(1, 8):
            rot.debug_set_phase(phase)
            shots = []
            rot._fire_pattern(*rot._center(), shots)
            self.assertTrue(shots, f"Rot phase {phase} fired nothing")

    def test_path_boss_arenas_have_distinct_shapes_and_center_spawns(self):
        expected = {"rot": "square", "chronos": "triangle", "ache": "jagged",
                    "malady": "atomic"}
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        for key, shape in expected.items():
            boss = BOSS_CATALOG.spawn(key, random.Random(6))
            self.assertEqual(boss.arenaShape, shape)
            self.assertLessEqual(abs(boss._center()[0]-center[0]), vH.tileSizeGlobal*2)
            self.assertLessEqual(abs(boss._center()[1]-center[1]), vH.tileSizeGlobal*2)
            outside = (center[0]+boss.arenaRadius*2, center[1]+boss.arenaRadius*2)
            constrained = boss.constrain_player_position(*outside, cS.playerSize)
            constrained_center = (constrained[0]+cS.playerSize/2,
                                  constrained[1]+cS.playerSize/2)
            self.assertTrue(boss._point_in_polygon(constrained_center,
                                                   boss._arena_vertices()))

    def test_atomic_arena_draw_avoids_giant_rotated_surfaces(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        surface = pygame.Surface((900, 700))
        original_rotate = pygame.transform.rotate

        def reject_giant_rotation(source, angle):
            self.assertLess(source.get_width(), boss.arenaRadius)
            return original_rotate(source, angle)

        pygame.transform.rotate = reject_giant_rotation
        try:
            boss._draw_path_arena(surface)
        finally:
            pygame.transform.rotate = original_rotate

    def test_chemical_field_reuses_its_large_phase_surface(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Kage(rect.x, rect.y, random.Random(5))
        surface = pygame.Surface((900, 700))
        boss._draw_field_diagram(surface)
        cached = boss._fieldDiagramSurface
        boss.age += 1
        boss._draw_field_diagram(surface)
        self.assertIs(boss._fieldDiagramSurface, cached)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 100)

    def test_arena_containment_never_moves_interior_player_positions(self):
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        for boss_type in (Rot, Chronos, Ache, Malady):
            boss = boss_type(center[0], center[1], random.Random(11))
            # One quarter-radius is safely inside even the triangular arena.
            for angle in (0, pi/2, pi, 3*pi/2):
                player_x = center[0]+cos(angle)*boss.arenaRadius*.25-cS.playerSize/2
                player_y = center[1]+sin(angle)*boss.arenaRadius*.25-cS.playerSize/2
                constrained = boss.constrain_player_position(
                    player_x, player_y, cS.playerSize)
                self.assertAlmostEqual(constrained[0], player_x, places=4,
                                       msg=f"{boss_type.__name__} moved interior x")
                self.assertAlmostEqual(constrained[1], player_y, places=4,
                                       msg=f"{boss_type.__name__} moved interior y")

    def test_sight_triangle_upper_right_edge_contains_full_player_body(self):
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        boss = Chronos(center[0], center[1], random.Random(17))
        vertices = boss._arena_vertices()
        start, end = vertices[0], vertices[1]  # top-right sloping edge
        midpoint = ((start[0]+end[0])/2, (start[1]+end[1])/2)
        dx, dy = end[0]-start[0], end[1]-start[1]
        length = hypot(dx, dy)
        outward = (dy/length, -dx/length)
        attempted_center = (midpoint[0]+outward[0]*cS.playerSize*1.5,
                            midpoint[1]+outward[1]*cS.playerSize*1.5)
        corrected = boss.constrain_player_position(
            attempted_center[0]-cS.playerSize/2,
            attempted_center[1]-cS.playerSize/2, cS.playerSize)
        corrected_center = (corrected[0]+cS.playerSize/2,
                            corrected[1]+cS.playerSize/2)
        self.assertTrue(boss._point_in_polygon(corrected_center, vertices))
        nearest, _, distance_sq = boss._closest_boundary_point(corrected_center, vertices)
        self.assertGreaterEqual(distance_sq ** .5, cS.playerSize*.7)

    def test_boundary_projection_preserves_tangential_sliding(self):
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        for boss_type in (Rot, Chronos, Ache, Malady):
            boss = boss_type(center[0], center[1], random.Random(23))
            vertices = boss._arena_vertices()
            start, end = vertices[0], vertices[1]
            dx, dy = end[0]-start[0], end[1]-start[1]
            length = hypot(dx, dy)
            outward = (dy/length, -dx/length)
            corrected_points = []
            for amount in (.3, .7):
                boundary = (start[0]+dx*amount, start[1]+dy*amount)
                attempted = (boundary[0]+outward[0]*cS.playerSize,
                             boundary[1]+outward[1]*cS.playerSize)
                corrected = boss.constrain_player_position(
                    attempted[0]-cS.playerSize/2,
                    attempted[1]-cS.playerSize/2, cS.playerSize)
                corrected_points.append((corrected[0]+cS.playerSize/2,
                                         corrected[1]+cS.playerSize/2))
            travel = hypot(corrected_points[1][0]-corrected_points[0][0],
                           corrected_points[1][1]-corrected_points[0][1])
            self.assertGreater(travel, length*.25,
                               f"{boss_type.__name__} collapsed sliding to center")

    def test_square_corner_rejects_repeated_outward_input_without_vibration(self):
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        boss = Rot(center[0], center[1], random.Random(29))
        corner = boss._arena_vertices()[1]
        margin = cS.playerSize * .72
        stable_center = (corner[0] - margin, corner[1] + margin)
        stable = (stable_center[0] - cS.playerSize/2,
                  stable_center[1] - cS.playerSize/2)
        for _ in range(20):
            attempted = (stable[0] + 4, stable[1] - 4)
            corrected = boss.constrain_player_position(
                attempted[0], attempted[1], cS.playerSize)
            self.assertAlmostEqual(corrected[0], stable[0], places=4)
            self.assertAlmostEqual(corrected[1], stable[1], places=4)
            stable = corrected

    def test_mobile_path_bosses_expose_chase_static_and_path_movement(self):
        for boss_type in (Chronos, Ache, Malady):
            modes = set(boss_type.movementModes)
            self.assertTrue({"chase", "static", "path"}.issubset(modes), boss_type.__name__)
        self.assertEqual(set(Rot.movementModes), {"static", "path"})

    def test_bair_touch_gates_use_square_paths_and_heavy_volleys(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.7)
        bair = Bair(rect.x, rect.y, random.Random(4))
        bair.debug_set_phase(4)
        self.assertEqual(len(bair.projectilePortals), 2)
        self.assertTrue(all(portal.movementPath == "square"
                            for portal in bair.projectilePortals))
        shots = []
        bair.portalCooldown = 0
        bair._update_touch_portals(*bair._center(), shots, .1)
        self.assertTrue(shots)
        self.assertTrue(all(shot.speed < .5 and shot.damage >= 240 for shot in shots))

    def test_sight_bosses_expose_meaningful_symbols(self):
        self.assertEqual([name for name, _ in Ishe.SIGHT_SYMBOLS],
                         ["GLIMPSE", "BLINK", "FLASH", "AFTERGLOW"])
        surface = pygame.Surface((900, 700), pygame.SRCALPHA)
        world_x, world_y = bG.screen_to_world(450, 350)
        boss = Chronos(world_x, world_y, random.Random(4))
        for phase in range(1, 4):
            boss.phase = phase
            boss.drawEnemy(surface)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 100)

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
