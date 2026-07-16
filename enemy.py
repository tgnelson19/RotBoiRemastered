"""Base world-space enemy entity and shared combat contract."""

from dataclasses import dataclass
from math import cos, hypot, pi, sin
import random

import pygame

import background as bG
import uiTheme as ui
import variableHolster as vH


@dataclass(frozen=True)
class HitResult:
    applied: bool
    killed: bool
    amount: float = 0
    blocked: bool = False


class Enemy:
    def __init__(self, world_x, world_y, speed, size, color, damage, hp,
                 exp_value, difficulty, archetype="drifter", difficulty_tier="easy"):
        self.worldX = float(world_x)
        self.worldY = float(world_y)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        self.speed = speed
        self.size = size
        self.color = color
        self.damage = damage
        self.hp = hp
        self.maxHp = hp
        self.cantTouchMeList = []
        self.expValue = exp_value
        self.difficulty = difficulty
        self.archetype = archetype
        self.difficultyTier = difficulty_tier
        self.tierRank = {"easy": 1, "medium": 2, "hard": 3}.get(difficulty_tier, 1)
        self.age = 0.0
        self.awarenessState = "wandering"
        self.awarenessRange = vH.sH * .5
        self.disengageRange = self.awarenessRange * 1.25
        self.wanderAngle = random.uniform(-pi, pi)
        self.wanderTimer = random.randint(55, 135)
        self.threatCost = 1.0
        self.spawnedEnemies = []
        self.engagementAllowed = True
        self.combatRole = "pressure"
        self.interactionTags = frozenset()
        self.behaviorModifier = None
        self.modifierColor = None
        self.regenerationRate = 0.0
        self.volatileBurst = 0
        self.visualAttackTimer = 0.0
        self.visualAttackCooldown = random.uniform(.7, 1.4) * vH.frameRate
        self._lastVisualWorld = (self.worldX, self.worldY)
        self.encounter = None
        self.encounterSlot = 0
        self.encounterPatrolTarget = None
        self.encounterCombatTarget = None
        self.combatSide = 0

    def _mark_attack(self, duration=.22):
        self.visualAttackTimer = max(self.visualAttackTimer, vH.frameRate * duration)

    def drawEnemy(self, screen):
        self.visualAttackTimer = max(0, self.visualAttackTimer - vH.get_timer_step())
        moved = hypot(self.worldX - self._lastVisualWorld[0],
                      self.worldY - self._lastVisualWorld[1]) > .02
        self._lastVisualWorld = (self.worldX, self.worldY)
        walk = sin(self.age * (.16 + self.tierRank * .018)) if moved else 0.0
        bob = int(abs(walk) * min(4, self.size * .055))
        squash = abs(walk) * .045 if moved else 0.0
        if self.visualAttackTimer > 0:
            attack_progress = self.visualAttackTimer / max(1, vH.frameRate * .22)
            squash -= sin(min(1, attack_progress) * pi) * .12
        rect = pygame.Rect(0, 0, self.size * (1 + squash), self.size * (1 - squash))
        rect.midbottom = (self.posX + self.size / 2, self.posY + self.size - bob)
        pygame.draw.rect(screen, ui.SHADOW, rect.move(4, 4))
        pygame.draw.rect(screen, self.color, rect)
        border_width = max(2, int(self.size * (.1 if self.archetype == "bulwark" else .07)))
        pygame.draw.rect(screen, ui.INK, rect, border_width)
        if self.behaviorModifier:
            pip = max(4, int(self.size * .11))
            pygame.draw.rect(screen, ui.INK,
                             (rect.right - pip - 3, rect.y + 3, pip + 2, pip + 2))
            pygame.draw.rect(screen, self.modifierColor,
                             (rect.right - pip - 2, rect.y + 4, pip, pip))

        if self.archetype == "runner":
            pygame.draw.polygon(screen, ui.lighten(self.color, 55), (
                (rect.centerx, rect.y + rect.height * .2),
                (rect.right - rect.width * .2, rect.centery),
                (rect.centerx, rect.bottom - rect.height * .2),
                (rect.x + rect.width * .2, rect.centery),
            ))
        elif self.archetype == "bulwark":
            pygame.draw.rect(screen, ui.lighten(self.color, 38), rect.inflate(-int(self.size * .34), -int(self.size * .34)), 3)
        elif self.archetype == "skirmisher":
            pygame.draw.line(screen, ui.lighten(self.color, 60), rect.midtop, rect.midbottom, max(2, int(self.size * .08)))
            pygame.draw.line(screen, ui.lighten(self.color, 60), rect.midleft, rect.midright, max(2, int(self.size * .08)))
        else:
            pygame.draw.rect(screen, ui.lighten(self.color, 42), rect.inflate(-int(self.size * .48), -int(self.size * .48)))

        if self.hp < self.maxHp:
            bar = pygame.Rect(rect.x, rect.y - 9, rect.width, 5)
            pygame.draw.rect(screen, ui.INK, bar)
            fill = bar.copy()
            fill.width = int(bar.width * max(0, self.hp / self.maxHp))
            pygame.draw.rect(screen, ui.RED, fill)

    def _world_rect(self, x=None, y=None):
        return pygame.Rect(
            self.worldX if x is None else x,
            self.worldY if y is None else y,
            self.size,
            self.size,
        )

    def _try_axis_move(self, amount, axis):
        if amount == 0:
            return False
        next_x = self.worldX + amount if axis == "x" else self.worldX
        next_y = self.worldY + amount if axis == "y" else self.worldY
        candidate = self._world_rect(next_x, next_y)
        if bG.rect_hits_wall(candidate):
            return False
        self.worldX, self.worldY = next_x, next_y
        return True

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        """Move toward the player while retaining motion parallel to solid walls."""
        self.age += vH.get_timer_step()
        timer_step = vH.get_timer_step()
        self.visualAttackCooldown -= timer_step
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        delta_x = player_world_x - center_x
        delta_y = player_world_y - center_y
        distance = max(1.0, hypot(delta_x, delta_y))
        direction_x = delta_x / distance
        direction_y = delta_y / distance

        if not self._update_awareness(distance):
            if self.regenerationRate and self.hp < self.maxHp:
                self.hp = min(self.maxHp, self.hp + self.regenerationRate * timer_step)
            self._wander()
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            return

        # Skirmishers weave in open ground, producing a distinct approach without
        # changing collision behavior at walls.
        if self.archetype == "skirmisher":
            weave = sin(self.age * .055) * .42
            direction_x, direction_y = direction_x - direction_y * weave, direction_y + direction_x * weave
            direction_length = max(1.0, hypot(direction_x, direction_y))
            direction_x /= direction_length
            direction_y /= direction_length
        elif self.encounter is not None and self.combatRole == "pressure":
            flank = self.combatSide * (.12 + .04 * self.tierRank)
            direction_x, direction_y = (direction_x - direction_y * flank,
                                        direction_y + direction_x * flank)
            direction_length = max(1.0, hypot(direction_x, direction_y))
            direction_x /= direction_length
            direction_y /= direction_length
        elif (self.encounterCombatTarget is not None
              and self.combatRole in ("tank", "support")):
            target_dx = self.encounterCombatTarget[0] - center_x
            target_dy = self.encounterCombatTarget[1] - center_y
            target_distance = hypot(target_dx, target_dy)
            if target_distance > vH.tileSizeGlobal * .65:
                direction_x, direction_y = target_dx / target_distance, target_dy / target_distance

        lunge = 1.0
        if self.tierRank > 1 and distance <= vH.tileSizeGlobal * 4:
            if self.visualAttackCooldown <= 0:
                self._mark_attack(.28)
                self.visualAttackCooldown = vH.frameRate * (2.8 if self.tierRank == 2 else 2.0)
            if self.visualAttackTimer > 0:
                lunge += .22 * (self.tierRank - 1)
        step = self.speed * lunge * vH.get_frame_scale()
        move_x = direction_x * step
        move_y = direction_y * step

        # Axis separation is the important behavior change: a blocked perpendicular
        # component is discarded while the wall-parallel component proceeds in full.
        # There are no partial retries to flip between on consecutive frames.
        self._try_axis_move(move_x, "x")
        self._try_axis_move(move_y, "y")

        if bG.rect_hits_wall(self._world_rect()):
            safe_rect = bG.find_nearest_open_rect(self._world_rect(), self.size)
            self.worldX, self.worldY = safe_rect.x, safe_rect.y

        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _update_awareness(self, distance):
        """Update the shared wander/alert/disengage state with hysteresis."""
        if not self.engagementAllowed:
            self.awarenessState = "wandering"
            return False
        if self.awarenessState == "wandering":
            if distance <= self.awarenessRange:
                self.awarenessState = "alerted"
        elif distance > self.disengageRange:
            self.awarenessState = "wandering"
        elif distance > self.awarenessRange:
            self.awarenessState = "disengaging"
        else:
            self.awarenessState = "alerted"
        return self.awarenessState != "wandering"

    def _wander(self, speed_multiplier=.2):
        """Low-cost MMO-style roaming shared by otherwise simple enemies."""
        if self.encounterPatrolTarget is not None:
            target_x, target_y = self.encounterPatrolTarget
            dx = target_x - (self.worldX + self.size / 2)
            dy = target_y - (self.worldY + self.size / 2)
            distance = hypot(dx, dy)
            if distance > self.size * .35:
                step = self.speed * speed_multiplier * vH.get_frame_scale()
                self._try_axis_move(dx / distance * step, "x")
                self._try_axis_move(dy / distance * step, "y")
                return
        self.wanderTimer -= vH.get_timer_step()
        if self.wanderTimer <= 0:
            self.wanderAngle += random.uniform(-1.35, 1.35)
            self.wanderTimer = random.randint(55, 135)
        step = self.speed * speed_multiplier * vH.get_frame_scale()
        moved_x = self._try_axis_move(cos(self.wanderAngle) * step, "x")
        moved_y = self._try_axis_move(sin(self.wanderAngle) * step, "y")
        if not moved_x or not moved_y:
            self.wanderAngle += random.uniform(.75, 2.2)

    def get_screen_hitboxes(self):
        return [("body", pygame.Rect(self.posX, self.posY, self.size, self.size))]

    def get_world_hitboxes(self):
        return [("body", self._world_rect())]

    def take_damage(self, amount, part_id="body"):
        self.hp -= amount
        return HitResult(True, self.hp <= 0, amount)

    def is_dead(self):
        return self.hp <= 0

    def apply_knockback(self, delta_x, delta_y):
        self._try_axis_move(delta_x, "x")
        self._try_axis_move(delta_y, "y")
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
