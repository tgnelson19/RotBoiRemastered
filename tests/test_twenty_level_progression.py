import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")

import pygame

import background as bG
from bossTypes import BOSS_CATALOG, Beaudis, Dissonance
import character as game
import characterStats as cS
from enemyTypes import ENEMY_CATALOG
from enemyProjectile import EnemyProjectile
from progression import (FINAL_BOSS_LEVEL, MAX_LEVEL, MID_BOSS_LEVEL,
                         encounter_caps, enemy_stat_scales)
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
