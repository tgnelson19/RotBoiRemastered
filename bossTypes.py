"""Midpoint Beaudis encounter and the endgame Dissonance fight."""

from dataclasses import dataclass
from math import atan2, cos, hypot, pi, sin
import random

import pygame

import background as bG
from enemy import Enemy, HitResult
from enemyProjectile import EnemyProjectile
from projectilePortal import ProjectilePortal
import uiTheme as ui
import variableHolster as vH
import gameProfile


class Beaudis(Enemy):
    """A restrained midpoint echo fought in the ordinary world."""

    bossName = "BEAUDIS"
    subtitle = "THE ECHO THAT FOLLOWS"
    DAMAGE_PHASES = (1, 2, 3, 4)
    SURVIVAL_PHASES = (5,)
    SURVIVAL_THRESHOLDS = (2 / 3, 1 / 3, 0)
    PHASE_COUNT = 5
    FINAL_FLAVOR = "You can't escape me..."

    PHASE_METADATA = {
        1: ("AWAKEN", "You hear it too.", ui.PURPLE),
        2: ("ANSWER", "Stay a while.", ui.BLUE),
        3: ("PRESS", "The pattern remembers.", ui.GOLD),
        4: ("PERSIST", "This is not the end.", ui.RED),
        5: ("ENDURE", "Run, while you still can.", ui.CREAM),
    }

    def __init__(self, world_x, world_y, rng=None):
        size = vH.tileSizeGlobal * 1.55
        super().__init__(world_x, world_y, .38, size, ui.PURPLE,
                         200, 26000, 240, 3.2, "beaudis")
        self.rng = rng or random
        self.phase = 1
        self.damagePhaseHistory = [1]
        self.nextSurvivalIndex = 0
        self.phaseLabel, self.phaseFlavor, self.phaseAccent = self.PHASE_METADATA[1]
        self.phaseElapsed = 0.0
        self.phaseTimeLimit = 28.0
        self.phaseAnnouncementTimer = 2.4
        self.phaseProtectionTimer = 0.0
        self.transitionRemaining = 0.0
        self.transitionCleanupRequested = False
        # The midpoint encounter is isolated, so phase changes clear its entire
        # small projectile field rather than filtering one exact owner string.
        self.transitionCleanupOwner = None
        self.projectilePortals = []
        self.survivalPortals = []
        self.survivalActive = False
        self.survivalDuration = 14.0
        self.survivalRemaining = 0.0
        self.survivalCooldown = .7
        self.portalIndex = 0
        self.attackCooldown = 1.25
        self.attackPattern = 0
        self.entranceRemaining = 1.25
        self.entranceDuration = 1.25
        self.dying = False
        self.deathDuration = 3.0
        self.deathRemaining = 0.0
        self.finalFlavorItalic = True
        self.debugPhaseLocked = False
        self.phaseForcedByTimer = False

        # The midpoint break is intentionally forgiving and never gates damage.
        self.stagger = 0.0
        self.maxStagger = 90.0
        self.minimumStaggerPerHit = 4.0
        self.staggerDuration = 3.0
        self.staggerRemaining = 0.0
        self.isStaggered = False
        self.perfectStagger = False
        self.staggerRecoveryRemaining = 0.0
        self.runeSilenceRemaining = 0.0

    def _seconds(self):
        return vH.get_timer_step() / max(1, vH.frameRate)

    def _center(self):
        return self.worldX + self.size / 2, self.worldY + self.size / 2

    def _set_phase(self, phase):
        phase = max(1, min(self.PHASE_COUNT, int(phase)))
        if phase == self.phase:
            return
        self.phase = phase
        if phase in self.DAMAGE_PHASES:
            self.damagePhaseHistory.append(phase)
            self.damagePhaseHistory = self.damagePhaseHistory[-2:]
        self.phaseElapsed = 0.0
        self.phaseLabel, self.phaseFlavor, self.phaseAccent = self.PHASE_METADATA[phase]
        self.phaseAnnouncementTimer = 2.4
        self.phaseProtectionTimer = .55
        self.transitionCleanupRequested = True
        self.attackCooldown = 1.0
        self.stagger = 0.0
        self.isStaggered = False
        self.staggerRemaining = 0.0
        self.phaseForcedByTimer = False
        self.survivalActive = phase == 5
        if self.survivalActive:
            self.hp = max(1, self.hp)
            self.survivalRemaining = self.survivalDuration
            self.survivalCooldown = .75
            self._deploy_finale_portals()
        else:
            self._clear_portals()

    def debug_set_phase(self, phase):
        phase = max(1, min(self.PHASE_COUNT, int(phase)))
        if phase == 5:
            # Phase-five practice remains the final survival and fade sequence.
            self.nextSurvivalIndex = len(self.SURVIVAL_THRESHOLDS) - 1
        if phase == self.phase:
            self.phase = 0
        self._set_phase(phase)

    def _survival_health(self):
        ratio = self.SURVIVAL_THRESHOLDS[self.nextSurvivalIndex]
        return self.maxHp * ratio

    def _choose_damage_phase(self):
        pools = ((1, 2), (2, 3), (3, 4))
        pool = pools[min(self.nextSurvivalIndex, len(pools) - 1)]
        choices = [phase for phase in pool if phase != self.phase]
        return self.rng.choice(choices or list(pool))

    def take_damage(self, amount, part_id="body"):
        if self.dying or self.survivalActive or self.phaseProtectionTimer > 0:
            return HitResult(False, False, 0, blocked=True)
        multiplier = 1.25 if self.isStaggered else 1.0
        applied = round(amount * multiplier)
        self.hp -= applied
        self.stagger = min(self.maxStagger,
                           self.stagger + max(self.minimumStaggerPerHit, amount * .014))
        if self.stagger >= self.maxStagger and not self.isStaggered:
            self.isStaggered = True
            self.staggerRemaining = self.staggerDuration
            self.transitionCleanupRequested = True
        threshold_hp = self._survival_health()
        if self.hp <= threshold_hp and not self.debugPhaseLocked:
            # Pin health to the gate so one large hit cannot skip a survival.
            self.hp = max(1, threshold_hp)
            self._set_phase(5)
        else:
            self.hp = max(0, self.hp)
        return HitResult(True, False, applied)

    def _clear_portals(self):
        for portal in self.projectilePortals:
            portal.remFlag = True
        self.projectilePortals.clear()

    def _deploy_finale_portals(self):
        self._clear_portals()
        center = self._center()
        for index in range(4):
            self.projectilePortals.append(ProjectilePortal(
                center, vH.tileSizeGlobal * 3.8, index * pi / 2,
                angular_speed=.18, fire_interval=999,
                pellet_count=2, spread=.22, owner="beaudis_finale",
                color=ui.PURPLE if index % 2 == 0 else ui.BLUE,
            ))

    def _projectile(self, sink, direction, speed=.68, damage=1.0,
                    color=None, owner="beaudis_shot"):
        center_x, center_y = self._center()
        size = vH.tileSizeGlobal * .34
        sink.append(EnemyProjectile(
            center_x - size / 2, center_y - size / 2, direction,
            speed, damage, size, travel_range=vH.tileSizeGlobal * 30,
            color=color or self.phaseAccent, shape="diamond", owner=owner,
        ))

    def _fire_fan(self, player_x, player_y, sink, count, spread, speed=.68):
        center_x, center_y = self._center()
        base = atan2(player_y - center_y, player_x - center_x)
        for index in range(count):
            offset = (index - (count - 1) / 2) * spread / max(1, count - 1)
            self._projectile(sink, base + offset, speed)

    def _fire_radial(self, sink, count=6, speed=.62):
        offset = self.attackPattern * pi / max(1, count)
        for index in range(count):
            self._projectile(sink, offset + index * 2 * pi / count,
                             speed, .9, ui.GOLD, "beaudis_pulse")

    def _move(self, player_x, player_y):
        center_x, center_y = self._center()
        dx, dy = player_x - center_x, player_y - center_y
        distance = max(1.0, hypot(dx, dy))
        if distance > vH.tileSizeGlobal * 6.5:
            move_x, move_y = dx / distance, dy / distance
        elif distance < vH.tileSizeGlobal * 3.5:
            move_x, move_y = -dx / distance * .7, -dy / distance * .7
        else:
            direction = 1 if self.phase % 2 else -1
            move_x, move_y = -dy / distance * direction * .45, dx / distance * direction * .45
        step = self.speed * vH.get_frame_scale()
        self._try_axis_move(move_x * step, "x")
        self._try_axis_move(move_y * step, "y")

    def _update_damage_phase(self, player_x, player_y, sink, dt):
        self._move(player_x, player_y)
        self.attackCooldown -= dt
        if self.attackCooldown > 0:
            return
        if self.phase == 1:
            self._fire_fan(player_x, player_y, sink, 1, 0, .62)
            self.attackCooldown = 1.75
        elif self.phase == 2:
            self._fire_fan(player_x, player_y, sink, 3, .46, .66)
            self.attackCooldown = 2.05
        elif self.phase == 3:
            self._fire_radial(sink, 6, .60)
            self.attackCooldown = 2.45
        else:
            self._fire_fan(player_x, player_y, sink, 4, .68, .72)
            self.attackCooldown = 1.65
        self.attackPattern += 1

    def _update_survival(self, player_x, player_y, sink, dt):
        self.survivalRemaining = max(0.0, self.survivalRemaining - dt)
        center = self._center()
        for portal in self.projectilePortals:
            portal.orbitCenter = center
            portal.angle += portal.angularSpeed * dt
            portal._place()
            portal.update_bursts(sink, dt)
        self.survivalCooldown -= dt
        if self.survivalCooldown <= 0 and self.projectilePortals:
            portal = self.projectilePortals[self.portalIndex % len(self.projectilePortals)]
            portal.fire_toward(sink, (player_x, player_y), 2, .22, .72, 1.0,
                               self.phaseAccent, "survival")
            self.portalIndex += 1
            self.survivalCooldown = .85
        if self.survivalRemaining <= 0:
            if self.nextSurvivalIndex >= len(self.SURVIVAL_THRESHOLDS) - 1:
                self._begin_fade()
            else:
                self.nextSurvivalIndex += 1
                self._set_phase(self._choose_damage_phase())

    def _begin_fade(self):
        if self.dying:
            return
        self.survivalActive = False
        self.dying = True
        self.deathRemaining = self.deathDuration
        self.phaseFlavor = self.FINAL_FLAVOR
        self.phaseAnnouncementTimer = self.deathDuration
        self.transitionCleanupRequested = True
        self._clear_portals()

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        dt = self._seconds()
        self.age += vH.get_timer_step()
        self.phaseElapsed += dt
        self.phaseAnnouncementTimer = max(0.0, self.phaseAnnouncementTimer - dt)
        self.phaseProtectionTimer = max(0.0, self.phaseProtectionTimer - dt)
        if self.dying:
            self.deathRemaining = max(0.0, self.deathRemaining - dt)
            if self.deathRemaining <= 0:
                self.hp = 0
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.entranceRemaining > 0:
            self.entranceRemaining = max(0.0, self.entranceRemaining - dt)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.isStaggered:
            self.staggerRemaining = max(0.0, self.staggerRemaining - dt)
            if self.staggerRemaining <= 0:
                self.isStaggered = False
                self.stagger = 0.0
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.survivalActive:
            self._update_survival(player_world_x, player_world_y, projectile_sink, dt)
        else:
            if (not self.debugPhaseLocked and self.phaseElapsed >= self.phaseTimeLimit
                    and self.phase in self.DAMAGE_PHASES):
                self._set_phase(self._choose_damage_phase())
                self.phaseForcedByTimer = True
            self._update_damage_phase(player_world_x, player_world_y, projectile_sink, dt)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        for portal in self.projectilePortals:
            portal.draw(screen)
        fade = (self.deathRemaining / self.deathDuration) if self.dying else 1.0
        extent = int(self.size * 1.35)
        sprite = pygame.Surface((extent, extent), pygame.SRCALPHA)
        rect = pygame.Rect(0, 0, self.size, self.size)
        rect.center = (extent // 2, extent // 2)
        color = ui.CREAM if self.isStaggered else self.phaseAccent
        pygame.draw.rect(sprite, ui.SHADOW, rect.move(5, 6))
        pygame.draw.rect(sprite, color, rect)
        pygame.draw.rect(sprite, ui.INK, rect, max(3, int(self.size * .06)))
        inner = rect.inflate(-self.size * .42, -self.size * .42)
        pygame.draw.rect(sprite, ui.VOID, inner)
        pygame.draw.rect(sprite, color, inner, max(4, int(self.size * .045)))
        pip_size = max(4, int(self.size * .07))
        for index in range(min(self.phase, 4)):
            pygame.draw.rect(sprite, ui.CREAM,
                             (rect.x + 8 + index * (pip_size + 3), rect.bottom - pip_size - 8,
                              pip_size, pip_size))
        sprite.set_alpha(max(0, min(255, int(255 * fade))))
        screen.blit(sprite, (self.posX + self.size / 2 - extent / 2,
                             self.posY + self.size / 2 - extent / 2))
        if self.dying:
            flavor = ui.font(max(12, int(self.size * .17)), italic=True).render(
                self.FINAL_FLAVOR, True, ui.CREAM)
            flavor.set_alpha(max(0, min(255, int(255 * fade))))
            screen.blit(flavor, flavor.get_rect(
                midbottom=(self.posX + self.size / 2, self.posY - 12)))
        elif self.phaseAnnouncementTimer > 0:
            label = ui.font(max(11, int(self.size * .13)), bold=True).render(
                f"PHASE {self.phase} // {self.phaseLabel}", True, self.phaseAccent)
            screen.blit(label, label.get_rect(
                midbottom=(self.posX + self.size / 2, self.posY - 10)))

    def challenge_results(self):
        return {"midpoint_survived": self.dying or self.hp <= 0}


class Dissonance(Enemy):
    bossName = "DISSONANCE"
    subtitle = "THE ROT AT THE CENTER"
    PHASE_RUNES = {
        1: ("OTHALA", (((-.34, -.12), (0, -.5), (.34, -.12), (0, .28), (-.34, -.12)),
                       ((0, .28), (-.38, .52)), ((0, .28), (.38, .52)))),
        2: ("RAIDHO", (((-.35, .52), (-.35, -.52), (.18, -.52), (.38, -.3),
                         (.18, -.08), (-.35, -.08)),
                        ((-.02, -.08), (.4, .52)))),
        3: ("KENAZ", (((.35, -.5), (-.35, 0), (.35, .5)),)),
        4: ("HAGALAZ", (((-.38, -.52), (-.38, .52)),
                        ((.38, -.52), (.38, .52)),
                        ((-.38, .28), (.38, -.28)))),
        5: ("EIHWAZ", (((-.28, -.52), (.28, -.28), (-.28, .28), (.28, .52)),)),
        6: ("SOWILO", (((.3, -.52), (-.24, -.18), (.22, .05), (-.3, .52)),)),
        7: ("TIWAZ", (((0, .52), (0, -.52)),
                      ((-.36, -.18), (0, -.52), (.36, -.18)))),
        8: ("DAGAZ", (((-.4, -.5), (.4, .5), (.4, -.5), (-.4, .5), (-.4, -.5)),)),
        9: ("JERA", (((-.42, -.48), (-.04, -.48), (.24, -.22)),
                     ((.42, .48), (.04, .48), (-.24, .22)))),
    }
    SURVIVAL_PHASES = (3, 6, 9)
    DAMAGE_PHASES = (1, 2, 4, 5, 7, 8)
    PHASE_MOVEMENT = {
        1: "hearth_tornado", 2: "road_anchor", 3: "torch_tornado",
        4: "hail_chase", 5: "yew_anchor", 6: "sun_revolution",
        7: "spear_intercept", 8: "day_anchor", 9: "harvest_chase",
    }

    def __init__(self, world_x, world_y, rng=None):
        size = vH.tileSizeGlobal * 1.9
        # Dissonance expects a complete twenty-card build and preserves the full
        # nine-rune encounter as the run's final mechanical examination.
        super().__init__(world_x, world_y, .72, size, ui.PURPLE, 520, 135000, 900, 5, "dissonance")
        self.rng = rng or random
        self.phase = 1
        self.damagePhaseHistory = [1]
        self.nextSurvivalPhase = 3
        self.phaseAccent = ui.PURPLE
        self.phaseLabel = "BOLSTER"
        self.phaseFlavor = "I did not expect a challenger..."
        self.phaseAnnouncementTimer = 3.2
        self.phaseProtectionTimer = 0.0
        self.phaseElapsed = 0.0
        self.phaseTimeLimit = 36.0
        self.phaseForcedByTimer = False
        self.stagger = 0.0
        # Sixty baseline hits break Dissonance. Endgame attack-speed, damage,
        # pierce, and critical builds can all contribute without trivializing it.
        # retained briefly, then drains gradually if the player disengages.
        self.maxStagger = 360.0
        self.staggerPerDamage = .02
        self.minimumStaggerPerHit = 6.0
        self.staggerDecayDelay = 2.0
        self.staggerDecayTimer = 0.0
        self.staggerDecayPerSecond = 16.0
        self.staggerDuration = 5.0
        self.staggerRemaining = 0.0
        self.isStaggered = False
        self.portalBreakStagger = 12.0
        self.runeDisruption = 0.0
        self.runeDisruptionNeeded = 18.0
        self.runeSilenceRemaining = 0.0
        self.perfectStagger = False
        self.fracture = 0.0
        self.maxFracture = 20.0
        self.staggerRecoveryRemaining = 0.0
        self.mineCooldown = 1.6
        self.patternCooldown = .7
        self.radialCooldown = .4
        self.aimedCooldown = 1.4
        self.jumpCooldown = 5.5
        self.jumpWindup = 0.0
        self.jumpRecovery = 0.0
        self.fieldCooldown = 0.0
        self.fieldShotCooldown = .8
        self.fieldDeployed = False
        self.fieldProjectiles = []
        self.projectilePortals = []
        self.survivalPortals = []
        self.relayCooldown = .6
        self.relayPending = None
        self.relayIndex = 0
        self.carouselCooldown = .45
        self.carouselIndex = 0
        self.mirrorCooldown = 1.1
        self.horizonCooldown = .4
        self.lastWordCooldown = .3
        self.portalPatternCooldown = .35
        self.callbackCooldown = 2.0
        self.callbackIndex = 0
        self.runeCannonCooldown = 5.0
        self.runeCannonCharge = 0.0
        self.runeCannonReceiver = None
        self.portalsBroken = 0
        self.runesInterrupted = 0
        self.perfectStaggers = 0
        self.staggerEverDecayed = False
        self.runeWasInterrupted = False
        self.visualParticles = []
        self.motionTrail = []
        self.motionTrailCooldown = 0.0
        self.actTransitionTimer = 2.2
        self.actTitle = "ACT I // THE CYCLE"
        self.hitFlash = 0.0
        self.perfectBreakFlash = 0.0
        self.ambientParticleCooldown = 0.0
        self.arenaRadius = vH.tileSizeGlobal * (32.0 / 3.0)
        self.arenaFormationScale = 4.0 / 3.0
        self.entranceRemaining = 3.0
        self.entranceDuration = 3.0
        self.dying = False
        self.deathRemaining = 0.0
        self.deathDuration = 10.0
        self.deathBurstDuration = 10.0
        self.shakeStrength = 0.0
        self.debugPhaseLocked = False
        self.survivalDuration = 30.0
        self.survivalRemaining = 0.0
        self.survivalActive = False
        self.survivalCooldown = .2
        self.transitionDuration = 5.0
        self.transitionRemaining = 0.0
        self.transitionStart = None
        self.transitionTarget = None
        self.transitionCleanupRequested = False
        self.cinematicTransitionsEnabled = True
        self.specialAttackCooldown = 2.4
        self.bossBurstQueue = []
        self.survivalFormationCycle = 0
        self._arenaMaskCache = None
        self._arenaMaskCacheKey = None
        self.mirrorJumpDuration = .48
        self.mirrorJumpRemaining = 0.0
        self.mirrorJumpStart = None
        self.mirrorJumpTarget = None
        self.mirrorJumpEchoOrigin = None
        self._deploy_phase_one_portals()
        for portal in self.projectilePortals:
            portal.reset_for_phase(self.PHASE_RUNES[self.phase][1])
            portal.hitsToDisable = 15

    def _seconds(self):
        return vH.get_timer_step() / max(1, vH.frameRate)

    def _burst_particles(self, world_x, world_y, color, count, speed=2.0):
        for index in range(count):
            angle = 2 * pi * index / max(1, count) + self.rng.uniform(-.14, .14)
            velocity = speed * self.rng.uniform(.55, 1.15)
            self.visualParticles.append({
                "x": world_x, "y": world_y,
                "vx": cos(angle) * velocity, "vy": sin(angle) * velocity,
                "life": self.rng.uniform(.35, .85),
                "size": self.rng.choice((3, 4, 6, 8)), "color": pygame.Color(color),
            })

    def _update_visuals(self, dt):
        self.hitFlash = max(0.0, self.hitFlash - dt)
        self.perfectBreakFlash = max(0.0, self.perfectBreakFlash - dt)
        self.actTransitionTimer = max(0.0, self.actTransitionTimer - dt)
        self.ambientParticleCooldown -= dt
        self.motionTrailCooldown -= dt
        self.shakeStrength = max(0.0, self.shakeStrength - 16 * dt)
        shake_scale = float(gameProfile.profile["screen_shake"])
        vH.screenShakeX = int(sin(self.age * .73) * self.shakeStrength * shake_scale)
        vH.screenShakeY = int(cos(self.age * .61) * self.shakeStrength * .65 * shake_scale)
        if self.ambientParticleCooldown <= 0:
            center_x, center_y = self._center()
            angle = self.age * .013 + self.rng.uniform(-.5, .5)
            self.visualParticles.append({
                "x": center_x + cos(angle) * self.size * .7,
                "y": center_y + sin(angle) * self.size * .45,
                "vx": -cos(angle) * .25, "vy": -sin(angle) * .25,
                "life": .8, "size": self.rng.choice((3, 4, 5)),
                "color": pygame.Color(self.phaseAccent),
            })
            self.ambientParticleCooldown = .08
        if self.motionTrailCooldown <= 0:
            center_x, center_y = self._center()
            self.motionTrail.append({
                "x": center_x, "y": center_y, "life": .52,
                "phase": self.phase, "accent": pygame.Color(self.phaseAccent),
            })
            self.motionTrailCooldown = .045
        for particle in self.visualParticles:
            particle["x"] += particle["vx"] * vH.get_frame_scale()
            particle["y"] += particle["vy"] * vH.get_frame_scale()
            particle["vy"] += .16 * dt
            particle["life"] -= dt
        self.visualParticles[:] = [particle for particle in self.visualParticles
                                   if particle["life"] > 0]
        for ghost in self.motionTrail:
            ghost["life"] -= dt
        self.motionTrail[:] = [ghost for ghost in self.motionTrail if ghost["life"] > 0]

    def take_damage(self, amount, part_id="body"):
        if self.dying:
            return HitResult(False, False, 0, blocked=True)
        if self.transitionRemaining > 0 or self.phaseProtectionTimer > 0:
            return HitResult(False, False, 0, blocked=True)
        if self.survivalActive:
            return HitResult(False, False, 0, blocked=True)
        if str(part_id).startswith("portal:"):
            index = int(str(part_id).split(":", 1)[1])
            if 0 <= index < len(self.projectilePortals):
                broken = self.projectilePortals[index].take_damage(amount)
                if broken:
                    self.portalsBroken += 1
                    portal = self.projectilePortals[index]
                    self._burst_particles(portal.worldX + portal.size / 2,
                                          portal.worldY + portal.size / 2,
                                          portal.color, 18, 2.4)
                return HitResult(True, False, amount)
            return HitResult(False, False, 0, blocked=True)
        if self.isStaggered:
            multiplier = 1.35 + self.fracture * .02
            applied = round(amount * multiplier)
            self.hp -= applied
            self.fracture = min(self.maxFracture, self.fracture + amount * .0035)
            self.hitFlash = .12
            self._burst_particles(*self._center(), ui.CREAM, 4, 1.0)
            self.hp = max(0, self.hp)
            return HitResult(True, False, applied)

        # Normal pressure chips Dissonance as well as building toward a break.
        applied = round(amount * .45)
        self.hp -= applied
        self.hitFlash = .08
        gained = max(self.minimumStaggerPerHit, amount * self.staggerPerDamage)
        self.stagger = min(self.maxStagger, self.stagger + gained)
        if self.phase > 1 and self.phaseElapsed <= .75:
            self.runeDisruption += gained
            if self.runeDisruption >= self.runeDisruptionNeeded:
                self.runeSilenceRemaining = max(self.runeSilenceRemaining, 2.5)
                if not self.runeWasInterrupted:
                    self.runesInterrupted += 1
                    self.runeWasInterrupted = True
                    self._burst_particles(*self._center(), self.phaseAccent, 28, 3.0)
        self.staggerDecayTimer = self.staggerDecayDelay
        if self.stagger >= self.maxStagger:
            self._trigger_stagger()
        self.hp = max(0, self.hp)
        return HitResult(True, False, applied)

    def _trigger_stagger(self):
        if self.isStaggered or self.survivalActive:
            return
        was_perfect = self.phase > 1 and self.phaseElapsed <= .75
        self.isStaggered = True
        self.transitionCleanupRequested = True
        self.runeCannonCharge = 0.0
        self.runeCannonReceiver = None
        self.perfectStagger = was_perfect
        if self.perfectStagger:
            self.perfectStaggers += 1
            self.perfectBreakFlash = 1.0
            self.shakeStrength = max(self.shakeStrength, 9)
            self._burst_particles(*self._center(), ui.CREAM, 44, 3.8)
        self.staggerRemaining = self.staggerDuration
        self.fracture = 0.0
        self._burst_particles(*self._center(), ui.CREAM, 34, 3.4)

    def _update_stagger(self, dt):
        if self.survivalActive:
            self.stagger = 0.0
            self.staggerDecayTimer = 0.0
            self.isStaggered = False
            self.staggerRemaining = 0.0
            return
        if self.isStaggered:
            self.staggerRemaining = max(0.0, self.staggerRemaining - dt)
            if self.staggerRemaining <= 0:
                next_phase = (self._health_unlocked_survival()
                              or self._choose_damage_phase())
                if next_phase is not None:
                    self._set_phase(next_phase)
                else:
                    self.isStaggered = False
                    self.stagger = 0.0
                    self.staggerDecayTimer = 0.0
                self.perfectStagger = False
                self.fracture = 0.0
            return

        self.staggerDecayTimer = max(0.0, self.staggerDecayTimer - dt)
        if self.staggerDecayTimer <= 0 and self.stagger > 0:
            self.stagger = max(0.0, self.stagger - self.staggerDecayPerSecond * dt)
            self.staggerEverDecayed = True

    def _center(self):
        return self.worldX + self.size / 2, self.worldY + self.size / 2

    def _set_phase(self, phase):
        if phase == self.phase:
            return
        self._clear_survival_portals()
        self.phase = phase
        if phase in self.DAMAGE_PHASES:
            self.damagePhaseHistory.append(phase)
            self.damagePhaseHistory = self.damagePhaseHistory[-3:]
        self.phaseElapsed = 0.0
        self.stagger = 0.0
        self.staggerDecayTimer = 0.0
        self.isStaggered = False
        self.staggerRemaining = 0.0
        self.phaseForcedByTimer = False
        self.runeDisruption = 0.0
        self.runeWasInterrupted = False
        self.runeCannonCharge = 0.0
        self.runeCannonReceiver = None
        self.mirrorJumpRemaining = 0.0
        self.mirrorJumpStart = None
        self.mirrorJumpTarget = None
        self.mirrorJumpEchoOrigin = None
        self.phaseAnnouncementTimer = 5.0
        self.phaseProtectionTimer = 5.0 if self.cinematicTransitionsEnabled else 0.0
        self.survivalActive = phase in self.SURVIVAL_PHASES
        self.survivalRemaining = ((30.0 if phase == 9 else 20.0)
                                  if self.survivalActive else 0.0)
        self.survivalCooldown = .2
        self.bossBurstQueue.clear()
        self.survivalFormationCycle = 0
        if self.cinematicTransitionsEnabled:
            self.transitionRemaining = self.transitionDuration
            self.transitionCleanupRequested = True
            self._clear_field()
            self._clear_portals()
            center_x, center_y = self._arena_center()
            self.transitionStart = (self.worldX, self.worldY)
            self.transitionTarget = (center_x - self.size / 2, center_y - self.size / 2)
        self._burst_particles(*self._center(), self.phaseAccent, 24, 2.8)
        phase_metadata = {
            1: ("BOLSTER", "I did not expect a challenger...", ui.PURPLE),
            2: ("PREPARE", "Leave at once.", ui.CREAM),
            3: ("RETALIATE", "...", ui.GOLD),
            4: ("CONTEMPLATION", "Your pulse betrays you.", ui.RED),
            5: ("MIRROR", "Eons surpass years...", ui.GOLD),
            6: ("DOMINATE", "LEAVE.", ui.CREAM),
            7: ("REVOLVE", "All paths return to me.", ui.BLUE),
            8: ("DISPLAY", "Even light bends inward.", ui.PURPLE),
            9: ("GRANDEUR", "YOU BRING UPON YOURSELF A FATE WORSE THAN DEATH.", ui.RED),
        }
        self.phaseLabel, self.phaseFlavor, self.phaseAccent = phase_metadata[phase]
        if phase in (4, 7):
            self.actTransitionTimer = 2.2
            self.actTitle = "ACT II // THE FRACTURE" if phase == 4 else "ACT III // THE RETURN"
        if phase == 1:
            self._clear_portals()
            self._deploy_phase_one_portals()
        elif phase == 2:
            if not self.projectilePortals:
                self._deploy_phase_one_portals()
            self.carouselCooldown = .35
            for portal in self.projectilePortals:
                portal.angularSpeed = .48
                portal.fireInterval = 999
                portal.movementPath = "wave"
        elif phase == 3:
            if not self.projectilePortals:
                self._deploy_phase_one_portals()
            for portal in self.projectilePortals:
                portal.angularSpeed = .55
                portal.fireInterval = 1.4
                portal.fireCooldown = min(portal.fireCooldown, .35)
                portal.movementPath = "tornado"
        elif phase == 4:
            self.jumpCooldown = 4.5
            self._deploy_pattern_portals(4, 6.4, -.34, ui.RED, "dissonance_static")
            self.portalPatternCooldown = .25
        elif phase == 5:
            self._deploy_pattern_portals(2, 4.4, .42, ui.GOLD, "dissonance_mirror_portal")
            self.mirrorCooldown = .7
        elif phase == 6:
            self._deploy_relay_portals()
            for portal in self.projectilePortals:
                portal.movementPath = "wave"
            self.relayCooldown = .5
            self.relayPending = None
        elif phase == 7:
            self._deploy_pattern_portals(4, 5.4, .23, ui.BLUE, "dissonance_constellation")
            self.fieldDeployed = False
        elif phase == 8:
            if not self.projectilePortals or self.projectilePortals[0].owner != "dissonance_constellation":
                self._deploy_pattern_portals(4, 5.4, -.38, ui.BLUE, "dissonance_constellation")
            self.horizonCooldown = .3
            for portal in self.projectilePortals:
                portal.angularSpeed = -.38
                portal.movementPath = "tornado"
            for projectile in self.fieldProjectiles:
                projectile.angularSpeed *= 1.65
        elif phase == 9:
            self._clear_field()
            self._clear_portals()
            self._deploy_last_word_portals()
            for portal in self.projectilePortals:
                portal.movementPath = "tornado"
            self.lastWordCooldown = .2
            self.callbackCooldown = 1.4
            self.callbackIndex = 0
        if self.survivalActive:
            self._deploy_survival_portals()
        rune_strokes = self.PHASE_RUNES[phase][1]
        for portal in self.projectilePortals + self.survivalPortals:
            portal.reset_for_phase(rune_strokes)
            portal.hitsToDisable = 15

    def _choose_damage_phase(self):
        """Cycle attacks within the current act until its HP gate is reached."""
        act_phases = {
            3: (1, 2),
            6: (4, 5),
            9: (7, 8),
        }.get(self.nextSurvivalPhase, self.DAMAGE_PHASES)
        recent = set(self.damagePhaseHistory[-3:])
        choices = [phase for phase in act_phases if phase not in recent]
        if not choices:
            choices = [phase for phase in act_phases if phase != self.phase]
        if not choices:
            choices = list(act_phases)
        return self.rng.choice(choices)

    def _health_unlocked_survival(self):
        """Return the next ordered survival rune once its HP gate is reached."""
        if self.nextSurvivalPhase == 3 and self.hp <= self.maxHp * (2 / 3):
            return 3
        if self.nextSurvivalPhase == 6 and self.hp <= self.maxHp * (1 / 3):
            return 6
        if self.nextSurvivalPhase == 9 and self.hp <= 0:
            return 9
        return None

    def debug_set_phase(self, phase):
        phase = max(1, min(9, int(phase)))
        if phase == self.phase:
            self.phase = 0
        self._set_phase(phase)
        self.entranceRemaining = 0
        self.phaseElapsed = 0
        self.phaseProtectionTimer = 0

    def _survival_barrage(self, player_x, player_y, sink, dt):
        self.survivalCooldown -= dt
        if self.survivalCooldown > 0 or not self.projectilePortals:
            return
        active = [portal for portal in self.projectilePortals if portal.active]
        if active:
            source = active[self.carouselIndex % len(active)]
            portal_center = (source.worldX + source.size / 2,
                             source.worldY + source.size / 2)
            target = self._arena_center() if self.phase == 3 else (
                portal_center[0] + cos(source.angle) * self.arenaRadius,
                portal_center[1] + sin(source.angle) * self.arenaRadius)
            waves = (((3, .42, .82, .28), (5, .68, 1.08, .38), (4, .5, 1.38, .32))
                     if self.phase < 9 else
                     ((5, .72, .78, .27), (7, 1.02, 1.08, .36),
                      (3, .28, 1.48, .48), (8, 1.18, 1.24, .25)))
            source.fire_pattern_burst(sink, target, waves, .12 if self.phase == 9 else .16,
                                      .9, self.phaseAccent, "survival_burst")
            speed_burst_stride = 2 if self.phase == 9 else 4
            if self.carouselIndex % speed_burst_stride == 0:
                source.fire_speed_burst(sink, (player_x, player_y),
                                        7 if self.phase == 9 else 3 + self.phase // 4,
                                        self.phaseAccent,
                                        "survival_speed_burst")
            self.carouselIndex += 1
        self.survivalCooldown = .5 if self.phase == 9 else .82

        if self.survivalPortals:
            stride = 2 if self.phase == 9 else 3
            offset = self.carouselIndex % stride
            for index in range(offset, len(self.survivalPortals), stride):
                portal = self.survivalPortals[index]
                portal_center = (portal.worldX + portal.size / 2,
                                 portal.worldY + portal.size / 2)
                if self.phase == 3:
                    target = self._arena_center()
                else:
                    target = (portal_center[0] + cos(portal.angle) * self.arenaRadius,
                              portal_center[1] + sin(portal.angle) * self.arenaRadius)
                speed = (.65, .9, 1.15)[(index // stride + self.carouselIndex) % 3]
                portal.fire_pattern_burst(
                    sink, target,
                    ((1, 0, speed, .3), (1, 0, speed * 1.22, .4),
                     (1, 0, speed * .78, .24)), .11, .9,
                    self.phaseAccent, ("boundary_inward" if self.phase == 3
                                       else "boundary_outward"),
                )
                if self.phase == 9:
                    portal_center = (portal.worldX + portal.size / 2,
                                     portal.worldY + portal.size / 2)
                    tangent_target = (portal_center[0] + cos(portal.angle + pi / 2) * self.arenaRadius,
                                      portal_center[1] + sin(portal.angle + pi / 2) * self.arenaRadius)
                    portal.fire_toward(sink, tangent_target, 2, .24, speed * 1.15, .9,
                                       ui.CREAM, "boundary_tangent")

    def _deploy_survival_portals(self):
        self._clear_survival_portals()
        center = self._arena_center()
        count = 6 if self.phase < 9 else 8
        radius = (self.arenaRadius * .91 if self.phase == 3
                  else vH.tileSizeGlobal * 3.4 * self.arenaFormationScale)
        speed = {3: .2, 6: .34, 9: .48}[self.phase]
        for index in range(count):
            self.survivalPortals.append(ProjectilePortal(
                center, radius, index * 2 * pi / count,
                angular_speed=speed, fire_interval=999,
                pellet_count=2, spread=.22, owner="dissonance_survival_boundary",
                color=self.phaseAccent, movement_path="orbit",
            ))

    def _update_survival_formation(self, dt):
        """Choreograph the three survival rings as distinct set pieces."""
        center = self._arena_center()
        inner = vH.tileSizeGlobal * 3.4 * self.arenaFormationScale
        outer = self.arenaRadius * .91
        cycle = int(self.phaseElapsed / 3.2)
        for index, portal in enumerate(self.survivalPortals):
            portal.orbitCenter = self._center() if self.phase == 6 else center
            if self.phase == 3:
                target_radius, speed, path = outer, .2, "orbit"
            elif self.phase == 6:
                target_radius, speed, path = inner, .34, "orbit"
            else:
                # Jera swaps concentric ranks every few seconds.  During every
                # third exchange the ranks cross on interlocked infinity paths.
                outside = (index + cycle) % 2 == 0
                target_radius = outer if outside else inner
                speed = (.22, .42, .68)[cycle % 3]
                path = "figure8" if cycle % 3 == 2 else "orbit"
            portal.radius += (target_radius - portal.radius) * min(1, dt * 2.4)
            portal.angularSpeed = speed
            portal.movementPath = path
            portal.angle += portal.angularSpeed * dt
            portal._place()

    def _clear_survival_portals(self):
        for portal in self.survivalPortals:
            portal.remFlag = True
        self.survivalPortals.clear()

    def _clear_portals(self):
        for portal in self.projectilePortals:
            portal.remFlag = True
        self.projectilePortals.clear()

    def _begin_death(self):
        if self.dying:
            return
        self.hp = 1
        self.dying = True
        self.deathRemaining = self.deathDuration
        self.shakeStrength = 6
        self.transitionCleanupRequested = True
        self._clear_field()
        self._clear_portals()
        self._clear_survival_portals()
        center_x, center_y = self._arena_center()
        self.worldX, self.worldY = center_x - self.size / 2, center_y - self.size / 2

    def _clear_field(self):
        for projectile in self.fieldProjectiles:
            projectile.remFlag = True
        self.fieldProjectiles.clear()
        self.fieldDeployed = False

    def _arena_center(self):
        return (len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2,
                len(bG.currRoomRects) * vH.tileSizeGlobal / 2)

    def _deploy_phase_one_portals(self):
        center = self._arena_center()
        radius = vH.tileSizeGlobal * 7.2 * self.arenaFormationScale
        for index in range(3):
            self.projectilePortals.append(ProjectilePortal(
                center, radius, index * pi / 2, angular_speed=.32,
                fire_interval=1.85, pellet_count=7, spread=1.15,
            ))

    def _deploy_relay_portals(self):
        self._clear_portals()
        center = self._arena_center()
        radius = vH.tileSizeGlobal * 6.2 * self.arenaFormationScale
        for index in range(3):
            portal = ProjectilePortal(
                center, radius, index * pi / 2 + pi / 4,
                angular_speed=-.2, fire_interval=999,
                pellet_count=7, spread=1.0, owner="dissonance_relay",
            )
            self.projectilePortals.append(portal)

    def _deploy_pattern_portals(self, count, radius, angular_speed, color, owner):
        self._clear_portals()
        center = self._arena_center()
        paths = {"dissonance_static": "square", "dissonance_mirror_portal": "figure8",
                 "dissonance_constellation": "figure8"}
        for index in range(count):
            self.projectilePortals.append(ProjectilePortal(
                center, vH.tileSizeGlobal * radius * self.arenaFormationScale,
                index * 2 * pi / count,
                angular_speed=angular_speed, fire_interval=999,
                pellet_count=5, spread=.72, owner=owner, color=color,
                polarity=1 if index % 2 == 0 else -1,
                movement_path=paths.get(owner, "orbit"),
            ))

    def route_player_bullet(self, bullet, portal_index):
        """Send a player shot through a paired portal and empower the rerouted hit."""
        if bullet.portalCooldown > 0 or not self.projectilePortals:
            return False
        source = self.projectilePortals[portal_index]
        if not source.blocks_shots or source.polarity < 0:
            return False
        destination_index = (portal_index + len(self.projectilePortals) // 2) % len(self.projectilePortals)
        destination = self.projectilePortals[destination_index]
        if not destination.blocks_shots or destination is source:
            return False
        exit_distance = destination.size * .8
        bullet.worldX = destination.worldX + destination.size / 2 + cos(bullet.direc) * exit_distance
        bullet.worldY = destination.worldY + destination.size / 2 - sin(bullet.direc) * exit_distance
        bullet.posX, bullet.posY = bG.world_to_screen(bullet.worldX, bullet.worldY)
        bullet.damage *= 1.15 if destination.polarity > 0 else 1.05
        if destination.polarity < 0:
            bullet.direc += pi
        bullet.portalCooldown = .35
        return True

    def _deploy_last_word_portals(self):
        center = self._arena_center()
        for index in range(4):
            self.projectilePortals.append(ProjectilePortal(
                center, vH.tileSizeGlobal * 5.1 * self.arenaFormationScale, index * pi / 3,
                angular_speed=.72, fire_interval=1.05,
                pellet_count=5, spread=.7, owner="dissonance_last_word",
            ))

    def _move_toward(self, target_x, target_y, multiplier=1.0):
        center_x, center_y = self._center()
        delta_x, delta_y = target_x - center_x, target_y - center_y
        distance = max(1, hypot(delta_x, delta_y))
        step = self.speed * multiplier * vH.get_frame_scale()
        self._try_axis_move(delta_x / distance * step, "x")
        self._try_axis_move(delta_y / distance * step, "y")
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        return distance

    def _phase_movement(self, player_x, player_y, dt):
        """Give every rune a readable locomotion identity within its act."""
        arena_x, arena_y = self._arena_center()
        tile = vH.tileSizeGlobal
        mode = self.PHASE_MOVEMENT[self.phase]
        if mode == "harvest_chase":
            # Jera reaps in alternating curves instead of simply sitting on the player.
            side = sin(self.phaseElapsed * 1.35) * tile * 3.2
            angle = atan2(player_y - arena_y, player_x - arena_x) + pi / 2
            self._move_toward(player_x + cos(angle) * side,
                              player_y + sin(angle) * side, .72)
        elif mode in ("road_anchor", "day_anchor"):
            # Raidho and Dagaz command the roads/light from an unwavering central dais.
            self._move_toward(arena_x, arena_y, .75 if mode == "road_anchor" else 1.05)
        elif mode in ("torch_tornado", "hearth_tornado"):
            # A breathing orbit makes the cube corkscrew through the room like a storm eye.
            speed = .72 if mode == "torch_tornado" else 1.05
            radius = tile * (7.0 + 2.2 * sin(self.phaseElapsed * .62))
            angle = self.phaseElapsed * speed
            self._move_toward(arena_x + cos(angle) * radius,
                              arena_y + sin(angle) * radius, 1.35)
        elif mode == "hail_chase":
            # Hagalaz advances in abrupt diagonal hail-steps; its attack handles the leaps.
            diagonal = (pi / 4) * round(atan2(player_y - self._center()[1],
                                              player_x - self._center()[0]) / (pi / 4))
            self._move_toward(player_x + cos(diagonal) * tile,
                              player_y + sin(diagonal) * tile, .48)
        elif mode == "yew_anchor":
            self._move_toward(arena_x, arena_y, .38)
        elif mode == "sun_revolution":
            # Sowilo traces its lightning-bolt shape as two interlocked oscillations.
            self._move_toward(arena_x + sin(self.phaseElapsed * .82) * tile * 9,
                              arena_y + sin(self.phaseElapsed * 1.64) * tile * 4.5, 1.15)
        elif mode == "spear_intercept":
            # Tiwaz aims ahead of the hero, crossing their intended escape lane.
            lead = tile * 4.5
            angle = atan2(player_y - self._center()[1], player_x - self._center()[0])
            self._move_toward(player_x + cos(angle) * lead,
                              player_y + sin(angle) * lead, .92)

    def _projectile(self, sink, direction, speed, damage, size, **kwargs):
        center_x, center_y = self._center()
        sink.append(EnemyProjectile(
            center_x - size / 2, center_y - size / 2, direction,
            speed, damage, size, **kwargs,
        ))

    def _fire_laser(self, sink, target_x, target_y, color=None):
        center_x, center_y = self._center()
        direction = atan2(target_y - center_y, target_x - center_x)
        sink.append(EnemyProjectile(
            center_x, center_y, direction, 0, 2.0, vH.tileSizeGlobal * .42,
            travel_range=self.arenaRadius * 2.2, color=color or self.phaseAccent,
            shape="laser", path="laser", lifetime=4.0,
            owner="dissonance_rune_laser", ignore_walls=True,
        ))

    def _fire_speed_burst(self, sink, target_x, target_y, count=None):
        center_x, center_y = self._center()
        direction = atan2(target_y - center_y, target_x - center_x)
        speeds = (1.45, 1.12, .82, .56, .38)
        count = count or self.rng.randint(3, 5)
        for index in range(count):
            self._projectile(
                sink, direction, speeds[index], .9, vH.tileSizeGlobal * (.34 + index * .035),
                travel_range=float("inf"), color=self.phaseAccent, shape="diamond",
                owner="dissonance_speed_burst", ignore_walls=True,
            )
        # Two delayed echoes turn the speed stack into a true three-shot boss
        # burst while retaining the fast-leader/slow-tail dodge timing.
        self.bossBurstQueue.extend([
            [.13, target_x, target_y, max(3, count - 1), .82],
            [.28, target_x, target_y, min(5, count + 1), 1.12],
        ])

    def _update_boss_bursts(self, sink, dt):
        remaining = []
        for burst in self.bossBurstQueue:
            burst[0] -= dt
            if burst[0] > 0:
                remaining.append(burst)
                continue
            _, target_x, target_y, count, speed_scale = burst
            center_x, center_y = self._center()
            direction = atan2(target_y - center_y, target_x - center_x)
            for index in range(count):
                speed = (1.45, 1.12, .82, .56, .38)[index] * speed_scale
                self._projectile(
                    sink, direction, speed, .9,
                    vH.tileSizeGlobal * (.28 + index * .045),
                    travel_range=float("inf"), color=self.phaseAccent,
                    shape="diamond", owner="dissonance_speed_burst_echo",
                    ignore_walls=True,
                )
        self.bossBurstQueue = remaining

    def _lob_bomb(self, sink, target_x, target_y, color=None):
        center_x, center_y = self._center()
        sink.append(EnemyProjectile(
            center_x, center_y, 0, 0, 1.25, vH.tileSizeGlobal * .72,
            color=color or self.phaseAccent, shape="bomb", path="bomb",
            lifetime=3.0, target=(target_x, target_y), owner="dissonance_rune_bomb",
            ignore_walls=True,
        ))

    def _update_special_attacks(self, player_x, player_y, sink, dt):
        self.specialAttackCooldown -= dt
        if self.specialAttackCooldown > 0:
            return
        mode = self.callbackIndex % 3
        self._fire_laser(sink, player_x, player_y)
        if self.survivalActive and self.phase == 9:
            # Jera's finale overlaps the full learned vocabulary instead of
            # reducing the boss itself to an occasional laser.
            self._lob_bomb(sink, player_x, player_y, ui.RED)
            self._fire_speed_burst(sink, player_x, player_y, 5)
            self.specialAttackCooldown = 3.4
        elif self.survivalActive:
            self.specialAttackCooldown = 5.8
        elif mode == 1:
            self._lob_bomb(sink, player_x, player_y)
            self.specialAttackCooldown = 5.8
        elif mode == 2:
            self._fire_speed_burst(sink, player_x, player_y)
            self.specialAttackCooldown = 5.2
        else:
            self.specialAttackCooldown = 4.8
        self.callbackIndex += 1

    def _fire_mines(self, sink):
        count = 9
        for index in range(count):
            direction = 2 * pi * index / count + self.age * .013
            self._projectile(
                sink, direction, speed=1.15, damage=3.0, size=vH.tileSizeGlobal * .7,
                travel_range=5000, lifetime=22, speed_decay=.2,
                color=ui.RED, shape="mine", path="mine", owner="dissonance_mine",
            )

    def _fire_portal_mines(self, sink):
        for portal in self.projectilePortals:
            size = vH.tileSizeGlobal * .62
            portal_x, portal_y = portal.worldX + portal.size / 2, portal.worldY + portal.size / 2
            direction = atan2(self._arena_center()[1] - portal_y,
                              self._arena_center()[0] - portal_x)
            sink.append(EnemyProjectile(
                portal_x - size / 2, portal_y - size / 2, direction,
                .9, 2.5, size, travel_range=vH.tileSizeGlobal * 18,
                lifetime=18, speed_decay=.16, color=ui.RED,
                shape="mine", path="mine", owner="dissonance_portal_mine",
                ignore_walls=True,
            ))

    def _fire_sine_from_portal(self, portal, target, sink, count=3, color=None):
        portal_x, portal_y = portal.worldX + portal.size / 2, portal.worldY + portal.size / 2
        base = atan2(target[1] - portal_y, target[0] - portal_x)
        for index in range(count):
            offset = (index - (count - 1) / 2) * .42
            size = vH.tileSizeGlobal * .3
            sink.append(EnemyProjectile(
                portal_x - size / 2, portal_y - size / 2,
                base + offset, .72, 1.05, size,
                travel_range=vH.tileSizeGlobal * 72,
                color=color or portal.color, shape="diamond", path="sine",
                amplitude=vH.tileSizeGlobal * .22, frequency=.04,
                owner=f"{portal.owner}_sine", ignore_walls=True,
            ))

    def _fire_sine_fan(self, player_x, player_y, sink, count=6, color=None):
        center_x, center_y = self._center()
        base = atan2(player_y - center_y, player_x - center_x)
        for index in range(count):
            # Wide lanes make the fan something the player reads and pushes through,
            # rather than a compact wall that travels directly on top of itself.
            offset = (index - (count - 1) / 2) * .64
            self._projectile(
                sink, base + offset, speed=.45, damage=1.25,
                size=vH.tileSizeGlobal * .42, travel_range=vH.tileSizeGlobal * 72,
                color=color or ui.PURPLE, shape="diamond", path="sine",
                amplitude=vH.tileSizeGlobal * (0.32 + abs(offset)) * .5, frequency=.035,
                owner="dissonance_sine",
            )

    def _phase_one(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.update(sink, dt)
        self.patternCooldown -= dt
        if self.patternCooldown <= 0:
            self._fire_sine_fan(player_x, player_y, sink, 6)
            self.patternCooldown = 1.05

        self.mineCooldown -= dt
        if self.mineCooldown <= 0:
            self._fire_portal_mines(sink)
            self.mineCooldown = 4.6

    def _phase_closing_spiral(self, player_x, player_y, sink, dt):
        # The shrinking radius is time-based, so the transition remains threatening
        # even if the player briefly stops dealing damage.
        target_radius = (vH.tileSizeGlobal * self.arenaFormationScale
                         * max(3.8, 7.2 - self.phaseElapsed * .7))
        for portal in self.projectilePortals:
            portal.radius += (target_radius - portal.radius) * min(1, dt * 2.2)
            portal.update(sink, dt)

    def _phase_crossfire_carousel(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed * dt
            portal._place()
        self.carouselCooldown -= dt
        if self.carouselCooldown <= 0 and self.projectilePortals:
            portal = self.projectilePortals[self.carouselIndex % len(self.projectilePortals)]
            portal_center = (portal.worldX + portal.size / 2, portal.worldY + portal.size / 2)
            tangent = portal.angle + (pi / 2 if self.carouselIndex % 2 == 0 else -pi / 2)
            target = (portal_center[0] + cos(tangent) * vH.tileSizeGlobal * 12,
                      portal_center[1] + sin(tangent) * vH.tileSizeGlobal * 12)
            portal.fire_toward(
                sink, target, pellet_count=6, spread=.9, speed=1.3,
                damage=.9, color=ui.CREAM, owner_suffix="carousel",
            )
            self.carouselIndex += 1
            self.carouselCooldown = .48

        self.patternCooldown -= dt
        if self.patternCooldown <= 0:
            self._fire_sine_fan(player_x, player_y, sink, 5, ui.PURPLE)
            self.patternCooldown = 1.35

    def _jump_toward_player(self, player_x, player_y):
        center_x, center_y = self._center()
        delta_x, delta_y = player_x - center_x, player_y - center_y
        distance = max(1, hypot(delta_x, delta_y))
        jump_distance = min(vH.tileSizeGlobal * 7, max(0, distance - vH.tileSizeGlobal * 2.5))
        target = pygame.Rect(
            self.worldX + delta_x / distance * jump_distance,
            self.worldY + delta_y / distance * jump_distance,
            self.size, self.size,
        )
        safe = bG.find_nearest_open_rect(target, self.size)
        self.worldX, self.worldY = safe.x, safe.y
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _start_mirror_jump(self, player_x, player_y):
        center_x, center_y = self._center()
        delta_x, delta_y = player_x - center_x, player_y - center_y
        distance = max(1, hypot(delta_x, delta_y))
        jump_distance = min(vH.tileSizeGlobal * 7,
                            max(0, distance - vH.tileSizeGlobal * 2.5))
        target = pygame.Rect(
            self.worldX + delta_x / distance * jump_distance,
            self.worldY + delta_y / distance * jump_distance,
            self.size, self.size,
        )
        safe = bG.find_nearest_open_rect(target, self.size)
        self.mirrorJumpStart = (self.worldX, self.worldY)
        self.mirrorJumpTarget = (safe.x, safe.y)
        self.mirrorJumpEchoOrigin = self._center()
        self.mirrorJumpRemaining = self.mirrorJumpDuration

    def _update_mirror_jump(self, sink, dt):
        if self.mirrorJumpRemaining <= 0:
            return False
        self.mirrorJumpRemaining = max(0.0, self.mirrorJumpRemaining - dt)
        progress = 1 - self.mirrorJumpRemaining / self.mirrorJumpDuration
        eased = progress * progress * (3 - 2 * progress)
        self.worldX = self.mirrorJumpStart[0] + (self.mirrorJumpTarget[0] - self.mirrorJumpStart[0]) * eased
        self.worldY = (self.mirrorJumpStart[1] +
                       (self.mirrorJumpTarget[1] - self.mirrorJumpStart[1]) * eased -
                       sin(progress * pi) * vH.tileSizeGlobal * 1.35)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        if self.mirrorJumpRemaining > 0:
            return True
        for index, (portal, echo) in enumerate(zip(
                self.projectilePortals, (self.mirrorJumpEchoOrigin, self._center()))):
            portal.orbitCenter = echo
            portal.radius = 0
            portal._place()
            self._fire_radial_from(sink, echo, 10, self.phaseElapsed * .18,
                                   ui.GOLD if portal is self.projectilePortals[-1] else ui.RED,
                                   "dissonance_mirror_portal_landing_echo" if index
                                   else "dissonance_mirror_portal_afterimage")
        return False

    def _phase_two(self, player_x, player_y, sink, dt):
        if self.phaseElapsed < 1.6:
            return
        if self.jumpRecovery > 0:
            self.jumpRecovery -= dt
            return
        if self.jumpWindup > 0:
            self.jumpWindup -= dt
            if self.jumpWindup <= 0:
                self._jump_toward_player(player_x, player_y)
                self.jumpRecovery = .85
            return

        self.jumpCooldown -= dt
        if self.jumpCooldown <= 0:
            self.jumpWindup = 1.15
            self.jumpCooldown = 7.2
            return

        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed * dt
            portal._place()

        # Opposite portals draw narrow, rotating chords across the room.
        self.radialCooldown -= dt
        if self.radialCooldown <= 0 and self.projectilePortals:
            half = max(1, len(self.projectilePortals) // 2)
            source_index = self.carouselIndex % half
            for index in (source_index, source_index + half):
                source = self.projectilePortals[index]
                target = self.projectilePortals[(index + half) % len(self.projectilePortals)]
                source.fire_toward(
                    sink, (target.worldX + target.size / 2, target.worldY + target.size / 2),
                    pellet_count=5, spread=.3, speed=1.65, damage=1.0,
                    color=ui.RED, owner_suffix="chord",
                )
            self.carouselIndex += 1
            self.radialCooldown = .72

        self.aimedCooldown -= dt
        if self.aimedCooldown <= 0 and self.projectilePortals:
            for index in range(0, len(self.projectilePortals), 2):
                self._fire_sine_from_portal(
                    self.projectilePortals[index], (player_x, player_y), sink, 3, ui.GOLD,
                )
            self.aimedCooldown = 1.8

    def _fire_radial_from(self, sink, origin, count, offset, color, owner):
        size = vH.tileSizeGlobal * .3
        for index in range(count):
            sink.append(EnemyProjectile(
                origin[0] - size / 2, origin[1] - size / 2,
                2 * pi * index / count + offset, 1.35, 1.0, size,
                travel_range=vH.tileSizeGlobal * 15, color=color,
                shape="diamond", owner=owner,
            ))

    def _phase_mirror_step(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed * dt
            portal._place()
        if self._update_mirror_jump(sink, dt):
            return
        self.mirrorCooldown -= dt
        if self.mirrorCooldown <= 0:
            self._start_mirror_jump(player_x, player_y)
            self.mirrorCooldown = 1.55

        self.aimedCooldown -= dt
        if self.aimedCooldown <= 0 and self.projectilePortals:
            for portal in self.projectilePortals:
                self._fire_sine_from_portal(portal, (player_x, player_y), sink, 3, ui.CREAM)
            self.aimedCooldown = 1.1

    def _deploy_rotating_diamond(self, player_x, player_y, sink):
        center_x, center_y = self._center()
        away_angle = atan2(center_y - player_y, center_x - player_x)
        spacing = vH.tileSizeGlobal * .66
        for diamond_index in range(4):
            diamond_angle = away_angle + diamond_index * pi / 2
            for row in range(1, 7):
                half_width = min(row - 1, 6 - row)
                for column in range(-half_width, half_width + 1):
                    forward = vH.tileSizeGlobal * 1.2 + row * spacing
                    lateral = column * spacing
                    offset_x = cos(diamond_angle) * forward - sin(diamond_angle) * lateral
                    offset_y = sin(diamond_angle) * forward + cos(diamond_angle) * lateral
                    radius = hypot(offset_x, offset_y)
                    angle = atan2(offset_y, offset_x)
                    projectile = EnemyProjectile(
                        center_x, center_y, 0, 0, 1.55, vH.tileSizeGlobal * .34,
                        travel_range=float("inf"), lifetime=90, color=ui.BLUE, shape="mine",
                        path="orbit", orbit_center=(center_x, center_y),
                        orbit_radius=radius, orbit_angle=angle,
                        angular_speed=.12 + diamond_index * .025,
                        owner="dissonance_field", ignore_walls=True,
                    )
                    sink.append(projectile)
                    self.fieldProjectiles.append(projectile)
        self.fieldDeployed = True

    def _phase_three(self, player_x, player_y, sink, dt):
        center_world_x = len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2
        center_world_y = len(bG.currRoomRects) * vH.tileSizeGlobal / 2
        if not self.fieldDeployed:
            self._deploy_rotating_diamond(player_x, player_y, sink)

        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed * dt
            # Each point breathes at a different radius, making a moving
            # constellation rather than a single uniform ring.
            portal.radius = (vH.tileSizeGlobal * self.arenaFormationScale
                             * (4.8 + .8 * sin(self.phaseElapsed * .7 + portal.angle * 2)))
            portal._place()

        self.patternCooldown -= dt
        if self.patternCooldown <= 0 and self.projectilePortals:
            source = self.projectilePortals[self.carouselIndex % len(self.projectilePortals)]
            self._fire_sine_from_portal(source, (player_x, player_y), sink, 5, ui.BLUE)
            self.carouselIndex += 2
            self.patternCooldown = .78

        self.fieldShotCooldown -= dt
        if self.fieldShotCooldown <= 0 and self.projectilePortals:
            # The other constellation points cross-connect, drawing a pentagram one
            # edge at a time before the next aimed sine fan arrives.
            source = self.projectilePortals[self.carouselIndex % len(self.projectilePortals)]
            target = self.projectilePortals[(self.carouselIndex + 2) % len(self.projectilePortals)]
            source.fire_toward(
                sink, (target.worldX + target.size / 2, target.worldY + target.size / 2),
                pellet_count=2, spread=.12, speed=1.5, damage=1.0,
                color=ui.CREAM, owner_suffix="constellation_edge",
            )
            self.fieldShotCooldown = 1.15
        self._update_rune_cannon(player_x, player_y, sink, dt)

    def _phase_event_horizon(self, player_x, player_y, sink, dt):
        self._phase_three(player_x, player_y, sink, dt)
        self.horizonCooldown -= dt
        if self.horizonCooldown <= 0 and self.projectilePortals:
            # Two constellation portals on opposite sides fire through the core;
            # the selected diameter rotates on every pulse.
            start = self.carouselIndex % len(self.projectilePortals)
            sources = (self.projectilePortals[start],
                       self.projectilePortals[(start + len(self.projectilePortals) // 2) % len(self.projectilePortals)])
            for source in sources:
                source.fire_toward(
                    sink, self._arena_center(), pellet_count=5, spread=.42,
                    speed=1.75, damage=1.05, color=ui.PURPLE,
                    owner_suffix="horizon",
                )
            self.carouselIndex += 1
            self.horizonCooldown = .62

    def _phase_last_word(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.update(sink, dt)
        self.lastWordCooldown -= dt
        if self.lastWordCooldown <= 0:
            count = 14
            gap = int((atan2(player_y - self._center()[1], player_x - self._center()[0])
                       % (2 * pi)) / (2 * pi) * count)
            for index in range(count):
                # A rotating two-projectile gap gives the player a deliberate exit.
                if index in (gap, (gap + 1) % count):
                    continue
                self._projectile(
                    sink, 2 * pi * index / count + self.phaseElapsed * .22,
                    speed=1.55, damage=1.1, size=vH.tileSizeGlobal * .3,
                    travel_range=vH.tileSizeGlobal * 16, color=ui.RED,
                    owner="dissonance_last_word_ring",
                )
            self.lastWordCooldown = .72

        self.callbackCooldown -= dt
        if self.callbackCooldown <= 0 and self.projectilePortals:
            mode = self.callbackIndex % 3
            if mode == 0:
                # Crossfire Carousel callback: every other portal fires tangent.
                for index, portal in enumerate(self.projectilePortals[::2]):
                    center = (portal.worldX + portal.size / 2, portal.worldY + portal.size / 2)
                    tangent = portal.angle + (pi / 2 if index % 2 == 0 else -pi / 2)
                    target = (center[0] + cos(tangent) * vH.tileSizeGlobal * 11,
                              center[1] + sin(tangent) * vH.tileSizeGlobal * 11)
                    portal.fire_toward(sink, target, 3, .34, 1.45, 1.0,
                                       ui.CREAM, "callback_carousel")
            elif mode == 1:
                # Red Static callback: opposite portal chords.
                half = len(self.projectilePortals) // 2
                for index in range(half):
                    source, target = self.projectilePortals[index], self.projectilePortals[index + half]
                    source.fire_toward(
                        sink, (target.worldX + target.size / 2, target.worldY + target.size / 2),
                        2, .1, 1.7, 1.0, ui.RED, "callback_chord",
                    )
            else:
                # Portal Relay callback: a transfer walks one step around the ring.
                source = self.projectilePortals[self.callbackIndex % len(self.projectilePortals)]
                target = self.projectilePortals[(self.callbackIndex + 1) % len(self.projectilePortals)]
                source.fire_toward(
                    sink, (target.worldX + target.size / 2, target.worldY + target.size / 2),
                    1, 0, 1.9, .9, ui.GOLD, "callback_relay",
                )
            self.callbackIndex += 1
            self.callbackCooldown = 2.2
        self._update_rune_cannon(player_x, player_y, sink, dt)

    def _update_rune_cannon(self, player_x, player_y, sink, dt):
        if self.runeCannonCharge > 0:
            self.runeCannonCharge = max(0.0, self.runeCannonCharge - dt)
            receiver = (self.projectilePortals[self.runeCannonReceiver]
                        if self.runeCannonReceiver is not None
                        and self.runeCannonReceiver < len(self.projectilePortals) else None)
            if receiver is None or not receiver.blocks_shots:
                self.stagger = min(self.maxStagger, self.stagger + 20)
                self.runeSilenceRemaining = max(self.runeSilenceRemaining, 1.2)
                self.runeCannonCharge = 0
                self.runeCannonReceiver = None
            elif self.runeCannonCharge <= 0:
                receiver.fire_toward(
                    sink, (player_x, player_y), 9, 1.25, 1.8, 1.25,
                    ui.CREAM, "rune_cannon",
                )
                self.runeCannonReceiver = None
            return

        self.runeCannonCooldown -= dt
        active = [(index, portal) for index, portal in enumerate(self.projectilePortals)
                  if portal.blocks_shots]
        if self.runeCannonCooldown <= 0 and len(active) >= 2:
            self.runeCannonReceiver = active[self.carouselIndex % len(active)][0]
            receiver = self.projectilePortals[self.runeCannonReceiver]
            target = (receiver.worldX + receiver.size / 2, receiver.worldY + receiver.size / 2)
            for _, portal in active:
                portal.telegraphTimer = 1.4
                portal.telegraphKind = "line"
                portal.telegraphTarget = target
            self.runeCannonCharge = 1.4
            self.runeCannonCooldown = 7.5

    def _phase_portal_relay(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed * dt
            portal._place()

        if self.relayPending is not None:
            self.relayPending[0] -= dt
            if self.relayPending[0] <= 0:
                receiver, direction_target = self.relayPending[1], self.relayPending[2]
                receiver.fire_toward(
                    sink, direction_target, pellet_count=5, spread=.82,
                    speed=1.4, damage=1.0, color=ui.CREAM,
                    owner_suffix="redirect",
                )
                self.relayPending = None

        self.relayCooldown -= dt
        if self.relayCooldown <= 0 and self.relayPending is None and self.projectilePortals:
            source = self.projectilePortals[self.relayIndex % len(self.projectilePortals)]
            receiver = self.projectilePortals[(self.relayIndex + 1) % len(self.projectilePortals)]
            receiver_center = (receiver.worldX + receiver.size / 2, receiver.worldY + receiver.size / 2)
            source.fire_toward(
                sink, receiver_center, pellet_count=1, spread=0,
                speed=1.8, damage=.8, color=ui.GOLD, owner_suffix="transfer",
            )
            source_center = (source.worldX + source.size / 2, source.worldY + source.size / 2)
            receiver_center_now = (receiver.worldX + receiver.size / 2, receiver.worldY + receiver.size / 2)
            continuation = (receiver_center_now[0] + (receiver_center_now[0] - source_center[0]),
                            receiver_center_now[1] + (receiver_center_now[1] - source_center[1]))
            self.relayPending = [.42, receiver, continuation]
            self.relayIndex += 1
            self.relayCooldown = 1.35

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        dt = self._seconds()
        self._update_stagger(dt)
        self.runeSilenceRemaining = max(0.0, self.runeSilenceRemaining - dt)
        self.phaseProtectionTimer = max(0.0, self.phaseProtectionTimer - dt)
        self.staggerRecoveryRemaining = max(0.0, self.staggerRecoveryRemaining - dt)
        for portal in self.projectilePortals:
            portal.update_status(dt)
        for portal in self.survivalPortals:
            portal.update_status(dt)
        self.age += vH.get_timer_step()
        self._update_visuals(dt)
        if self.dying:
            self.shakeStrength = max(self.shakeStrength,
                                     2.5 + 1.5 * abs(sin(self.deathRemaining * 1.35)))
            previous_tick = int(self.deathRemaining * 8)
            self.deathRemaining = max(0.0, self.deathRemaining - dt)
            if self.deathRemaining <= 0:
                self.hp = 0
                vH.screenShakeX = 0
                vH.screenShakeY = 0
            elif (self.deathRemaining <= self.deathBurstDuration
                  and int(self.deathRemaining * 8) != previous_tick):
                self._burst_particles(*self._center(), self.phaseAccent, 8, 2.5)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.entranceRemaining > 0:
            self.entranceRemaining = max(0.0, self.entranceRemaining - dt)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.transitionRemaining > 0:
            self.transitionRemaining = max(0.0, self.transitionRemaining - dt)
            if self.transitionStart is not None and self.transitionTarget is not None:
                progress = 1 - self.transitionRemaining / self.transitionDuration
                eased = progress * progress * (3 - 2 * progress)
                self.worldX = self.transitionStart[0] + (self.transitionTarget[0] - self.transitionStart[0]) * eased
                self.worldY = self.transitionStart[1] + (self.transitionTarget[1] - self.transitionStart[1]) * eased
                if self.transitionRemaining <= 0:
                    self.worldX, self.worldY = self.transitionTarget
                    self.transitionStart = self.transitionTarget = None
            self.phaseAnnouncementTimer = max(0.0, self.phaseAnnouncementTimer - dt)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.isStaggered:
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.staggerRecoveryRemaining > 0:
            progress = 1 - self.staggerRecoveryRemaining
            for index, portal in enumerate(self.projectilePortals):
                portal.showTether = index < int(progress * len(self.projectilePortals)) + 1
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        for portal in self.projectilePortals:
            portal.showTether = True
        self.phaseElapsed += dt
        self._update_boss_bursts(projectile_sink, dt)
        self.phaseAnnouncementTimer = max(0.0, self.phaseAnnouncementTimer - dt)
        if not self.debugPhaseLocked and not self.survivalActive:
            survival_phase = self._health_unlocked_survival()
            if survival_phase is not None:
                self._set_phase(survival_phase)
            elif self.phaseElapsed >= self.phaseTimeLimit and self.phase in self.DAMAGE_PHASES:
                self._set_phase(self._choose_damage_phase())
                self.phaseForcedByTimer = True

        if self.transitionRemaining > 0:
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        if self.survivalActive:
            self.survivalRemaining = max(0.0, self.survivalRemaining - dt)
            if self.survivalRemaining <= 0:
                self.survivalActive = False
                if self.phase < 9:
                    self.nextSurvivalPhase = 6 if self.phase == 3 else 9
                    self._set_phase(self._choose_damage_phase())
                    self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
                    return
                self._begin_death()
                self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
                return

        if self.runeSilenceRemaining > 0:
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        if self.survivalActive:
            self._phase_movement(player_world_x, player_world_y, dt)
            for portal in self.projectilePortals:
                portal.angle += portal.angularSpeed * dt
                portal._place()
                portal.update_bursts(projectile_sink, dt)
            self._update_survival_formation(dt)
            for portal in self.survivalPortals:
                portal.update_bursts(projectile_sink, dt)
            self._survival_barrage(player_world_x, player_world_y, projectile_sink, dt)
            self._update_special_attacks(player_world_x, player_world_y, projectile_sink, dt)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        if self.phase == 1:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_one(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 2:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_crossfire_carousel(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 3:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_closing_spiral(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 4:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_two(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 5:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_mirror_step(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 6:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_portal_relay(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 7:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_three(player_world_x, player_world_y, projectile_sink, dt)
        elif self.phase == 8:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_event_horizon(player_world_x, player_world_y, projectile_sink, dt)
        else:
            self._phase_movement(player_world_x, player_world_y, dt)
            self._phase_last_word(player_world_x, player_world_y, projectile_sink, dt)
        self._update_special_attacks(player_world_x, player_world_y, projectile_sink, dt)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def get_screen_hitboxes(self):
        hitboxes = super().get_screen_hitboxes()
        if self.survivalActive:
            return hitboxes
        for index, portal in enumerate(self.projectilePortals):
            if portal.blocks_shots:
                x, y = bG.world_to_screen(portal.worldX, portal.worldY)
                hitboxes.append((f"portal:{index}", pygame.Rect(x, y, portal.size, portal.size)))
        return hitboxes

    def get_world_hitboxes(self):
        hitboxes = super().get_world_hitboxes()
        if self.survivalActive:
            return hitboxes
        for index, portal in enumerate(self.projectilePortals):
            if portal.blocks_shots:
                hitboxes.append((f"portal:{index}", pygame.Rect(
                    portal.worldX, portal.worldY, portal.size, portal.size,
                )))
        return hitboxes

    def challenge_results(self):
        """Stable hooks for future achievements, drops, and hard-mode unlocks."""
        return {
            "no_portals_broken": self.portalsBroken == 0,
            "unbroken_pressure": not self.staggerEverDecayed,
            "rune_interrupter": self.runesInterrupted >= 3,
            "perfect_breaker": self.perfectStaggers >= 3,
        }

    def _cube_geometry(self, center, extent):
        transition = max(0.0, 1.0 - self.phaseElapsed) if self.phase > 1 else 0.0
        stagger_wobble = sin(self.age * .09) * .12 if self.isStaggered else 0.0
        yaw = self.age * (.0075 + self.phase * .00055) + transition * transition * pi + stagger_wobble
        pitch = (.42 + sin(self.age * (.0055 + self.phase * .0002)) * .16
                 + transition * .22)
        vertices = []
        for x, y, z in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                        (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
            rotated_x = x * cos(yaw) + z * sin(yaw)
            rotated_z = -x * sin(yaw) + z * cos(yaw)
            rotated_y = y * cos(pitch) - rotated_z * sin(pitch)
            rotated_z = y * sin(pitch) + rotated_z * cos(pitch)
            perspective = 3.8 / (3.8 - rotated_z)
            vertices.append((center[0] + rotated_x * extent * perspective,
                             center[1] + rotated_y * extent * perspective,
                             rotated_z))
        faces = ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1),
                 (3, 2, 6, 7), (0, 3, 7, 4), (1, 5, 6, 2))
        return vertices, sorted(faces, key=lambda face: sum(vertices[i][2] for i in face) / 4)

    def _draw_cube_aura(self, screen, center, color):
        transition = max(0.0, 1.0 - self.phaseElapsed) if self.phase > 1 else 0.0
        beat = (1 + sin(self.age * .035) * .055) * (1 + transition * .22)
        for index in range(3):
            width = self.size * (1.18 + index * .18) * beat
            height = self.size * (.56 + index * .1) * beat
            arc_rect = pygame.Rect(0, 0, width, height)
            arc_rect.center = center
            start = self.age * (.012 + index * .004) * (-1 if index % 2 else 1)
            pygame.draw.arc(screen, ui.INK, arc_rect, start, start + pi * 1.18,
                            max(4, int(self.size * .065)))
            pygame.draw.arc(screen, color, arc_rect, start, start + pi * 1.18,
                            max(1, int(self.size * .022)))

        shard_count = 4 + self.phase // 3
        orbit = self.age * (.016 + self.phase * .0008)
        for index in range(shard_count):
            angle = orbit + index * 2 * pi / shard_count
            distance = self.size * (.67 + .08 * sin(self.age * .025 + index))
            shard_x = center[0] + cos(angle) * distance
            shard_y = center[1] + sin(angle) * distance * .48
            shard_size = self.size * (.055 + .012 * sin(self.age * .04 + index))
            points = ((shard_x, shard_y - shard_size * 1.5),
                      (shard_x + shard_size, shard_y),
                      (shard_x, shard_y + shard_size * 1.5),
                      (shard_x - shard_size, shard_y))
            pygame.draw.polygon(screen, ui.INK, points)
            inner = tuple((center[0] + (x - center[0]) * .82,
                           center[1] + (y - center[1]) * .82) for x, y in points)
            pygame.draw.polygon(screen, color, inner)

        if transition > 0:
            burst_radius = self.size * (1.35 - transition * .65)
            pygame.draw.circle(screen, color, center, burst_radius,
                               max(1, int(self.size * .025)))

        if self.isStaggered:
            for index in range(4):
                angle = self.age * .04 + index * pi / 2
                start = (center[0] + cos(angle) * self.size * .48,
                         center[1] + sin(angle) * self.size * .48)
                middle = (center[0] + cos(angle + .18) * self.size * .64,
                          center[1] + sin(angle + .18) * self.size * .64)
                end = (center[0] + cos(angle) * self.size * .78,
                       center[1] + sin(angle) * self.size * .78)
                pygame.draw.lines(screen, ui.CREAM, False, (start, middle, end), 2)

        if self.staggerRecoveryRemaining > 0:
            recovery_radius = self.size * (1.55 - self.staggerRecoveryRemaining * .55)
            pygame.draw.circle(screen, ui.CREAM, center, recovery_radius,
                               max(2, int(self.size * .035)))

    def _draw_motion_trail(self, screen):
        """Layer translucent interpolated echoes behind Dissonance's live cube."""
        for index, ghost in enumerate(self.motionTrail):
            alpha = int(72 * (ghost["life"] / .52) ** 2)
            if alpha <= 2:
                continue
            x, y = bG.world_to_screen(ghost["x"], ghost["y"])
            radius = self.size * (.19 + .035 * index / max(1, len(self.motionTrail)))
            echo = pygame.Surface((int(radius * 2 + 8), int(radius * 2 + 8)), pygame.SRCALPHA)
            echo_rect = echo.get_rect()
            color = ghost["accent"]
            pygame.draw.circle(echo, (color.r, color.g, color.b, alpha),
                               echo_rect.center, int(radius), max(2, int(radius * .18)))
            pygame.draw.circle(echo, (ui.CREAM.r, ui.CREAM.g, ui.CREAM.b, alpha // 2),
                               echo_rect.center, max(2, int(radius * .38)), 2)
            screen.blit(echo, (x + self.size / 2 - echo_rect.centerx,
                               y + self.size / 2 - echo_rect.centery))

    def _draw_arena_inscription(self, screen):
        rune_phase = 9 if (self.dying or self.phase == 9 and self.survivalActive) else self.phase
        _, strokes = self.PHASE_RUNES[rune_phase]
        center = bG.world_to_screen(*self._arena_center())
        radius = (vH.tileSizeGlobal * self.arenaFormationScale
                  * (3.2 + .25 * sin(self.age * .01)))
        for stroke_index, stroke in enumerate(strokes):
            points = [(center[0] + x * radius, center[1] + y * radius) for x, y in stroke]
            if len(points) > 1:
                pulse = max(1, int(2 + 2 * (1 + sin(self.age * .025 + stroke_index))))
                pygame.draw.lines(screen, ui.SHADOW, False, points, 14)
                pygame.draw.lines(screen, ui.INK, False, points, 8)
                pygame.draw.lines(screen, self.phaseAccent, False, points, pulse)
                segment = int(self.age * .018 + stroke_index) % (len(points) - 1)
                travel = (self.age * .018 + stroke_index) % 1
                spark_x = points[segment][0] + (points[segment + 1][0] - points[segment][0]) * travel
                spark_y = points[segment][1] + (points[segment + 1][1] - points[segment][1]) * travel
                spark = pygame.Rect(int(spark_x) - 4, int(spark_y) - 4, 8, 8)
                pygame.draw.rect(screen, ui.CREAM, spark)

    def _phase_timer_ratio(self):
        if self.transitionRemaining > 0:
            return 0.0
        if self.survivalActive:
            duration = 30.0 if self.phase == 9 else 20.0
            return max(0.0, min(1.0, self.survivalRemaining / duration))
        return max(0.0, min(1.0, 1.0 - self.phaseElapsed / self.phaseTimeLimit))

    def _draw_arena_boundary(self, screen):
        center = bG.world_to_screen(*self._arena_center())
        radius = self.arenaRadius
        timer_radius = radius + 22
        timer_rect = pygame.Rect(0, 0, timer_radius * 2, timer_radius * 2)
        timer_rect.center = center
        pygame.draw.circle(screen, ui.SHADOW, center, timer_radius, 12)
        pygame.draw.circle(screen, ui.INK, center, timer_radius, 7)
        if self.transitionRemaining <= 0:
            timer_ratio = self._phase_timer_ratio()
            if timer_ratio > 0:
                start = -pi / 2
                end = start + 2 * pi * timer_ratio
                pygame.draw.arc(screen, self.phaseAccent, timer_rect, start, end, 7)
                tip = (center[0] + cos(end) * timer_radius,
                       center[1] + sin(end) * timer_radius)
                pygame.draw.circle(screen, ui.CREAM, tip, 5)
        pygame.draw.circle(screen, ui.SHADOW, center, radius + 14, 26)
        pygame.draw.circle(screen, ui.INK, center, radius + 5, 14)
        pygame.draw.circle(screen, self.phaseAccent, center, radius + 2, 6)
        pygame.draw.circle(screen, ui.CREAM, center, radius - 5, 2)
        for index in range(24):
            angle = index * 2 * pi / 24 + self.age * .0015
            inner = radius - (8 if index % 3 else 15)
            start = (center[0] + cos(angle) * inner, center[1] + sin(angle) * inner)
            end = (center[0] + cos(angle) * radius, center[1] + sin(angle) * radius)
            pygame.draw.line(screen, self.phaseAccent if index % 3 else ui.CREAM,
                             start, end, 2)
        for index in range(8):
            angle = self.age * .012 + index * pi / 4
            packet_center = (center[0] + cos(angle) * radius,
                             center[1] + sin(angle) * radius)
            packet = pygame.Rect(packet_center[0] - 5, packet_center[1] - 5, 10, 10)
            pygame.draw.rect(screen, ui.INK, packet.inflate(4, 4))
            pygame.draw.rect(screen, self.phaseAccent, packet)
        wave_radius = radius - (self.age * .8 % 34)
        pygame.draw.circle(screen, self.phaseAccent, center, wave_radius, 1)
        for ring_index in range(3):
            points = []
            for step in range(64):
                angle = step * 2 * pi / 64 + self.age * (.004 + ring_index * .0015)
                ripple = sin(angle * (3 + ring_index) + self.age * .025) * (5 + ring_index * 3)
                ring_radius = radius + ripple - ring_index * 7
                points.append((center[0] + cos(angle) * ring_radius,
                               center[1] + sin(angle) * ring_radius))
            pygame.draw.lines(screen, self.phaseAccent if ring_index != 1 else ui.CREAM,
                              True, points, 2)
        for index in range(18):
            angle = self.age * .018 + index * 2 * pi / 18
            drift = sin(self.age * .03 + index * 1.7) * 10
            point = (center[0] + cos(angle) * (radius + drift),
                     center[1] + sin(angle) * (radius + drift))
            pygame.draw.circle(screen, self.phaseAccent, point, 2 + index % 3)

    def _draw_arena_mask(self, screen):
        center = bG.world_to_screen(*self._arena_center())
        key = (screen.get_size(), tuple(map(int, center)), int(self.arenaRadius))
        if key != self._arenaMaskCacheKey:
            mask = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
            mask.fill((0, 0, 0, 255))
            pygame.draw.circle(mask, (0, 0, 0, 0), center, self.arenaRadius + 8)
            self._arenaMaskCache = mask
            self._arenaMaskCacheKey = key
        screen.blit(self._arenaMaskCache, (0, 0))

    def _draw_death_spectacle(self, screen, center):
        if not self.dying:
            return
        progress = 1 - self.deathRemaining / self.deathDuration
        # These beams are purely celebratory and never enter the collision system.
        beam_count = 3 + int(progress * 5)
        arena_radius = self.arenaRadius * (1.05 + .08 * sin(self.age * .02))
        for index in range(beam_count):
            angle = self.age * (.011 + index * .0007) + index * 2 * pi / beam_count
            end = (center[0] + cos(angle) * arena_radius,
                   center[1] + sin(angle) * arena_radius)
            width = 2 + int(4 * abs(sin(self.age * .035 + index)))
            pygame.draw.line(screen, ui.INK, center, end, width + 6)
            pygame.draw.line(screen, self.phaseAccent, center, end, width)
            pygame.draw.line(screen, ui.CREAM, center, end, max(1, width // 3))
        for ring_index in range(5):
            cycle = (progress * 4 + ring_index / 5) % 1
            radius = self.size * (.4 + cycle * 4.2)
            color = ui.CREAM if ring_index % 2 else self.phaseAccent
            pygame.draw.circle(screen, color, center, radius, max(1, int(5 * (1-cycle))))
        for index in range(24):
            angle = index * 2 * pi / 24 + self.age * .019
            distance = self.size * (.7 + (progress * 6 + index * .17) % 4)
            point = (center[0] + cos(angle) * distance,
                     center[1] + sin(angle) * distance)
            pygame.draw.rect(screen, ui.CREAM if index % 3 == 0 else self.phaseAccent,
                             (point[0] - 2, point[1] - 2, 4 + index % 3, 4 + index % 3))

    def _draw_rune(self, screen, center, radius, rune_phase=None):
        rune_phase = self.phase if rune_phase is None else rune_phase
        rune_name, strokes = self.PHASE_RUNES[rune_phase]
        transition = max(0.0, .75 - self.phaseElapsed) / .75
        angle = transition * pi * 1.5 + sin(self.age * .015) * .035
        pulse = 1 + sin(self.age * .04) * .06
        cos_angle, sin_angle = cos(angle), sin(angle)
        glow_width = max(6, int(radius * .16))
        line_width = max(3, int(radius * .075))
        for stroke in strokes:
            points = []
            for x, y in stroke:
                x, y = x * radius * pulse, y * radius * pulse
                points.append((center[0] + x * cos_angle - y * sin_angle,
                               center[1] + x * sin_angle + y * cos_angle))
            if len(points) > 1:
                pygame.draw.lines(screen, ui.INK, False, points, glow_width)
                if transition > 0:
                    ghost_offset = radius * transition * .12
                    ghost = [(x + cos(self.age * .05) * ghost_offset,
                              y + sin(self.age * .05) * ghost_offset) for x, y in points]
                    pygame.draw.lines(screen, self.phaseAccent, False, ghost,
                                      max(2, line_width // 2))
                pygame.draw.lines(screen, self.phaseAccent, False, points, line_width)
                pygame.draw.lines(screen, ui.CREAM, False, points, max(1, line_width // 3))
        return rune_name

    def drawEnemy(self, screen):
        self._draw_arena_mask(screen)
        self._draw_arena_boundary(screen)
        self._draw_arena_inscription(screen)
        self._draw_motion_trail(screen)
        for particle in self.visualParticles:
            x, y = bG.world_to_screen(particle["x"], particle["y"])
            size = max(2, int(particle["size"] * min(1, particle["life"] * 2)))
            pixel = pygame.Rect(int(x // 2 * 2), int(y // 2 * 2), size, size)
            pygame.draw.rect(screen, ui.INK, pixel.inflate(2, 2))
            pygame.draw.rect(screen, particle["color"], pixel)
        if self.transitionRemaining <= 0:
            for portal in self.projectilePortals:
                portal.draw(screen)
            for portal in self.survivalPortals:
                portal.draw(screen)
        if self.runeCannonCharge > 0 and self.runeCannonReceiver is not None:
            receiver = self.projectilePortals[self.runeCannonReceiver]
            target = bG.world_to_screen(receiver.worldX + receiver.size / 2,
                                        receiver.worldY + receiver.size / 2)
            for portal in self.projectilePortals:
                if portal is not receiver and portal.active:
                    start = bG.world_to_screen(portal.worldX + portal.size / 2,
                                               portal.worldY + portal.size / 2)
                    pygame.draw.line(screen, self.phaseAccent, start, target, 2)
        bob = sin(self.age * .055) * (3 if self.jumpWindup <= 0 else 8)
        rect = pygame.Rect(self.posX, self.posY + bob, self.size, self.size)
        self._draw_death_spectacle(screen, rect.center)
        if self.hitFlash > 0:
            color = ui.TEXT
        elif self.isStaggered:
            color = ui.CREAM if int(self.staggerRemaining * 8) % 2 == 0 else ui.MUTED
            rect = rect.inflate(-self.size * .12, self.size * .1)
            rect.move_ip(sin(self.age * .31) * 4, cos(self.age * .27) * 3)
        elif self.phase <= 3:
            color = self.phaseAccent
        elif self.phase == 4:
            blink = int(self.phaseElapsed * 8) % 2 == 0
            color = ui.RED if blink or self.phaseElapsed >= 1.6 else ui.CREAM
        elif self.phase in (5, 6, 7, 8):
            color = self.phaseAccent
        else:
            color = ui.RED

        if self.jumpWindup > 0:
            rect = rect.inflate(self.size * .16, -self.size * .18)
        elif self.jumpRecovery > 0:
            rect = rect.inflate(-self.size * .12, self.size * .16)

        self._draw_cube_aura(screen, rect.center, self.phaseAccent)
        vertices, faces = self._cube_geometry(rect.center, self.size * .43)
        entrance_spread = max(0.0, self.entranceRemaining / self.entranceDuration) * 2.8
        death_progress = (max(0.0, 1 - self.deathRemaining / self.deathBurstDuration)
                          if self.dying else 0.0)
        death_spread = death_progress * 3.4
        face_spread = max(entrance_spread, death_spread)
        projected_faces = []
        for face_index, face in enumerate(faces):
            points = [(vertices[index][0], vertices[index][1]) for index in face]
            if face_spread:
                face_center_x = sum(point[0] for point in points) / len(points)
                face_center_y = sum(point[1] for point in points) / len(points)
                offset_x = (face_center_x - rect.centerx) * face_spread
                offset_y = (face_center_y - rect.centery) * face_spread
                points = [(x + offset_x, y + offset_y) for x, y in points]
            projected_faces.append((face_index, points))
        for _, points in projected_faces:
            pygame.draw.polygon(screen, ui.SHADOW,
                                [(x + 7, y + 9) for x, y in points])
        for face_index, points in projected_faces:
            shimmer = int(8 * (1 + sin(self.age * .025 + face_index * 1.7)))
            face_color = ui.lighten(color, 5 + face_index * 6 + shimmer)
            pygame.draw.polygon(screen, face_color, points)
            pygame.draw.polygon(screen, ui.INK, points, max(3, int(self.size * .045)))
            highlight = (points[0], points[1])
            pygame.draw.line(screen, ui.lighten(self.phaseAccent, 35),
                             highlight[0], highlight[1], max(1, int(self.size * .012)))

        core_visibility = max(.15, 1 - entrance_spread * .22)
        core_radius = self.size * (.31 + sin(self.age * .025) * .015) * core_visibility
        core = pygame.Rect(0, 0, core_radius * 1.55, core_radius * 1.55)
        core.center = rect.center
        pygame.draw.rect(screen, ui.INK, core.inflate(8, 8))
        pygame.draw.rect(screen, ui.VOID, core)
        pygame.draw.rect(screen, self.phaseAccent, core, max(2, int(self.size * .035)))
        inner_pulse = core.inflate(-core.width * (.64 + .08 * sin(self.age * .045)),
                                  -core.height * (.64 + .08 * sin(self.age * .045)))
        pygame.draw.rect(screen, self.phaseAccent, inner_pulse, 1)
        death_rune = 9 if self.dying or self.phase == 9 and self.survivalActive else self.phase
        rune_name = self._draw_rune(screen, rect.center, core_radius, death_rune)
        ui.draw_text(screen, rune_name, max(8, self.size * .075), self.phaseAccent,
                     (rect.centerx, core.bottom + self.size * .045), "midtop")

        if self.phaseAnnouncementTimer > 0:
            self._draw_phase_announcement(screen, rect)
        if self.actTransitionTimer > 0:
            self._draw_act_transition(screen)
        if self.perfectBreakFlash > 0:
            self._draw_perfect_break(screen)

    def _draw_perfect_break(self, screen):
        alpha = int(150 * min(1, self.perfectBreakFlash * 2))
        veil = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        veil.fill((12, 14, 18, alpha))
        screen.blit(veil, (0, 0))
        scale = ui.display_scale(screen)
        center = (screen.get_width() / 2, screen.get_height() * .46)
        ui.draw_text(screen, "PERFECT BREAK", 36 * scale, ui.INK,
                     (center[0] + 5, center[1] + 6), "center")
        ui.draw_text(screen, "PERFECT BREAK", 36 * scale, ui.CREAM, center, "center")
        width = screen.get_width() * .34 * self.perfectBreakFlash
        pygame.draw.line(screen, self.phaseAccent,
                         (center[0] - width, center[1] + 34 * scale),
                         (center[0] + width, center[1] + 34 * scale), 4)

    def _draw_act_transition(self, screen):
        progress = 1 - self.actTransitionTimer / 2.2
        alpha = int(185 * min(1, progress * 5, (1 - progress) * 5))
        veil = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        pygame.draw.rect(veil, (ui.VOID.r, ui.VOID.g, ui.VOID.b, alpha),
                         (0, screen.get_height() * .3, screen.get_width(), screen.get_height() * .4))
        screen.blit(veil, (0, 0))
        scale = ui.display_scale(screen)
        jitter = 2 if int(self.age) % 4 == 0 else 0
        ui.draw_text(screen, self.actTitle, 31 * scale, ui.INK,
                     (screen.get_width() / 2 + 4, screen.get_height() * .43 + 5), "center")
        ui.draw_text(screen, self.actTitle, 31 * scale, self.phaseAccent,
                     (screen.get_width() / 2 + jitter, screen.get_height() * .43), "center")
        rune_name = self.PHASE_RUNES[self.phase][0]
        ui.draw_text(screen, f"{rune_name} AWAKENS", 13 * scale, ui.CREAM,
                     (screen.get_width() / 2, screen.get_height() * .51), "center")

    def _draw_phase_announcement(self, screen, boss_rect):
        scale = ui.display_scale(screen)
        rune_name = self.PHASE_RUNES[self.phase][0].upper()
        rune_surface = ui.font(13 * scale, italic=True, bold=True).render(
            rune_name, True, ui.CREAM)
        phase_surface = ui.font(13 * scale).render(f"  {self.phaseLabel}", True, ui.CREAM)
        label_surface = pygame.Surface(
            (rune_surface.get_width() + phase_surface.get_width(),
             max(rune_surface.get_height(), phase_surface.get_height())),
            pygame.SRCALPHA,
        )
        label_surface.blit(rune_surface, (0, 0))
        label_surface.blit(phase_surface, (rune_surface.get_width(), 0))
        flavor_font = ui.font(16 * scale, italic=True)
        max_text_width = min(screen.get_width() * .68, 680 * scale)
        words = self.phaseFlavor.split()
        lines = []
        current = ""
        for word in words:
            candidate = f"{current} {word}".strip()
            if current and flavor_font.size(candidate)[0] > max_text_width:
                lines.append(current)
                current = word
            else:
                current = candidate
        if current or not lines:
            lines.append(current)
        flavor_surfaces = [flavor_font.render(line, True, ui.TEXT) for line in lines]
        widest_flavor = max(surface.get_width() for surface in flavor_surfaces)
        line_gap = max(1, int(2 * scale))
        flavor_height = (sum(surface.get_height() for surface in flavor_surfaces)
                         + line_gap * (len(flavor_surfaces) - 1))
        width = max(label_surface.get_width(), widest_flavor) + 28 * scale
        height = label_surface.get_height() + flavor_height + 22 * scale
        bubble = pygame.Rect(0, 0, width, height)
        bubble.midbottom = (boss_rect.centerx, boss_rect.top - 18 * scale)
        bubble.clamp_ip(screen.get_rect().inflate(-12 * scale, -12 * scale))
        ui.draw_panel(screen, bubble, ui.PANEL_RAISED, self.phaseAccent, shadow=4)
        screen.blit(label_surface, label_surface.get_rect(midtop=(bubble.centerx, bubble.y + 7 * scale)))
        flavor_y = bubble.bottom - 7 * scale - flavor_height
        for flavor_surface in flavor_surfaces:
            screen.blit(flavor_surface,
                        flavor_surface.get_rect(midtop=(bubble.centerx, flavor_y)))
            flavor_y += flavor_surface.get_height() + line_gap
        pointer = ((boss_rect.centerx - 7 * scale, bubble.bottom),
                   (boss_rect.centerx + 7 * scale, bubble.bottom),
                   (boss_rect.centerx, bubble.bottom + 10 * scale))
        pygame.draw.polygon(screen, self.phaseAccent, pointer)


class PathChaseBoss(Enemy):
    """Configurable three-phase placeholder for rapidly prototyping path bosses."""

    bossName = "PATH BOSS"
    subtitle = "CONTENT PLACEHOLDER"
    phaseLabels = ("HUNT", "PRESS", "OVERWHELM")
    finalBoss = False
    pattern = "fan"
    ownerPrefix = "path"
    bodyColor = pygame.Color(91, 103, 53)
    finalBodyColor = pygame.Color(48, 82, 48)
    accentColor = pygame.Color(132, 119, 63)
    finalAccentColor = pygame.Color(74, 125, 67)
    movementSpeed = .21
    bodyScale = 1.9
    finalBodyScale = 2.35
    cooldownSeconds = 2.85
    finalCooldownSeconds = 2.35
    shotSpeed = .68
    finalShotSpeed = .82
    shotDamage = 275
    finalShotDamage = 360
    shotScale = .30
    finalShotScale = .34
    shotRangeTiles = 18
    arenaShape = "circle"
    arenaScale = 10.4
    movementModes = ("chase", "static", "path")

    def __init__(self, world_x, world_y, rng=None):
        final = self.finalBoss
        size = vH.tileSizeGlobal * (self.finalBodyScale if final else self.bodyScale)
        super().__init__(world_x, world_y,
                         self.movementSpeed * (1.16 if final else 1), size,
                         self.finalBodyColor if final else self.bodyColor,
                         360 if final else 270, 48000 if final else 29000,
                         520 if final else 280, 4.0 if final else 3.3,
                         f"{self.ownerPrefix}_boss", "hard")
        self.rng = rng or random
        self.phase = 1
        self.phaseLabel = self.phaseLabels[0]
        self.phaseFlavor = self.subtitle.title()
        self.phaseAccent = self.finalAccentColor if final else self.accentColor
        self.attackCooldown = vH.frameRate * 1.1
        self.attackCooldownMax = vH.frameRate * (
            self.finalCooldownSeconds if final else self.cooldownSeconds)
        self.entranceRemaining = .9
        self.stagger = 0.0
        self.maxStagger = 100.0
        self.minimumStaggerPerHit = 4.0
        self.staggerDuration = 2.5
        self.staggerRemaining = 0.0
        self.isStaggered = False
        self.perfectStagger = False
        self.staggerRecoveryRemaining = 0.0
        self.runeSilenceRemaining = 0.0
        self.survivalActive = False
        self.survivalRemaining = 0.0
        self.transitionCleanupRequested = False
        self.debugPhaseLocked = False
        self.awarenessRange = float("inf")
        self.disengageRange = float("inf")
        self.awarenessState = "alerted"
        self.arenaRadius = vH.tileSizeGlobal * self.arenaScale
        self.phaseElapsed = 0.0
        self.phaseTimeLimit = 28.0 if final else 24.0
        self.arenaSeed = [self.rng.uniform(-.15, .15) for _ in range(28)]

    def _center(self):
        return self.worldX + self.size / 2, self.worldY + self.size / 2

    def _arena_center(self):
        return (len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2,
                len(bG.currRoomRects) * vH.tileSizeGlobal / 2)

    def _arena_vertices(self):
        center_x, center_y = self._arena_center()
        radius = self.arenaRadius
        if self.arenaShape == "square":
            return [(center_x-radius, center_y-radius), (center_x+radius, center_y-radius),
                    (center_x+radius, center_y+radius), (center_x-radius, center_y+radius)]
        if self.arenaShape == "triangle":
            return [(center_x + cos(-pi/2+i*2*pi/3)*radius,
                     center_y + sin(-pi/2+i*2*pi/3)*radius) for i in range(3)]
        count = 28 if self.arenaShape == "jagged" else 64
        points = []
        for index in range(count):
            angle = index * 2 * pi / count
            if self.arenaShape == "jagged":
                noise = self.arenaSeed[index] + sin(self.age*.013+index*1.71)*.13
                local_radius = radius * (1 + noise)
            elif self.arenaShape == "atomic":
                local_radius = radius * (.88 + .1*sin(angle*3+self.age*.008)
                                         + .045*sin(angle*7-self.age*.011))
            else:
                local_radius = radius
            points.append((center_x+cos(angle)*local_radius,
                           center_y+sin(angle)*local_radius))
        return points

    @staticmethod
    def _point_in_polygon(point, vertices):
        x, y = point
        inside = False
        previous = vertices[-1]
        for current in vertices:
            if (current[1] > y) != (previous[1] > y):
                # The crossing test must preserve the edge's sign. Replacing a
                # negative denominator with epsilon classifies half of a clockwise
                # polygon as exterior and repeatedly drags the player to its center.
                crossing_x = ((previous[0]-current[0])*(y-current[1])
                              / (previous[1]-current[1]) + current[0])
                if x < crossing_x:
                    inside = not inside
            previous = current
        return inside

    @staticmethod
    def _closest_boundary_point(point, vertices):
        """Return the nearest point, segment, and squared distance on a polygon."""
        best_point, best_segment, best_distance = vertices[0], 0, float("inf")
        px, py = point
        for index, start in enumerate(vertices):
            end = vertices[(index + 1) % len(vertices)]
            dx, dy = end[0] - start[0], end[1] - start[1]
            length_sq = dx*dx + dy*dy
            amount = 0.0 if length_sq <= 1e-9 else max(
                0.0, min(1.0, ((px-start[0])*dx + (py-start[1])*dy) / length_sq))
            candidate = (start[0] + dx*amount, start[1] + dy*amount)
            distance = (px-candidate[0])**2 + (py-candidate[1])**2
            if distance < best_distance:
                best_point, best_segment, best_distance = candidate, index, distance
        return best_point, best_segment, best_distance

    def constrain_player_position(self, player_x, player_y, player_size):
        player_center = (player_x + player_size/2, player_y + player_size/2)
        vertices = self._arena_vertices()
        nearest, segment_index, distance_sq = self._closest_boundary_point(
            player_center, vertices)
        # A center-only test permits half the player body to leak through diagonal
        # edges. Keep a circular body margin inside every segment instead.
        margin = player_size * .72
        inside = self._point_in_polygon(player_center, vertices)
        if inside and distance_sq >= margin*margin:
            return player_x, player_y

        start = vertices[segment_index]
        end = vertices[(segment_index + 1) % len(vertices)]
        dx, dy = end[0]-start[0], end[1]-start[1]
        length = max(1e-9, hypot(dx, dy))
        signed_area = sum(
            vertices[index][0]*vertices[(index+1)%len(vertices)][1]
            - vertices[(index+1)%len(vertices)][0]*vertices[index][1]
            for index in range(len(vertices)))
        # These world polygons currently wind with positive signed area. The left
        # segment normal is therefore inward; retain support for reversed winding.
        if signed_area >= 0:
            normal = (-dy/length, dx/length)
        else:
            normal = (dy/length, -dx/length)
        corrected = (nearest[0] + normal[0]*margin,
                     nearest[1] + normal[1]*margin)

        # Mildly concave animated boundaries can place a local normal outside an
        # adjacent spike. Fall back to a short centerward inset only in that case.
        if not self._point_in_polygon(corrected, vertices):
            center = self._arena_center()
            toward_x, toward_y = center[0]-nearest[0], center[1]-nearest[1]
            toward_length = max(1e-9, hypot(toward_x, toward_y))
            corrected = (nearest[0] + toward_x/toward_length*margin,
                         nearest[1] + toward_y/toward_length*margin)
        return corrected[0]-player_size/2, corrected[1]-player_size/2

    def _draw_path_arena(self, screen):
        vertices = [bG.world_to_screen(*point) for point in self._arena_vertices()]
        if len(vertices) < 3:
            return
        pygame.draw.lines(screen, ui.SHADOW, True, vertices, 14)
        pygame.draw.lines(screen, ui.INK, True, vertices, 8)
        pygame.draw.lines(screen, self.phaseAccent, True, vertices, 3)
        progress = 1 - (self.phaseElapsed % self.phaseTimeLimit) / self.phaseTimeLimit
        lit = max(2, int(len(vertices) * progress))
        pygame.draw.lines(screen, ui.CREAM, False, vertices[:lit], 2)
        if self.arenaShape == "atomic":
            center = bG.world_to_screen(*self._arena_center())
            for index in range(3):
                extent = int(self.arenaRadius*2.2)
                orbit = pygame.Surface((extent, extent), pygame.SRCALPHA)
                rect = pygame.Rect(0, 0, self.arenaRadius*1.8, self.arenaRadius*.62)
                rect.center = (extent/2, extent/2)
                pygame.draw.ellipse(orbit, (*self.phaseAccent[:3], 70), rect, 3)
                orbit = pygame.transform.rotate(orbit, index*60 + self.age*.012)
                screen.blit(orbit, orbit.get_rect(center=center))
        marker_index = min(len(vertices)-1, int((1-progress)*(len(vertices)-1)))
        pygame.draw.circle(screen, ui.CREAM, vertices[marker_index], 5)

    def _update_phase(self):
        if self.debugPhaseLocked:
            return
        ratio = max(0, self.hp / self.maxHp)
        new_phase = 3 if ratio <= .34 else 2 if ratio <= .67 else 1
        if new_phase != self.phase:
            self.phase = new_phase
            self.phaseLabel = self.phaseLabels[new_phase - 1]
            self.attackCooldown = min(self.attackCooldown, vH.frameRate * .7)

    def debug_set_phase(self, phase):
        self.phase = max(1, min(3, int(phase)))
        self.phaseLabel = self.phaseLabels[self.phase - 1]
        self.debugPhaseLocked = True
        self.attackCooldown = 0

    def _fire_pattern(self, player_x, player_y, projectile_sink):
        center_x, center_y = self._center()
        direction = atan2(player_y - center_y, player_x - center_x)
        if self.pattern == "minefield":
            count = (2, 3, 5)[self.phase - 1] if self.finalBoss else (1, 2, 3)[self.phase - 1]
        elif self.pattern == "mirage":
            count = (3, 5, 7)[self.phase - 1] if self.finalBoss else (2, 3, 5)[self.phase - 1]
        else:
            count = (1, 2, 3)[self.phase - 1] if self.finalBoss else (1, 1, 2)[self.phase - 1]
        spread = {"rush": .22, "minefield": 2.5, "mirage": 1.15}.get(self.pattern, .34)
        for index in range(count):
            offset = 0 if count == 1 else -spread / 2 + spread * index / (count - 1)
            shot_size = self.size * (self.finalShotScale if self.finalBoss else self.shotScale)
            shot = EnemyProjectile(
                center_x - shot_size / 2, center_y - shot_size / 2,
                direction + offset,
                self.finalShotSpeed if self.finalBoss else self.shotSpeed,
                self.finalShotDamage if self.finalBoss else self.shotDamage,
                shot_size, travel_range=vH.tileSizeGlobal * self.shotRangeTiles,
                color=self.phaseAccent,
                shape="mine" if self.pattern == "minefield" else "diamond"
                if self.pattern in ("rush", "mirage") else "square",
                path="sine" if self.pattern == "mirage" else "linear",
                amplitude=vH.tileSizeGlobal * .65 if self.pattern == "mirage" else 0,
                owner=f"{self.ownerPrefix}_{'final' if self.finalBoss else 'mid'}",
                ignore_walls=self.pattern == "minefield",
            )
            if self.pattern == "minefield":
                shot.lifetime = 20.0
                shot.speedDecay = .08
            projectile_sink.append(shot)
        # Touch's final boss retains the initial slow radial cage placeholder.
        if self.pattern == "boulder" and self.finalBoss and self.phase == 3:
            for index in range(8):
                projectile_sink.append(EnemyProjectile(
                    center_x, center_y, index * pi / 4, .48, 300,
                    self.size * .23, travel_range=vH.tileSizeGlobal * 11,
                    color=self.phaseAccent, shape="diamond",
                    owner=f"{self.ownerPrefix}_ring",
                ))
        self._mark_attack(.42)

    # Kept as a compatibility alias for early Touch prototype tests/tools.
    def _fire_boulders(self, player_x, player_y, projectile_sink):
        self._fire_pattern(player_x, player_y, projectile_sink)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.entranceRemaining = max(0, self.entranceRemaining - self._seconds())
        self.phaseElapsed += self._seconds()
        self._update_phase()
        mode = self.movementModes[(self.phase-1) % len(self.movementModes)]
        original_speed = self.speed
        if mode == "static":
            self.speed = 0
        elif mode == "path":
            center = self._arena_center()
            player_world_x = center[0] + cos(self.phaseElapsed*.8)*self.arenaRadius*.55
            player_world_y = center[1] + sin(self.phaseElapsed*.8)*self.arenaRadius*.55
        super().updateEnemy(player_world_x, player_world_y, projectile_sink)
        self.speed = original_speed
        self.attackCooldown -= vH.get_timer_step()
        if self.entranceRemaining <= 0 and self.attackCooldown <= 0:
            self._fire_pattern(player_world_x, player_world_y, projectile_sink)
            rate = 1.0 - .11 * (self.phase - 1)
            self.attackCooldown = self.attackCooldownMax * rate * self.rng.uniform(.9, 1.12)

    def _seconds(self):
        return vH.get_timer_step() / max(1, vH.frameRate)

    def drawEnemy(self, screen):
        self._draw_path_arena(screen)
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        inset = rect.inflate(-self.size * .34, -self.size * .34)
        pygame.draw.ellipse(screen, ui.INK, inset)
        pygame.draw.ellipse(screen, self.phaseAccent, inset, max(3, int(self.size * .06)))
        for offset in (-.22, .22):
            x = rect.centerx + rect.width * offset
            pygame.draw.line(screen, ui.lighten(self.phaseAccent, 42),
                             (x, rect.y + rect.height * .22),
                             (x, rect.bottom - rect.height * .18), 3)


PLAGUE_SIGILS = (
    ("CORRUPTION", (((-.68, -.48), (0, -.72), (.68, -.48), (0, .72), (-.68, -.48)),
                    ((-.5, .05), (.5, .05)))),
    ("OVERRUN", (((-.7, .45), (-.35, -.35), (0, .15), (.35, -.35), (.7, .45)),
                 ((-.48, .45), (0, .68), (.48, .45)))),
    ("INFESTATION", (((0, -.72), (0, .72)), ((-.65, -.35), (.65, .35)),
                     (((-.65, .35), (.65, -.35))))),
    ("INVASION", (((-.72, .55), (-.35, -.55), (0, .05), (.35, -.55), (.72, .55)),
                  ((-.72, .1), (.72, .1)))),
    ("PESTILENCE", (((-.7, -.5), (.7, .5)), ((.7, -.5), (-.7, .5)),
                    ((0, -.76), (0, .76)))),
    ("AFFLICTION", (((-.65, 0), (-.3, -.5), (0, 0), (.3, -.5), (.65, 0),
                     (.3, .5), (0, 0), (-.3, .5), (-.65, 0)),)),
    ("IMPACT", (((0, -.78), (-.55, .1), (-.12, .1), (-.48, .72)),
                ((.18, -.35), (.65, .05), (.28, .05), (.55, .68)))),
    ("DEVOUR", (((-.72, -.42), (0, 0), (-.72, .42)),
                ((.72, -.42), (0, 0), (.72, .42)), ((0, -.72), (0, .72)))),
    ("DARKNESS", (((-.72, 0), (-.35, -.48), (.35, -.48), (.72, 0),
                   (.35, .48), (-.35, .48), (-.72, 0)), ((-.28, 0), (.28, 0)))),
    ("SEVERANCE", (((-.72, -.58), (.72, .58)), ((.72, -.58), (-.72, .58)),
                   ((-.72, 0), (-.18, 0)), ((.18, 0), (.72, 0)))),
)


class TouchPortal(ProjectilePortal):
    """A heavy square gate that marches along Touch's arena walls."""

    def __init__(self, *args, **kwargs):
        kwargs.setdefault("movement_path", "square")
        super().__init__(*args, **kwargs)
        self.size = vH.tileSizeGlobal * 1.12
        self.hitsToDisable = 8
        self._place()

    def draw(self, screen):
        if self.remFlag:
            return
        super().draw(screen)
        point = bG.world_to_screen(self.worldX, self.worldY)
        rect = pygame.Rect(point[0], point[1], self.size, self.size)
        pygame.draw.rect(screen, ui.INK, rect.inflate(10, 10), 5)
        pygame.draw.rect(screen, self.color, rect.inflate(4, 4), 3)
        for index in range(3):
            y = rect.y + rect.height * (.25 + index*.25)
            pygame.draw.line(screen, ui.CREAM, (rect.x+8, y), (rect.right-8, y), 2)


class PlagueTouchBoss(PathChaseBoss):
    arenaShape = "square"
    arenaScale = 9.4
    phaseFlavors = ()
    phaseColors = ()
    phaseSigils = ()
    movementModes = ()

    def __init__(self, world_x, world_y, rng=None):
        super().__init__(world_x, world_y, rng)
        self.phase = 1
        self.phaseLabel = self.phaseLabels[0]
        self.phaseFlavor = self.phaseFlavors[0]
        self.phaseAccent = self.phaseColors[0]
        self.projectilePortals = []
        self.portalCooldown = .4
        self.portalIndex = 0
        self.patternRotation = 0
        self.phaseAnnouncementTimer = 3.0
        self.pathAngle = 0.0

    def _update_phase(self):
        if self.debugPhaseLocked:
            return
        count = len(self.phaseLabels)
        ratio = max(0.0, min(1.0, self.hp/self.maxHp))
        phase = min(count, int((1-ratio)*count+1e-9)+1)
        if phase != self.phase:
            self._set_plague_phase(phase)

    def _set_plague_phase(self, phase):
        self.phase = max(1, min(len(self.phaseLabels), int(phase)))
        self.phaseLabel = self.phaseLabels[self.phase-1]
        self.phaseFlavor = self.phaseFlavors[self.phase-1]
        self.phaseAccent = self.phaseColors[self.phase-1]
        self.phaseElapsed = 0.0
        self.phaseAnnouncementTimer = 3.0
        self.transitionCleanupRequested = True
        self._clear_touch_portals()
        if self.phase in ((2, 4) if not self.finalBoss else (2, 4, 7, 9, 10)):
            self._deploy_touch_portals(2 if not self.finalBoss else 4)

    def debug_set_phase(self, phase):
        self._set_plague_phase(phase)
        self.debugPhaseLocked = True
        self.attackCooldown = 0

    def take_damage(self, amount, part_id="body"):
        if str(part_id).startswith("portal:"):
            index = int(str(part_id).split(":", 1)[1])
            if 0 <= index < len(self.projectilePortals):
                broken = self.projectilePortals[index].take_damage(amount)
                return HitResult(True, False, amount, blocked=not broken and False)
        previous = self.hp
        result = super().take_damage(amount, part_id)
        if not self.debugPhaseLocked and self.phase < len(self.phaseLabels):
            gate = self.maxHp*(len(self.phaseLabels)-self.phase)/len(self.phaseLabels)
            self.hp = max(self.hp, gate)
        return HitResult(result.applied, self.hp <= 0, previous-self.hp, result.blocked)

    def get_screen_hitboxes(self):
        hitboxes = super().get_screen_hitboxes()
        for index, portal in enumerate(self.projectilePortals):
            if portal.blocks_shots:
                x, y = bG.world_to_screen(portal.worldX, portal.worldY)
                hitboxes.append((f"portal:{index}", pygame.Rect(x, y, portal.size, portal.size)))
        return hitboxes

    def _clear_touch_portals(self):
        for portal in self.projectilePortals:
            portal.remFlag = True
        self.projectilePortals.clear()

    def _deploy_touch_portals(self, count):
        center = self._arena_center()
        for index in range(count):
            portal = TouchPortal(center, self.arenaRadius*.78, index*2*pi/count,
                                 angular_speed=.09 if index%2==0 else -.09,
                                 fire_interval=999, pellet_count=2, spread=.2,
                                 owner=f"{self.ownerPrefix}_plague_gate",
                                 color=self.phaseAccent)
            portal.reset_for_phase(PLAGUE_SIGILS[self.phaseSigils[self.phase-1]][1])
            self.projectilePortals.append(portal)

    def _projectile(self, sink, direction, speed, damage, suffix, size_scale=.25,
                    path="linear", target=None):
        center = self._center()
        size = self.size*size_scale
        shot = EnemyProjectile(center[0]-size/2, center[1]-size/2, direction,
                               speed, damage, size, travel_range=vH.tileSizeGlobal*35,
                               color=self.phaseAccent,
                               shape="bomb" if path=="bomb" else "diamond",
                               path=path, target=target,
                               owner=f"{self.ownerPrefix}_{suffix}", ignore_walls=True)
        if path == "bomb":
            shot.fuseDuration = 2.8
            shot.blastRadius = vH.tileSizeGlobal*1.7
            shot.burstCount = 8
        sink.append(shot)
        return shot

    def _radial(self, sink, count, speed, damage, suffix):
        for index in range(count):
            self._projectile(sink, index*2*pi/count+self.patternRotation*.11,
                             speed, damage, suffix)

    def _fan(self, sink, player_x, player_y, count, spread, speed, damage, suffix):
        center = self._center()
        aimed = atan2(player_y-center[1], player_x-center[0])
        for index in range(count):
            offset = 0 if count==1 else -spread/2+spread*index/(count-1)
            self._projectile(sink, aimed+offset, speed, damage, suffix)

    def _movement_step(self, player_x, player_y):
        mode = self.movementModes[self.phase-1]
        if mode == "static":
            return
        if mode == "path":
            self.pathAngle += .005*vH.get_frame_scale()
            center = self._arena_center()
            target = (center[0]+cos(self.pathAngle)*self.arenaRadius*.48,
                      center[1]+sin(self.pathAngle)*self.arenaRadius*.48)
        else:
            target = (player_x, player_y)
        center = self._center()
        distance = max(1, hypot(target[0]-center[0], target[1]-center[1]))
        step = self.speed*vH.get_frame_scale()
        self._try_axis_move((target[0]-center[0])/distance*step, "x")
        self._try_axis_move((target[1]-center[1])/distance*step, "y")
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _update_touch_portals(self, player_x, player_y, sink, dt):
        for portal in self.projectilePortals:
            portal.angle += portal.angularSpeed*dt
            portal._place()
            portal.update_bursts(sink, dt)
        self.portalCooldown -= dt
        if self.portalCooldown <= 0 and self.projectilePortals:
            portal = self.projectilePortals[self.portalIndex%len(self.projectilePortals)]
            portal.fire_toward(sink, (player_x, player_y), pellet_count=2,
                               spread=.12, speed=.42, damage=300 if self.finalBoss else 240,
                               color=self.phaseAccent, owner_suffix="heavy")
            self.portalIndex += 1
            self.portalCooldown = 1.15

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        dt = self._seconds()
        self.entranceRemaining = max(0, self.entranceRemaining-dt)
        self.phaseElapsed += dt
        self.phaseAnnouncementTimer = max(0, self.phaseAnnouncementTimer-dt)
        self._update_phase()
        self.age += vH.get_timer_step()
        self._movement_step(player_world_x, player_world_y)
        self._update_touch_portals(player_world_x, player_world_y, projectile_sink, dt)
        self.attackCooldown -= vH.get_timer_step()
        if self.entranceRemaining <= 0 and self.attackCooldown <= 0:
            self._fire_pattern(player_world_x, player_world_y, projectile_sink)
            self.attackCooldown = self.attackCooldownMax*max(.4, 1-.055*(self.phase-1))

    def _draw_plague_sigil(self, screen, center, radius):
        name, strokes = PLAGUE_SIGILS[self.phaseSigils[self.phase-1]]
        for stroke in strokes:
            points = [(center[0]+x*radius, center[1]+y*radius) for x, y in stroke]
            if len(points)>1:
                pygame.draw.lines(screen, ui.INK, False, points, max(5, int(radius*.14)))
                pygame.draw.lines(screen, self.phaseAccent, False, points, max(2, int(radius*.07)))
                pygame.draw.lines(screen, ui.CREAM, False, points, max(1, int(radius*.025)))
        return name

    def drawEnemy(self, screen):
        for portal in self.projectilePortals:
            portal.draw(screen)
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        # Layered stone plates make Touch feel massive rather than fluid.
        for inset in (0, self.size*.14, self.size*.28):
            plate = rect.inflate(-inset, -inset)
            pygame.draw.rect(screen, ui.INK, plate, max(3, int(self.size*.055)))
        sigil = self._draw_plague_sigil(screen, rect.center, self.size*.32)
        if self.phaseAnnouncementTimer > 0:
            ui.draw_text(screen, f"{sigil} // {self.phaseLabel}",
                         11*ui.display_scale(screen), self.phaseAccent,
                         (rect.centerx, rect.y-18), "midbottom")


class Bair(PlagueTouchBoss):
    bossName = "BAIR"
    subtitle = "THE FIRST LOCK"
    ownerPrefix = "bair_touch"
    phaseLabels = ("RIVER", "SWARM", "BLIGHT", "RUIN", "SILENCE")
    phaseFlavors = ("The current carries judgment.", "The small become countless.",
                    "The body and field fail together.", "Stone descends; hunger follows.",
                    "What remains cannot answer.")
    phaseColors = tuple(pygame.Color(*c) for c in
                        ((137,48,45),(76,135,80),(126,104,61),(151,123,94),(54,57,71)))
    phaseSigils = (0, 2, 4, 6, 8)
    movementModes = ("chase", "path", "static", "path", "static")
    movementSpeed = .10
    cooldownSeconds = 2.2

    def _fire_pattern(self, player_x, player_y, sink):
        if self.phase == 1:
            self._fan(sink, player_x, player_y, 3, .55, .45, 255, "river")
        elif self.phase == 2:
            self._radial(sink, 7, .34, 245, "swarm")
        elif self.phase == 3:
            self._projectile(sink, 0, 0, 275, "blight", .34, "bomb", (player_x, player_y))
        elif self.phase == 4:
            self._radial(sink, 8, .48, 270, "ruin")
        else:
            self._fan(sink, player_x, player_y, 5, 1.1, .38, 285, "silence")
        self.patternRotation += 1


class Sting(PlagueTouchBoss):
    bossName = "STING"
    subtitle = "THE THING THE PRISON KEPT"
    finalBoss = True
    ownerPrefix = "sting_touch"
    phaseLabels = ("BLOOD", "FROGS", "GNATS", "FLIES", "PESTILENCE",
                   "BOILS", "HAIL", "LOCUSTS", "DARKNESS", "FIRSTBORN")
    phaseFlavors = tuple(name.title() for name, _ in PLAGUE_SIGILS)
    phaseColors = tuple(pygame.Color(*c) for c in ((142,38,43),(69,139,75),(112,91,57),
        (91,76,53),(93,122,67),(166,71,61),(137,169,194),(117,139,58),(45,46,61),(189,163,119)))
    phaseSigils = tuple(range(10))
    movementModes = ("chase","path","static","path","static",
                     "chase","static","path","static","chase")
    movementSpeed = .075
    finalCooldownSeconds = 1.7
    finalBodyScale = 2.65

    def _fire_pattern(self, player_x, player_y, sink):
        if self.phase == 1:
            self._radial(sink, 10, .3, 330, "blood")
        elif self.phase == 2:
            for offset in (-1,0,1):
                self._projectile(sink, 0, 0, 340, "frogs", .31, "bomb",
                                 (player_x+offset*vH.tileSizeGlobal*1.4, player_y))
        elif self.phase == 3:
            self._radial(sink, 16, .52, 305, "gnats")
        elif self.phase == 4:
            self._fan(sink, player_x, player_y, 7, 1.6, .5, 325, "flies")
        elif self.phase == 5:
            self._radial(sink, 12, .36, 350, "pestilence")
        elif self.phase == 6:
            self._projectile(sink, 0, 0, 380, "boils", .4, "bomb", (player_x,player_y))
        elif self.phase == 7:
            for offset in range(-2,3):
                self._projectile(sink, pi/2+offset*.13, .58, 365, "hail", .28)
        elif self.phase == 8:
            self._fan(sink, player_x, player_y, 11, 2.1, .48, 340, "locusts")
        elif self.phase == 9:
            self._fan(sink, player_x, player_y, 5, .72, .28, 390, "darkness")
        else:
            self._radial(sink, 10, .48, 380, "severance")
            self._fan(sink, player_x, player_y, 3, .26, .72, 400, "firstborn")
        self.patternRotation += 1


class Ishe(PathChaseBoss):
    bossName = "ISHE"
    subtitle = "THE NEAR HORIZON"
    phaseLabels = ("GLIMPSE", "BLINK", "FLASH")
    pattern = "rush"
    ownerPrefix = "ishe_sight"
    bodyColor = pygame.Color(107, 190, 221)
    accentColor = pygame.Color(235, 142, 59)
    movementSpeed = .43
    bodyScale = 1.42
    cooldownSeconds = 1.18
    shotSpeed = 1.9
    shotScale = .20
    shotRangeTiles = 8
    arenaShape = "triangle"
    arenaScale = 11.2
    SIGHT_SYMBOLS = (
        ("GLIMPSE", (((-.7, 0), (0, -.45), (.7, 0), (0, .45), (-.7, 0)),
                    ((0, -.18), (0, .18)))),
        ("BLINK", (((-.72, -.3), (0, 0), (.72, -.3)),
                   ((-.72, .3), (0, 0), (.72, .3)))),
        ("FLASH", (((0, -.76), (-.18, -.14), (.3, -.14), (-.22, .76),
                   (0, .12), (-.34, .12), (0, -.76)),)),
    )

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        name, strokes = self.SIGHT_SYMBOLS[(self.phase-1) % len(self.SIGHT_SYMBOLS)]
        radius = self.size*.34
        for stroke in strokes:
            points = [(rect.centerx+x*radius, rect.centery+y*radius) for x, y in stroke]
            pygame.draw.lines(screen, ui.INK, False, points, max(4, int(radius*.13)))
            pygame.draw.lines(screen, self.phaseAccent, False, points, max(2, int(radius*.06)))
            pygame.draw.lines(screen, ui.CREAM, False, points, 1)
        if self.entranceRemaining > 0:
            ui.draw_text(screen, name, 9*ui.display_scale(screen), self.phaseAccent,
                         (rect.centerx, rect.y-12), "midbottom")


class Chronos(Ishe):
    bossName = "CHRONOS"
    subtitle = "THE LAST SECOND"
    finalBoss = True
    ownerPrefix = "chronos_sight"
    finalBodyColor = pygame.Color(81, 164, 204)
    finalAccentColor = pygame.Color(244, 166, 73)
    finalBodyScale = 1.7
    finalCooldownSeconds = .92
    finalShotSpeed = 2.15
    finalShotScale = .22


class SinChemesthesisBoss(PathChaseBoss):
    """Shared seven-sins pattern language for the Chemesthesis bosses."""

    phaseFlavors = ()
    phaseColors = ()
    ACT_METADATA = {}
    SIN_SIGILS = ()
    arenaShape = "jagged"
    arenaScale = 10.1
    movementModes = ("chase", "static", "path")

    def __init__(self, world_x, world_y, rng=None):
        super().__init__(world_x, world_y, rng)
        self.phaseFlavor = self.phaseFlavors[0]
        self.phaseAccent = self.phaseColors[0]
        self.patternRotation = 0
        self.actTransitionTimer = 0.0
        self.actTransitionDuration = 2.2
        self.actTitle = ""
        self.phaseProtectionTimer = 0.0
        self.previousSigilPhase = 1
        self.sigilTransitionTimer = 1.25
        self.sigilTransitionDuration = 1.25

    def _update_phase(self):
        if self.debugPhaseLocked:
            return
        count = len(self.phaseLabels)
        ratio = max(0.0, min(1.0, self.hp / self.maxHp))
        new_phase = min(count, int((1.0 - ratio) * count + 1e-9) + 1)
        if new_phase != self.phase:
            self._set_sin_phase(new_phase)

    def _set_sin_phase(self, phase):
        self.previousSigilPhase = self.phase
        self.phase = max(1, min(len(self.phaseLabels), int(phase)))
        self.phaseLabel = self.phaseLabels[self.phase - 1]
        self.phaseFlavor = self.phaseFlavors[self.phase - 1]
        self.phaseAccent = self.phaseColors[self.phase - 1]
        self.attackCooldown = min(self.attackCooldown, vH.frameRate * .45)
        self.transitionCleanupRequested = True
        self.sigilTransitionTimer = self.sigilTransitionDuration
        if self.phase in self.ACT_METADATA:
            self.actTitle = self.ACT_METADATA[self.phase]
            self.actTransitionTimer = self.actTransitionDuration
            self.phaseProtectionTimer = self.actTransitionDuration

    def debug_set_phase(self, phase):
        self._set_sin_phase(phase)
        self.debugPhaseLocked = True
        self.attackCooldown = 0

    def _shot(self, sink, direction, speed, damage, scale=.25, *, shape="diamond",
              path="linear", lifetime=None, speed_decay=0, orbit_radius=0,
              angular_speed=0, owner_suffix="sin", affliction=None,
              affliction_duration=0.0, affliction_strength=0.0, exposure=0.0,
              affliction_source=None):
        center_x, center_y = self._center()
        size = self.size * scale
        shot = EnemyProjectile(
            center_x - size / 2, center_y - size / 2, direction, speed, damage, size,
            travel_range=vH.tileSizeGlobal * self.shotRangeTiles,
            color=self.phaseAccent, shape=shape, path=path, lifetime=lifetime,
            speed_decay=speed_decay, orbit_center=self._center() if orbit_radius else None,
            orbit_radius=orbit_radius, orbit_angle=direction,
            angular_speed=angular_speed,
            owner=f"{self.ownerPrefix}_{owner_suffix}", ignore_walls=True,
        )
        shot.affliction = affliction
        shot.afflictionDuration = affliction_duration
        shot.afflictionStrength = affliction_strength
        shot.exposure = exposure
        shot.afflictionSource = affliction_source
        sink.append(shot)

    def _fan(self, sink, base, count, spread, speed, damage, suffix):
        for index in range(count):
            offset = 0 if count == 1 else -spread / 2 + spread * index / (count - 1)
            self._shot(sink, base + offset, speed, damage, owner_suffix=suffix)

    def _radial(self, sink, count, speed, damage, suffix, *, mine=False):
        offset = self.patternRotation * .19
        for index in range(count):
            self._shot(sink, 2 * pi * index / count + offset, speed, damage,
                       scale=.22 if not mine else .27,
                       shape="mine" if mine else "diamond",
                       path="mine" if mine else "linear",
                       lifetime=18 if mine else None,
                       speed_decay=.12 if mine else 0, owner_suffix=suffix,
                       exposure=.45 if mine else 0.0)
        self.patternRotation += 1

    def _bomb(self, sink, target_x, target_y, damage, suffix):
        center_x, center_y = self._center()
        size = self.size * .34
        bomb = EnemyProjectile(
            center_x, center_y, 0, 0, damage, size, color=self.phaseAccent,
            shape="bomb", path="bomb", lifetime=4.0,
            target=(target_x, target_y), owner=f"{self.ownerPrefix}_{suffix}",
            ignore_walls=True,
        )
        bomb.fuseDuration = 2.6
        bomb.blastRadius = vH.tileSizeGlobal * 1.8
        bomb.burstCount = 10
        sink.append(bomb)

    def _laser(self, sink, direction, damage, suffix, angular_speed=0):
        center_x, center_y = self._center()
        laser = EnemyProjectile(
            center_x, center_y, direction, 0, damage, self.size * .16,
            travel_range=vH.tileSizeGlobal * 30, color=self.phaseAccent,
            shape="laser", path="laser", lifetime=2.35,
            angular_speed=angular_speed, owner=f"{self.ownerPrefix}_{suffix}",
            ignore_walls=True,
        )
        laser.telegraphDuration = .85
        sink.append(laser)

    def _parallel_lanes(self, sink, direction, count, spacing, damage, suffix):
        """Draw an immediately legible wall of parallel, telegraphed lanes."""
        center_x, center_y = self._center()
        perpendicular = direction + pi / 2
        for index in range(count):
            offset = (index - (count - 1) / 2) * spacing
            start_x = center_x + cos(perpendicular) * offset
            start_y = center_y + sin(perpendicular) * offset
            laser = EnemyProjectile(
                start_x, start_y, direction, 0, damage, self.size * .14,
                travel_range=vH.tileSizeGlobal * 34, color=self.phaseAccent,
                shape="laser", path="laser", lifetime=2.3,
                owner=f"{self.ownerPrefix}_{suffix}", ignore_walls=True,
            )
            laser.telegraphDuration = .95
            sink.append(laser)

    def take_damage(self, amount, part_id="body"):
        if self.phaseProtectionTimer > 0 or self.actTransitionTimer > 0:
            return HitResult(False, False, 0, blocked=True)
        if str(part_id).startswith("crystal:"):
            return self._damage_crystal(part_id, amount)
        previous_hp = self.hp
        multiplier = 1.25 if self.isStaggered else 1.0
        result = super().take_damage(amount * multiplier, part_id)
        if not self.isStaggered:
            self.stagger = min(self.maxStagger,
                               self.stagger + max(self.minimumStaggerPerHit,
                                                  amount * .012))
            if self.stagger >= self.maxStagger:
                self.isStaggered = True
                self.staggerRemaining = self.staggerDuration
                self.transitionCleanupRequested = True
        if not self.debugPhaseLocked and self.phase < len(self.phaseLabels):
            threshold = self.maxHp * (len(self.phaseLabels) - self.phase) / len(self.phaseLabels)
            self.hp = max(self.hp, threshold)
        applied = max(0, previous_hp - self.hp)
        return HitResult(result.applied, self.hp <= 0, applied, result.blocked)

    def _damage_crystal(self, part_id, amount):
        return HitResult(False, False, 0, blocked=True)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        dt = self._seconds()
        self.entranceRemaining = max(0.0, self.entranceRemaining - dt)
        self.actTransitionTimer = max(0.0, self.actTransitionTimer - dt)
        self.phaseProtectionTimer = max(0.0, self.phaseProtectionTimer - dt)
        self.phaseElapsed += dt
        self.sigilTransitionTimer = max(0.0, self.sigilTransitionTimer - dt)
        self._update_terrain(player_world_x, player_world_y, dt)
        self._update_phase()
        if self.entranceRemaining > 0 or self.actTransitionTimer > 0:
            self.age += vH.get_timer_step()
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.isStaggered:
            self.staggerRemaining = max(0.0, self.staggerRemaining - dt)
            if self.staggerRemaining <= 0:
                self.isStaggered = False
                self.stagger = 0.0
            self.age += vH.get_timer_step()
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        mode = self.movementModes[(self.phase-1) % len(self.movementModes)]
        original_speed = self.speed
        if mode == "static":
            self.speed = 0
        elif mode == "path":
            center = self._arena_center()
            jitter = self.arenaSeed[(self.phase+self.patternRotation) % len(self.arenaSeed)]
            player_world_x = center[0] + cos(self.phaseElapsed*(.45+jitter))*self.arenaRadius*.5
            player_world_y = center[1] + sin(self.phaseElapsed*(.7-jitter))*self.arenaRadius*.42
        Enemy.updateEnemy(self, player_world_x, player_world_y, projectile_sink)
        self.speed = original_speed
        self.attackCooldown -= vH.get_timer_step()
        if self.attackCooldown <= 0:
            self._fire_pattern(player_world_x, player_world_y, projectile_sink)
            rate = max(.36, 1.0 - .08 * (self.phase - 1))
            self.attackCooldown = self.attackCooldownMax * rate * self.rng.uniform(.92, 1.08)

    def _update_terrain(self, player_x, player_y, dt):
        """Optional persistent terrain hook used by Rot."""

    def movement_obstacles(self):
        return ()

    def get_screen_hitboxes(self):
        return super().get_screen_hitboxes()

    def drawEnemy(self, screen):
        self._draw_field_diagram(screen)
        super().drawEnemy(screen)
        self._draw_chemical_body(screen)
        if self.actTransitionTimer <= 0:
            return
        progress = 1 - self.actTransitionTimer / self.actTransitionDuration
        alpha = int(185 * min(1, progress * 5, (1 - progress) * 5))
        veil = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        pygame.draw.rect(veil, (ui.VOID.r, ui.VOID.g, ui.VOID.b, alpha),
                         (0, screen.get_height() * .3,
                          screen.get_width(), screen.get_height() * .4))
        screen.blit(veil, (0, 0))
        scale = ui.display_scale(screen)
        ui.draw_text(screen, self.actTitle, 31 * scale, self.phaseAccent,
                     (screen.get_width() / 2, screen.get_height() * .43), "center")
        ui.draw_text(screen, f"{self.phaseLabel} SPREADS", 13 * scale, ui.CREAM,
                     (screen.get_width() / 2, screen.get_height() * .51), "center")
        ui.draw_text(screen, self.phaseFlavor, 11 * scale, ui.lighten(self.phaseAccent, 45),
                     (screen.get_width() / 2, screen.get_height() * .56), "center")
        sigil_name = self._draw_sigil(
            screen, (screen.get_width() / 2, screen.get_height() * .64),
            34 * scale, min(1.0, progress * 2.4), 0, phase=self.phase)
        ui.draw_text(screen, sigil_name, 9 * scale, self.phaseAccent,
                     (screen.get_width() / 2, screen.get_height() * .70), "center")

    def _draw_field_diagram(self, screen):
        """Render a phase-specific chemical diagram beneath the static boss."""
        center = bG.world_to_screen(*self._center())
        tile = vH.tileSizeGlobal
        extent = int(tile * (7.2 if self.finalBoss else 4.8))
        field = pygame.Surface((extent * 2, extent * 2), pygame.SRCALPHA)
        local = (extent, extent)
        pulse = .5 + .5 * sin(self.age * .025)
        faint = (*self.phaseAccent[:3], int(24 + pulse * 18))
        bright = (*ui.lighten(self.phaseAccent, 35)[:3], int(58 + pulse * 30))

        for ring in range(1, 4):
            radius = int(tile * (1.15 + ring * 1.18 + .08 * pulse))
            pygame.draw.circle(field, faint, local, radius, max(1, ring % 3 + 1))

        motif = (self.phase - 1) % 7
        if motif == 0:  # Pride: a rigid crown and cardinal axes.
            for angle in (0, pi / 2, pi, 3 * pi / 2):
                end = (local[0] + cos(angle) * extent * .82,
                       local[1] + sin(angle) * extent * .82)
                pygame.draw.line(field, bright, local, end, 2)
            crown = [(local[0] - tile * 1.5, local[1] - tile * .6),
                     (local[0] - tile * .75, local[1] - tile * 1.45),
                     (local[0], local[1] - tile * .72),
                     (local[0] + tile * .75, local[1] - tile * 1.45),
                     (local[0] + tile * 1.5, local[1] - tile * .6)]
            pygame.draw.lines(field, bright, False, crown, 3)
        elif motif == 1:  # Greed: nested containment cells.
            for radius in (tile * 1.7, tile * 2.7, tile * 3.7):
                rect = pygame.Rect(0, 0, radius * 2, radius * 2)
                rect.center = local
                pygame.draw.rect(field, bright, rect, 2)
        elif motif == 2:  # Lust: converging attraction spokes.
            for index in range(8):
                angle = index * pi / 4 + self.age * .002
                outer = (local[0] + cos(angle) * extent * .82,
                         local[1] + sin(angle) * extent * .82)
                inner = (local[0] + cos(angle + .22) * tile * 1.1,
                         local[1] + sin(angle + .22) * tile * 1.1)
                pygame.draw.line(field, faint, outer, inner, 3)
        elif motif == 3:  # Envy: two offset copies of one diagram.
            for side in (-1, 1):
                copy = pygame.Rect(0, 0, tile * 2.5, tile * 2.5)
                copy.center = (local[0] + side * tile * 1.35, local[1])
                pygame.draw.rect(field, bright if side > 0 else faint, copy, 3)
        elif motif == 4:  # Gluttony: an open maw of broken rings.
            for radius in (tile * 1.5, tile * 2.5, tile * 3.5):
                rect = pygame.Rect(0, 0, radius * 2, radius * 2)
                rect.center = local
                pygame.draw.arc(field, bright, rect, .35, 2 * pi - .35, 3)
        elif motif == 5:  # Wrath: an uncompromising cross-lane reticle.
            pygame.draw.line(field, bright, (local[0] - extent, local[1]),
                             (local[0] + extent, local[1]), 4)
            pygame.draw.line(field, bright, (local[0], local[1] - extent),
                             (local[0], local[1] + extent), 4)
        else:  # Sloth: a sagging, incomplete containment spiral.
            points = []
            for index in range(48):
                angle = index * .34
                radius = tile * (.35 + index * .075)
                points.append((local[0] + cos(angle) * radius,
                               local[1] + sin(angle) * radius * .72))
            pygame.draw.lines(field, faint, False, points, 4)

        # Slow rising spores make the field feel alive without hiding projectile lanes.
        for index in range(9 if self.finalBoss else 5):
            angle = index * 2.399 + self.age * .003
            radius = tile * (1.2 + (index % 4) * 1.1)
            point = (local[0] + cos(angle) * radius,
                     local[1] + sin(angle) * radius - (self.age * .04 + index * 13) % tile)
            pygame.draw.circle(field, bright, point, 2 + index % 3)
        screen.blit(field, (center[0] - extent, center[1] - extent))
        sigil_progress = 1 - self.sigilTransitionTimer / self.sigilTransitionDuration
        self._draw_sigil(screen, center, tile * (2.25 if self.finalBoss else 1.55),
                         max(0.0, sigil_progress), self.age * .0008,
                         alpha=55, phase=self.phase)
        self._draw_persistent_terrain(screen)

    def _draw_persistent_terrain(self, screen):
        """Optional world-terrain rendering hook used by Rot."""

    def _draw_chemical_body(self, screen):
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pulse = .5 + .5 * sin(self.age * .04)
        # Seven reaction vessels make Rot's phase progression readable on its body.
        pip_count = 7 if self.finalBoss else 4
        pip_radius = self.size * .62
        for index in range(pip_count):
            angle = -pi / 2 + index * 2 * pi / pip_count
            point = (rect.centerx + cos(angle) * pip_radius,
                     rect.centery + sin(angle) * pip_radius)
            active = index < self.phase
            color = self.phaseAccent if active else ui.BORDER
            pygame.draw.circle(screen, ui.INK, point, max(4, int(self.size * .065)))
            pygame.draw.circle(screen, color, point,
                               max(2, int(self.size * (.034 + .008 * pulse))))
        # A glassy central ampoule and rising reaction line distinguish this family.
        vessel = rect.inflate(-self.size * .58, -self.size * .24)
        pygame.draw.rect(screen, ui.INK, vessel.inflate(6, 6), border_radius=8)
        pygame.draw.rect(screen, ui.VOID, vessel, border_radius=6)
        fill_height = vessel.height * min(1.0, self.phase / max(1, len(self.phaseLabels)))
        fluid = pygame.Rect(vessel.x, vessel.bottom - fill_height, vessel.width, fill_height)
        pygame.draw.rect(screen, self.phaseAccent, fluid, border_radius=5)
        pygame.draw.line(screen, ui.CREAM, (vessel.x + vessel.width * .28, vessel.y + 5),
                         (vessel.x + vessel.width * .28, vessel.bottom - 5), 2)
        transition = self.sigilTransitionTimer / self.sigilTransitionDuration
        if transition > 0 and self.previousSigilPhase != self.phase:
            self._draw_sigil(screen, rect.center, self.size * .34, 1.0,
                             -transition * pi * .35, alpha=int(120 * transition),
                             phase=self.previousSigilPhase)
        self._draw_sigil(screen, rect.center, self.size * .36,
                         max(0.05, 1 - transition), transition * pi * .55,
                         alpha=255, phase=self.phase)
        if getattr(self, "consumedCrystalPulse", 0) > 0:
            radius = self.size * (1.0 + (1 - self.consumedCrystalPulse) * .65)
            pulse_rect = pygame.Rect(0, 0, radius * 2, radius * 2)
            pulse_rect.center = rect.center
            pygame.draw.ellipse(screen, ui.CREAM, pulse_rect,
                                max(2, int(5 * self.consumedCrystalPulse)))

    def _draw_sigil(self, screen, center, radius, progress=1.0, rotation=0.0,
                    alpha=255, phase=None):
        if not self.SIN_SIGILS:
            return ""
        phase = self.phase if phase is None else phase
        index = max(0, min(len(self.SIN_SIGILS) - 1, phase - 1))
        name, strokes = self.SIN_SIGILS[index]
        progress = max(0.0, min(1.0, progress))
        extent = max(8, int(radius * 2.8))
        aa = 2
        symbol = pygame.Surface((extent * aa, extent * aa), pygame.SRCALPHA)
        origin = (extent * aa / 2, extent * aa / 2)
        cos_angle, sin_angle = cos(rotation), sin(rotation)
        line_width = max(2, int(radius * .09)) * aa
        glow_width = line_width + max(3, int(radius * .09)) * aa
        stroke_budget = progress * len(strokes)
        for stroke_index, stroke in enumerate(strokes):
            local_progress = max(0.0, min(1.0, stroke_budget - stroke_index))
            if local_progress <= 0 or len(stroke) < 2:
                continue
            transformed = []
            for x, y in stroke:
                x, y = x * radius * aa, y * radius * aa
                transformed.append((origin[0] + x * cos_angle - y * sin_angle,
                                    origin[1] + x * sin_angle + y * cos_angle))
            visible = []
            segment_budget = local_progress * (len(transformed) - 1)
            visible.append(transformed[0])
            for segment in range(len(transformed) - 1):
                if segment_budget <= segment:
                    break
                fraction = min(1.0, segment_budget - segment)
                start, end = transformed[segment], transformed[segment + 1]
                visible.append((start[0] + (end[0] - start[0]) * fraction,
                                start[1] + (end[1] - start[1]) * fraction))
                if fraction < 1:
                    break
            if len(visible) > 1:
                pygame.draw.lines(symbol, (*ui.INK[:3], alpha), False, visible, glow_width)
                pygame.draw.lines(symbol, (*self.phaseAccent[:3], alpha), False,
                                  visible, line_width)
                pygame.draw.lines(symbol, (*ui.CREAM[:3], alpha), False, visible,
                                  max(1, line_width // 3))
                pygame.draw.circle(symbol, (*ui.CREAM[:3], alpha), visible[-1],
                                   max(1, line_width // 2))
        symbol = pygame.transform.smoothscale(symbol, (extent, extent))
        screen.blit(symbol, (center[0] - extent / 2, center[1] - extent / 2))
        return name


class Kage(SinChemesthesisBoss):
    bossName = "KAGE"
    subtitle = "THE FIRST REACTION"
    phaseLabels = ("FEAST", "PROVOCATION", "STAGNANT MIRROR", "LURE")
    phaseFlavors = (
        "Take all that you can carry.",
        "Strike. I insist.",
        "Stillness learns your shape.",
        "Come closer. There is plenty.",
    )
    phaseColors = (
        pygame.Color(214, 154, 52), pygame.Color(205, 62, 38),
        pygame.Color(101, 133, 64), pygame.Color(202, 82, 99),
    )
    SIN_SIGILS = (
        ("HUNGER / WANT", (
            ((-.72, -.25), (-.28, -.72), (.28, -.72), (.72, -.25),
             (.28, .18), (-.28, .18), (-.72, -.25)),
            ((0, -.72), (0, .72)), ((-.42, .42), (0, .72), (.42, .42)),
        )),
        ("CROWN / RETORT", (
            ((-.7, .38), (-.52, -.5), (0, -.12), (.52, -.5), (.7, .38)),
            ((-.52, .08), (.52, .08)), ((-.34, .68), (0, .2), (.34, .68)),
        )),
        ("MIRROR / STILLNESS", (
            ((-.68, -.35), (-.2, -.68), (-.2, .5), (-.68, .18), (-.68, -.35)),
            ((.68, -.35), (.2, -.68), (.2, .5), (.68, .18), (.68, -.35)),
            ((-.2, .5), (0, .72), (.2, .5)),
        )),
        ("LURE / AVARICE", (
            ((-.68, -.38), (0, .08), (.68, -.38)),
            ((-.68, .38), (0, -.08), (.68, .38)),
            ((0, -.72), (0, .72)), ((-.22, .48), (0, .72), (.22, .48)),
        )),
    )
    pattern = "minefield"
    ownerPrefix = "kage_chemesthesis"
    bodyColor = pygame.Color(169, 65, 36)
    accentColor = pygame.Color(106, 132, 52)
    movementSpeed = .055
    bodyScale = 2.05
    cooldownSeconds = 1.8
    shotSpeed = .30
    shotScale = .26
    shotRangeTiles = 34

    def _fire_pattern(self, player_x, player_y, projectile_sink):
        center_x, center_y = self._center()
        aimed = atan2(player_y - center_y, player_x - center_x)
        if self.phase == 1:  # Gluttony / Greed: a feast of lingering morsels.
            self._radial(projectile_sink, 5, .34, 245, "feast", mine=True)
        elif self.phase == 2:  # Wrath / Pride: invitation followed by retaliation.
            self._fan(projectile_sink, aimed, 5, 1.05, .82, 270, "provocation")
            self._laser(projectile_sink, aimed + pi, 240, "retort")
        elif self.phase == 3:  # Sloth / Envy: slow mirrors occupy the field.
            for side in (-1, 1):
                self._shot(projectile_sink, aimed + side * .72, .42, 250,
                           path="sine", owner_suffix="stagnant_mirror")
            self._radial(projectile_sink, 4, .18, 230, "stagnation", mine=True)
        else:  # Lust / Avarice: converging lanes make tempting gaps.
            self._fan(projectile_sink, aimed, 7, 2.2, .56, 265, "lure")
            self._bomb(projectile_sink, player_x, player_y, 280, "lure_reward")
        self._mark_attack(.5)


class Rot(Kage):
    bossName = "ROT"
    subtitle = "THE FIELD THAT REMAINS"
    finalBoss = True
    ownerPrefix = "rot_chemesthesis"
    finalBodyColor = pygame.Color(122, 47, 36)
    finalAccentColor = pygame.Color(210, 85, 36)
    finalBodyScale = 2.5
    finalCooldownSeconds = 1.35
    finalShotSpeed = .38
    finalShotScale = .29
    movementSpeed = .07
    movementModes = ("static", "path", "chase", "static", "path", "chase", "static")
    ACT_METADATA = {
        3: "ACT II // TEMPTATION",
        5: "ACT III // SATURATION",
    }
    phaseLabels = ("CROWN", "HOARD", "PULL", "BORROWED SHAPE",
                   "CONSUMPTION", "RETORT", "THE ROT")
    phaseFlavors = (
        "There is room for only one above.",
        "Nothing is enough.",
        "Every nerve bends toward desire.",
        "Your strength looks better on me.",
        "The field must feed.",
        "Every wound demands an answer.",
        "Rest. Become part of the garden.",
    )
    phaseColors = (
        pygame.Color(232, 196, 84), pygame.Color(211, 145, 45),
        pygame.Color(216, 80, 112), pygame.Color(111, 155, 88),
        pygame.Color(153, 77, 42), pygame.Color(224, 55, 35),
        pygame.Color(91, 117, 52),
    )
    SIN_SIGILS = (
        ("PRIDE", (
            ((-.72, .52), (-.58, -.22), (-.25, .08), (0, -.72),
             (.25, .08), (.58, -.22), (.72, .52)),
            ((-.58, .28), (.58, .28)), ((0, -.72), (0, .68)),
        )),
        ("GREED", (
            ((0, -.74), (.62, -.18), (.42, .58), (0, .74),
             (-.42, .58), (-.62, -.18), (0, -.74)),
            ((-.42, -.06), (0, .28), (.42, -.06)),
            ((0, -.42), (0, .74)),
        )),
        ("LUST", (
            ((0, .72), (-.68, -.04), (-.42, -.6), (0, -.22),
             (.42, -.6), (.68, -.04), (0, .72)),
            ((-.72, 0), (.72, 0)),
        )),
        ("ENVY", (
            ((-.74, 0), (-.36, -.42), (0, 0), (-.36, .42), (-.74, 0)),
            ((.74, 0), (.36, -.42), (0, 0), (.36, .42), (.74, 0)),
            ((-.36, 0), (.36, 0)),
        )),
        ("GLUTTONY", (
            ((-.7, -.34), (-.34, -.68), (.34, -.68), (.7, -.34)),
            ((-.7, .34), (-.34, .68), (.34, .68), (.7, .34)),
            ((-.7, -.34), (-.28, 0), (-.7, .34)),
            ((.7, -.34), (.28, 0), (.7, .34)),
        )),
        ("WRATH", (
            ((-.58, -.7), (.1, -.08), (-.18, .08), (.58, .7)),
            ((.58, -.7), (-.1, -.08), (.18, .08), (-.58, .7)),
            ((-.72, 0), (.72, 0)),
        )),
        ("SLOTH", (
            ((-.62, -.56), (.48, -.56), (.48, .34), (-.28, .34),
             (-.28, -.1), (.14, -.1), (.14, .06)),
            ((0, -.76), (0, -.56)),
            ((-.48, .62), (0, .76), (.48, .62)),
        )),
    )

    def __init__(self, world_x, world_y, rng=None):
        super().__init__(world_x, world_y, rng)
        self.actTitle = "ACT I // APPETITE"
        self.actTransitionTimer = self.actTransitionDuration
        self.phaseProtectionTimer = self.actTransitionDuration
        self.crystalWalls = []
        self.cleansingVents = []
        self.ventsUsed = 0
        self.peakExposure = 0.0
        self.compressionCooldown = 5.0
        self.consumedCrystalPulse = 0.0
        center_x, center_y = self._center()
        for index in range(4):
            angle = index * pi / 2 + pi / 4
            self.cleansingVents.append({
                "x": center_x + cos(angle) * vH.tileSizeGlobal * 5.7,
                "y": center_y + sin(angle) * vH.tileSizeGlobal * 5.7,
                "angle": angle, "cooldown": 0.0, "flash": 0.0,
            })

    def _set_sin_phase(self, phase):
        super()._set_sin_phase(phase)
        if hasattr(self, "crystalWalls"):
            self.crystalWalls.clear()
        if phase == 7 and hasattr(self, "compressionCooldown"):
            self.compressionCooldown = 5.0

    def _camera_cardinal_angle(self, quarter_turn=0):
        screen_x, screen_y = bG.screen_vector_to_world(1, 0)
        base = atan2(screen_y, screen_x)
        return base + quarter_turn * pi / 2

    def _grow_crystal_wall(self, angle, duration=8.0, kind=None, distance_tiles=3.9,
                           compression=False):
        center_x, center_y = self._center()
        distance = vH.tileSizeGlobal * distance_tiles
        wall_center_x = center_x + cos(angle) * distance
        wall_center_y = center_y + sin(angle) * distance
        horizontal = abs(cos(angle)) < abs(sin(angle))
        width = vH.tileSizeGlobal * (3.5 if horizontal else .72)
        height = vH.tileSizeGlobal * (.72 if horizontal else 3.5)
        rect = pygame.Rect(0, 0, width, height)
        rect.center = (wall_center_x, wall_center_y)
        kind = kind or ("brittle" if self.patternRotation % 2 == 0 else "reinforced")
        self.crystalWalls.append({"rect": rect, "remaining": duration,
                                  "duration": duration, "angle": angle,
                                  "kind": kind, "hp": 420 if kind == "brittle" else None,
                                  "warning": 2.5 if compression else 0.0,
                                  "compression": compression})
        self.crystalWalls[:] = self.crystalWalls[-6:]

    def _update_terrain(self, player_x, player_y, dt):
        import characterStats as cS
        self.peakExposure = max(self.peakExposure, cS.bossAfflictions["exposure"])
        self.consumedCrystalPulse = max(0.0, self.consumedCrystalPulse - dt)
        for wall in self.crystalWalls:
            wall["remaining"] = max(0.0, wall["remaining"] - dt)
            wall["warning"] = max(0.0, wall["warning"] - dt)
            if wall["compression"] and wall["warning"] <= 0:
                center_x, center_y = self._center()
                delta_x = center_x - wall["rect"].centerx
                delta_y = center_y - wall["rect"].centery
                distance = max(1.0, hypot(delta_x, delta_y))
                if distance > vH.tileSizeGlobal * 2.25:
                    step = vH.tileSizeGlobal * .34 * dt
                    wall["rect"].centerx += delta_x / distance * step
                    wall["rect"].centery += delta_y / distance * step
        self.crystalWalls[:] = [wall for wall in self.crystalWalls
                                if wall["remaining"] > 0]
        for vent in self.cleansingVents:
            vent["cooldown"] = max(0.0, vent["cooldown"] - dt)
            vent["flash"] = max(0.0, vent["flash"] - dt)
            if (vent["cooldown"] <= 0 and cS.bossAfflictions["exposure"] > .25
                    and hypot(player_x - vent["x"], player_y - vent["y"])
                    <= vH.tileSizeGlobal * 1.05):
                cS.reset_boss_afflictions()
                vent["cooldown"] = 12.0
                vent["flash"] = 1.0
                self.ventsUsed += 1
                # Cleansing opens the player's immediate position but seals the
                # corresponding inner route, turning relief into a terrain choice.
                self._grow_crystal_wall(vent["angle"], 7.0)
        if self.phase == 7 and self.actTransitionTimer <= 0:
            self.compressionCooldown -= dt
            if self.compressionCooldown <= 0:
                angle = self._camera_cardinal_angle(self.patternRotation % 2)
                self._grow_crystal_wall(angle, 11.0, "reinforced", 6.2, True)
                self._grow_crystal_wall(angle + pi, 11.0, "reinforced", 6.2, True)
                self.compressionCooldown = 12.0

    def movement_obstacles(self):
        return tuple(wall["rect"] for wall in self.crystalWalls
                     if wall["warning"] <= 0)

    def get_screen_hitboxes(self):
        hitboxes = super().get_screen_hitboxes()
        for index, wall in enumerate(self.crystalWalls):
            if wall["kind"] != "brittle" or wall["warning"] > 0:
                continue
            rect = wall["rect"]
            corners = tuple(bG.world_to_screen(x, y) for x, y in (
                (rect.left, rect.top), (rect.right, rect.top),
                (rect.right, rect.bottom), (rect.left, rect.bottom)))
            left = min(point[0] for point in corners)
            top = min(point[1] for point in corners)
            right = max(point[0] for point in corners)
            bottom = max(point[1] for point in corners)
            hitboxes.append((f"crystal:{index}", pygame.Rect(
                left, top, max(1, right - left), max(1, bottom - top))))
        return hitboxes

    def _damage_crystal(self, part_id, amount):
        index = int(str(part_id).split(":", 1)[1])
        if not 0 <= index < len(self.crystalWalls):
            return HitResult(False, False, 0, blocked=True)
        wall = self.crystalWalls[index]
        if wall["kind"] != "brittle":
            return HitResult(False, False, 0, blocked=True)
        applied = min(wall["hp"], round(amount))
        wall["hp"] -= applied
        if wall["hp"] <= 0:
            self.crystalWalls.pop(index)
        return HitResult(True, False, applied)

    def _draw_persistent_terrain(self, screen):
        for vent in self.cleansingVents:
            point = bG.world_to_screen(vent["x"], vent["y"])
            ready = vent["cooldown"] <= 0
            color = ui.CREAM if vent["flash"] > 0 else (
                pygame.Color(96, 185, 151) if ready else ui.BORDER)
            radius = int(vH.tileSizeGlobal * (.42 if ready else .32))
            pygame.draw.circle(screen, ui.INK, point, radius + 6)
            pygame.draw.circle(screen, color, point, radius, 4)
            pygame.draw.line(screen, color, (point[0] - radius, point[1]),
                             (point[0] + radius, point[1]), 2)
            pygame.draw.line(screen, color, (point[0], point[1] - radius),
                             (point[0], point[1] + radius), 2)
        for wall in self.crystalWalls:
            rect = wall["rect"]
            top_left = bG.world_to_screen(rect.left, rect.top)
            bottom_right = bG.world_to_screen(rect.right, rect.bottom)
            screen_rect = pygame.Rect(min(top_left[0], bottom_right[0]),
                                      min(top_left[1], bottom_right[1]),
                                      max(8, abs(bottom_right[0] - top_left[0])),
                                      max(8, abs(bottom_right[1] - top_left[1])))
            fade = min(1.0, wall["remaining"] * 2)
            warning = wall["warning"] > 0
            color = (ui.CREAM if warning else ui.lighten(
                self.phaseAccent, 48 if wall["kind"] == "brittle" else int(20 * fade)))
            pygame.draw.rect(screen, ui.INK, screen_rect.inflate(8, 8))
            pygame.draw.rect(screen, color, screen_rect, 3 if warning else 0)
            for offset in range(0, max(screen_rect.width, screen_rect.height),
                                max(8, int(vH.tileSizeGlobal * .4))):
                if screen_rect.width >= screen_rect.height:
                    pygame.draw.line(screen, ui.CREAM,
                                     (screen_rect.x + offset, screen_rect.bottom),
                                     (screen_rect.x + offset + 9, screen_rect.y), 2)
                else:
                    pygame.draw.line(screen, ui.CREAM,
                                     (screen_rect.x, screen_rect.y + offset),
                                     (screen_rect.right, screen_rect.y + offset + 9), 2)
        if self.actTransitionTimer > 0:
            self._draw_route_preview(screen)

    def _draw_route_preview(self, screen):
        ready = [vent for vent in self.cleansingVents if vent["cooldown"] <= 0]
        if len(ready) < 2:
            return
        color = pygame.Color(96, 185, 151)
        start = bG.world_to_screen(ready[0]["x"], ready[0]["y"])
        end = bG.world_to_screen(ready[2 if len(ready) > 2 else 1]["x"],
                                 ready[2 if len(ready) > 2 else 1]["y"])
        pygame.draw.line(screen, ui.INK, start, end, 9)
        pygame.draw.line(screen, color, start, end, 3)
        midpoint = ((start[0] + end[0]) / 2, (start[1] + end[1]) / 2)
        ui.draw_text(screen, "PREVIEW // CLEAN ROUTE", 9 * ui.display_scale(screen),
                     color, midpoint, "center")

    def challenge_results(self):
        return {
            "clean_traversal": self.peakExposure <= 3.0,
            "vent_discipline": self.ventsUsed <= 1,
            "uncontaminated": self.peakExposure <= .25,
        }

    def _fire_pattern(self, player_x, player_y, projectile_sink):
        center_x, center_y = self._center()
        aimed = atan2(player_y - center_y, player_x - center_x)
        if self.phase == 1:  # Pride
            lane_angle = self._camera_cardinal_angle(self.patternRotation % 2)
            self._parallel_lanes(projectile_sink, lane_angle, 3,
                                 vH.tileSizeGlobal * 2.4, 330, "pride_crown")
            self.patternRotation += 1
        elif self.phase == 2:  # Greed
            self._radial(projectile_sink, 9, .25, 315, "greed_hoard", mine=True)
            for index in range(3):
                self._shot(projectile_sink, index * 2 * pi / 3, 0, 290, scale=.20,
                           shape="mine", path="orbit", lifetime=12,
                           orbit_radius=vH.tileSizeGlobal * (3.2 + index),
                           angular_speed=.26 + index * .05, owner_suffix="greed_coin")
            self._grow_crystal_wall(self._camera_cardinal_angle(self.patternRotation % 4))
        elif self.phase == 3:  # Lust
            for index in range(9):
                offset = -1.3 + 2.6 * index / 8
                self._shot(projectile_sink, aimed + offset, .62, 325,
                           owner_suffix="lust_pull", affliction="pull",
                           affliction_duration=1.4, affliction_strength=.32,
                           exposure=.8, affliction_source=self._center())
            self._bomb(projectile_sink, player_x, player_y, 340, "lust_lure")
        elif self.phase == 4:  # Envy
            import characterStats as cS
            build = cS.player_build_snapshot()
            identity = build["dominant_offense"]
            count = max(3, min(9, round(build["stats"]["projectile_count"])))
            if identity == "critical":
                self._laser(projectile_sink, aimed, 375, "envy_critical")
            elif identity == "tempo":
                self._fan(projectile_sink, aimed, count, .55, 1.38, 300, "envy_tempo")
            elif identity == "precision":
                self._fan(projectile_sink, aimed, 3, .22, 1.65, 350, "envy_precision")
            else:
                self._fan(projectile_sink, aimed, count, .9, 1.05, 335,
                          f"envy_{identity}")
            self._fan(projectile_sink, aimed + pi, count, .9, .62, 310,
                      "envy_reflection")
        elif self.phase == 5:  # Gluttony
            if self.crystalWalls:
                self.crystalWalls.pop(0)
                self.stagger = max(0.0, self.stagger - self.maxStagger * .25)
                self.consumedCrystalPulse = 1.0
            self._bomb(projectile_sink, player_x, player_y, 390, "gluttony_feast")
            self._radial(projectile_sink, 7, .32, 325, "gluttony_morsel", mine=True)
        elif self.phase == 6:  # Wrath
            self._fan(projectile_sink, aimed, 7, .65, 1.2, 370, "wrath_retort")
            lane_angle = self._camera_cardinal_angle(self.patternRotation % 2)
            self._parallel_lanes(projectile_sink, lane_angle, 2,
                                 vH.tileSizeGlobal * 3.2, 360, "wrath_answer")
            self._parallel_lanes(projectile_sink, lane_angle + pi / 2, 2,
                                 vH.tileSizeGlobal * 3.2, 360, "wrath_cross")
            self.patternRotation += 1
        else:  # Sloth: persistent rot plus callbacks from the other sins.
            self._radial(projectile_sink, 12, .16, 335, "sloth_rot", mine=True)
            for projectile in projectile_sink[-12:]:
                projectile.affliction = "slow"
                projectile.afflictionDuration = 2.1
                projectile.afflictionStrength = .16
                projectile.exposure = 1.15
            callback = self.patternRotation % 3
            if callback == 0:
                self._laser(projectile_sink, aimed, 345, "rot_crown", .08)
            elif callback == 1:
                self._bomb(projectile_sink, player_x, player_y, 355, "rot_feast")
            else:
                self._fan(projectile_sink, aimed, 7, 1.5, .72, 345, "rot_desire")
        self._mark_attack(.58)


COMMANDMENT_SIGILS = (
    ("AUTHORITY", (((0, -.78), (0, .72)), ((-.62, -.26), (0, -.78), (.62, -.26)),
                   ((-.48, .5), (0, .72), (.48, .5)))),
    ("IMAGE", (((-.7, 0), (0, -.62), (.7, 0), (0, .62), (-.7, 0)),
               ((-.34, 0), (0, -.28), (.34, 0), (0, .28), (-.34, 0)))),
    ("REVERENCE", (((-.55, .66), (-.55, -.5), (0, -.76), (.55, -.5), (.55, .66)),
                   ((-.28, -.1), (0, -.38), (.28, -.1)), ((0, -.38), (0, .48)))),
    ("REST", (((-.7, -.45), (.7, -.45)), ((-.7, .45), (.7, .45)),
              ((-.52, -.45), (-.52, .45)), ((.52, -.45), (.52, .45)))),
    ("LINEAGE", (((0, .75), (0, -.18), (-.58, -.7)), ((0, -.18), (.58, -.7)),
                 ((-.58, -.7), (-.22, -.7)), ((.58, -.7), (.22, -.7)))),
    ("MERCY", (((-.65, -.58), (0, .68), (.65, -.58)),
               ((-.42, -.22), (0, .15), (.42, -.22)), ((0, -.72), (0, .15)))),
    ("FIDELITY", (((-.5, -.68), (-.5, .68), (.5, .68), (.5, -.68)),
                  ((-.5, 0), (.5, 0)))),
    ("OWNERSHIP", (((-.7, -.5), (0, -.12), (.7, -.5)),
                   ((-.7, .5), (0, .12), (.7, .5)), ((0, -.72), (0, .72)))),
    ("TRUTH", (((-.72, 0), (0, -.5), (.72, 0), (0, .5), (-.72, 0)),
               ((0, -.68), (0, .68)), ((-.18, 0), (.18, 0)))),
    ("CONTENTMENT", (((-.68, -.28), (-.3, .58), (0, .72), (.3, .58), (.68, -.28)),
                     ((-.68, -.28), (-.3, -.62)), ((.68, -.28), (.3, -.62)),
                     ((-.3, .1), (.3, .1)))),
)


class PhantasiaBoss(PathChaseBoss):
    """Commandment-driven dream court shared by Hypno and Malady."""

    phaseFlavors = ()
    phaseColors = ()
    phaseSigils = ()
    ACT_METADATA = {}
    arenaShape = "atomic"
    arenaScale = 10.8
    movementModes = ("chase", "path", "static")

    def __init__(self, world_x, world_y, rng=None):
        super().__init__(world_x, world_y, rng)
        self.phase = 1
        self.phaseLabel = self.phaseLabels[0]
        self.phaseFlavor = self.phaseFlavors[0]
        self.phaseAccent = self.phaseColors[0]
        self.phaseElapsed = 0.0
        self.patternRotation = 0
        self.actTransitionDuration = 2.4
        self.actTransitionTimer = 0.0
        self.actTitle = ""
        self.phaseProtectionTimer = 0.0
        self.previousSigilPhase = 1
        self.sigilTransitionTimer = 1.35
        self.sigilTransitionDuration = 1.35
        self.restActive = False
        self.restViolationLatched = False
        self.acceptedOfferings = set()
        self.offeringPositions = []
        self.ruleText = "THE SIGIL SPEAKS TRUE"
        self.ruleTruth = True
        self.truthIndex = 0
        self.peakBelief = 0.0
        self.phaseAnnouncementTimer = 3.0

    def _seconds(self):
        return vH.get_timer_step() / max(1, vH.frameRate)

    def _update_phase(self):
        if self.debugPhaseLocked:
            return
        count = len(self.phaseLabels)
        ratio = max(0.0, min(1.0, self.hp / self.maxHp))
        phase = min(count, int((1 - ratio) * count + 1e-9) + 1)
        if phase != self.phase:
            self._set_dream_phase(phase)

    def _set_dream_phase(self, phase):
        self.previousSigilPhase = self.phase
        self.phase = max(1, min(len(self.phaseLabels), int(phase)))
        self.phaseLabel = self.phaseLabels[self.phase - 1]
        self.phaseFlavor = self.phaseFlavors[self.phase - 1]
        self.phaseAccent = self.phaseColors[self.phase - 1]
        self.phaseElapsed = 0.0
        self.sigilTransitionTimer = self.sigilTransitionDuration
        self.phaseAnnouncementTimer = 3.2
        self.attackCooldown = min(self.attackCooldown, vH.frameRate * .4)
        self.transitionCleanupRequested = True
        self.restActive = False
        self.restViolationLatched = False
        self.ruleTruth = True
        if self.phase in self.ACT_METADATA:
            self.actTitle = self.ACT_METADATA[self.phase]
            self.actTransitionTimer = self.actTransitionDuration
            self.phaseProtectionTimer = self.actTransitionDuration
        if self.phase == len(self.phaseLabels):
            self._place_offerings()

    def debug_set_phase(self, phase):
        self._set_dream_phase(phase)
        self.debugPhaseLocked = True
        self.attackCooldown = 0

    def take_damage(self, amount, part_id="body"):
        if self.actTransitionTimer > 0 or self.phaseProtectionTimer > 0:
            return HitResult(False, False, 0, blocked=True)
        previous = self.hp
        result = super().take_damage(amount, part_id)
        if not self.debugPhaseLocked and self.phase < len(self.phaseLabels):
            gate = self.maxHp * (len(self.phaseLabels) - self.phase) / len(self.phaseLabels)
            self.hp = max(self.hp, gate)
        return HitResult(result.applied, self.hp <= 0, max(0, previous - self.hp), result.blocked)

    def _shot_from(self, sink, origin, direction, speed, damage, suffix,
                   *, illusion=False, shape="diamond", path="linear", color=None,
                   belief=.45, clarity=0.0):
        size = self.size * (.19 if self.finalBoss else .17)
        shot = EnemyProjectile(
            origin[0] - size / 2, origin[1] - size / 2, direction, speed,
            damage, size, travel_range=vH.tileSizeGlobal * 34,
            color=color or self.phaseAccent, shape=shape, path=path,
            amplitude=vH.tileSizeGlobal * .58 if path == "sine" else 0,
            owner=f"{self.ownerPrefix}_{suffix}", ignore_walls=True,
        )
        shot.illusory = illusion
        shot.truthMarked = not illusion
        shot.beliefGain = 0 if illusion else belief
        shot.clarityGain = clarity
        sink.append(shot)
        return shot

    def _fan_from(self, sink, origin, target, count, spread, speed, damage, suffix,
                  illusion=False, path="linear", belief=.45):
        base = atan2(target[1] - origin[1], target[0] - origin[0])
        for index in range(count):
            offset = 0 if count == 1 else -spread / 2 + spread * index / (count - 1)
            self._shot_from(sink, origin, base + offset, speed, damage, suffix,
                            illusion=illusion, path=path, belief=belief)

    def _radial(self, sink, origin, count, speed, damage, suffix, illusion=False):
        for index in range(count):
            self._shot_from(sink, origin, index * 2 * pi / count + self.patternRotation * .13,
                            speed, damage, suffix, illusion=illusion)

    def _laser_from(self, sink, origin, direction, damage, suffix, illusion=False):
        laser = EnemyProjectile(
            origin[0], origin[1], direction, 0, damage, self.size * .13,
            travel_range=vH.tileSizeGlobal * 35,
            color=ui.MUTED if illusion else self.phaseAccent,
            shape="laser", path="laser", lifetime=2.5,
            owner=f"{self.ownerPrefix}_{suffix}", ignore_walls=True,
        )
        laser.telegraphDuration = 1.05
        laser.illusory = illusion
        laser.truthMarked = not illusion
        laser.beliefGain = 0 if illusion else .7
        sink.append(laser)

    def _place_offerings(self):
        center = self._center()
        self.offeringPositions = [
            {"x": center[0] + cos(i * pi / 2) * vH.tileSizeGlobal * 4.8,
             "y": center[1] + sin(i * pi / 2) * vH.tileSizeGlobal * 4.8,
             "name": ("POWER", "HASTE", "LIFE", "MULTITUDE")[i], "taken": False}
            for i in range(4)
        ]

    def _update_special_rules(self, player_x, player_y, dt):
        import characterStats as cS
        self.peakBelief = max(self.peakBelief, cS.dreamState["belief"])
        if self.finalBoss and self.phase == 4:
            self.restActive = self.phaseElapsed % 7.0 >= 5.4
            if self.restActive and cS.bulletHolster and not self.restViolationLatched:
                cS.alter_belief(1.25, false_rule=True)
                self.restViolationLatched = True
            elif not self.restActive:
                self.restViolationLatched = False
        if self.finalBoss and self.phase == 10:
            for offering in self.offeringPositions:
                if (not offering["taken"] and hypot(player_x - offering["x"],
                                                     player_y - offering["y"])
                        <= vH.tileSizeGlobal * .9):
                    offering["taken"] = True
                    self.acceptedOfferings.add(offering["name"])
                    cS.alter_belief(1.2, false_rule=True)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        dt = self._seconds()
        self.entranceRemaining = max(0.0, self.entranceRemaining - dt)
        self.actTransitionTimer = max(0.0, self.actTransitionTimer - dt)
        self.phaseProtectionTimer = max(0.0, self.phaseProtectionTimer - dt)
        self.sigilTransitionTimer = max(0.0, self.sigilTransitionTimer - dt)
        self.phaseAnnouncementTimer = max(0.0, self.phaseAnnouncementTimer - dt)
        self.phaseElapsed += dt
        self._update_phase()
        self._update_special_rules(player_world_x, player_world_y, dt)
        if self.entranceRemaining > 0 or self.actTransitionTimer > 0:
            self.age += vH.get_timer_step()
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        mode = self.movementModes[(self.phase-1) % len(self.movementModes)]
        original_speed = self.speed
        if mode == "static":
            self.speed = 0
        elif mode == "path":
            center = self._arena_center()
            player_world_x = center[0] + sin(self.phaseElapsed*.62)*self.arenaRadius*.58
            player_world_y = center[1] + sin(self.phaseElapsed*1.24)*self.arenaRadius*.32
        Enemy.updateEnemy(self, player_world_x, player_world_y, projectile_sink)
        self.speed = original_speed
        self.attackCooldown -= vH.get_timer_step()
        if self.restActive:
            return
        if self.attackCooldown <= 0:
            self._fire_pattern(player_world_x, player_world_y, projectile_sink)
            rate = max(.34, 1.0 - .055 * (self.phase - 1))
            self.attackCooldown = self.attackCooldownMax * rate * self.rng.uniform(.88, 1.08)

    def _draw_commandment_sigil(self, screen, center, radius, progress=1.0,
                                phase=None, alpha=255, rotation=0.0):
        phase = self.phase if phase is None else phase
        if not self.phaseSigils:
            return ""
        sigil_index = self.phaseSigils[max(0, min(len(self.phaseSigils) - 1, phase - 1))]
        name, strokes = COMMANDMENT_SIGILS[sigil_index]
        extent = max(12, int(radius * 2.7))
        aa = 2
        layer = pygame.Surface((extent * aa, extent * aa), pygame.SRCALPHA)
        origin = extent * aa / 2
        c, s = cos(rotation), sin(rotation)
        budget = max(0.0, min(1.0, progress)) * len(strokes)
        width = max(2, int(radius * .08)) * aa
        for stroke_index, stroke in enumerate(strokes):
            amount = max(0.0, min(1.0, budget - stroke_index))
            if amount <= 0:
                continue
            points = []
            for x, y in stroke:
                x, y = x * radius * aa, y * radius * aa
                points.append((origin + x * c - y * s, origin + x * s + y * c))
            count = max(2, min(len(points), int(amount * (len(points) - 1)) + 2))
            points = points[:count]
            pygame.draw.lines(layer, (*ui.INK[:3], alpha), False, points, width + 7)
            pygame.draw.lines(layer, (*self.phaseAccent[:3], alpha), False, points, width)
            pygame.draw.lines(layer, (*ui.CREAM[:3], alpha), False, points, max(1, width // 3))
        layer = pygame.transform.smoothscale(layer, (extent, extent))
        screen.blit(layer, (center[0] - extent / 2, center[1] - extent / 2))
        return name

    def _draw_dream_court(self, screen):
        center = bG.world_to_screen(*self._center())
        tile = vH.tileSizeGlobal
        belief = __import__("characterStats").dreamState["belief"]
        chaos = (1.0 + belief * .055) if self.finalBoss else 1.0
        court = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        for ring in range(3 if not self.finalBoss else 5):
            radius = tile * (2.1 + ring * 1.22 + sin(self.age * .018 + ring) * .18) * chaos
            rect = pygame.Rect(0, 0, radius * 2, radius * 2)
            rect.center = center
            start = self.age * (.0018 + ring * .0007) * (-1 if ring % 2 else 1)
            for segment in range(8 + ring * 2):
                angle = start + segment * 2 * pi / (8 + ring * 2)
                pygame.draw.arc(court, (*self.phaseAccent[:3], 32 + ring * 6), rect,
                                angle, angle + pi / (9 + ring), 2 + ring % 2)
        for index in range((8 if self.finalBoss else 4) + int(belief * .5)):
            angle = index * 2.399 + self.age * .0025
            radius = tile * (1.7 + index % 5)
            point = (center[0] + cos(angle) * radius, center[1] + sin(angle) * radius)
            size = 5 + index % 4
            pygame.draw.polygon(court, (*self.phaseAccent[:3], 70),
                                ((point[0], point[1] - size), (point[0] + size, point[1]),
                                 (point[0], point[1] + size), (point[0] - size, point[1])))
        screen.blit(court, (0, 0))
        progress = 1 - self.sigilTransitionTimer / self.sigilTransitionDuration
        self._draw_commandment_sigil(screen, center, tile * (2.0 if self.finalBoss else 1.45),
                                     progress, rotation=self.age * .0007, alpha=62)

    def _draw_mask_and_halоs(self, screen):
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        center = rect.center
        for index in range(5 if not self.finalBoss else 10):
            angle = self.age * (.005 + index * .0003) + index * 2 * pi / (10 if self.finalBoss else 5)
            radius = self.size * (.72 + .08 * sin(self.age * .02 + index))
            point = (center[0] + cos(angle) * radius, center[1] + sin(angle) * radius * .62)
            pygame.draw.circle(screen, ui.INK, point, max(3, int(self.size * .045)))
            pygame.draw.circle(screen, self.phaseAccent, point, max(2, int(self.size * .025)))
        mask = rect.inflate(-self.size * .26, -self.size * .12)
        pygame.draw.ellipse(screen, ui.INK, mask.inflate(8, 8))
        pygame.draw.ellipse(screen, ui.CREAM, mask)
        pygame.draw.arc(screen, self.phaseAccent, mask, pi, 2 * pi, max(3, int(self.size * .06)))
        eye_y = mask.y + mask.height * .42
        for side in (-1, 1):
            eye = (mask.centerx + side * mask.width * .2, eye_y)
            pygame.draw.line(screen, ui.INK, (eye[0] - 7, eye[1]), (eye[0] + 7, eye[1]), 4)
            pygame.draw.circle(screen, self.phaseAccent, eye, 3)
        transition = self.sigilTransitionTimer / self.sigilTransitionDuration
        self._draw_commandment_sigil(screen, center, self.size * .3,
                                     max(.05, 1 - transition), rotation=transition * pi)

    def _draw_act_transition(self, screen):
        progress = 1 - self.actTransitionTimer / self.actTransitionDuration
        alpha = int(210 * min(1, progress * 6, (1 - progress) * 6))
        veil = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        curtain = int(screen.get_width() * min(.5, progress * .7))
        pygame.draw.rect(veil, (42, 13, 52, alpha), (0, 0, curtain, screen.get_height()))
        pygame.draw.rect(veil, (42, 13, 52, alpha),
                         (screen.get_width() - curtain, 0, curtain, screen.get_height()))
        screen.blit(veil, (0, 0))
        scale = ui.display_scale(screen)
        ui.draw_text(screen, self.actTitle, 34 * scale, self.phaseAccent,
                     (screen.get_width() / 2, screen.get_height() * .4), "center")
        name = self._draw_commandment_sigil(
            screen, (screen.get_width() / 2, screen.get_height() * .55),
            44 * scale, min(1, progress * 2.5), alpha=255)
        ui.draw_text(screen, name, 11 * scale, ui.CREAM,
                     (screen.get_width() / 2, screen.get_height() * .65), "center")

    def drawEnemy(self, screen):
        self._draw_dream_court(screen)
        super().drawEnemy(screen)
        self._draw_mask_and_halоs(screen)
        import characterStats as cS
        belief = cS.dreamState["belief"]
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        for index in range(int(belief // 2)):
            angle = self.age * (.006 + index * .001) + index * 2.1
            offset = self.size * (1.0 + index * .22)
            ghost_center = (rect.centerx + cos(angle) * offset,
                            rect.centery + sin(angle) * offset * .55)
            ghost = pygame.Rect(0, 0, self.size * .42, self.size * .58)
            ghost.center = ghost_center
            pygame.draw.ellipse(screen, ui.INK, ghost.inflate(5, 5), 3)
            pygame.draw.ellipse(screen, ui.MUTED, ghost, 2)
        if ((not self.finalBoss and self.phase == 2)
                or (self.finalBoss and self.phase in (3, 9))):
            scale = ui.display_scale(screen)
            banner = pygame.Rect(0, 0, min(screen.get_width() * .48, 620 * scale), 42 * scale)
            banner.midtop = (screen.get_width() / 2, screen.get_height() * .13)
            ui.draw_panel(screen, banner, ui.PANEL_RAISED,
                          ui.CREAM if self.ruleTruth else self.phaseAccent, shadow=5)
            ui.draw_text(screen, self.ruleText, 11 * scale,
                         ui.CREAM if self.ruleTruth else self.phaseAccent,
                         banner.center, "center")
        if self.phaseAnnouncementTimer > 0 and self.actTransitionTimer <= 0:
            scale = ui.display_scale(screen)
            width = min(screen.get_width() * .56, 680 * scale)
            banner = pygame.Rect(0, 0, width, 66 * scale)
            banner.midbottom = (screen.get_width() / 2, screen.get_height() - 32 * scale)
            ui.draw_panel(screen, banner, ui.PANEL, self.phaseAccent, shadow=6)
            sigil_name = COMMANDMENT_SIGILS[self.phaseSigils[self.phase - 1]][0]
            ui.draw_text(screen, f"{sigil_name} // {self.phaseLabel}", 13 * scale,
                         self.phaseAccent, (banner.centerx, banner.y + 12 * scale), "midtop")
            ui.draw_text(screen, self.phaseFlavor, 9 * scale, ui.CREAM,
                         (banner.centerx, banner.bottom - 12 * scale), "midbottom")
        if self.finalBoss and self.phase == 10:
            for offering in self.offeringPositions:
                if offering["taken"]:
                    continue
                point = bG.world_to_screen(offering["x"], offering["y"])
                pygame.draw.circle(screen, ui.INK, point, int(vH.tileSizeGlobal * .38))
                pygame.draw.circle(screen, self.phaseAccent, point, int(vH.tileSizeGlobal * .3), 4)
                ui.draw_text(screen, offering["name"], 8 * ui.display_scale(screen),
                             ui.CREAM, (point[0], point[1] + vH.tileSizeGlobal * .48), "center")
        if self.restActive:
            ui.draw_text(screen, "REST // DO NOT FIRE", 16 * ui.display_scale(screen),
                         ui.CREAM, (screen.get_width() / 2, screen.get_height() * .18), "center")
        if self.actTransitionTimer > 0:
            self._draw_act_transition(screen)

    def challenge_results(self):
        import characterStats as cS
        return {
            "unbelieving": self.peakBelief <= 3.0,
            "true_witness": cS.dreamState["false_rules"] == 0,
            "content": len(self.acceptedOfferings) == 0,
            "measured_desire": len(self.acceptedOfferings) == 1,
        }


class Hypno(PhantasiaBoss):
    bossName = "HYPNO"
    subtitle = "THE ORNATE SUGGESTION"
    phaseLabels = ("IDOL", "SPOKEN RULE", "INHERITANCE", "CHOSEN", "OFFERING")
    phaseFlavors = (
        "Surely you recognize the one before you.",
        "A command is true because it is spoken.",
        "What sleeps in one generation wakes in the next.",
        "You chose. Do not pretend otherwise.",
        "Everything offered was already mine.",
    )
    phaseColors = tuple(pygame.Color(*color) for color in (
        (214, 89, 188), (111, 164, 224), (227, 180, 75),
        (126, 205, 159), (211, 105, 115)))
    phaseSigils = (0, 2, 4, 6, 9)
    ownerPrefix = "hypno_phantasia"
    bodyColor = pygame.Color(151, 56, 144)
    accentColor = pygame.Color(211, 91, 183)
    movementSpeed = .18
    bodyScale = 1.8
    cooldownSeconds = 1.8
    shotRangeTiles = 28

    def _fire_pattern(self, player_x, player_y, sink):
        center = self._center()
        target = (player_x, player_y)
        if self.phase == 1:
            for index in range(3):
                origin = (center[0] + cos(index * 2 * pi / 3) * vH.tileSizeGlobal * 2.8,
                          center[1] + sin(index * 2 * pi / 3) * vH.tileSizeGlobal * 2.8)
                self._fan_from(sink, origin, target, 3, .55, .78, 260, "idol",
                               illusion=index != self.patternRotation % 3)
        elif self.phase == 2:
            self.ruleTruth = self.patternRotation % 3 != 2
            self.ruleText = "MOVE" if self.ruleTruth else "REMAIN"
            self._fan_from(sink, center, target, 5, 1.25, .82, 270, "spoken_rule",
                           illusion=not self.ruleTruth, path="sine")
            if not self.ruleTruth:
                self._radial(sink, center, 6, .5, 245, "true_sigil")
        elif self.phase == 3:
            for index in range(3):
                shot = self._shot_from(sink, center, atan2(player_y-center[1], player_x-center[0])
                                       + (index-1)*.42, .72, 260, "lineage")
                shot.splitCount = 3
                shot.splitAt = vH.tileSizeGlobal * (3.2 + index)
                shot.splitGeneration = 1
        elif self.phase == 4:
            self._fan_from(sink, center, target, 3, .42, .92, 275, "chosen")
            self._radial(sink, center, 8, .38, 230, "spared", illusion=True)
        else:
            self._radial(sink, center, 10, .42, 275, "offering",
                         illusion=self.patternRotation % 2 == 0)
            self._fan_from(sink, center, target, 5, .8, .8, 285, "debt")
        self.patternRotation += 1
        self._mark_attack(.52)


class Malady(PhantasiaBoss):
    bossName = "MALADY"
    subtitle = "THE DREAM MADE ILL"
    finalBoss = True
    ownerPrefix = "malady_phantasia"
    phaseLabels = ("THRONE", "GRAVEN HALL", "THE NAME", "SABBATH", "THE HOUSE",
                   "THE UNSTRUCK", "THE VOW", "MINE AND YOURS", "THE WITNESS", "ENOUGH")
    phaseFlavors = (
        "Kneel to the source.", "An image need not be real to wound.",
        "Do not spend what you cannot name.", "On the seventh beat, be still.",
        "Every child carries the first design.", "Power is proven by what it spares.",
        "A chosen path remembers betrayal.", "Possession is merely a change of color.",
        "Only one witness keeps its line.", "Was what you carried ever not enough?",
    )
    phaseColors = tuple(pygame.Color(*color) for color in (
        (233, 192, 78), (193, 84, 215), (111, 174, 228), (235, 228, 185),
        (107, 191, 145), (218, 102, 118), (225, 128, 190), (98, 189, 206),
        (244, 244, 232), (220, 71, 133)))
    phaseSigils = tuple(range(10))
    ACT_METADATA = {4: "ACT II // THE COVENANT", 7: "ACT III // THE TESTIMONY"}
    finalBodyColor = pygame.Color(99, 48, 126)
    finalAccentColor = pygame.Color(225, 95, 178)
    finalBodyScale = 2.35
    finalCooldownSeconds = 1.25
    movementSpeed = .25
    movementModes = ("static", "path", "chase", "static", "path",
                     "chase", "path", "static", "chase", "path")
    shotRangeTiles = 38

    def __init__(self, world_x, world_y, rng=None):
        super().__init__(world_x, world_y, rng)
        self.actTitle = "ACT I // THE DOCTRINE"
        self.actTransitionTimer = self.actTransitionDuration
        self.phaseProtectionTimer = self.actTransitionDuration

    def _fire_pattern(self, player_x, player_y, sink):
        center, target = self._center(), (player_x, player_y)
        aimed = atan2(player_y - center[1], player_x - center[0])
        if self.phase == 1:  # Authority
            throne = self.patternRotation % 4
            for index in range(4):
                origin = (center[0] + cos(index*pi/2)*vH.tileSizeGlobal*3.6,
                          center[1] + sin(index*pi/2)*vH.tileSizeGlobal*3.6)
                self._fan_from(sink, origin, target, 4, .62, .92, 330, "authority",
                               illusion=index != throne)
        elif self.phase == 2:  # Image
            for index in range(4):
                origin = (center[0] + cos(index*pi/2+pi/4)*vH.tileSizeGlobal*3,
                          center[1] + sin(index*pi/2+pi/4)*vH.tileSizeGlobal*3)
                self._radial(sink, origin, 6, .7, 320, "image", illusion=index != self.patternRotation % 4)
        elif self.phase == 3:  # Reverence
            self.ruleTruth = self.patternRotation % 3 != 1
            self.ruleText = ("PRESERVE THE NAME" if self.ruleTruth else "BREAK THE NAME")
            self._laser_from(sink, center, aimed, 350, "name", illusion=not self.ruleTruth)
            self._fan_from(sink, center, target, 7, 1.4, .72, 325, "reverence")
        elif self.phase == 4:  # Rest
            self._radial(sink, center, 12, .58, 320, "six_beats")
        elif self.phase == 5:  # Lineage
            for index in range(5):
                shot = self._shot_from(sink, center, aimed+(index-2)*.3, .8, 330, "lineage")
                shot.splitCount = 3
                shot.splitAt = vH.tileSizeGlobal * (2.8 + index*.55)
                shot.splitGeneration = 2
        elif self.phase == 6:  # Mercy
            self._fan_from(sink, center, target, 3, .3, 1.0, 350, "mercy")
            for index in range(12):
                self._shot_from(sink, center, index*2*pi/12, .42, 0, "procession", illusion=True)
        elif self.phase == 7:  # Fidelity
            chosen = self.patternRotation % 4
            for index in range(4):
                self._laser_from(sink, center, index*pi/2, 355, "vow", illusion=index == chosen)
            self._fan_from(sink, center, target, 5, .72, .9, 340, "betrayal")
        elif self.phase == 8:  # Ownership
            build = __import__("characterStats").player_build_snapshot()
            count = max(4, min(10, round(build["stats"]["projectile_count"])+2))
            self._radial(sink, center, count, .82, 345, "stolen")
            self._radial(sink, center, count, .45, 0, "unowned", illusion=True)
        elif self.phase == 9:  # Truth
            true_index = self.patternRotation % 5
            self.truthIndex = true_index
            for index in range(5):
                self._laser_from(sink, center, aimed + (index-2)*.34, 370,
                                 "witness", illusion=index != true_index)
        else:  # Contentment
            intensity = len(self.acceptedOfferings)
            self._radial(sink, center, 10 + intensity*2, .62 + intensity*.08,
                         350, "enough")
            self._fan_from(sink, center, target, 5 + intensity, 1.25, 1.0,
                           365, "covetous", path="sine")
        self.patternRotation += 1
        self._mark_attack(.62)


@dataclass(frozen=True)
class BossDefinition:
    key: str
    display_name: str
    boss_class: type


class BossCatalog:
    def __init__(self):
        self.definitions = {}

    def register(self, definition):
        self.definitions[definition.key] = definition

    def spawn(self, key, rng=None):
        definition = self.definitions[key]
        size = vH.tileSizeGlobal * 1.9
        center_x = len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2
        center_y = len(bG.currRoomRects) * vH.tileSizeGlobal / 2
        requested = pygame.Rect(center_x-size/2, center_y-size/2, size, size)
        spawn_rect = bG.find_nearest_open_rect(requested, size)
        boss = definition.boss_class(spawn_rect.x, spawn_rect.y, rng=rng)
        boss.contentKey = key
        return boss


BOSS_CATALOG = BossCatalog()
BOSS_CATALOG.register(BossDefinition("beaudis", "Beaudis", Beaudis))
BOSS_CATALOG.register(BossDefinition("dissonance", "Dissonance", Dissonance))
BOSS_CATALOG.register(BossDefinition("bair", "Bair", Bair))
BOSS_CATALOG.register(BossDefinition("sting", "Sting", Sting))
BOSS_CATALOG.register(BossDefinition("ishe", "Ishe", Ishe))
BOSS_CATALOG.register(BossDefinition("chronos", "Chronos", Chronos))
BOSS_CATALOG.register(BossDefinition("kage", "Kage", Kage))
BOSS_CATALOG.register(BossDefinition("rot", "Rot", Rot))
BOSS_CATALOG.register(BossDefinition("hypno", "Hypno", Hypno))
BOSS_CATALOG.register(BossDefinition("malady", "Malady", Malady))
