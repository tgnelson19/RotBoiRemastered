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
import characterStats as cS
from progression import encounter_pacing, enemy_stat_scales


BASE_ENEMY_SPEED_SCALE = .66

TIER_BALANCE = {
    "easy": {"rank": 1, "speed": 1.0, "health": 1.0, "damage": 1.0,
             "experience": 1.0, "threat": 1.0},
    "medium": {"rank": 2, "speed": 1.06, "health": 1.45, "damage": 1.28,
               "experience": 1.65, "threat": 1.55},
    "hard": {"rank": 3, "speed": 1.12, "health": 2.05, "damage": 1.65,
             "experience": 2.5, "threat": 2.25},
}

FAMILY_IDENTITIES = {
    "runner": ("pressure", {"melee", "mobile"}),
    "drifter": ("pressure", {"melee", "mobile"}),
    "skirmisher": ("pressure", {"melee", "flanker"}),
    "bulwark": ("tank", {"melee", "durable"}),
    "ranged_wanderer": ("artillery", {"ranged", "mobile"}),
    "shotgunner": ("artillery", {"ranged", "close_range"}),
    "snake": ("tank", {"composite", "ranged"}),
    "parent": ("squad", {"summoner", "ranged"}),
    "pillar": ("control", {"stationary", "radial"}),
    "volley": ("artillery", {"ranged", "cone"}),
    "laser": ("control", {"ranged", "beam"}),
    "bomb": ("control", {"ranged", "area"}),
    "banner": ("squad", {"leader", "melee"}),
    "rammer": ("pressure", {"charge", "terrain"}),
    "warder": ("support", {"shield", "ranged"}),
    "splitter": ("artillery", {"ranged", "splitting"}),
    "collector": ("economy", {"mobile", "xp"}),
    "miniboss": ("elite", {"phased"}),
}

MODIFIER_RULES = {
    "hasty": {"min_level": 5, "roles": {"pressure", "artillery"},
              "color": ui.GOLD},
    "armored": {"min_level": 6, "roles": {"tank", "support", "squad"},
                "color": ui.BLUE},
    "volatile": {"min_level": 8, "roles": {"pressure", "control"},
                 "color": ui.RED},
    "regenerating": {"min_level": 10, "roles": {"tank", "artillery", "support"},
                     "color": ui.GREEN},
    "champion": {"min_level": 12, "roles": {"pressure", "tank", "artillery", "control"},
                 "color": ui.PURPLE},
}


@dataclass(frozen=True)
class EncounterPackage:
    key: str
    min_level: int
    max_level: int
    families: tuple
    weight: float = 1.0
    max_concurrent: int = 1


class RuntimeEncounter:
    """Persistent coordination shared by every ordinary encounter group."""

    _next_id = 1

    def __init__(self, key, members, anchor, level):
        self.id = RuntimeEncounter._next_id
        RuntimeEncounter._next_id += 1
        self.key = key
        self.members = list(members)
        self.anchor = [float(anchor[0]), float(anchor[1])]
        self.level = level
        self.state = "patrolling"
        self.patrolAngle = random.uniform(-pi, pi)
        self.patrolTimer = random.randint(100, 220)
        self.activationRange = vH.sH * (.48 + min(20, level) * .006)
        self.disengageRange = self.activationRange * 1.45
        self.engagementAllowed = False
        self.alertTimer = 0.0
        for index, enemy in enumerate(self.members):
            enemy.encounter = self
            enemy.encounterSlot = index
            enemy.combatSide = -1 if index % 2 else 1
            if hasattr(enemy, "attackCooldown"):
                enemy.attackCooldown += index * vH.frameRate * .18

    @property
    def threat_cost(self):
        return sum(getattr(enemy, "threatCost", 1.0)
                   for enemy in self.members if not enemy.is_dead())

    def center(self):
        living = [enemy for enemy in self.members if not enemy.is_dead()]
        if not living:
            return tuple(self.anchor)
        return (sum(enemy.worldX + enemy.size / 2 for enemy in living) / len(living),
                sum(enemy.worldY + enemy.size / 2 for enemy in living) / len(living))

    def distance_to(self, player_x, player_y):
        center_x, center_y = self.center()
        return hypot(player_x - center_x, player_y - center_y)

    def update(self, player_x, player_y, allowed=True):
        self.members[:] = [enemy for enemy in self.members if not enemy.is_dead()]
        if not self.members:
            return
        distance = self.distance_to(player_x, player_y)
        if self.state == "patrolling" and allowed and distance <= self.activationRange:
            self.state = "engaged"
            self.alertTimer = vH.frameRate * .8
        elif self.state == "engaged" and (not allowed or distance > self.disengageRange):
            self.state = "patrolling"

        self.engagementAllowed = allowed and self.state == "engaged"
        self.alertTimer = max(0, self.alertTimer - vH.get_timer_step())
        if self.state == "patrolling":
            self.patrolTimer -= vH.get_timer_step()
            if self.patrolTimer <= 0:
                self.patrolAngle += random.uniform(-1.2, 1.2)
                self.anchor[0] += cos(self.patrolAngle) * vH.tileSizeGlobal * 2.5
                self.anchor[1] += sin(self.patrolAngle) * vH.tileSizeGlobal * 2.5
                safe = bG.find_nearest_open_rect(
                    pygame.Rect(self.anchor[0], self.anchor[1],
                                vH.tileSizeGlobal, vH.tileSizeGlobal),
                    vH.tileSizeGlobal)
                self.anchor[:] = [safe.x, safe.y]
                self.patrolTimer = random.randint(120, 260)

        count = max(1, len(self.members))
        center_x, center_y = self.center()
        player_dx, player_dy = player_x - center_x, player_y - center_y
        player_distance = max(1.0, hypot(player_dx, player_dy))
        toward_x, toward_y = player_dx / player_distance, player_dy / player_distance
        for index, enemy in enumerate(self.members):
            enemy.engagementAllowed = self.engagementAllowed
            if self.engagementAllowed:
                enemy.encounterPatrolTarget = None
                enemy.awarenessState = "alerted"
                if enemy.combatRole in ("tank", "support"):
                    distance = vH.tileSizeGlobal * (1.65 if enemy.combatRole == "tank" else 1.15)
                    enemy.encounterCombatTarget = (center_x + toward_x * distance,
                                                   center_y + toward_y * distance)
                elif enemy.combatRole == "artillery":
                    enemy.encounterCombatTarget = (center_x - toward_x * vH.tileSizeGlobal,
                                                   center_y - toward_y * vH.tileSizeGlobal)
                else:
                    enemy.encounterCombatTarget = None
            else:
                enemy.encounterCombatTarget = None
                angle = self.patrolAngle + index * 2 * pi / count
                radius = vH.tileSizeGlobal * (1.0 + .25 * (index % 3))
                enemy.encounterPatrolTarget = (
                    self.anchor[0] + cos(angle) * radius,
                    self.anchor[1] + sin(angle) * radius,
                )
                enemy.awarenessState = "wandering"

    def draw(self, screen):
        if self.alertTimer <= 0 or not self.members:
            return
        center = bG.world_to_screen(*self.center())
        progress = self.alertTimer / max(1, vH.frameRate * .8)
        radius = vH.tileSizeGlobal * (1.2 + progress * 1.1)
        pygame.draw.circle(screen, ui.INK, center, radius, 6)
        pygame.draw.circle(screen, ui.GOLD, center, radius, 3)
        for enemy in self.members:
            target = bG.world_to_screen(enemy.worldX + enemy.size / 2,
                                        enemy.worldY + enemy.size / 2)
            pygame.draw.line(screen, ui.GOLD, center, target, 1)


