"""Base world-space enemy entity and shared combat contract."""

from dataclasses import dataclass
from math import hypot, sin

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
                 exp_value, difficulty, archetype="drifter"):
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
        self.age = 0.0

    def drawEnemy(self, screen):
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.SHADOW, rect.move(4, 4))
        pygame.draw.rect(screen, self.color, rect)
        border_width = max(2, int(self.size * (.1 if self.archetype == "bulwark" else .07)))
        pygame.draw.rect(screen, ui.INK, rect, border_width)

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
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        delta_x = player_world_x - center_x
        delta_y = player_world_y - center_y
        distance = max(1.0, hypot(delta_x, delta_y))
        direction_x = delta_x / distance
        direction_y = delta_y / distance

        # Skirmishers weave in open ground, producing a distinct approach without
        # changing collision behavior at walls.
        if self.archetype == "skirmisher":
            weave = sin(self.age * .055) * .42
            direction_x, direction_y = direction_x - direction_y * weave, direction_y + direction_x * weave
            direction_length = max(1.0, hypot(direction_x, direction_y))
            direction_x /= direction_length
            direction_y /= direction_length

        step = self.speed * vH.get_frame_scale()
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
