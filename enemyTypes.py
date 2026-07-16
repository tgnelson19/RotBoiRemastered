"""Enemy catalog and special enemy implementations.

Adding a regular enemy requires one ``EnemyDefinition`` registration. More complex
enemies subclass ``Enemy`` while preserving the same update, hitbox, damage, and
projectile contracts used by combat.
"""

from dataclasses import dataclass, field
from math import atan2, cos, hypot, pi, sin
import random

import pygame

import background as bG
from enemy import Enemy, HitResult
from enemyProjectile import EnemyProjectile
import uiTheme as ui
import variableHolster as vH
from progression import enemy_stat_scales


BASE_ENEMY_SPEED_SCALE = .66


def _normalise(x, y):
    length = max(1.0, hypot(x, y))
    return x / length, y / length, length


class WanderingRangedEnemy(Enemy):
    attackRangeTiles = 10
    awarenessRangeTiles = 17

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.wanderAngle = self.rng.uniform(-pi, pi)
        self.wanderTimer = self.rng.randint(45, 110)
        self.attackCooldown = self.rng.randint(30, 90)
        self.attackCooldownMax = vH.frameRate * 1.45

    def _move(self, direction_x, direction_y, speed_multiplier):
        step = self.speed * speed_multiplier * vH.get_frame_scale()
        self._try_axis_move(direction_x * step, "x")
        self._try_axis_move(direction_y * step, "y")
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        direction = atan2(player_world_y - center_y, player_world_x - center_x)
        projectile_size = max(12, self.size * .4)
        projectile_sink.append(EnemyProjectile(
            center_x - projectile_size / 2,
            center_y - projectile_size / 2,
            direction,
            speed=1.4,
            damage=self.damage * .72,
            size=projectile_size,
            travel_range=vH.tileSizeGlobal * 16,
            color=ui.RED,
            shape="diamond",
        ))

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        self.wanderTimer -= vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.size / 2)
        delta_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)

        attack_range = vH.tileSizeGlobal * self.attackRangeTiles
        if self._update_awareness(distance):
            move_multiplier = .16 if distance <= attack_range else .48
            self._move(direction_x, direction_y, move_multiplier)
            if distance <= attack_range and self.attackCooldown <= 0:
                self._fire(player_world_x, player_world_y, projectile_sink)
                self.attackCooldown = self.attackCooldownMax * self.rng.uniform(.85, 1.2)
        else:
            if self.wanderTimer <= 0:
                self.wanderAngle += self.rng.uniform(-1.4, 1.4)
                self.wanderTimer = self.rng.randint(55, 135)
            self._move(cos(self.wanderAngle), sin(self.wanderAngle), .2)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.INK, rect.inflate(-int(self.size * .35), -int(self.size * .35)))
        pygame.draw.rect(screen, ui.RED, rect.inflate(-int(self.size * .58), -int(self.size * .58)))


class ShotgunEnemy(WanderingRangedEnemy):
    attackRangeTiles = 8

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.attackCooldownMax = vH.frameRate * 2.35

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base_direction = atan2(player_world_y - center_y, player_world_x - center_x)
        pellet_count = self.rng.randint(4, 7)
        spread = self.rng.uniform(.48, .78)
        for index in range(pellet_count):
            fraction = 0 if pellet_count == 1 else index / (pellet_count - 1)
            direction = base_direction - spread / 2 + spread * fraction + self.rng.uniform(-.055, .055)
            projectile_size = self.size * self.rng.uniform(.26, .46)
            projectile_sink.append(EnemyProjectile(
                center_x - projectile_size / 2,
                center_y - projectile_size / 2,
                direction,
                speed=self.rng.uniform(1.0, 1.9),
                damage=self.damage * self.rng.uniform(.28, .52),
                size=projectile_size,
                travel_range=vH.tileSizeGlobal * self.rng.uniform(7, 11),
                color=ui.GOLD,
            ))

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        for offset in (-.22, 0, .22):
            y = rect.centery + int(rect.height * offset)
            pygame.draw.line(screen, ui.GOLD, (rect.x + rect.width * .25, y), (rect.right - rect.width * .18, y), max(2, int(self.size * .07)))


