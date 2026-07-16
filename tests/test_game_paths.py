import os
import random
import unittest
from types import MappingProxyType
from math import cos, pi, sin

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
            expected_phases = ({"bair": 5, "sting": 10, "kage": 4, "rot": 7,
                                "hypno": 5, "malady": 10}
                               .get(key, 3))
            self.assertEqual(len(boss.phaseLabels), expected_phases)

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

    def test_malady_uses_three_acts_and_health_gates_every_commandment(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.4)
        boss = Malady(rect.x, rect.y, random.Random(5))
        self.assertEqual(boss.actTitle, "ACT I // THE DOCTRINE")
        boss.actTransitionTimer = 0
        boss.phaseProtectionTimer = 0
        boss.take_damage(boss.maxHp * 2)
        self.assertEqual(boss.hp, boss.maxHp * .9)
        boss.updateEnemy(*boss._center(), [])
        self.assertEqual(boss.phase, 2)
        boss.debug_set_phase(4)
        self.assertEqual(boss.actTitle, "ACT II // THE COVENANT")
        boss.debug_set_phase(7)
        self.assertEqual(boss.actTitle, "ACT III // THE TESTIMONY")

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

    def test_chemesthesis_bosses_cover_composite_and_seven_sin_phases(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        kage = Kage(rect.x, rect.y, random.Random(8))
        rot = Rot(rect.x, rect.y, random.Random(8))
        self.assertEqual(kage.phaseLabels,
                         ("FEAST", "PROVOCATION", "STAGNANT MIRROR", "LURE"))
        self.assertEqual(rot.phaseLabels,
                         ("CROWN", "HOARD", "PULL", "BORROWED SHAPE",
                          "CONSUMPTION", "RETORT", "THE ROT"))
        for boss in (kage, rot):
            for phase in range(1, len(boss.phaseLabels) + 1):
                boss.debug_set_phase(phase)
                shots = []
                boss._fire_pattern(*boss._center(), shots)
                self.assertTrue(shots, f"{boss.bossName} phase {phase} fired nothing")
                self.assertTrue(all(boss.ownerPrefix in str(shot.owner) for shot in shots))

    def test_chemesthesis_sin_phases_use_persistent_and_telegraphed_hazards(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        rot.debug_set_phase(1)
        crown = []
        rot._fire_pattern(*rot._center(), crown)
        self.assertTrue(all(shot.path == "laser" for shot in crown))
        rot.debug_set_phase(7)
        finale = []
        rot._fire_pattern(*rot._center(), finale)
        self.assertTrue(any(shot.path == "mine" and shot.lifetime == 18
                            for shot in finale))

    def test_rot_opens_static_and_uses_three_cinematic_acts(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        self.assertEqual(rot.movementModes[0], "static")
        self.assertLess(rot.speed, .1)
        self.assertEqual(rot.actTitle, "ACT I // APPETITE")
        self.assertGreater(rot.actTransitionTimer, 0)
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.debug_set_phase(3)
        self.assertEqual(rot.actTitle, "ACT II // TEMPTATION")
        self.assertTrue(rot.take_damage(10).blocked)
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.debug_set_phase(5)
        self.assertEqual(rot.actTitle, "ACT III // SATURATION")

    def test_rot_health_gates_require_every_sin_in_order(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot.take_damage(rot.maxHp * 2)
        self.assertEqual(rot.hp, rot.maxHp * 6 / 7)
        self.assertFalse(rot.is_dead())
        rot.updateEnemy(*rot._center(), [])
        self.assertEqual(rot.phase, 2)

    def test_player_build_snapshot_is_immutable_and_drives_envy_identity(self):
        cS.reset_upgrade_tracking()
        cS.record_upgrade("Crit Chance", "Rare", "additive")
        cS.record_upgrade("Crit Damage", "Rare", "additive")
        snapshot = cS.player_build_snapshot()
        self.assertIsInstance(snapshot, MappingProxyType)
        self.assertEqual(snapshot["dominant_offense"], "critical")
        with self.assertRaises(TypeError):
            snapshot["dominant_offense"] = "volley"
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(3))
        rot.debug_set_phase(4)
        shots = []
        rot._fire_pattern(*rot._center(), shots)
        self.assertTrue(any("envy_critical" in shot.owner for shot in shots))

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

    def test_every_chemesthesis_phase_renders_its_visual_diagram(self):
        surface = pygame.Surface((1280, 720), pygame.SRCALPHA)
        world_x, world_y = bG.screen_to_world(640, 360)
        for boss_type in (Kage, Rot):
            boss = boss_type(world_x - vH.tileSizeGlobal,
                             world_y - vH.tileSizeGlobal, random.Random(4))
            boss.actTransitionTimer = 0
            for phase in range(1, len(boss.phaseLabels) + 1):
                boss.debug_set_phase(phase)
                boss.actTransitionTimer = 0
                boss.drawEnemy(surface)
        self.assertGreater(pygame.mask.from_surface(surface).count(), 0)

    def test_rot_vents_cleanse_exposure_and_crystallize_a_route(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        vent = rot.cleansingVents[0]
        cS.reset_boss_afflictions()
        cS.apply_boss_affliction("slow", 3, .2, 6)
        rot._update_terrain(vent["x"], vent["y"], .1)
        self.assertEqual(cS.bossAfflictions["exposure"], 0)
        self.assertEqual(rot.ventsUsed, 1)
        self.assertTrue(rot.movement_obstacles())
        self.assertGreater(vent["cooldown"], 0)

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

    def test_brittle_crystals_break_but_reinforced_crystals_expire(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot._grow_crystal_wall(0, kind="brittle")
        rot._grow_crystal_wall(pi, kind="reinforced")
        self.assertEqual(len(rot.get_screen_hitboxes()), 2)
        result = rot.take_damage(500, "crystal:0")
        self.assertTrue(result.applied)
        self.assertEqual(len(rot.crystalWalls), 1)
        self.assertEqual(rot.crystalWalls[0]["kind"], "reinforced")

    def test_gluttony_consumes_crystal_to_relieve_stagger(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        rot.debug_set_phase(5)
        rot.actTransitionTimer = 0
        rot.phaseProtectionTimer = 0
        rot._grow_crystal_wall(0, kind="reinforced")
        rot.stagger = 80
        shots = []
        rot._fire_pattern(*rot._center(), shots)
        self.assertFalse(rot.crystalWalls)
        self.assertEqual(rot.stagger, 55)
        self.assertGreater(rot.consumedCrystalPulse, 0)

    def test_sloth_compression_warns_before_becoming_solid(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.5)
        rot = Rot(rect.x, rect.y, random.Random(4))
        rot.debug_set_phase(7)
        rot.actTransitionTimer = 0
        rot.compressionCooldown = 0
        rot._update_terrain(*rot._center(), .1)
        self.assertEqual(len(rot.crystalWalls), 2)
        self.assertFalse(rot.movement_obstacles())
        rot._update_terrain(*rot._center(), 2.6)
        self.assertEqual(len(rot.movement_obstacles()), 2)

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

    def test_touch_bosses_cover_five_pairs_and_all_ten_plagues(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.7)
        bair = Bair(rect.x, rect.y, random.Random(4))
        sting = Sting(rect.x, rect.y, random.Random(4))
        self.assertEqual(len(bair.phaseLabels), 5)
        self.assertEqual(sting.phaseLabels, ("BLOOD", "FROGS", "GNATS", "FLIES",
                                             "PESTILENCE", "BOILS", "HAIL",
                                             "LOCUSTS", "DARKNESS", "FIRSTBORN"))
        for phase in range(1, 11):
            sting.debug_set_phase(phase)
            shots = []
            sting._fire_pattern(*sting._center(), shots)
            self.assertTrue(shots, f"plague phase {phase} fired nothing")

    def test_path_boss_arenas_have_distinct_shapes_and_center_spawns(self):
        expected = {"sting": "square", "chronos": "triangle", "rot": "jagged",
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

    def test_arena_containment_never_moves_interior_player_positions(self):
        center = (len(bG.currRoomRects[0])*vH.tileSizeGlobal/2,
                  len(bG.currRoomRects)*vH.tileSizeGlobal/2)
        for boss_type in (Sting, Chronos, Rot, Malady):
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

    def test_every_path_boss_exposes_chase_static_and_path_movement(self):
        expected = (Sting, Chronos, Rot, Malady)
        for boss_type in expected:
            modes = set(boss_type.movementModes)
            self.assertTrue({"chase", "static", "path"}.issubset(modes), boss_type.__name__)

    def test_touch_gate_portals_use_square_paths_and_heavy_volleys(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 2.7)
        sting = Sting(rect.x, rect.y, random.Random(4))
        sting.debug_set_phase(4)
        self.assertEqual(len(sting.projectilePortals), 4)
        self.assertTrue(all(portal.movementPath == "square"
                            for portal in sting.projectilePortals))
        shots = []
        sting.portalCooldown = 0
        sting._update_touch_portals(*sting._center(), shots, .1)
        self.assertTrue(shots)
        self.assertTrue(all(shot.speed < .5 and shot.damage >= 300 for shot in shots))

    def test_sight_bosses_expose_meaningful_symbols(self):
        self.assertEqual([name for name, _ in Ishe.SIGHT_SYMBOLS],
                         ["GLIMPSE", "BLINK", "FLASH"])
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
