import os
import random
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
from bossTypes import BOSS_CATALOG, Beaudis, Dissonance
import character as game
import characterStats as cS
from enemyTypes import ArsenalMiniBoss, ENEMY_CATALOG
from bullet import Bullet
from enemyProjectile import EnemyProjectile
import uiTheme as ui
import variableHolster as vH


class DissonanceBossTests(unittest.TestCase):
    def setUp(self):
        self.frame_rate = vH.frameRate
        self.has_delta = vH.hasFrameDelta
        vH.frameRate = 120
        vH.hasFrameDelta = False
        center_x = len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2
        center_y = len(bG.currRoomRects) * vH.tileSizeGlobal / 2
        size = vH.tileSizeGlobal * 1.9
        self.boss = Dissonance(center_x - size / 2, center_y - size / 2, random.Random(5))
        self.boss.entranceRemaining = 0
        self.boss.cinematicTransitionsEnabled = False

    def tearDown(self):
        vH.frameRate = self.frame_rate
        vH.hasFrameDelta = self.has_delta
        vH.screenShakeX = 0
        vH.screenShakeY = 0

    def test_boss_health_and_stagger_match_level_twenty_balance_target(self):
        self.assertEqual(self.boss.maxHp, 135000)
        self.assertEqual(self.boss.maxStagger / self.boss.minimumStaggerPerHit, 60)

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

    def test_standard_dissonance_shots_cross_the_full_arena(self):
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
        for _ in range(15):
            self.boss.take_damage(1, "portal:0")
        self.assertGreater(len(self.boss.visualParticles), phase_particles)

    def test_entrance_delays_attacks_inside_compact_arena(self):
        self.assertAlmostEqual(self.boss.arenaRadius, vH.tileSizeGlobal * 32 / 3)
        self.boss.entranceRemaining = self.boss.entranceDuration
        self.boss.patternCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertFalse(shots)
        self.assertGreater(self.boss.entranceRemaining, 0)

    def test_lethal_damage_unlocks_final_survival_before_collapse(self):
        self.boss.entranceRemaining = 0
        self.boss.debug_set_phase(8)
        self.boss.nextSurvivalPhase = 9
        self.boss.isStaggered = True
        self.boss.hp = 1
        result = self.boss.take_damage(10)
        self.assertFalse(result.killed)
        self.assertFalse(self.boss.dying)
        self.boss.isStaggered = False
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 9)
        self.assertTrue(self.boss.survivalActive)
        self.assertEqual(self.boss.deathRemaining, 0)
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
            boss = Dissonance(*self.boss._center(), random.Random(4))
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
            self.assertFalse(any(str(part).startswith("portal:")
                                 for part, _ in boss.get_world_hitboxes()))
            portal = boss.projectilePortals[0]
            portal_hp = portal.hp
            self.assertTrue(boss.take_damage(1, "portal:0").blocked)
            self.assertEqual(portal.hp, portal_hp)

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
        self.assertEqual(self.boss.hp, 1)

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
        self.assertIn("dissonance_rune_laser", owners)
        self.assertIn("dissonance_rune_bomb", owners)
        self.assertIn("dissonance_speed_burst", owners)

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

    def test_phase_announcement_window_blocks_damage_and_stagger(self):
        self.boss.cinematicTransitionsEnabled = True
        self.boss._set_phase(2)
        self.boss.transitionRemaining = 0
        self.assertGreater(self.boss.phaseProtectionTimer, 0)
        hp, stagger = self.boss.hp, self.boss.stagger
        result = self.boss.take_damage(20)
        self.assertTrue(result.blocked)
        self.assertEqual(self.boss.hp, hp)
        self.assertEqual(self.boss.stagger, stagger)

    def test_phase_announcements_use_supported_styled_rune_names(self):
        self.assertEqual(self.boss.PHASE_RUNES[1][0].upper(), "OTHALA")
        styled = ui.font(13, italic=True, bold=True)
        self.assertTrue(styled.get_italic())
        self.assertTrue(styled.get_bold())

    def test_laser_telegraphs_then_becomes_persistent_hazard(self):
        laser = EnemyProjectile(100, 100, 0, 0, 2, 20, travel_range=300,
                                path="laser", shape="laser", lifetime=4,
                                owner="dissonance_rune_laser")
        player = __import__("pygame").Rect(180, 95, 20, 20)
        laser.age = .9
        self.assertFalse(laser.collides(player))
        laser.age = 1.1
        self.assertTrue(laser.collides(player))
        self.assertTrue(laser.persistentHazard)

    def test_bomb_waits_then_emits_octagonal_burst(self):
        bomb = EnemyProjectile(100, 100, 0, 0, 1, 30, path="bomb", shape="bomb",
                               lifetime=3, target=(200, 200), owner="dissonance_rune_bomb")
        bomb.age = 3.0
        bomb.updateAndDraw(__import__("pygame").Surface((800, 600)))
        self.assertTrue(bomb.remFlag)
        self.assertEqual(len(bomb.spawnedProjectiles), 8)

    def test_hits_damage_boss_while_building_stagger(self):
        result = self.boss.take_damage(2)
        self.assertTrue(result.applied)
        self.assertLess(self.boss.hp, self.boss.maxHp)
        self.assertEqual(self.boss.stagger, 6)

    def test_stagger_gradually_decays_after_two_seconds_without_damage(self):
        self.boss.take_damage(2)
        starting_stagger = self.boss.stagger
        self.boss._update_stagger(1.9)
        self.assertEqual(self.boss.stagger, starting_stagger)
        self.boss.staggerDecayTimer = 0
        self.boss._update_stagger(.25)
        self.assertEqual(self.boss.stagger,
                         starting_stagger - self.boss.staggerDecayPerSecond * .25)

    def test_base_player_hits_require_sixty_hits_without_immediate_phase_swap(self):
        for _ in range(59):
            self.boss.take_damage(1)
        self.assertFalse(self.boss.isStaggered)
        self.boss.take_damage(1)
        self.assertTrue(self.boss.isStaggered)
        self.assertEqual(self.boss.phase, 1)
        self.assertEqual(self.boss.staggerRemaining, 5)

    def test_full_stagger_opens_temporary_damage_window_and_stops_attacks(self):
        for _ in range(60):
            self.boss.take_damage(1)
        self.assertTrue(self.boss.isStaggered)
        self.assertTrue(self.boss.transitionCleanupRequested)
        old_hp = self.boss.hp
        self.assertTrue(self.boss.take_damage(7).applied)
        self.assertEqual(self.boss.hp, old_hp - round(7 * 1.35))
        self.boss.patternCooldown = 0
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertFalse(projectiles)

    def test_stagger_window_expires_into_invulnerable_phase_transition(self):
        self.boss.cinematicTransitionsEnabled = True
        self.boss.stagger = self.boss.maxStagger
        self.boss.isStaggered = True
        self.boss.staggerRemaining = .1
        self.boss._update_stagger(.2)
        self.assertFalse(self.boss.isStaggered)
        self.assertEqual(self.boss.stagger, 0)
        self.assertIn(self.boss.phase, self.boss.DAMAGE_PHASES)
        self.assertNotEqual(self.boss.phase, 1)
        self.assertEqual(self.boss.transitionRemaining, 5)
        hp = self.boss.hp
        self.assertTrue(self.boss.take_damage(10).blocked)
        self.assertEqual(self.boss.hp, hp)
        self.assertEqual(self.boss.stagger, 0)

    def test_fifteen_portal_hits_disable_interception_and_halve_firepower_for_phase(self):
        portal = self.boss.projectilePortals[0]
        starting_stagger = self.boss.stagger
        for _ in range(14):
            result = self.boss.take_damage(1, "portal:0")
            self.assertTrue(result.applied)
        self.assertTrue(portal.blocks_shots)
        result = self.boss.take_damage(1, "portal:0")
        self.assertTrue(result.applied)
        self.assertTrue(portal.active)
        self.assertTrue(portal.phaseDisabled)
        self.assertFalse(portal.blocks_shots)
        self.assertEqual(self.boss.stagger, starting_stagger)
        self.assertFalse(any(part == "portal:0" for part, _ in self.boss.get_world_hitboxes()))
        target = self.boss._arena_center()
        disabled_shots = []
        portal.fire_toward(disabled_shots, target, pellet_count=7)
        self.assertEqual(len(disabled_shots), 4)
        self.boss._set_phase(2)
        self.assertFalse(portal.phaseDisabled)
        self.assertTrue(portal.blocks_shots)
        self.assertTrue(portal.runeStrokes)

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

    def test_perfect_stagger_keeps_five_second_window_and_fracture_scales_damage(self):
        self.boss._set_phase(2)
        self.boss.stagger = self.boss.maxStagger - 5
        self.boss.take_damage(1)
        self.assertTrue(self.boss.perfectStagger)
        self.assertEqual(self.boss.staggerRemaining, self.boss.staggerDuration)
        first = self.boss.take_damage(100).amount
        second = self.boss.take_damage(100).amount
        self.assertGreater(second, first)

    def test_stagger_expiry_starts_projectile_free_phase_transition(self):
        self.boss.cinematicTransitionsEnabled = True
        self.boss.isStaggered = True
        self.boss.staggerRemaining = 0
        self.boss._update_stagger(.1)
        self.assertEqual(self.boss.transitionRemaining, 5)
        self.assertTrue(self.boss.transitionCleanupRequested)
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertFalse(shots)

    def test_arena_timer_ring_tracks_damage_and_survival_phase_time(self):
        self.boss.phaseElapsed = self.boss.phaseTimeLimit / 2
        self.assertAlmostEqual(self.boss._phase_timer_ratio(), .5)
        self.boss.debug_set_phase(3)
        self.boss.survivalRemaining = 5
        self.assertAlmostEqual(self.boss._phase_timer_ratio(), .25)

    def test_rune_cannon_can_be_interrupted_by_breaking_receiver(self):
        self.boss._set_phase(7)
        self.boss.runeCannonCooldown = 0
        self.boss._update_rune_cannon(*self.boss._center(), [], .1)
        receiver_index = self.boss.runeCannonReceiver
        self.assertIsNotNone(receiver_index)
        receiver = self.boss.projectilePortals[receiver_index]
        for _ in range(15):
            receiver.take_damage(1)
        previous_stagger = self.boss.stagger
        self.boss._update_rune_cannon(*self.boss._center(), [], .1)
        self.assertGreater(self.boss.stagger, previous_stagger)
        self.assertGreater(self.boss.runeSilenceRemaining, 0)

    def test_boss_exposes_reward_ready_challenge_results(self):
        initial = self.boss.challenge_results()
        self.assertTrue(initial["no_portals_broken"])
        self.assertTrue(initial["unbroken_pressure"])
        for _ in range(15):
            self.boss.take_damage(1, "portal:0")
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
        self.assertTrue(all(mine.owner == "dissonance_portal_mine" for mine in mines))
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
        self.assertTrue(all(shot.owner == "dissonance_portal_shot" for shot in projectiles))

    def test_phase_transition_sets_flavor_announcement_and_red_portal_hexagon(self):
        self.boss.debug_set_phase(4)
        self.assertEqual(self.boss.phase, 4)
        self.assertEqual(self.boss.phaseFlavor, "Your pulse betrays you.")
        self.assertGreater(self.boss.phaseAnnouncementTimer, 0)
        self.assertEqual(len(self.boss.projectilePortals), 4)
        self.assertTrue(all(portal.owner == "dissonance_static"
                            for portal in self.boss.projectilePortals))

    def test_red_static_transitions_at_two_thirds_and_emits_radial_pattern(self):
        self.boss.debug_set_phase(4)
        self.assertEqual(self.boss.phase, 4)
        self.boss.phaseElapsed = 2
        self.boss.radialCooldown = 0
        self.boss.aimedCooldown = 0
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertEqual(len([shot for shot in projectiles
                              if shot.owner == "dissonance_static_chord"]), 10)
        self.assertEqual(len([shot for shot in projectiles
                              if shot.owner == "dissonance_static_sine"]), 6)

    def test_phase_three_builds_rotating_diamond_field(self):
        self.boss.debug_set_phase(7)
        projectiles = []
        self.boss.updateEnemy(*self.boss._center(), projectiles)
        self.assertEqual(self.boss.phase, 7)
        field = [projectile for projectile in projectiles if projectile.owner == "dissonance_field"]
        self.assertGreaterEqual(len(field), 30)
        self.assertTrue(all(projectile.path == "orbit" for projectile in field))
        self.assertEqual(len({projectile.angularSpeed for projectile in field}), 4)

    def test_closing_spiral_shrinks_and_accelerates_portals(self):
        self.boss.debug_set_phase(3)
        old_radius = self.boss.projectilePortals[0].radius
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.phase, 3)
        self.boss.phaseElapsed = 4
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertEqual(self.boss.projectilePortals[0].radius, old_radius)
        self.assertEqual(self.boss.phaseLabel, "RETALIATE")

    def test_portal_relay_transfers_then_redirects_a_shotgun(self):
        self.boss.debug_set_phase(6)
        transfer = []
        self.boss.updateEnemy(*self.boss._center(), transfer)
        self.assertEqual(self.boss.phase, 6)
        self.boss.relayCooldown = 0
        self.boss._phase_portal_relay(*self.boss._center(), transfer, .1)
        self.assertTrue(any(shot.owner == "dissonance_relay_transfer" for shot in transfer))
        redirected = []
        self.boss._phase_portal_relay(*self.boss._center(), redirected, .5)
        self.assertEqual(len([shot for shot in redirected
                              if shot.owner == "dissonance_relay_redirect"]), 5)

    def test_health_gates_unlock_only_each_acts_survival_phase(self):
        samples = ((1, 2 / 3, 3), (4, 1 / 3, 6), (7, 0, 9))
        for phase, ratio, expected in samples:
            boss = Dissonance(*self.boss._center(), random.Random(5))
            boss.entranceRemaining = 0
            boss.debug_set_phase(phase)
            boss.transitionRemaining = 0
            boss.nextSurvivalPhase = expected
            boss.hp = boss.maxHp * ratio
            boss.updateEnemy(*boss._center(), [])
            self.assertEqual(boss.phase, expected)
            self.assertTrue(boss.survivalActive)

    def test_phase_randomizes_after_doubled_time_limit_without_health_progress(self):
        self.assertEqual(self.boss.phaseTimeLimit, 36)
        self.assertEqual(self.boss.phase, 1)
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertIn(self.boss.phase, self.boss.DAMAGE_PHASES)
        self.assertNotEqual(self.boss.phase, 1)
        self.assertTrue(self.boss.phaseForcedByTimer)
        self.assertEqual(self.boss.hp, self.boss.maxHp)

    def test_timed_phase_advance_never_regresses_from_health_check(self):
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertIn(self.boss.phase, self.boss.DAMAGE_PHASES)

    def test_damage_phase_cycles_stay_inside_the_current_health_gated_act(self):
        for next_survival, expected_pool in ((3, {1, 2}), (6, {4, 5}), (9, {7, 8})):
            self.boss.nextSurvivalPhase = next_survival
            seen = set()
            for _ in range(12):
                next_phase = self.boss._choose_damage_phase()
                self.assertIn(next_phase, expected_pool)
                self.boss._set_phase(next_phase)
                seen.add(next_phase)
            self.assertEqual(seen, expected_pool)

    def test_final_damage_phases_repeat_until_zero_health(self):
        self.boss.debug_set_phase(8)
        self.boss.nextSurvivalPhase = 9
        self.boss.hp = 1
        self.boss.phaseElapsed = self.boss.phaseTimeLimit
        self.boss.updateEnemy(*self.boss._center(), [])
        self.assertIn(self.boss.phase, self.boss.DAMAGE_PHASES)
        self.assertNotEqual(self.boss.phase, 8)
        self.assertFalse(self.boss.survivalActive)

    def test_crossfire_carousel_fires_tangential_volley(self):
        self.boss.debug_set_phase(2)
        self.boss.carouselCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertEqual(self.boss.phase, 2)
        self.assertEqual(len([shot for shot in shots if shot.owner == "dissonance_portal_carousel"]), 6)

    def test_mirror_step_leaves_bursts_at_both_positions(self):
        self.boss.debug_set_phase(5)
        self.boss.mirrorCooldown = 0
        shots = []
        self.boss.updateEnemy(self.boss._center()[0] + 300, self.boss._center()[1], shots)
        self.boss._phase_mirror_step(self.boss._center()[0] + 300,
                                     self.boss._center()[1], shots,
                                     self.boss.mirrorJumpDuration)
        self.assertEqual(self.boss.phase, 5)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "dissonance_mirror_portal_afterimage"]), 10)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "dissonance_mirror_portal_landing_echo"]), 10)

    def test_event_horizon_fires_from_opposite_field_points(self):
        self.boss.debug_set_phase(8)
        self.boss.horizonCooldown = 0
        shots = []
        self.boss.updateEnemy(*self.boss._center(), shots)
        self.assertEqual(self.boss.phase, 8)
        self.assertEqual(len([shot for shot in shots
                              if shot.owner == "dissonance_constellation_horizon"]), 10)

    def test_every_phase_uses_live_portals_as_projectile_sources(self):
        ratios = (.95, .83, .72, .61, .50, .38, .28, .17, .05)
        for ratio in ratios:
            boss = Dissonance(*self.boss._center(), random.Random(5))
            boss.hp = boss.maxHp * ratio
            boss.updateEnemy(*boss._center(), [])
            self.assertGreaterEqual(len(boss.projectilePortals), 2,
                                    f"phase {boss.phase} has no portal formation")

    def test_final_jera_uses_reduced_portals_and_infinite_survival_lanes(self):
        self.boss.debug_set_phase(9)
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

    def test_b_remains_a_hidden_debug_summon_shortcut(self):
        game.resetAllStats()
        vH.keyPressed = {__import__("pygame").K_b}
        __import__("main").update_input_toggles()
        self.assertTrue(cS.bossDebugRequested)
        self.assertFalse(cS.beaudisEncounterStarted)
        self.assertFalse(cS.beaudisDefeated)
        vH.keyPressed = set()

    def test_last_word_cycles_callbacks_from_earlier_portal_phases(self):
        self.boss.debug_set_phase(9)
        owners = set()
        for callback_index in range(3):
            self.boss.callbackIndex = callback_index
            self.boss.callbackCooldown = 0
            shots = []
            self.boss._phase_last_word(*self.boss._center(), shots, .1)
            owners.update(shot.owner for shot in shots if "callback" in str(shot.owner))
        self.assertIn("dissonance_last_word_callback_carousel", owners)
        self.assertIn("dissonance_last_word_callback_chord", owners)
        self.assertIn("dissonance_last_word_callback_relay", owners)

    def test_debug_request_clears_arena_and_disables_regular_spawning(self):
        game.resetAllStats()
        cS.enemyHolster.append(ENEMY_CATALOG.spawn(0, key="runner"))
        cS.bossDebugRequested = True
        game.handlingEnemyCreation()
        self.assertIsInstance(cS.activeBoss, Dissonance)
        self.assertEqual(cS.enemyHolster, [cS.activeBoss])
        self.assertFalse(cS.enemySpawningEnabled)
        self.assertFalse(cS.bossDebugInvincible)

    def test_natural_beaudis_encounter_is_gated_to_level_ten_and_triggers_once(self):
        game.resetAllStats()
        cS.enemySpawningEnabled = False
        cS.currentLevel = 9
        game.handlingEnemyCreation()
        self.assertIsNone(cS.activeBoss)
        self.assertFalse(cS.beaudisEncounterStarted)

        cS.currentLevel = 10
        game.handlingEnemyCreation()
        self.assertIsInstance(cS.activeBoss, Beaudis)
        self.assertTrue(cS.beaudisEncounterStarted)

        cS.activeBoss = None
        cS.enemyHolster.clear()
        game.handlingEnemyCreation()
        self.assertIsNone(cS.activeBoss)

    def test_minibosses_spawn_once_in_each_half_at_levels_five_and_fifteen(self):
        game.resetAllStats()
        cS.enemySpawnTimer = 999999
        starting_player_position = (bG.playerPosX, bG.playerPosY)

        cS.currentLevel = 3
        game.handlingEnemyCreation()
        self.assertFalse(any(isinstance(enemy, ArsenalMiniBoss)
                             for enemy in cS.enemyHolster))

        cS.currentLevel = 5
        game.handlingEnemyCreation()
        minibosses = [enemy for enemy in cS.enemyHolster
                      if isinstance(enemy, ArsenalMiniBoss)]
        self.assertEqual(len(minibosses), 1)
        self.assertIsNone(cS.activeBoss)
        self.assertTrue(cS.enemySpawningEnabled)
        self.assertEqual((bG.playerPosX, bG.playerPosY), starting_player_position)
        distance = ((minibosses[0].worldX + minibosses[0].size / 2 - bG.playerPosX) ** 2
                    + (minibosses[0].worldY + minibosses[0].size / 2 - bG.playerPosY) ** 2) ** .5
        self.assertGreater(distance, minibosses[0].awarenessRange)

        game.handlingEnemyCreation()
        self.assertEqual(len([enemy for enemy in cS.enemyHolster
                              if isinstance(enemy, ArsenalMiniBoss)]), 1)

        cS.beaudisEncounterStarted = True
        cS.beaudisDefeated = True
        cS.currentLevel = 15
        game.handlingEnemyCreation()
        self.assertEqual(len([enemy for enemy in cS.enemyHolster
                              if isinstance(enemy, ArsenalMiniBoss)]), 2)
        self.assertEqual(cS.guaranteedMiniBossesSpawned,
                         {"miniboss_arsenal", "miniboss_siege"})
        self.assertTrue(cS.beaudisEncounterStarted)
        self.assertTrue(cS.beaudisDefeated)

    def test_debug_invincibility_prevents_damage(self):
        game.resetAllStats()
        cS.bossDebugInvincible = True
        cS.healthPoints = 1
        game.hurtPlayer()
        self.assertEqual(cS.healthPoints, cS.maxHealthPoints)


if __name__ == "__main__":
    unittest.main()