class VolleyEnemy(WanderingRangedEnemy):
    """Tiered cone attacker; higher tiers trade mobility for wider volleys."""

    tierSettings = {
        "small": (4, .46, .25, 1.75, 8),
        "medium": (7, .72, .55, 2.45, 10),
        "large": (10, 1.02, .9, 3.35, 12),
    }

    def __init__(self, *args, tier="small", **kwargs):
        super().__init__(*args, **kwargs)
        self.tier = tier
        count, spread, charge, cooldown, attack_range = self.tierSettings[tier]
        self.pelletCount = count
        self.spread = spread
        self.chargeDuration = charge
        self.attackCooldownMax = vH.frameRate * cooldown
        self.attackRangeTiles = attack_range
        self.charging = False
        self.chargeRemaining = 0

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base_direction = atan2(player_world_y - center_y, player_world_x - center_x)
        for index in range(self.pelletCount):
            fraction = .5 if self.pelletCount == 1 else index / (self.pelletCount - 1)
            direction = base_direction - self.spread / 2 + self.spread * fraction
            projectile_sink.append(EnemyProjectile(
                center_x, center_y, direction,
                speed=self.rng.uniform(1.05, 1.65),
                damage=self.damage * (.78 / self.pelletCount),
                size=self.size * self.rng.uniform(.24, .34),
                travel_range=vH.tileSizeGlobal * self.attackRangeTiles,
                color=ui.GOLD, owner=f"volley_{self.tier}",
            ))

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        self.wanderTimer -= vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.size / 2)
        delta_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)
        if not self._update_awareness(distance):
            self.charging = False
            if self.wanderTimer <= 0:
                self.wanderAngle += self.rng.uniform(-1.4, 1.4)
                self.wanderTimer = self.rng.randint(55, 135)
            self._move(cos(self.wanderAngle), sin(self.wanderAngle), .2)
            return

        attack_range = vH.tileSizeGlobal * self.attackRangeTiles
        self._move(direction_x, direction_y, .1 if distance <= attack_range else .38)
        if self.charging:
            self.chargeRemaining -= vH.get_timer_step()
            if self.chargeRemaining <= 0:
                self._fire(player_world_x, player_world_y, projectile_sink)
                self.charging = False
                self.attackCooldown = self.attackCooldownMax * self.rng.uniform(.9, 1.12)
        elif distance <= attack_range and self.attackCooldown <= 0:
            self.charging = True
            self.chargeRemaining = vH.frameRate * self.chargeDuration

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        bars = {"small": 1, "medium": 2, "large": 3}[self.tier]
        for index in range(bars):
            offset = (index - (bars - 1) / 2) * self.size * .18
            pygame.draw.line(screen, ui.GOLD,
                             (rect.centerx - self.size * .2, rect.centery + offset),
                             (rect.centerx + self.size * .25, rect.centery + offset), 3)
        if self.charging:
            progress = 1 - self.chargeRemaining / max(1, vH.frameRate * self.chargeDuration)
            pygame.draw.arc(screen, ui.CREAM, rect.inflate(10, 10), -pi / 2,
                            -pi / 2 + 2 * pi * progress, 4)


class LaserEnemy(WanderingRangedEnemy):
    """Telegraphed beam family: aimed, sweeping, then sector-controlling."""

    tierSettings = {
        "small": (1, .8, 1.55, 0, 2.5),
        "medium": (1, 1.0, 2.5, .16, 3.5),
        "large": (2, 1.25, 3.0, .1, 5.0),
    }

    def __init__(self, *args, tier="small", **kwargs):
        super().__init__(*args, **kwargs)
        self.tier = tier
        self.attackCooldownMax = vH.frameRate * self.tierSettings[tier][4]
        self.attackRangeTiles = 15

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        count, telegraph, lifetime, angular_speed, _ = self.tierSettings[self.tier]
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base = atan2(player_world_y - center_y, player_world_x - center_x)
        for index in range(count):
            direction = base + index * pi
            laser = EnemyProjectile(
                center_x, center_y, direction, speed=0,
                damage=self.damage * (.7 if count == 1 else .48),
                size=self.size * (.16 if self.tier != "large" else .2),
                travel_range=vH.tileSizeGlobal * 17, color=ui.RED,
                path="laser", lifetime=lifetime, angular_speed=angular_speed,
                owner=f"laser_{self.tier}", ignore_walls=True,
            )
            laser.telegraphDuration = telegraph
            projectile_sink.append(laser)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.circle(screen, ui.RED, rect.center, max(4, int(self.size * .2)), 3)
        pygame.draw.line(screen, ui.CREAM, rect.midleft, rect.midright, 2)