ENCOUNTER_PACKAGES = (
    EncounterPackage("shield_wall", 5, 14, ("warder", "shotgunner", "shotgunner"), 1.2),
    EncounterPackage("royal_procession", 6, 14, ("banner", "collector"), .8),
    EncounterPackage("demolition_crew", 7, 18, ("rammer", "bomb", "bomb"), 1.0),
    EncounterPackage("crossfire", 7, 17, ("pillar", "volley", "volley"), 1.0),
    EncounterPackage("brood_guard", 8, 18, ("parent", "warder"), .8),
    EncounterPackage("fractured_choir", 10, 20, ("splitter", "splitter", "laser"), 1.0),
    EncounterPackage("stampede", 11, 20, ("banner", "rammer"), .7),
    EncounterPackage("salvage_team", 6, 16, ("collector", "bulwark", "ranged_wanderer"), .9),
)


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
        self.attackCooldownMax = vH.frameRate * (1.45 - .16 * (self.tierRank - 1))

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
        self._mark_attack()
        count = self.tierRank
        for index in range(count):
            offset = (index - (count - 1) / 2) * .13
            projectile_sink.append(EnemyProjectile(
                center_x - projectile_size / 2,
                center_y - projectile_size / 2,
                direction + offset,
                speed=1.4 + .08 * (self.tierRank - 1),
                damage=self.damage * (.72 / count),
                size=projectile_size,
                travel_range=vH.tileSizeGlobal * (16 + self.tierRank),
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
            if self.combatRole == "support" and self.encounterCombatTarget is not None:
                guard_dx = self.encounterCombatTarget[0] - (self.worldX + self.size / 2)
                guard_dy = self.encounterCombatTarget[1] - (self.worldY + self.size / 2)
                guard_x, guard_y, guard_distance = _normalise(guard_dx, guard_dy)
                if guard_distance > vH.tileSizeGlobal * .6:
                    self._move(guard_x, guard_y, .38)
            preferred_min = attack_range * (.52 if self.combatRole == "artillery" else .38)
            if distance < preferred_min:
                self._move(-direction_x, -direction_y, .34)
            elif distance <= attack_range:
                side = self.combatSide or 1
                self._move(-direction_y * side, direction_x * side, .18)
            else:
                self._move(direction_x, direction_y, .48)
            if distance <= attack_range and self.attackCooldown <= 0:
                self._fire(player_world_x, player_world_y, projectile_sink)
                self.attackCooldown = self.attackCooldownMax * self.rng.uniform(.85, 1.2)
        else:
            self._wander(.2)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.INK, rect.inflate(-int(self.size * .35), -int(self.size * .35)))
        pygame.draw.rect(screen, ui.RED, rect.inflate(-int(self.size * .58), -int(self.size * .58)))


class ShotgunEnemy(WanderingRangedEnemy):
    attackRangeTiles = 8

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.attackCooldownMax = vH.frameRate * (2.35 - .22 * (self.tierRank - 1))

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base_direction = atan2(player_world_y - center_y, player_world_x - center_x)
        self._mark_attack(.28)
        pellet_count = self.rng.randint(4, 7) + 2 * (self.tierRank - 1)
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
        self._mark_attack(.3)
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
            self._wander(.2)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        attack_range = vH.tileSizeGlobal * self.attackRangeTiles
        if distance < attack_range * .48:
            self._move(-direction_x, -direction_y, .28)
        elif distance <= attack_range:
            side = self.combatSide or 1
            self._move(-direction_y * side, direction_x * side, .12)
        else:
            self._move(direction_x, direction_y, .38)
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
        self._mark_attack(.34)
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
        self._mark_attack(.3)
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
            self._wander(.2)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
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
        self._mark_attack(.2)
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
        self._mark_attack(.2)
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
        self._mark_attack(.2)
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
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        phase_colors = (ui.GOLD, ui.RED, ui.PURPLE)
        # Mini-bosses deliberately use a rigid, low-spectacle silhouette so the
        # smooth final-boss rendering remains visually exceptional.
        pygame.draw.rect(screen, ui.SHADOW, rect.move(6, 6))
        pygame.draw.rect(screen, self.color, rect)
        pygame.draw.rect(screen, ui.INK, rect, max(5, int(self.size * .08)))
        core = rect.inflate(-int(self.size * .38), -int(self.size * .38))
        pygame.draw.rect(screen, ui.VOID, core)
        pygame.draw.rect(screen, phase_colors[self.phase], core, 5)
        for notch in range(self.phase + 1):
            notch_rect = pygame.Rect(rect.x + 8 + notch * 10, rect.bottom - 14, 6, 6)
            pygame.draw.rect(screen, phase_colors[self.phase], notch_rect)
        if self.invulnerable:
            pygame.draw.rect(screen, ui.CREAM, rect.inflate(12, 12), 5)
        if self.hp < self.maxHp:
            bar = pygame.Rect(rect.x, rect.y - 10, rect.width, 5)
            pygame.draw.rect(screen, ui.INK, bar)
            pygame.draw.rect(screen, ui.RED,
                             (bar.x, bar.y, bar.width * max(0, self.hp / self.maxHp), bar.height))


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
                self.pendingChildren += self.tierRank + 1
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
            difficulty_tier=self.difficultyTier,
        )
        self.spawnedEnemies.append(child)

    def _fire_burst_shot(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base = atan2(player_world_y - center_y, player_world_x - center_x)
        self._mark_attack(.25)
        projectile_count = 2 * self.tierRank + 1
        for index in range(projectile_count):
            offset = (index - (projectile_count - 1) / 2) * .16
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
            self.burstRemaining = 2 + self.tierRank
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
        self._mark_attack(.18)
        projectile_count = 2 + self.tierRank * 2
        for index in range(projectile_count):
            projectile_sink.append(EnemyProjectile(
                center_x, center_y, rotation + index * 2 * pi / projectile_count,
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
            if self.volleyIndex >= 5 + self.tierRank:
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
        self.initialSegmentCount = max(1, int(segment_count))
        segment_hp = round(segment_hp or self.maxHp * .6)
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

    def movement_speed_multiplier(self):
        """Lose almost all chase speed as the snake loses its protective body."""
        remaining = len(self.segments) / self.initialSegmentCount
        return .05 + .95 * remaining * remaining

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
        step = self.speed * self.movement_speed_multiplier() * vH.get_frame_scale()
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
            self._mark_attack(.25)
            base = atan2(player_world_y - center_y, player_world_x - center_x)
            for index in range(self.tierRank):
                offset = (index - (self.tierRank - 1) / 2) * .18
                projectile_sink.append(EnemyProjectile(
                    center_x - projectile_size / 2,
                    center_y - projectile_size / 2,
                    base + offset,
                    speed=.9 + .08 * (self.tierRank - 1),
                    damage=self.damage * (.65 / self.tierRank),
                    size=projectile_size,
                    travel_range=vH.tileSizeGlobal * (14 + self.tierRank),
                    color=ui.PURPLE,
                    shape="diamond",
                ))
            self.attackCooldown = vH.frameRate * self.rng.uniform(
                2.0 - .18 * (self.tierRank - 1), 3.15 - .2 * (self.tierRank - 1))

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
        amount = round(amount)
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


class BannerMinion(Enemy):
    """Cheap squad body that changes behavior when its Captain falls."""

    def __init__(self, *args, leader=None, formation_angle=0, **kwargs):
        super().__init__(*args, **kwargs)
        self.leader = leader
        self.formationAngle = formation_angle
        self.threatCost = .55

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        if self.leader is not None and not self.leader.is_dead():
            radius = self.leader.size * (1.0 + .18 * self.tierRank)
            target_x = (self.leader.worldX + self.leader.size / 2
                        + cos(self.formationAngle) * radius)
            target_y = (self.leader.worldY + self.leader.size / 2
                        + sin(self.formationAngle) * radius)
            dx = target_x - (self.worldX + self.size / 2)
            dy = target_y - (self.worldY + self.size / 2)
            direction_x, direction_y, distance = _normalise(dx, dy)
            if distance > self.size * .4:
                step = self.speed * .72 * vH.get_frame_scale()
                self._try_axis_move(direction_x * step, "x")
                self._try_axis_move(direction_y * step, "y")
            self.age += vH.get_timer_step()
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        self.leader = None
        self.speed *= 1.0015
        super().updateEnemy(player_world_x, player_world_y, projectile_sink)


class BannerCaptain(Enemy):
    """Tiny leader whose minions form up, then charge on its command."""

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.commandCooldown = vH.frameRate * 2.6
        self.commandRemaining = 0
        count = 3 + self.tierRank * 2
        minion_size = self.size * .42
        for index in range(count):
            angle = index * 2 * pi / count
            safe = bG.find_nearest_open_rect(pygame.Rect(
                self.worldX + cos(angle) * self.size * 1.2,
                self.worldY + sin(angle) * self.size * 1.2,
                minion_size, minion_size), minion_size)
            minion = BannerMinion(
                safe.x, safe.y, self.speed * 1.65, minion_size, ui.RED,
                self.damage * .55, self.maxHp * .16, self.expValue * .12,
                self.difficulty, archetype="runner",
                difficulty_tier=self.difficultyTier, leader=self,
                formation_angle=angle,
            )
            minion.family = "banner"
            self.spawnedEnemies.append(minion)
        self.atomicSpawnGroup = True

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        super().updateEnemy(player_world_x, player_world_y, projectile_sink)
        self.commandCooldown -= vH.get_timer_step()
        if self.awarenessState != "wandering" and self.commandCooldown <= 0:
            self._mark_attack(.38)
            living = [enemy for enemy in cS.enemyHolster
                      if isinstance(enemy, BannerMinion) and enemy.leader is self]
            for minion in living:
                direction = atan2(player_world_y - minion.worldY,
                                  player_world_x - minion.worldX)
                minion.formationAngle = direction
                minion.speed *= 1.12
            self.commandCooldown = vH.frameRate * max(1.5, 3.0 - .45 * self.tierRank)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.GOLD, (rect.centerx - 3, rect.y - 10, 6, 16))
        pygame.draw.polygon(screen, ui.CREAM,
                            ((rect.centerx + 3, rect.y - 10),
                             (rect.centerx + self.size * .35, rect.y - 3),
                             (rect.centerx + 3, rect.y + 3)))


class RammerEnemy(Enemy):
    """Telegraphs a fixed charge, damages enemies, then stalls after impact."""

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.ramState = "tracking"
        self.ramTimer = vH.frameRate * 1.4
        self.ramDirection = 0
        self.ramHitTargets = set()

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        self.age += vH.get_timer_step()
        self.ramTimer -= vH.get_timer_step()
        if self.ramState == "stunned":
            if self.ramTimer <= 0:
                self.ramState, self.ramTimer = "tracking", vH.frameRate * 1.5
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        if self.ramState == "tracking":
            distance = hypot(player_world_x - self.worldX, player_world_y - self.worldY)
            if not self._update_awareness(distance):
                self._wander(.14)
            elif self.ramTimer <= 0:
                self.ramDirection = atan2(player_world_y - self.worldY,
                                          player_world_x - self.worldX)
                self.ramState, self.ramTimer = "windup", vH.frameRate * .7
        elif self.ramState == "windup":
            if self.ramTimer <= 0:
                self.ramState, self.ramTimer = "charging", vH.frameRate * (1.0 + .2 * self.tierRank)
                self.ramHitTargets.clear()
                self._mark_attack(.4)
        else:
            step = self.speed * (3.0 + .45 * self.tierRank) * vH.get_frame_scale()
            move_x = cos(self.ramDirection) * step
            move_y = sin(self.ramDirection) * step
            moved_x = abs(move_x) < .001 or self._try_axis_move(move_x, "x")
            moved_y = abs(move_y) < .001 or self._try_axis_move(move_y, "y")
            moved = moved_x and moved_y
            own_rect = self._world_rect()
            for enemy in cS.enemyHolster:
                if (enemy is self or isinstance(enemy, RammerEnemy)
                        or enemy in self.ramHitTargets):
                    continue
                if any(own_rect.colliderect(hitbox) for _, hitbox in enemy.get_world_hitboxes()):
                    self.ramHitTargets.add(enemy)
                    enemy.take_damage(self.damage * .38)
                    enemy.apply_knockback(cos(self.ramDirection) * self.size * .3,
                                          sin(self.ramDirection) * self.size * .3)
            if not moved or self.ramTimer <= 0:
                self.ramState, self.ramTimer = "stunned", vH.frameRate * 1.15
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        if self.ramState == "windup":
            end = (rect.centerx + cos(self.ramDirection) * vH.tileSizeGlobal * 6,
                   rect.centery + sin(self.ramDirection) * vH.tileSizeGlobal * 6)
            pygame.draw.line(screen, ui.RED, rect.center, end, 3)
        pygame.draw.polygon(screen, ui.CREAM,
                            (rect.midright, (rect.centerx, rect.y + 7),
                             (rect.centerx, rect.bottom - 7)))


class WarderEnemy(WanderingRangedEnemy):
    """Carries a directional shield that can protect nearby allies."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.shieldAngle = 0
        self.shieldHp = round(self.maxHp * (.35 + .2 * self.tierRank))
        self.maxShieldHp = self.shieldHp

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        self.shieldAngle = atan2(player_world_y - (self.worldY + self.size / 2),
                                 player_world_x - (self.worldX + self.size / 2))
        super().updateEnemy(player_world_x, player_world_y, projectile_sink)

    def _shield_rect(self, screen=False):
        x, y = (self.posX, self.posY) if screen else (self.worldX, self.worldY)
        center_x, center_y = x + self.size / 2, y + self.size / 2
        shield_size = self.size * (1.15 + .55 * (self.tierRank - 1))
        shield_x = center_x + cos(self.shieldAngle) * self.size * .58
        shield_y = center_y + sin(self.shieldAngle) * self.size * .58
        return pygame.Rect(shield_x - shield_size * .15, shield_y - shield_size / 2,
                           shield_size * .3, shield_size)

    def get_screen_hitboxes(self):
        hitboxes = super().get_screen_hitboxes()
        if self.shieldHp > 0:
            hitboxes.insert(0, ("shield", self._shield_rect(True)))
        return hitboxes

    def get_world_hitboxes(self):
        hitboxes = super().get_world_hitboxes()
        if self.shieldHp > 0:
            hitboxes.insert(0, ("shield", self._shield_rect(False)))
        return hitboxes

    def take_damage(self, amount, part_id="body"):
        if part_id == "shield" and self.shieldHp > 0:
            self.shieldHp -= amount
            return HitResult(True, False, amount, True)
        return super().take_damage(amount, part_id)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        if self.shieldHp > 0:
            shield = self._shield_rect(True)
            pygame.draw.rect(screen, ui.INK, shield.inflate(5, 5))
            pygame.draw.rect(screen, ui.BLUE, shield)
            pygame.draw.rect(screen, ui.CREAM, shield, 2)


class SplitterEnemy(WanderingRangedEnemy):
    """Fires predictable distance-triggered splitting projectiles."""

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x, center_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        shot = EnemyProjectile(
            center_x, center_y, atan2(player_world_y - center_y, player_world_x - center_x),
            .82 + .08 * self.tierRank, self.damage * .72, self.size * .38,
            travel_range=vH.tileSizeGlobal * 18, color=ui.PURPLE,
            shape="diamond", owner=f"splitter_{self.difficultyTier}",
        )
        shot.splitCount = self.tierRank + 1
        shot.splitAt = vH.tileSizeGlobal * (5.5 - .5 * self.tierRank)
        shot.splitGeneration = 1 if self.tierRank == 3 else 0
        projectile_sink.append(shot)
        self._mark_attack(.32)


class CollectorEnemy(Enemy):
    """Steals loose XP temporarily, grows, then returns it with a bonus."""

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.storedExperience = 0.0
        self.baseSize = self.size
        self.fleeThreshold = self.expValue * (1.2 + .4 * self.tierRank)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        self.age += vH.get_timer_step()
        if self.encounter is not None and not self.engagementAllowed:
            self._wander(.2)
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return
        center_x, center_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        for bubble in cS.experienceList[:]:
            bubble_rect = bubble._world_rect()
            distance = hypot(bubble_rect.centerx - center_x, bubble_rect.centery - center_y)
            if distance <= vH.tileSizeGlobal * (2.5 + self.tierRank):
                if distance <= self.size:
                    self.storedExperience += bubble.value
                    cS.experienceList.remove(bubble)
                else:
                    dx, dy, _ = _normalise(bubble_rect.centerx - center_x,
                                           bubble_rect.centery - center_y)
                    step = self.speed * .55 * vH.get_frame_scale()
                    self._try_axis_move(dx * step, "x")
                    self._try_axis_move(dy * step, "y")
                break
        else:
            dx, dy, _ = _normalise(player_world_x - center_x, player_world_y - center_y)
            direction = -1 if self.storedExperience >= self.fleeThreshold else 1
            step = self.speed * (.65 if direction < 0 else .28) * vH.get_frame_scale()
            self._try_axis_move(dx * step * direction, "x")
            self._try_axis_move(dy * step * direction, "y")
        self.size = min(self.baseSize * 1.55,
                        self.baseSize * (1 + self.storedExperience / max(1, self.fleeThreshold) * .3))
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def is_dead(self):
        if self.hp <= 0 and self.storedExperience:
            self.expValue += self.storedExperience * 1.15
            self.storedExperience = 0
        return self.hp <= 0

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.polygon(screen, ui.GREEN,
                            (rect.midtop, rect.midright, rect.midbottom, rect.midleft))


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
    max_level: int = 20
    progression_tier: str = "easy"


class EnemyCatalog:
    def __init__(self):
        self.definitions = {}

    def register(self, definition):
        if definition.key in self.definitions:
            raise ValueError(f"Enemy type already registered: {definition.key}")
        self.definitions[definition.key] = definition

    def available(self, level):
        return [definition for definition in self.definitions.values()
                if definition.min_level <= level <= definition.max_level]

    def choose(self, level, rng=None, max_threat=None, existing=()):
        rng = rng or random
        available = [definition for definition in self.available(level)
                     if not definition.guaranteed_only
                     and definition.family != "banner"]
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

    def definition_for_family(self, family, level):
        candidates = [definition for definition in self.available(level)
                      if definition.family == family and not definition.guaranteed_only]
        return max(candidates, key=lambda item: item.min_level) if candidates else None

    def _attach_encounter(self, key, group, anchor, level):
        encounter = RuntimeEncounter(key, group, anchor, level)
        for enemy in group:
            enemy.encounterKey = key
        return encounter

    def _expand_atomic_members(self, enemy, key):
        group = [enemy]
        if getattr(enemy, "atomicSpawnGroup", False):
            minions = list(enemy.spawnedEnemies)
            enemy.spawnedEnemies.clear()
            for minion in minions:
                minion.encounterKey = key
            group.extend(minions)
        return group

    def apply_modifier(self, enemy, level, rng=None, forced=None):
        rng = rng or random
        role = getattr(enemy, "combatRole", "pressure")
        eligible = [key for key, rule in MODIFIER_RULES.items()
                    if level >= rule["min_level"] and role in rule["roles"]]
        if not eligible or (forced is None and rng.random() > min(.28, .06 + level * .011)):
            return enemy
        modifier = forced or rng.choice(eligible)
        if modifier not in eligible:
            return enemy
        enemy.behaviorModifier = modifier
        enemy.modifierColor = MODIFIER_RULES[modifier]["color"]
        if modifier == "hasty":
            enemy.speed *= 1.18
            if hasattr(enemy, "attackCooldownMax"):
                enemy.attackCooldownMax *= .72
            enemy.maxHp = round(enemy.maxHp * .82)
            enemy.hp = enemy.maxHp
            enemy.expValue *= 1.2
        elif modifier == "armored":
            enemy.maxHp = round(enemy.maxHp * 1.75)
            enemy.hp = enemy.maxHp
            enemy.speed *= .82
            enemy.expValue *= 1.45
        elif modifier == "volatile":
            enemy.volatileBurst = 4 + enemy.tierRank * 2
            enemy.expValue *= 1.3
        elif modifier == "regenerating":
            enemy.regenerationRate = enemy.maxHp / (vH.frameRate * 14)
            enemy.expValue *= 1.35
        elif modifier == "champion":
            enemy.size *= 1.18
            enemy.maxHp = round(enemy.maxHp * 1.55)
            enemy.hp = enemy.maxHp
            enemy.damage *= 1.25
            enemy.threatCost *= 1.5
            enemy.expValue *= 2.0
        return enemy

    def create(self, key, world_x, world_y, level, rng=None):
        rng = rng or random
        definition = self.definitions[key]
        scales = enemy_stat_scales(level)
        tier = TIER_BALANCE[definition.progression_tier]
        variation = rng.uniform(.9, 1.12)
        difficulty = rng.uniform(.92, 1.25)
        size = vH.tileSizeGlobal * definition.size / variation
        options = dict(definition.options)
        options.setdefault("archetype", definition.key)
        if definition.enemy_class is not Enemy:
            options["rng"] = rng
        options["difficulty_tier"] = definition.progression_tier
        if definition.enemy_class is SnakeEnemy:
            options.pop("archetype", None)
            options["segment_count"] = 3 + tier["rank"] * 2
        enemy = definition.enemy_class(
            world_x, world_y,
            BASE_ENEMY_SPEED_SCALE * scales["speed"] * tier["speed"] * definition.speed * variation,
            size,
            definition.color,
            round(90 * scales["damage"] * tier["damage"] * definition.damage / variation),
            round(220 * scales["health"] * tier["health"] * definition.health / variation),
            2.4 * scales["experience"] * tier["experience"] * definition.experience * difficulty,
            difficulty,
            **options,
        )
        enemy.threatCost = definition.threat_cost * tier["threat"]
        enemy.family = definition.family
        enemy.spawnDefinitionKey = definition.key
        enemy.combatRole, tags = FAMILY_IDENTITIES.get(
            definition.family, ("pressure", set()))
        enemy.interactionTags = frozenset(tags)
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
        enemy = self.create(definition.key, spawn_rect.x, spawn_rect.y, level, rng)
        if key is None:
            self.apply_modifier(enemy, level, rng)
        return enemy

    def spawn_encounter(self, level, max_threat, existing=(), rng=None):
        """Build an atomic curated composition using the tier live at this level."""
        rng = rng or random
        active_keys = [getattr(enemy, "encounterKey", None) for enemy in existing]
        packages = [package for package in ENCOUNTER_PACKAGES
                    if package.min_level <= level <= package.max_level
                    and active_keys.count(package.key) < package.max_concurrent]
        if not packages:
            return None
        package = rng.choices(packages, weights=[item.weight for item in packages], k=1)[0]
        definitions = [self.definition_for_family(family, level)
                       for family in package.families]
        if any(definition is None for definition in definitions):
            return None
        estimated = sum(definition.threat_cost
                        * TIER_BALANCE[definition.progression_tier]["threat"]
                        for definition in definitions)
        if estimated > max_threat:
            return None
        anchor = bG.find_spawn_rect(vH.tileSizeGlobal * 1.2, 5)
        group = []
        for index, definition in enumerate(definitions):
            angle = index * 2 * pi / max(1, len(definitions))
            candidate = pygame.Rect(anchor.x + cos(angle) * vH.tileSizeGlobal * 1.8,
                                    anchor.y + sin(angle) * vH.tileSizeGlobal * 1.8,
                                    vH.tileSizeGlobal, vH.tileSizeGlobal)
            safe = bG.find_nearest_open_rect(candidate, vH.tileSizeGlobal)
            enemy = self.create(definition.key, safe.x, safe.y, level, rng)
            enemy.encounterKey = package.key
            if index == 0 and level >= 8:
                self.apply_modifier(enemy, level, rng)
            group.extend(self._expand_atomic_members(enemy, package.key))
        self._attach_encounter(package.key, group, anchor.center, level)
        return package, group

    def spawn_patrol(self, level, max_threat, existing=(), rng=None):
        """Compose a coherent ambient encounter instead of loose random bodies."""
        rng = rng or random
        available = [definition for definition in self.available(level)
                     if not definition.guaranteed_only
                     and definition.family != "banner"]
        if not available:
            return None
        target_size = encounter_pacing(level)["patrol_size"]
        primary = self.choose(level, rng, max_threat, existing)
        if primary is None:
            return None
        role_pairs = {
            "pressure": ("tank", "artillery", "support"),
            "tank": ("pressure", "artillery"),
            "artillery": ("pressure", "tank", "support"),
            "control": ("pressure", "tank"),
            "support": ("pressure", "artillery"),
            "squad": ("control", "artillery"),
            "economy": ("tank", "pressure"),
        }
        primary_role = FAMILY_IDENTITIES.get(primary.family, ("pressure", set()))[0]
        preferred = role_pairs.get(primary_role, ("pressure",))
        definitions = [primary]
        for _ in range(target_size - 1):
            candidates = [definition for definition in available
                          if FAMILY_IDENTITIES.get(definition.family, ("pressure", set()))[0]
                          in preferred]
            if not candidates:
                candidates = available
            definitions.append(rng.choices(
                candidates, weights=[definition.weight for definition in candidates], k=1)[0])
        estimated = sum(definition.threat_cost
                        * TIER_BALANCE[definition.progression_tier]["threat"]
                        for definition in definitions)
        while len(definitions) > 1 and estimated > max_threat:
            removed = definitions.pop()
            estimated -= (removed.threat_cost
                          * TIER_BALANCE[removed.progression_tier]["threat"])
        if estimated > max_threat:
            return None

        anchor = bG.find_spawn_rect(vH.tileSizeGlobal * 1.1, 5)
        key = f"patrol_{RuntimeEncounter._next_id}"
        group = []
        for index, definition in enumerate(definitions):
            angle = index * 2 * pi / max(1, len(definitions))
            safe = bG.find_nearest_open_rect(pygame.Rect(
                anchor.centerx + cos(angle) * vH.tileSizeGlobal * 1.4,
                anchor.centery + sin(angle) * vH.tileSizeGlobal * 1.4,
                vH.tileSizeGlobal, vH.tileSizeGlobal), vH.tileSizeGlobal)
            enemy = self.create(definition.key, safe.x, safe.y, level, rng)
            self.apply_modifier(enemy, level, rng)
            group.extend(self._expand_atomic_members(enemy, key))
        if len(group) + len(existing) > 60:
            return None
        actual_threat = sum(enemy.threatCost for enemy in group)
        if actual_threat > max_threat:
            return None
        encounter = self._attach_encounter(key, group, anchor.center, level)
        return encounter, group


ENEMY_CATALOG = EnemyCatalog()


def _tier_color(color, rank):
    color = pygame.Color(color)
    if rank == 1:
        return color
    amount = 20 if rank == 2 else 38
    return pygame.Color(min(255, color.r + amount),
                        min(255, color.g + amount // 2),
                        min(255, color.b + amount))


def _tiered_family(key, enemy_class, weight, speed, size, damage, health,
                   experience, color, gates, threat_cost=1.0, max_active=99,
                   options=None):
    """Build compatible easy/medium/hard definitions for one enemy family."""
    definitions = []
    for rank, (tier, suffix, gate) in enumerate(zip(
            ("easy", "medium", "hard"), ("", "_medium", "_hard"), gates), 1):
        definitions.append(EnemyDefinition(
            f"{key}{suffix}", enemy_class,
            weight * (1.0 if rank == 1 else .72 if rank == 2 else .48),
            gate[0], speed, size * (1 + .08 * (rank - 1)), damage, health,
            experience, _tier_color(color, rank),
            dict(options or {}, archetype=key),
            threat_cost=threat_cost, family=key, max_active=max_active,
            max_level=gate[1], progression_tier=tier,
        ))
    return definitions


def _register_defaults():
    entries = []
    entries += _tiered_family("runner", Enemy, 22, 1.42, .58, .72, .62, .8,
                              pygame.Color(221, 76, 73), ((0, 6), (4, 13), (10, 20)))
    entries += _tiered_family("drifter", Enemy, 30, 1.0, .76, 1.0, 1.0, 1.0,
                              pygame.Color(184, 66, 75), ((0, 7), (4, 14), (10, 20)))
    entries += _tiered_family("skirmisher", Enemy, 18, 1.08, .82, .92, 1.18, 1.3,
                              pygame.Color(68, 151, 142), ((0, 8), (4, 14), (10, 20)))
    entries += _tiered_family("bulwark", Enemy, 12, .58, 1.18, 1.52, 2.65, 2.1,
                              pygame.Color(200, 132, 56), ((1, 8), (5, 14), (11, 20)))
    entries += _tiered_family("ranged_wanderer", WanderingRangedEnemy, 10, .62, .82,
                              .85, 1.45, 1.7, pygame.Color(82, 126, 190),
                              ((0, 8), (4, 14), (10, 20)), 1.2, 6)
    entries += _tiered_family("shotgunner", ShotgunEnemy, 5, .56, .96, 1.0, 2.0, 2.4,
                              pygame.Color(188, 112, 61),
                              ((3, 9), (6, 14), (11, 20)), 1.5, 5)
    entries += _tiered_family("snake", SnakeEnemy, 3, .92, .86, 1.15, 2.15, 5.2,
                              pygame.Color(142, 83, 184),
                              ((5, 9), (8, 15), (12, 20)), 2.5, 3)
    entries += _tiered_family("parent", ParentEnemy, 4, .54, 1.42, 1.15, 4.1, 5.8,
                              pygame.Color(126, 67, 146),
                              ((4, 9), (7, 15), (12, 20)), 3.0, 2)
    entries += _tiered_family("pillar", PillarEnemy, 4, 0, 1.12, 1.0, 3.2, 4.0,
                              pygame.Color(89, 103, 126),
                              ((4, 9), (7, 15), (12, 20)), 4.0, 2)
    entries += _tiered_family("banner", BannerCaptain, 5, .68, 1.05, .9, 2.6, 3.8,
                              pygame.Color(176, 78, 70),
                              ((2, 8), (6, 14), (11, 20)), 3.0, 2)
    entries += _tiered_family("rammer", RammerEnemy, 5, .74, 1.08, 1.35, 2.5, 3.2,
                              pygame.Color(169, 91, 58),
                              ((3, 8), (6, 14), (11, 20)), 2.5, 3)
    entries += _tiered_family("warder", WarderEnemy, 4, .48, 1.0, .72, 2.5, 3.4,
                              pygame.Color(64, 112, 158),
                              ((4, 9), (7, 14), (11, 20)), 2.8, 3)
    entries += _tiered_family("splitter", SplitterEnemy, 5, .55, .86, .88, 1.8, 2.6,
                              pygame.Color(126, 73, 166),
                              ((3, 8), (6, 14), (10, 20)), 2.0, 4)
    entries += _tiered_family("collector", CollectorEnemy, 3, .82, .72, .55, 1.6, 1.4,
                              pygame.Color(64, 158, 92),
                              ((2, 8), (6, 14), (11, 20)), 1.8, 2)
    entries += [
        EnemyDefinition("volley_small", VolleyEnemy, 8, 0, .70, .74, .82, 1.2, 1.6,
                        pygame.Color(201, 139, 55), {"tier": "small", "archetype": "volley"},
                        threat_cost=1.5, family="volley", max_active=5,
                        max_level=8, progression_tier="easy"),
        EnemyDefinition("volley_medium", VolleyEnemy, 5, 6, .55, .98, 1.05, 2.3, 3.0,
                        pygame.Color(211, 132, 67), {"tier": "medium", "archetype": "volley"},
                        threat_cost=2.0, family="volley", max_active=5,
                        max_level=14, progression_tier="medium"),
        EnemyDefinition("volley_large", VolleyEnemy, 2, 12, .38, 1.32, 1.3, 4.3, 5.3,
                        pygame.Color(202, 108, 85), {"tier": "large", "archetype": "volley"},
                        threat_cost=2.25, family="volley", max_active=5,
                        max_level=20, progression_tier="hard"),
        EnemyDefinition("laser_small", LaserEnemy, 7, 2, .58, .72, .85, 1.25, 1.8,
                        pygame.Color(174, 63, 77), {"tier": "small", "archetype": "laser"},
                        threat_cost=1.5, family="laser", max_active=2,
                        max_level=8, progression_tier="easy"),
        EnemyDefinition("laser_medium", LaserEnemy, 4, 7, .43, .98, 1.05, 2.4, 3.2,
                        pygame.Color(178, 72, 104), {"tier": "medium", "archetype": "laser"},
                        threat_cost=2.0, family="laser", max_active=2,
                        max_level=14, progression_tier="medium"),
        EnemyDefinition("laser_large", LaserEnemy, 2, 13, .28, 1.28, 1.25, 4.5, 5.4,
                        pygame.Color(164, 72, 122), {"tier": "large", "archetype": "laser"},
                        threat_cost=2.25, family="laser", max_active=2,
                        max_level=20, progression_tier="hard"),
        EnemyDefinition("bomb_small", BombEnemy, 7, 2, .72, .7, .9, 1.3, 1.8,
                        pygame.Color(190, 147, 57), {"tier": "small", "archetype": "bomb"},
                        threat_cost=1.5, family="bomb", max_active=4,
                        max_level=8, progression_tier="easy"),
        EnemyDefinition("bomb_medium", BombEnemy, 4, 8, .54, .96, 1.08, 2.5, 3.2,
                        pygame.Color(200, 132, 72), {"tier": "medium", "archetype": "bomb"},
                        threat_cost=2.0, family="bomb", max_active=4,
                        max_level=14, progression_tier="medium"),
        EnemyDefinition("bomb_large", BombEnemy, 2, 14, .34, 1.3, 1.3, 4.6, 5.5,
                        pygame.Color(194, 116, 87), {"tier": "large", "archetype": "bomb"},
                        threat_cost=2.25, family="bomb", max_active=4,
                        max_level=20, progression_tier="hard"),
        EnemyDefinition("miniboss_arsenal", ArsenalMiniBoss, .7, 5, .34, 1.75, 1.45, 10.0, 15.0,
                        pygame.Color(84, 72, 118),
                        {"phase_order": ("volley", "laser", "bomb")},
                        threat_cost=12.0, family="miniboss", max_active=1,
                        guaranteed_only=True, max_level=20, progression_tier="medium"),
        EnemyDefinition("miniboss_siege", ArsenalMiniBoss, .7, 15, .3, 1.85, 1.55, 11.0, 16.0,
                        pygame.Color(78, 91, 112),
                        {"phase_order": ("bomb", "volley", "laser")},
                        threat_cost=13.0, family="miniboss", max_active=1,
                        guaranteed_only=True, max_level=20, progression_tier="hard"),
    ]
    for definition in entries:
        ENEMY_CATALOG.register(definition)


_register_defaults()
