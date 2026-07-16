import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import pygame

import background as bG
from bossTypes import BOSS_CATALOG, Beaudis, Dissonance
import character as game
import characterStats as cS
from enemyTypes import ENCOUNTER_PACKAGES, ENEMY_CATALOG, FAMILY_IDENTITIES
from enemyProjectile import EnemyProjectile
from progression import (FINAL_BOSS_LEVEL, MAX_LEVEL, MID_BOSS_LEVEL,
                         encounter_caps, encounter_pacing, enemy_stat_scales)
import variableHolster as vH


class TwentyLevelProgressionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        pygame.init()

    def setUp(self):
        game.resetAllStats()

    def _midpoint_boss(self):
        rect = bG.find_spawn_rect(vH.tileSizeGlobal * 1.55)
        boss = Beaudis(rect.x, rect.y, random.Random(7))
        boss.entranceRemaining = 0
        return boss

    def test_catalog_keeps_beaudis_midpoint_and_names_final_boss_dissonance(self):
        self.assertIsInstance(BOSS_CATALOG.spawn("beaudis"), Beaudis)
        self.assertIsInstance(BOSS_CATALOG.spawn("dissonance"), Dissonance)
        self.assertEqual(Beaudis.bossName, "BEAUDIS")
        self.assertEqual(Dissonance.bossName, "DISSONANCE")

    def test_beaudis_has_four_damage_phases_and_only_finale_survival(self):
        boss = self._midpoint_boss()
        self.assertEqual(boss.PHASE_COUNT, 5)
        self.assertEqual(boss.DAMAGE_PHASES, (1, 2, 3, 4))
        self.assertEqual(boss.SURVIVAL_PHASES, (5,))
        for phase in boss.DAMAGE_PHASES:
            boss.debug_set_phase(phase)
            self.assertFalse(boss.survivalActive)
            self.assertEqual(boss.projectilePortals, [])
        boss.debug_set_phase(5)
        self.assertTrue(boss.survivalActive)
        self.assertEqual(len(boss.projectilePortals), 4)

    def test_beaudis_is_weaker_slower_and_not_arena_bound(self):
        midpoint = self._midpoint_boss()
        final = Dissonance(midpoint.worldX, midpoint.worldY, random.Random(7))
        self.assertLess(midpoint.maxHp, final.maxHp)
        self.assertLess(midpoint.damage, final.damage)
        self.assertLess(midpoint.speed, final.speed)
        self.assertFalse(hasattr(midpoint, "arenaRadius"))
        self.assertTrue(hasattr(final, "arenaRadius"))

    def test_only_dissonance_receives_final_boss_projectile_damage_scale(self):
        dissonance_shot = EnemyProjectile(0, 0, 0, 1, 2, 10,
                                               owner="dissonance_test")
        beaudis_shot = EnemyProjectile(0, 0, 0, 1, 2, 10,
                                            owner="beaudis_test")
        self.assertEqual(dissonance_shot.damage, 2.6)
        self.assertEqual(beaudis_shot.damage, 2)

    def test_beaudis_finale_fades_with_italic_last_line(self):
        boss = self._midpoint_boss()
        boss.debug_set_phase(5)
        boss.survivalRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.dying)
        self.assertEqual(boss.deathDuration, 3.0)
        self.assertEqual(boss.phaseFlavor, "You can't escape me...")
        self.assertTrue(boss.finalFlavorItalic)
        self.assertEqual(boss.projectilePortals, [])
        boss.deathRemaining = 0
        boss.updateEnemy(*boss._center(), [])
        self.assertTrue(boss.is_dead())

    def test_dissonance_builds_smooth_motion_echoes(self):
        boss = Dissonance(bG.spawnX, bG.spawnY, random.Random(7))
        boss.entranceRemaining = 0
        boss.updateEnemy(bG.spawnX + 200, bG.spawnY, [])
        self.assertTrue(boss.motionTrail)
        self.assertGreater(boss.motionTrail[0]["life"], 0)
        surface = pygame.Surface((900, 700))
        boss.drawEnemy(surface)

    def test_natural_encounters_are_gated_to_ten_then_twenty(self):
        cS.enemySpawnTimer = 999999
        cS.currentLevel = MID_BOSS_LEVEL
        start = (bG.playerPosX, bG.playerPosY)
        game.handlingEnemyCreation()
        self.assertIsInstance(cS.activeBoss, Beaudis)
        self.assertEqual((bG.playerPosX, bG.playerPosY), start)

        cS.activeBoss = None
        cS.enemyHolster.clear()
        cS.enemySpawningEnabled = True
        cS.beaudisDefeated = True
        cS.currentLevel = FINAL_BOSS_LEVEL
        game.handlingEnemyCreation()
        self.assertIsInstance(cS.activeBoss, Dissonance)
        self.assertTrue(cS.dissonanceEncounterStarted)

    def test_scaling_stretches_old_curve_and_adds_late_run_pressure(self):
        start = enemy_stat_scales(0)
        midpoint = enemy_stat_scales(10)
        final = enemy_stat_scales(20)
        self.assertAlmostEqual(final["speed"], 1.08 ** 10)
        self.assertGreater(midpoint["health"], start["health"])
        self.assertGreater(final["health"], 1.08 ** 10)
        self.assertGreater(final["experience"], final["speed"])
        self.assertGreater(encounter_caps(20)["threat_cap"],
                           encounter_caps(10)["threat_cap"])

    def test_enemy_tiers_are_distributed_across_both_halves(self):
        self.assertEqual(ENEMY_CATALOG.definitions["volley_medium"].min_level, 6)
        self.assertEqual(ENEMY_CATALOG.definitions["volley_large"].min_level, 12)
        self.assertEqual(ENEMY_CATALOG.definitions["bomb_large"].min_level, 14)
        self.assertEqual(ENEMY_CATALOG.definitions["miniboss_siege"].min_level, 15)

    def test_regular_enemy_families_have_easy_medium_and_hard_variants(self):
        expected_families = {
            "runner", "drifter", "skirmisher", "bulwark", "ranged_wanderer",
            "shotgunner", "snake", "parent", "pillar", "volley", "laser", "bomb",
            "banner", "rammer", "warder", "splitter", "collector",
        }
        family_tiers = {}
        for definition in ENEMY_CATALOG.definitions.values():
            if definition.guaranteed_only:
                continue
            family_tiers.setdefault(definition.family, set()).add(definition.progression_tier)
        self.assertEqual(set(family_tiers), expected_families)
        self.assertTrue(all(tiers == {"easy", "medium", "hard"}
                            for tiers in family_tiers.values()))

    def test_spawn_windows_retire_early_and_medium_enemies(self):
        def random_pool(level):
            return [definition for definition in ENEMY_CATALOG.available(level)
                    if not definition.guaranteed_only]

        self.assertTrue(all(item.progression_tier == "easy" for item in random_pool(0)))
        self.assertFalse(any(item.progression_tier == "easy" for item in random_pool(10)))
        self.assertFalse(any(item.progression_tier == "medium" for item in random_pool(16)))
        self.assertTrue(all(item.progression_tier == "hard" for item in random_pool(20)))

    def test_higher_tiers_raise_stats_rewards_and_threat(self):
        easy = ENEMY_CATALOG.create("runner", bG.spawnX, bG.spawnY, 10, random.Random(9))
        medium = ENEMY_CATALOG.create("runner_medium", bG.spawnX, bG.spawnY, 10,
                                      random.Random(9))
        hard = ENEMY_CATALOG.create("runner_hard", bG.spawnX, bG.spawnY, 10,
                                    random.Random(9))
        self.assertLess(easy.speed, medium.speed)
        self.assertLess(medium.speed, hard.speed)
        self.assertLess(easy.maxHp, medium.maxHp)
        self.assertLess(medium.maxHp, hard.maxHp)
        self.assertLess(easy.damage, medium.damage)
        self.assertLess(medium.damage, hard.damage)
        self.assertLess(easy.expValue, medium.expValue)
        self.assertLess(medium.expValue, hard.expValue)
        self.assertLess(easy.threatCost, medium.threatCost)
        self.assertLess(medium.threatCost, hard.threatCost)

    def test_every_regular_family_has_an_explicit_combat_identity(self):
        regular_families = {definition.family for definition in ENEMY_CATALOG.definitions.values()
                            if not definition.guaranteed_only}
        self.assertTrue(regular_families.issubset(FAMILY_IDENTITIES))
        enemy = ENEMY_CATALOG.create("warder", bG.spawnX, bG.spawnY, 6,
                                     random.Random(3))
        self.assertEqual(enemy.combatRole, "support")
        self.assertIn("shield", enemy.interactionTags)

    def test_modifiers_are_role_and_level_constrained(self):
        runner = ENEMY_CATALOG.create("runner_medium", bG.spawnX, bG.spawnY, 8,
                                      random.Random(3))
        base_speed, base_hp = runner.speed, runner.maxHp
        ENEMY_CATALOG.apply_modifier(runner, 8, random.Random(3), forced="hasty")
        self.assertEqual(runner.behaviorModifier, "hasty")
        self.assertGreater(runner.speed, base_speed)
        self.assertLess(runner.maxHp, base_hp)

        collector = ENEMY_CATALOG.create("collector_medium", bG.spawnX, bG.spawnY, 8,
                                         random.Random(3))
        ENEMY_CATALOG.apply_modifier(collector, 8, random.Random(3), forced="hasty")
        self.assertIsNone(collector.behaviorModifier)

    def test_curated_encounters_use_live_tiers_and_fit_threat_budget(self):
        self.assertGreaterEqual(len(ENCOUNTER_PACKAGES), 8)
        encounter = ENEMY_CATALOG.spawn_encounter(12, 80, (), random.Random(6))
        self.assertIsNotNone(encounter)
        package, enemies = encounter
        self.assertTrue(enemies)
        self.assertTrue(all(enemy.encounterKey == package.key for enemy in enemies))
        self.assertTrue(all(enemy.difficultyTier in {"medium", "hard"}
                            for enemy in enemies))
        self.assertIsNone(ENEMY_CATALOG.spawn_encounter(12, .1, (), random.Random(6)))

    def test_ambient_spawns_are_persistent_coordinated_patrols(self):
        result = ENEMY_CATALOG.spawn_patrol(8, 80, (), random.Random(12))
        self.assertIsNotNone(result)
        encounter, enemies = result
        self.assertGreaterEqual(len(enemies), 3)
        self.assertTrue(all(enemy.encounter is encounter for enemy in enemies))
        self.assertTrue(all(enemy.encounterKey.startswith("patrol_") for enemy in enemies))
        self.assertGreaterEqual(len({enemy.combatRole for enemy in enemies}), 2)

        center = encounter.center()
        encounter.update(*center, allowed=True)
        self.assertEqual(encounter.state, "engaged")
        self.assertTrue(all(enemy.awarenessState == "alerted" for enemy in enemies))
        self.assertTrue(all(enemy.engagementAllowed for enemy in enemies))

        encounter.update(center[0] + encounter.disengageRange * 2,
                         center[1], allowed=True)
        self.assertEqual(encounter.state, "patrolling")
        self.assertTrue(all(enemy.encounterPatrolTarget is not None for enemy in enemies))

    def test_patrol_size_and_tier_scale_through_the_run(self):
        early = ENEMY_CATALOG.spawn_patrol(0, 80, (), random.Random(2))
        late = ENEMY_CATALOG.spawn_patrol(18, 80, (), random.Random(2))
        self.assertIsNotNone(early)
        self.assertIsNotNone(late)
        self.assertGreaterEqual(len(late[1]), len(early[1]))
        self.assertTrue(all(enemy.difficultyTier == "easy" for enemy in early[1]))
        self.assertTrue(all(enemy.difficultyTier == "hard" for enemy in late[1]))

    def test_encounter_pacing_scales_fight_size_without_unbounded_clutter(self):
        early = encounter_pacing(0)
        middle = encounter_pacing(10)
        late = encounter_pacing(19)
        self.assertEqual(early["patrol_size"], 5)
        self.assertEqual(encounter_pacing(1)["patrol_size"], 5)
        self.assertEqual(middle["patrol_size"], 7)
        self.assertEqual(late["patrol_size"], 9)
        self.assertLess(early["max_world_encounters"], late["max_world_encounters"])
        self.assertGreater(early["spawn_interval_seconds"], late["spawn_interval_seconds"])
        self.assertEqual(early["curated_chance"], 0)
        self.assertGreater(late["curated_chance"], middle["curated_chance"])

    def test_level_cap_is_the_final_boss_level(self):
        self.assertEqual(MAX_LEVEL, 20)
        cS.currentLevel = MAX_LEVEL
        cS.expCount = cS.expNeededForNextLevel * 3
        bubble = type("Bubble", (), {
            "posX": bG.lockX, "posY": bG.lockY,
            "size": cS.playerSize, "value": 1,
            "naturalSpawn": True,
        })()
        cS.experienceList = [bubble]
        game.expForPlayer()
        self.assertEqual(cS.currentLevel, MAX_LEVEL)
        self.assertLessEqual(cS.expCount, cS.expNeededForNextLevel)


if __name__ == "__main__":
    unittest.main()