class BombEnemy(WanderingRangedEnemy):
    """Space-control family with honest fuse and blast-radius telegraphs."""

    tierSettings = {
        "small": (1, 2.3, 1.25, 3.0),
        "medium": (1, 2.6, 1.75, 3.8),
        "large": (3, 3.0, 2.1, 5.2),
    }

    def __init__(self, *args, tier="small", **kwargs):
        super().__init__(*args, **kwargs)
        self.tier = tier
        self.attackRangeTiles = 9 if tier == "small" else 13
        self.attackCooldownMax = vH.frameRate * self.tierSettings[tier][3]
        self.retreatRemaining = 0

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        count, fuse, radius_tiles, _ = self.tierSettings[self.tier]
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        for index in range(count):
            offset_angle = index * 2 * pi / max(1, count)
            offset = 0 if count == 1 else vH.tileSizeGlobal * 2.4
            target = (player_world_x + cos(offset_angle) * offset,
                      player_world_y + sin(offset_angle) * offset)
            bomb = EnemyProjectile(
                center_x, center_y, 0, speed=0,
                damage=self.damage * (.68 if count == 1 else .42),
                size=self.size * .38, travel_range=vH.tileSizeGlobal * 30,
                color=ui.GOLD, shape="bomb", path="bomb", target=target,
                owner=f"bomb_{self.tier}", ignore_walls=True,
            )
            bomb.fuseDuration = fuse
            bomb.blastRadius = vH.tileSizeGlobal * radius_tiles
            bomb.burstCount = 4 if self.tier == "small" else 6 if self.tier == "medium" else 8
            bomb.burstDamage = bomb.damage
            projectile_sink.append(bomb)
        self.retreatRemaining = vH.frameRate * 1.2

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        self.wanderTimer -= vH.get_timer_step()
        self.retreatRemaining = max(0, self.retreatRemaining - vH.get_timer_step())
        delta_x = player_world_x - (self.worldX + self.size / 2)
        delta_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)
        if not self._update_awareness(distance):
            self._move(cos(self.wanderAngle), sin(self.wanderAngle), .2)
            return
        if self.retreatRemaining:
            self._move(-direction_x, -direction_y, .42)
        else:
            self._move(direction_x, direction_y, .15 if distance <= vH.tileSizeGlobal * self.attackRangeTiles else .42)
        if distance <= vH.tileSizeGlobal * self.attackRangeTiles and self.attackCooldown <= 0:
            self._fire(player_world_x, player_world_y, projectile_sink)
            self.attackCooldown = self.attackCooldownMax * self.rng.uniform(.9, 1.15)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.circle(screen, ui.GOLD, rect.center, max(4, int(self.size * .22)))
        pygame.draw.line(screen, ui.CREAM, rect.center, rect.midtop, 3)


