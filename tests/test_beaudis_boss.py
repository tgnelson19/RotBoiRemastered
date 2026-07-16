import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
from bossTypes import BOSS_CATALOG, Beaudis
import character as game
import characterStats as cS
from enemyTypes import ENEMY_CATALOG
from bullet import Bullet
from enemyProjectile import EnemyProjectile
import variableHolster as vH


class BeaudisBossTests(unittest.TestCase):
    def setUp(self):
        self.frame_rate = vH.frameRate
        self.has_delta = vH.hasFrameDelta
        vH.frameRate = 120
        vH.hasFrameDelta = False
        center_x = len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2
        center_y = len(bG.currRoomRects) * vH.tileSizeGlobal / 2
        size = vH.tileSizeGlobal * 1.9
        self.boss = Beaudis(center_x - size / 2, center_y - size / 2, random.Random(5))
        self.boss.entranceRemaining = 0
        self.boss.cinematicTransitionsEnabled = False

    def tearDown(self):
        vH.frameRate = self.frame_rate
        vH.hasFrameDelta = self.has_delta
        vH.screenShakeX = 0
        vH.screenShakeY = 0

    def test_boss_health_uses_reduced_balance_target(self):
        self.assertEqual(self.boss.maxHp, 240)

    def test_initial_phase_uses_bolster_dialogue(self):
        self.assertEqual(self.boss.phaseLabel, "BOLSTER")
        self.assertEqual(self.boss.phaseFlavor, "I did not expect a challenger...")

    def test_each_phase_has_a_distinct_elder_futhark_rune(self):
        names = [self.boss.PHASE_RUNES[phase][0] for phase in range(1, 10)]
        self.assertEqual(names[0], "OTHALA")
        self.assertEqual(names[-1], "JERA")
        self.assertEqual(len(set(names)), 9)

    def test_each_act_has_chase_anchor_and_rotation_movement(self):
        expected = (
            ("hearth_tornado", "road_anchor", "torch_tornado"),
            ("hail_chase", "yew_anchor", "sun_revolution"),
            ("spear_intercept", "day_anchor", "harvest_chase"),
        )
        for act, phases in enumerate(((1, 2, 3), (4, 5, 6), (7, 8, 9))):
            self.assertEqual(tuple(self.boss.PHASE_MOVEMENT[p] for p in phases),
                             expected[act])

    def test_standard_beaudis_shots_cross_the_full_arena(self):
        portal = self.boss.projectilePortals[0]
        portal.fireCooldown = 0
        shots = []
        portal.update(shots, .1)
        self.assertTrue(shots)
        self.assertTrue(all(shot.size >= vH.tileSizeGlobal * .4 for shot in shots))
        self.assertTrue(all(shot.remainingRange >= vH.tileSizeGlobal * 72
                            for shot in shots))

    def test_animated_cube_and_rune_render_without_font_glyphs(self):
        surface = __import__("pygame").Surface((800, 600))
        self.boss.posX, self.boss.posY = 350, 250
        self.boss.drawEnemy(surface)
        self.assertNotEqual(surface.get_at((400, 300)), surface.get_at((0, 0)))

    def test_cube_geometry_changes_over_time_and_by_phase(self):
        first, _ = self.boss._cube_geometry((400, 300), 40)
        self.boss.age = 80
        self.boss.phase = 7
        later, _ = self.boss._cube_geometry((400, 300), 40)
        self.assertNotEqual(first, later)

    def test_phase_and_portal_breaks_spawn_pixel_spectacle_particles(self):
        self.boss._set_phase(4)
        phase_particles = len(self.boss.visualParticles)
        self.assertGreater(phase_particles, 0)
        self.assertGreater(self.boss.actTransitionTimer, 0)
        portal = self.boss.projectilePortals[0]
        self.boss.take_damage(portal.maxHp, "portal:0")
        self.assertGreater(len(self.boss.visualParticles), phase_particles)

    def test_entrance_delays_attacks_inside_compact_arena(self):
        self.assertAlmostEqual(self.boss.arenaRadius, vH.tileSizeGlobal * 32 / 3)
        self.boss.entranceRemaining = self.boss.entranceDuration
        self.boss.patternCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertFalse(shots)
        self.assertGreater(self.boss.entranceRemaining, 0)

    def test_lethal_damage_runs_collapse_before_boss_is_removed(self):
        self.boss.entranceRemaining = 0
        self.boss.isStaggered = True
        self.boss.hp = 1
        result = self.boss.take_damage(10)
        self.assertFalse(result.killed)
        self.assertTrue(self.boss.dying)
        self.assertGreater(self.boss.deathRemaining, 0)
        self.boss.deathRemaining = 0
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertTrue(self.boss.is_dead())

    def test_debug_phase_jump_and_lock_support_isolated_practice(self):
        self.boss.debug_set_phase(8)
        self.assertEqual(self.boss.phase, 8)
        self.assertEqual(self.boss.phaseElapsed, 0)
        self.boss.debugPhaseLocked = True
        self.boss.hp = self.boss.maxHp * .01
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 8)

    def test_perfect_break_starts_cinematic_flash(self):
        self.boss._set_phase(2)
        self.boss.stagger = self.boss.maxStagger - 5
        self.boss.take_damage(1)
        self.assertTrue(self.boss.perfectStagger)
        self.assertGreater(self.boss.perfectBreakFlash, 0)

    def test_each_act_ends_with_shorter_survival_and_finale_lasts_thirty_seconds(self):
        for phase in self.boss.SURVIVAL_PHASES:
            boss = Beaudis(*self.boss._center(), random.Random(4))
            boss.entranceRemaining = 0
            boss.cinematicTransitionsEnabled = False
            boss.debug_set_phase(phase)
            self.assertTrue(boss.survivalActive)
            self.assertEqual(boss.survivalRemaining, 30 if phase == 9 else 20)
            self.assertEqual(len(boss.survivalPortals), 8 if phase == 9 else 6)
            hp = boss.hp
            result = boss.take_damage(999)
            self.assertFalse(result.applied)
            self.assertEqual(boss.hp, hp)

    def test_survival_completion_advances_or_begins_final_death(self):
        self.boss.debug_set_phase(3)
        self.boss.survivalRemaining = 0
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 4)
        self.boss.debug_set_phase(9)
        self.boss.survivalRemaining = 0
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertTrue(self.boss.dying)
        self.assertEqual(self.boss.deathRemaining, 10)
        self.assertEqual(self.boss.hp, .01)

    def test_survival_boundary_portals_fire_variable_speed_inward_shots(self):
        self.boss.debug_set_phase(3)
        self.boss.survivalCooldown = 0
        shots = []
        self.boss._survival_barrage(*self.boss._center(), shots, .1)
        boundary = [shot for shot in shots if "boundary_inward" in shot.owner]
        self.assertTrue(boundary)
        self.assertGreater(len({shot.speed for shot in boundary}), 1)

    def test_final_death_holds_jera_before_reward_release(self):
        self.boss.debug_set_phase(9)
        self.boss.survivalRemaining = 0
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertTrue(self.boss.dying)
        self.assertEqual(self.boss.deathDuration, 10)
        self.assertEqual(self.boss.deathBurstDuration, 10)
        self.boss.deathRemaining = 0
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertTrue(self.boss.is_dead())

    def test_cinematic_transition_clears_formation_and_holds_five_seconds(self):
        self.boss.cinematicTransitionsEnabled = True
        self.boss._set_phase(4)
        self.assertEqual(self.boss.transitionRemaining, 5)
        self.assertEqual(self.boss.phaseAnnouncementTimer, 5)
        self.assertTrue(self.boss.transitionCleanupRequested)
        self.assertIsNotNone(self.boss.transitionTarget)
        self.assertEqual((self.boss.transitionTarget[0] + self.boss.size / 2,
                          self.boss.transitionTarget[1] + self.boss.size / 2),
                         self.boss._arena_center())

    def test_mirror_step_uses_visible_jump_before_landing_burst(self):
        self.boss.debug_set_phase(5)
        self.boss.mirrorCooldown = 0
        start = self.boss._center()
        shots = []
        self.boss._phase_mirror_step(start[0] + 300, start[1], shots, .1)
        self.assertGreater(self.boss.mirrorJumpRemaining, 0)
        self.assertFalse(shots)
        self.boss._phase_mirror_step(start[0] + 300, start[1], shots,
                                     self.boss.mirrorJumpDuration)
        self.assertEqual(self.boss.mirrorJumpRemaining, 0)
        self.assertEqual(len([shot for shot in shots if "landing_echo" in shot.owner]), 10)

    def test_final_survival_layers_more_projectile_families(self):
        self.boss.debug_set_phase(9)
        self.boss.survivalCooldown = 0
        self.boss.specialAttackCooldown = 0
        shots = []
        self.boss._survival_barrage(*self.boss._center(), shots, .1)
        self.boss._update_special_attacks(*self.boss._center(), shots, .1)
        owners = {shot.owner for shot in shots}
        self.assertTrue(any("boundary_tangent" in owner for owner in owners))
        self.assertIn("beaudis_rune_laser", owners)
        self.assertIn("beaudis_rune_bomb", owners)
        self.assertIn("beaudis_speed_burst", owners)

    def test_phase_transition_resets_and_locks_stagger(self):
        self.boss.stagger = 75
        self.boss.cinematicTransitionsEnabled = True
        self.boss._set_phase(2)
        self.assertEqual(self.boss.stagger, 0)
        hp = self.boss.hp
        result = self.boss.take_damage(20)
        self.assertFalse(result.applied)
        self.assertEqual(self.boss.stagger, 0)
        self.assertEqual(self.boss.hp, hp)

    def test_laser_telegraphs_then_becomes_persistent_hazard(self):
        laser = EnemyProjectile(100, 100, 0, 0, 2, 20, travel_range=300,
                                path="laser", shape="laser", lifetime=4,
                                owner="beaudis_rune_laser")
        player = __import__("pygame").Rect(180, 95, 20, 20)
        laser.age = .9
        self.assertFalse(laser.collides(player))
        laser.age = 1.1
        self.assertTrue(laser.collides(player))
        self.assertTrue(laser.persistentHazard)

    def test_bomb_waits_then_emits_octagonal_burst(self):
        bomb = EnemyProjectile(100, 100, 0, 0, 1, 30, path="bomb", shape="bomb",
                               lifetime=3, target=(200, 200), owner="beaudis_rune_bomb")
        bomb.age = 3.0
        bomb.updateAndDraw(__import__("pygame").Surface((800, 600)))
        self.assertTrue(bomb.remFlag)
        self.assertEqual(len(bomb.spawnedProjectiles), 8)

    def test_hits_damage_boss_while_building_stagger(self):
        result = self.boss.take_damage(2)
        self.assertTrue(result.applied)
        self.assertLess(self.boss.hp, self.boss.maxHp)
        self.assertEqual(self.boss.stagger, 6)

    def test_stagger_decays_after_no_hit_delay(self):
        self.boss.take_damage(2)
        self.boss.staggerDecayTimer = 0
        self.boss._update_stagger(.25)
        self.assertEqual(self.boss.stagger, 2)

    def test_base_player_hits_require_thirty_hits_to_stagger(self):
        for _ in range(29):
            self.boss.take_damage(1)
        self.assertFalse(self.boss.isStaggered)
        self.boss.take_damage(1)
        self.assertTrue(self.boss.isStaggered)

    def test_full_stagger_opens_temporary_damage_window_and_stops_attacks(self):
        for _ in range(30):
            self.boss.take_damage(1)
        self.assertTrue(self.boss.isStaggered)
        self.assertTrue(self.boss.transitionCleanupRequested)
        old_hp = self.boss.hp
        self.assertTrue(self.boss.take_damage(7).applied)
        self.assertEqual(self.boss.hp, old_hp - 7 * 1.35)
        self.boss.patternCooldown = 0
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertFalse(projectiles)

    def test_stagger_window_expires_and_resets_meter(self):
        self.boss.stagger = self.boss.maxStagger
        self.boss.isStaggered = True
        self.boss.staggerRemaining = .1
        self.boss._update_stagger(.2)
        self.assertFalse(self.boss.isStaggered)
        self.assertEqual(self.boss.stagger, 0)

    def test_portal_can_be_broken_for_stagger_and_regenerates(self):
        portal = self.boss.projectilePortals[0]
        starting_stagger = self.boss.stagger
        result = self.boss.take_damage(portal.maxHp, "portal:0")
        self.assertTrue(result.applied)
        self.assertFalse(portal.active)
        self.assertEqual(self.boss.stagger, starting_stagger + self.boss.portalBreakStagger)
        portal.update_status(portal.regenerationTime)
        self.assertTrue(portal.active)
        self.assertEqual(portal.hp, portal.maxHp)

    def test_rune_transition_can_be_disrupted_for_temporary_silence(self):
        self.boss._set_phase(2)
        for _ in range(4):
            self.boss.take_damage(1)
        self.assertGreater(self.boss.runeSilenceRemaining, 0)
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertFalse(shots)

    def test_portals_support_polarity_paths_and_route_player_bullets(self):
        self.boss._set_phase(4)
        self.assertEqual(self.boss.projectilePortals[0].movementPath, "square")
        self.assertEqual({portal.polarity for portal in self.boss.projectilePortals}, {-1, 1})
        source = self.boss.projectilePortals[0]
        bullet = Bullet(source.worldX, source.worldY, 4, 0, 300, 10,
                        __import__("pygame").Color("white"), 1, 2, False)
        self.assertTrue(self.boss.route_player_bullet(bullet, 0))
        self.assertGreater(bullet.portalCooldown, 0)
        self.assertGreater(bullet.damage, 2)

    def test_perfect_stagger_extends_window_and_fracture_scales_damage(self):
        self.boss._set_phase(2)
        self.boss.stagger = self.boss.maxStagger - 5
        self.boss.take_damage(1)
        self.assertTrue(self.boss.perfectStagger)
        self.assertEqual(self.boss.staggerRemaining, self.boss.staggerDuration + 2)
        first = self.boss.take_damage(10).amount
        second = self.boss.take_damage(10).amount
        self.assertGreater(second, first)

    def test_stagger_recovery_adds_attack_free_reconstruction_pause(self):
        self.boss.isStaggered = True
        self.boss.staggerRemaining = 0
        self.boss._update_stagger(.1)
        self.assertGreater(self.boss.staggerRecoveryRemaining, 0)
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertFalse(shots)

    def test_rune_cannon_can_be_interrupted_by_breaking_receiver(self):
        self.boss._set_phase(7)
        self.boss.runeCannonCooldown = 0
        self.boss._update_rune_cannon(*self.boss._center(), [], .1)
        receiver_index = self.boss.runeCannonReceiver
        self.assertIsNotNone(receiver_index)
        receiver = self.boss.projectilePortals[receiver_index]
        receiver.take_damage(receiver.maxHp)
        previous_stagger = self.boss.stagger
        self.boss._update_rune_cannon(*self.boss._center(), [], .1)
        self.assertGreater(self.boss.stagger, previous_stagger)
        self.assertGreater(self.boss.runeSilenceRemaining, 0)

    def test_boss_exposes_reward_ready_challenge_results(self):
        initial = self.boss.challenge_results()
        self.assertTrue(initial["no_portals_broken"])
        self.assertTrue(initial["unbroken_pressure"])
        self.boss.take_damage(self.boss.projectilePortals[0].maxHp, "portal:0")
        self.assertFalse(self.boss.challenge_results()["no_portals_broken"])

    def test_phase_one_emits_mines_and_sinusoidal_fan(self):
        self.boss.mineCooldown = 0
        self.boss.patternCooldown = 0
        center_x, center_y = self.boss._center()
        projectiles = []
        self.boss.updateEnemy(center_x + 100, center_y, projectiles)
        mines = [projectile for projectile in projectiles if projectile.shape == "mine"]
        sine_fan = [projectile for projectile in projectiles if projectile.path == "sine"]
        self.assertTrue(mines)
        self.assertTrue(sine_fan)
        self.assertEqual(mines[0].speed, .9)
        self.assertTrue(all(mine.owner == "beaudis_portal_mine" for mine in mines))
        self.assertEqual(len(sine_fan), 6)
        self.assertTrue(all(projectile.speed <= .45 for projectile in sine_fan))
        self.assertLessEqual(max(projectile.amplitude for projectile in sine_fan),
                             vH.tileSizeGlobal * .96 + .001)
        directions = sorted(projectile.direction for projectile in sine_fan)
        self.assertTrue(all(abs((right - left) - .64) < .001
                            for left, right in zip(directions, directions[1:])))

    def test_phase_one_portals_orbit_and_fire_inward_shotguns(self):
        self.assertEqual(len(self.boss.projectilePortals), 3)
        portal = self.boss.projectilePortals[0]
        portal.fireCooldown = 0
        old_angle = portal.angle
        projectiles = []
        portal.update(projectiles, .1)
        self.assertNotEqual(portal.angle, old_angle)
        self.assertEqual(len(projectiles), 7)
        self.assertTrue(all(shot.owner == "beaudis_portal_shot" for shot in projectiles))

    def test_phase_transition_sets_flavor_announcement_and_red_portal_hexagon(self):
        self.boss.hp = self.boss.maxHp * 2 / 3
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 4)
        self.assertEqual(self.boss.phaseFlavor, "Your pulse betrays you.")
        self.assertGreater(self.boss.phaseAnnouncementTimer, 0)
        self.assertEqual(len(self.boss.projectilePortals), 4)
        self.assertTrue(all(portal.owner == "beaudis_static"
                            for portal in self.boss.projectilePortals))

    def test_red_static_transitions_at_two_thirds_and_emits_radial_pattern(self):
        self.boss.hp = self.boss.maxHp * 2 / 3
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 4)
        self.boss.phaseElapsed = 2
        self.boss.radialCooldown = 0
        self.boss.aimedCooldown = 0
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertEqual(len([shot for shot in projectiles
                              if shot.owner == "beaudis_static_chord"]), 10)
        self.assertEqual(len([shot for shot in projectiles
                              if shot.owner == "beaudis_static_sine"]), 6)

    def test_phase_three_builds_rotating_diamond_field(self):
        self.boss.hp = self.boss.maxHp / 3
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertEqual(self.boss.phase, 7)
        field = [projectile for projectile in projectiles if projectile.owner == "beaudis_field"]
        self.assertGreaterEqual(len(field), 30)
        self.assertTrue(all(projectile.path == "orbit" for projectile in field))
        self.assertEqual(len({projectile.angularSpeed for projectile in field}), 4)

    def test_closing_spiral_shrinks_and_accelerates_portals(self):
        self.boss.hp = self.boss.maxHp * .7
        old_radius = self.boss.projectilePortals[0].radius
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 3)
        self.boss.phaseElapsed = 4
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.projectilePortals[0].radius, old_radius)
        self.assertEqual(self.boss.phaseLabel, "RETALIATE")

    def test_portal_relay_transfers_then_redirects_a_shotgun(self):
        self.boss.hp = self.boss.maxHp * .36
        transfer = []
        self.boss.updateEnemy(*self.boss._center(), transfer)
        self.assertEqual(self.boss.phase, 6)
        self.boss.relayCooldown = 0
        self.boss._phase_portal_relay(*self.boss._center(), transfer, .1)
        self.assertTrue(any(shot.owner == "beaudis_relay_transfer" for shot in transfer))
        redirected = []
        self.boss._phase_portal_relay(*self.boss._center(), redirected, .5)
        self.assertEqual(len([shot for shot in redirected
                              if shot.owner == "beaudis_relay_redirect"]), 5)

    def test_each_overall_third_contains_three_phases(self):
        samples = ((.95, 1), (.83, 2), (.72, 3),
                   (.61, 4), (.50, 5), (.38, 6),
                   (.28, 7), (.17, 8), (.05, 9))
        for ratio, expected in samples:
            boss = Beaudis(*self.boss._center(), random.Random(5))
            boss.entranceRemaining = 0
            boss.hp = boss.maxHp * ratio
            boss.updateEnemy(*boss._center(), [])
            self.assertEqual(boss.phase, expected)

    def test_phase_advances_after_time_limit_without_health_progress(self):
        self.assertEqual(self.boss.phase, 1)
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 2)
        self.assertTrue(self.boss.phaseForcedByTimer)
        self.assertEqual(self.boss.hp, self.boss.maxHp)

    def test_timed_phase_advance_never_regresses_from_health_check(self):
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 2)

    def test_final_phase_does_not_advance_past_nine(self):
        self.boss.hp = self.boss.maxHp * .05
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 9)

    def test_crossfire_carousel_fires_tangential_volley(self):
        self.boss.hp = self.boss.maxHp * .83
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.carouselCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertEqual(self.boss.phase, 2)
        self.assertEqual(len([shot for shot in shots if shot.owner == "beaudis_portal_carousel"]), 6)

    def test_mirror_step_leaves_bursts_at_both_positions(self):
        self.boss.hp = self.boss.maxHp * .5
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.mirrorCooldown = 0
        shots = []
        self.boss.updateEnemy(self.boss._center()[0] + 300, self.boss._center()[1], shots)
        self.boss._phase_mirror_step(self.boss._center()[0] + 300,
                                     self.boss._center()[1], shots,
                                     self.boss.mirrorJumpDuration)
        self.assertEqual(self.boss.phase, 5)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "beaudis_mirror_portal_afterimage"]), 10)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "beaudis_mirror_portal_landing_echo"]), 10)

    def test_event_horizon_fires_from_opposite_field_points(self):
        self.boss.hp = self.boss.maxHp * .17
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.horizonCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertEqual(self.boss.phase, 8)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "beaudis_constellation_horizon"]), 10)

    def test_every_phase_uses_live_portals_as_projectile_sources(self):
        ratios = (.95, .83, .72, .61, .50, .38, .28, .17, .05)
        for ratio in ratios:
            boss = Beaudis(*self.boss._center(), random.Random(5))
            boss.hp = boss.maxHp * ratio
            boss.updateEnemy(*boss._center(), [])
            self.assertGreaterEqual(len(boss.projectilePortals), 2,
                                    f"phase {boss.phase} has no portal formation")

    def test_final_jera_uses_reduced_portals_and_infinite_survival_lanes(self):
        self.boss.hp = self.boss.maxHp * .05
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.survivalCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertEqual(self.boss.phase, 9)
        self.assertEqual(len(self.boss.projectilePortals), 4)
        survival = [shot for shot in shots if "survival" in shot.owner]
        self.assertTrue(survival)
        self.assertTrue(all(shot.remainingRange == float("inf") for shot in survival))

    def test_y_toggles_player_invincibility(self):
        previous = cS.bossDebugInvincible
        previous_boss = cS.activeBoss
        cS.activeBoss = self.boss
        vH.keyPressed = {__import__("pygame").K_y}
        __import__("main").update_input_toggles()
        self.assertNotEqual(cS.bossDebugInvincible, previous)
        vH.keyPressed = set()
        cS.activeBoss = previous_boss
        cS.bossDebugInvincible = previous

    def test_last_word_cycles_callbacks_from_earlier_portal_phases(self):
        self.boss.hp = self.boss.maxHp * .05
        self.boss.updateEnemy(*self.boss._center(), [])
        owners = set()
        for callback_index in range(3):
            self.boss.callbackIndex = callback_index
            self.boss.callbackCooldown = 0
            shots = []
            self.boss._phase_last_word(*self.boss._center(), shots, .1)
            owners.update(shot.owner for shot in shots if "callback" in str(shot.owner))
        self.assertIn("beaudis_last_word_callback_carousel", owners)
        self.assertIn("beaudis_last_word_callback_chord", owners)
        self.assertIn("beaudis_last_word_callback_relay", owners)

    def test_debug_request_clears_arena_and_disables_regular_spawning(self):
        game.resetAllStats()
        cS.enemyHolster.append(ENEMY_CATALOG.spawn(0, key="runner"))
        cS.bossDebugRequested = True
        game.handlingEnemyCreation()
        self.assertIsInstance(cS.activeBoss, Beaudis)
        self.assertEqual(cS.enemyHolster, [cS.activeBoss])
        self.assertFalse(cS.enemySpawningEnabled)
        self.assertFalse(cS.bossDebugInvincible)

    def test_debug_invincibility_prevents_damage(self):
        game.resetAllStats()
        cS.bossDebugInvincible = True
        cS.healthPoints = 1
        game.hurtPlayer()
        self.assertEqual(cS.healthPoints, cS.maxHealthPoints)


if __name__ == "__main__":
    unittest.main()