class ArsenalMiniBoss(Enemy):
    """Compact three-phase elite whose variants reorder the shared attacks."""

    def __init__(self, *args, rng=None, phase_order=("volley", "laser", "bomb"), **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.phaseOrder = tuple(phase_order)
        self.phase = 0
        self.invulnerable = False
        self.transitionRemaining = 0
        self.attackCooldown = vH.frameRate * 1.2
        self.transitionCleanupRequested = False
        self.transitionCleanupOwner = f"miniboss_{id(self)}"

    def take_damage(self, amount, part_id="body"):
        if self.invulnerable:
            return HitResult(False, False, 0, True)
        previous_phase = self.phase
        result = super().take_damage(amount, part_id)
        if result.killed:
            return result
        ratio = self.hp / self.maxHp
        desired_phase = 2 if ratio <= 1 / 3 else 1 if ratio <= 2 / 3 else 0
        if desired_phase > previous_phase:
            self.phase = desired_phase
            self.invulnerable = True
            self.transitionRemaining = vH.frameRate * .8
            self.transitionCleanupRequested = True
        return result

    def _fire_volley(self, player_world_x, player_world_y, projectile_sink):
        center_x, center_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        base = atan2(player_world_y - center_y, player_world_x - center_x)
        for index in range(7):
            projectile_sink.append(EnemyProjectile(
                center_x, center_y, base - .48 + index * .16, 1.25,
                self.damage * .12, self.size * .15,
                travel_range=vH.tileSizeGlobal * 14, color=ui.GOLD,
                owner=self.transitionCleanupOwner,
            ))

    def _fire_laser(self, player_world_x, player_world_y, projectile_sink):
        center_x, center_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        base = atan2(player_world_y - center_y, player_world_x - center_x)
        for offset in (0, pi):
            laser = EnemyProjectile(
                center_x, center_y, base + offset, 0, self.damage * .42,
                self.size * .13, travel_range=vH.tileSizeGlobal * 18,
                color=ui.RED, path="laser", lifetime=2.4, angular_speed=.12,
                owner=self.transitionCleanupOwner, ignore_walls=True,
            )
            laser.telegraphDuration = 1.0
            projectile_sink.append(laser)

    def _fire_bomb(self, player_world_x, player_world_y, projectile_sink):
        center_x, center_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        for index in range(3):
            angle = index * 2 * pi / 3
            target = (player_world_x + cos(angle) * vH.tileSizeGlobal * 2,
                      player_world_y + sin(angle) * vH.tileSizeGlobal * 2)
            bomb = EnemyProjectile(
                center_x, center_y, 0, 0, self.damage * .38, self.size * .2,
                travel_range=vH.tileSizeGlobal * 30, color=ui.PURPLE,
                shape="bomb", path="bomb", target=target,
                owner=self.transitionCleanupOwner, ignore_walls=True,
            )
            bomb.fuseDuration = 2.7
            bomb.blastRadius = vH.tileSizeGlobal * 1.7
            bomb.burstCount = 6
            bomb.burstDamage = bomb.damage
            projectile_sink.append(bomb)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        distance_x = player_world_x - (self.worldX + self.size / 2)
        distance_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(distance_x, distance_y)
        if not self._update_awareness(distance):
            self._wander(.12)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.invulnerable:
            self.transitionRemaining -= vH.get_timer_step()
            if self.transitionRemaining <= 0:
                self.invulnerable = False
                self.attackCooldown = vH.frameRate * .65
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        orbit = sin(self.age * .035) * .32
        step = self.speed * .24 * vH.get_frame_scale()
        self._try_axis_move((direction_x - direction_y * orbit) * step, "x")
        self._try_axis_move((direction_y + direction_x * orbit) * step, "y")
        self.attackCooldown -= vH.get_timer_step()
        if self.attackCooldown <= 0:
            attack = self.phaseOrder[self.phase]
            getattr(self, f"_fire_{attack}")(player_world_x, player_world_y, projectile_sink)
            cooldown = {"volley": 2.4, "laser": 3.8, "bomb": 4.4}[attack]
            self.attackCooldown = vH.frameRate * cooldown
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        phase_colors = (ui.GOLD, ui.RED, ui.PURPLE)
        pygame.draw.rect(screen, phase_colors[self.phase],
                         rect.inflate(-int(self.size * .35), -int(self.size * .35)), 4)
        if self.invulnerable:
            pygame.draw.ellipse(screen, ui.CREAM, rect.inflate(16, 16), 5)


class ChildEnemy(Enemy):
    """Fragile, fast offspring created only by a ParentEnemy threshold."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.awarenessState = "alerted"
        self.threatCost = .5
        self.family = "parent"

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.circle(screen, ui.CREAM, rect.center, max(2, int(self.size * .12)))


class ParentEnemy(Enemy):
    """Slow pressure enemy that fires heavy bursts and births fragile chasers."""

    thresholds = (.70, .40, .15)

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.attackCooldown = vH.frameRate * self.rng.uniform(1.0, 1.8)
        self.burstTimer = 0
        self.burstRemaining = 0
        self.crossedThresholds = set()
        self.pendingChildren = 0

    def take_damage(self, amount, part_id="body"):
        previous_ratio = self.hp / self.maxHp
        result = super().take_damage(amount, part_id)
        current_ratio = max(0, self.hp) / self.maxHp
        for threshold in self.thresholds:
            if previous_ratio > threshold >= current_ratio and threshold not in self.crossedThresholds:
                self.crossedThresholds.add(threshold)
                self.pendingChildren += 2
        return result

    def _birth_child(self):
        angle = self.rng.uniform(-pi, pi)
        child_size = self.size * .38
        distance = self.size * .72
        candidate = pygame.Rect(
            self.worldX + self.size / 2 + cos(angle) * distance - child_size / 2,
            self.worldY + self.size / 2 + sin(angle) * distance - child_size / 2,
            child_size, child_size,
        )
        safe = bG.find_nearest_open_rect(candidate, child_size)
        child = ChildEnemy(
            safe.x, safe.y, self.speed * 3.4, child_size,
            ui.RED, self.damage * .48, max(1, self.maxHp * .08),
            self.expValue * .12, self.difficulty, archetype="runner",
        )
        self.spawnedEnemies.append(child)

    def _fire_burst_shot(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base = atan2(player_world_y - center_y, player_world_x - center_x)
        for offset in (-.16, 0, .16):
            projectile_sink.append(EnemyProjectile(
                center_x, center_y, base + offset, speed=.72,
                damage=self.damage * .42, size=self.size * .42,
                travel_range=vH.tileSizeGlobal * 18, color=ui.PURPLE,
                shape="diamond", owner="parent_burst",
            ))

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.size / 2)
        delta_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)
        if not self._update_awareness(distance):
            self._wander(.14)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        if self.pendingChildren:
            self._birth_child()
            self.pendingChildren -= 1

        step = self.speed * .34 * vH.get_frame_scale()
        self._try_axis_move(direction_x * step, "x")
        self._try_axis_move(direction_y * step, "y")
        self.attackCooldown -= vH.get_timer_step()
        self.burstTimer -= vH.get_timer_step()
        if self.burstRemaining and self.burstTimer <= 0:
            self._fire_burst_shot(player_world_x, player_world_y, projectile_sink)
            self.burstRemaining -= 1
            self.burstTimer = vH.frameRate * .18
        elif self.attackCooldown <= 0:
            self.burstRemaining = 3
            self.burstTimer = 0
            self.attackCooldown = vH.frameRate * self.rng.uniform(2.8, 3.5)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.circle(screen, ui.PURPLE, rect.center, int(self.size * .28))
        pygame.draw.circle(screen, ui.CREAM, rect.center, int(self.size * .12))


class PillarEnemy(Enemy):
    """Stationary pattern enemy that telegraphs jumps and alternating crosses."""

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.state = "waiting"
        self.stateTimer = vH.frameRate * self.rng.uniform(.8, 1.4)
        self.jumpTarget = None
        self.volleyIndex = 0

    def _pick_jump_target(self, player_world_x, player_world_y):
        minimum = vH.tileSizeGlobal * 4
        for _ in range(20):
            angle = self.rng.uniform(-pi, pi)
            radius = vH.tileSizeGlobal * self.rng.uniform(5, 10)
            candidate = pygame.Rect(
                self.worldX + cos(angle) * radius,
                self.worldY + sin(angle) * radius,
                self.size, self.size,
            )
            safe = bG.find_nearest_open_rect(candidate, self.size)
            center_x, center_y = safe.center
            if hypot(center_x - player_world_x, center_y - player_world_y) >= minimum:
                self.jumpTarget = (float(safe.x), float(safe.y))
                return
        safe = bG.find_spawn_rect(self.size)
        self.jumpTarget = (float(safe.x), float(safe.y))

    def _fire_cross(self, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        rotation = 0 if self.volleyIndex % 2 == 0 else pi / 4
        for index in range(4):
            projectile_sink.append(EnemyProjectile(
                center_x, center_y, rotation + index * pi / 2,
                speed=1.05, damage=self.damage * .55, size=self.size * .24,
                travel_range=vH.tileSizeGlobal * 20, color=ui.GOLD,
                shape="diamond", owner="pillar_cross",
            ))
        self.volleyIndex += 1

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        distance = hypot(player_world_x - (self.worldX + self.size / 2),
                         player_world_y - (self.worldY + self.size / 2))
        aware = self._update_awareness(distance)
        self.stateTimer -= vH.get_timer_step()
        if not aware:
            if self.state not in ("waiting", "telegraph"):
                self.state, self.stateTimer, self.volleyIndex = "waiting", vH.frameRate * 1.5, 0
            if self.stateTimer <= 0:
                self._pick_jump_target(player_world_x, player_world_y)
                self.state, self.stateTimer = "telegraph", vH.frameRate * .7
        elif self.state == "waiting" and self.stateTimer <= 0:
            self._pick_jump_target(player_world_x, player_world_y)
            self.state, self.stateTimer = "telegraph", vH.frameRate * .7
        elif self.state == "telegraph" and self.stateTimer <= 0:
            if self.jumpTarget:
                self.worldX, self.worldY = self.jumpTarget
            self.jumpTarget = None
            self.volleyIndex = 0
            self.state, self.stateTimer = "landed", vH.frameRate * 1.0
        elif self.state == "landed" and self.stateTimer <= 0:
            self.state, self.stateTimer = "firing", 0
        elif self.state == "firing" and self.stateTimer <= 0:
            self._fire_cross(projectile_sink)
            if self.volleyIndex >= 6:
                self.state, self.stateTimer = "waiting", vH.frameRate * .7
            else:
                self.stateTimer = vH.frameRate * .48
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        if self.jumpTarget:
            target_x, target_y = bG.world_to_screen(*self.jumpTarget)
            target = pygame.Rect(target_x, target_y, self.size, self.size)
            pygame.draw.ellipse(screen, ui.RED, target.inflate(16, 16), 4)
            pygame.draw.line(screen, ui.CREAM, target.midtop, target.midbottom, 2)
            pygame.draw.line(screen, ui.CREAM, target.midleft, target.midright, 2)
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.GOLD, rect.inflate(-int(self.size * .45), -int(self.size * .14)))


class SnakeEnemy(Enemy):
    def __init__(self, *args, segment_count=5, segment_hp=None, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        body_size = self.size
        self.headSize = body_size * 1.16
        self.segmentSize = body_size * .76
        self.size = self.headSize
        self.segmentSpacing = self.segmentSize * 1.18
        self.segments = []
        segment_hp = segment_hp or self.maxHp * .6
        for index in range(segment_count):
            segment_x = self.worldX - (index + 1) * self.segmentSpacing
            segment_y = self.worldY + self.size * .2
            safe = bG.find_nearest_open_rect(pygame.Rect(segment_x, segment_y, self.segmentSize, self.segmentSize), self.segmentSize)
            self.segments.append({
                "id": index,
                "x": float(safe.x),
                "y": float(safe.y),
                "hp": segment_hp,
                "max_hp": segment_hp,
            })
        self.attackCooldown = vH.frameRate * self.rng.uniform(1.2, 2.2)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.headSize / 2)
        delta_y = player_world_y - (self.worldY + self.headSize / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)
        if not self._update_awareness(distance):
            self._wander(.18)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        weave = sin(self.age * .075) * .32
        direction_x, direction_y = direction_x - direction_y * weave, direction_y + direction_x * weave
        direction_x, direction_y, _ = _normalise(direction_x, direction_y)
        step = self.speed * vH.get_frame_scale()
        self._try_axis_move(direction_x * step, "x")
        self._try_axis_move(direction_y * step, "y")

        previous_x = self.worldX + (self.headSize - self.segmentSize) / 2
        previous_y = self.worldY + (self.headSize - self.segmentSize) / 2
        for segment in self.segments:
            follow_x = previous_x - segment["x"]
            follow_y = previous_y - segment["y"]
            follow_distance = hypot(follow_x, follow_y)
            if follow_distance > self.segmentSpacing:
                amount = (follow_distance - self.segmentSpacing) / follow_distance
                segment["x"] += follow_x * amount
                segment["y"] += follow_y * amount
            previous_x, previous_y = segment["x"], segment["y"]

        if distance <= vH.tileSizeGlobal * 15 and self.attackCooldown <= 0:
            center_x = self.worldX + self.headSize / 2
            center_y = self.worldY + self.headSize / 2
            projectile_size = self.segmentSize * .54
            projectile_sink.append(EnemyProjectile(
                center_x - projectile_size / 2,
                center_y - projectile_size / 2,
                atan2(player_world_y - center_y, player_world_x - center_x),
                speed=.9,
                damage=self.damage * .65,
                size=projectile_size,
                travel_range=vH.tileSizeGlobal * 14,
                color=ui.PURPLE,
                shape="diamond",
            ))
            self.attackCooldown = vH.frameRate * self.rng.uniform(2.0, 3.15)

        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        for segment in reversed(self.segments):
            x, y = bG.world_to_screen(segment["x"], segment["y"])
            rect = pygame.Rect(x, y, self.segmentSize, self.segmentSize)
            pygame.draw.rect(screen, ui.SHADOW, rect.move(3, 3))
            pygame.draw.rect(screen, pygame.Color(72, 145, 104), rect)
            pygame.draw.rect(screen, ui.INK, rect, max(2, int(self.segmentSize * .09)))
            if segment["hp"] < segment["max_hp"]:
                fill = pygame.Rect(rect.x, rect.y - 6, rect.width * segment["hp"] / segment["max_hp"], 4)
                pygame.draw.rect(screen, ui.GREEN, fill)

        head_rect = pygame.Rect(self.posX, self.posY, self.headSize, self.headSize)
        pygame.draw.rect(screen, ui.SHADOW, head_rect.move(5, 5))
        pygame.draw.rect(screen, ui.PURPLE, head_rect)
        pygame.draw.rect(screen, ui.INK, head_rect, max(3, int(self.headSize * .09)))
        eye_size = max(3, int(self.headSize * .12))
        pygame.draw.rect(screen, ui.TEXT, (head_rect.x + head_rect.width * .25, head_rect.y + head_rect.height * .27, eye_size, eye_size))
        pygame.draw.rect(screen, ui.TEXT, (head_rect.right - head_rect.width * .25 - eye_size, head_rect.y + head_rect.height * .27, eye_size, eye_size))
        if self.segments:
            pygame.draw.rect(screen, ui.CREAM, head_rect.inflate(8, 8), max(2, int(self.headSize * .06)))

    def get_screen_hitboxes(self):
        hitboxes = [("head", pygame.Rect(self.posX, self.posY, self.headSize, self.headSize))]
        for segment in self.segments:
            x, y = bG.world_to_screen(segment["x"], segment["y"])
            hitboxes.append((segment["id"], pygame.Rect(x, y, self.segmentSize, self.segmentSize)))
        return hitboxes

    def get_world_hitboxes(self):
        hitboxes = [("head", pygame.Rect(self.worldX, self.worldY, self.headSize, self.headSize))]
        hitboxes.extend((segment["id"], pygame.Rect(segment["x"], segment["y"], self.segmentSize, self.segmentSize)) for segment in self.segments)
        return hitboxes

    def take_damage(self, amount, part_id="head"):
        if part_id == "head":
            if self.segments:
                return HitResult(False, False, 0, True)
            self.hp -= amount
            return HitResult(True, self.hp <= 0, amount)

        segment = next((item for item in self.segments if item["id"] == part_id), None)
        if segment is None:
            return HitResult(False, False)
        segment["hp"] -= amount
        if segment["hp"] <= 0:
            self.segments.remove(segment)
        return HitResult(True, False, amount)


@dataclass(frozen=True)
class EnemyDefinition:
    key: str
    enemy_class: type
    weight: float
    min_level: int
    speed: float
    size: float
    damage: float
    health: float
    experience: float
    color: pygame.Color
    options: dict = field(default_factory=dict)
    threat_cost: float = 1.0
    family: str = "basic"
    max_active: int = 99
    guaranteed_only: bool = False


class EnemyCatalog:
    def __init__(self):
        self.definitions = {}

    def register(self, definition):
        if definition.key in self.definitions:
            raise ValueError(f"Enemy type already registered: {definition.key}")
        self.definitions[definition.key] = definition

    def available(self, level):
        return [definition for definition in self.definitions.values() if level >= definition.min_level]

    def choose(self, level, rng=None, max_threat=None, existing=()):
        rng = rng or random
        available = [definition for definition in self.available(level)
                     if not definition.guaranteed_only]
        if max_threat is not None:
            family_counts = {}
            for enemy in existing:
                family = getattr(enemy, "family", "basic")
                family_counts[family] = family_counts.get(family, 0) + 1
            available = [definition for definition in available
                         if definition.threat_cost <= max_threat
                         and family_counts.get(definition.family, 0) < definition.max_active]
        if not available:
            return None
        return rng.choices(available, weights=[item.weight for item in available], k=1)[0]

    def create(self, key, world_x, world_y, level, rng=None):
        rng = rng or random
        definition = self.definitions[key]
        scales = enemy_stat_scales(level)
        variation = rng.uniform(.9, 1.12)
        difficulty = rng.uniform(.92, 1.25)
        size = vH.tileSizeGlobal * definition.size / variation
        options = dict(definition.options)
        options.setdefault("archetype", definition.key)
        if definition.enemy_class is not Enemy:
            options["rng"] = rng
        if definition.enemy_class is SnakeEnemy:
            options.pop("archetype", None)
            options["segment_count"] = min(8, 5 + level // 3)
        enemy = definition.enemy_class(
            world_x, world_y,
            BASE_ENEMY_SPEED_SCALE * scales["speed"] * definition.speed * variation,
            size,
            definition.color,
            .9 * scales["damage"] * definition.damage / variation,
            2.2 * scales["health"] * definition.health / variation,
            2.4 * scales["experience"] * definition.experience * difficulty,
            difficulty,
            **options,
        )
        enemy.threatCost = definition.threat_cost
        enemy.family = definition.family
        return enemy

    def spawn(self, level, rng=None, key=None, max_threat=None, existing=(),
              min_distance_tiles=4):
        rng = rng or random
        definition = self.definitions[key] if key else self.choose(level, rng, max_threat, existing)
        if definition is None:
            return None
        # Find a fitting spawn using the definition's nominal body size.
        nominal_size = vH.tileSizeGlobal * definition.size
        spawn_rect = bG.find_spawn_rect(nominal_size, min_distance_tiles)
        return self.create(definition.key, spawn_rect.x, spawn_rect.y, level, rng)


ENEMY_CATALOG = EnemyCatalog()


def _register_defaults():
    entries = (
        EnemyDefinition("runner", Enemy, 22, 0, 1.42, .58, .72, .62, .8, pygame.Color(221, 76, 73)),
        EnemyDefinition("drifter", Enemy, 30, 0, 1.0, .76, 1.0, 1.0, 1.0, pygame.Color(184, 66, 75)),
        EnemyDefinition("skirmisher", Enemy, 18, 0, 1.08, .82, .92, 1.18, 1.3, pygame.Color(68, 151, 142)),
        EnemyDefinition("bulwark", Enemy, 12, 0, .58, 1.18, 1.52, 2.65, 2.1, pygame.Color(200, 132, 56)),
        EnemyDefinition("ranged_wanderer", WanderingRangedEnemy, 10, 0, .62, .82, .85, 1.45, 1.7, pygame.Color(82, 126, 190)),
        EnemyDefinition("shotgunner", ShotgunEnemy, 5, 3, .56, .96, 1.0, 2.0, 2.4, pygame.Color(188, 112, 61)),
        EnemyDefinition("snake", SnakeEnemy, 3, 5, .92, .86, 1.15, 2.15, 5.2, pygame.Color(142, 83, 184)),
        EnemyDefinition("parent", ParentEnemy, 4, 4, .54, 1.42, 1.15, 4.1, 5.8,
                        pygame.Color(126, 67, 146), threat_cost=3.0,
                        family="parent", max_active=2),
        EnemyDefinition("pillar", PillarEnemy, 4, 4, 0, 1.12, 1.0, 3.2, 4.0,
                        pygame.Color(89, 103, 126), threat_cost=4.0,
                        family="pillar", max_active=2),
        EnemyDefinition("volley_small", VolleyEnemy, 8, 0, .70, .74, .82, 1.2, 1.6,
                        pygame.Color(201, 139, 55), {"tier": "small"},
                        threat_cost=1.5, family="volley", max_active=5),
        EnemyDefinition("volley_medium", VolleyEnemy, 5, 6, .55, .98, 1.05, 2.3, 3.0,
                        pygame.Color(191, 112, 47), {"tier": "medium"},
                        threat_cost=3.0, family="volley", max_active=5),
        EnemyDefinition("volley_large", VolleyEnemy, 2, 12, .38, 1.32, 1.3, 4.3, 5.3,
                        pygame.Color(164, 78, 47), {"tier": "large"},
                        threat_cost=5.0, family="volley", max_active=5),
        EnemyDefinition("laser_small", LaserEnemy, 7, 2, .58, .72, .85, 1.25, 1.8,
                        pygame.Color(174, 63, 77), {"tier": "small"},
                        threat_cost=1.5, family="laser", max_active=2),
        EnemyDefinition("laser_medium", LaserEnemy, 4, 7, .43, .98, 1.05, 2.4, 3.2,
                        pygame.Color(153, 52, 84), {"tier": "medium"},
                        threat_cost=3.0, family="laser", max_active=2),
        EnemyDefinition("laser_large", LaserEnemy, 2, 13, .28, 1.28, 1.25, 4.5, 5.4,
                        pygame.Color(126, 42, 84), {"tier": "large"},
                        threat_cost=5.0, family="laser", max_active=2),
        EnemyDefinition("bomb_small", BombEnemy, 7, 2, .72, .7, .9, 1.3, 1.8,
                        pygame.Color(190, 147, 57), {"tier": "small"},
                        threat_cost=1.5, family="bomb", max_active=4),
        EnemyDefinition("bomb_medium", BombEnemy, 4, 8, .54, .96, 1.08, 2.5, 3.2,
                        pygame.Color(180, 112, 52), {"tier": "medium"},
                        threat_cost=3.0, family="bomb", max_active=4),
        EnemyDefinition("bomb_large", BombEnemy, 2, 14, .34, 1.3, 1.3, 4.6, 5.5,
                        pygame.Color(156, 78, 49), {"tier": "large"},
                        threat_cost=5.0, family="bomb", max_active=4),
        EnemyDefinition("miniboss_arsenal", ArsenalMiniBoss, .7, 5, .34, 1.75, 1.45, 10.0, 15.0,
                        pygame.Color(84, 72, 118),
                        {"phase_order": ("volley", "laser", "bomb")},
                        threat_cost=12.0, family="miniboss", max_active=1,
                        guaranteed_only=True),
        EnemyDefinition("miniboss_siege", ArsenalMiniBoss, .7, 15, .3, 1.85, 1.55, 11.0, 16.0,
                        pygame.Color(78, 91, 112),
                        {"phase_order": ("bomb", "volley", "laser")},
                        threat_cost=13.0, family="miniboss", max_active=1,
                        guaranteed_only=True),
    )
    for definition in entries:
        ENEMY_CATALOG.register(definition)


_register_defaults()
