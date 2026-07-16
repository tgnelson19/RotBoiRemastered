"""Projectiles fired by enemies, including reusable boss path primitives."""

from math import cos, pi, sin

import pygame

import background as bG
import uiTheme as ui
import variableHolster as vH


class EnemyProjectile:
    HOSTILE_SPEED_SCALE = .52
    DISSONANCE_DAMAGE_SCALE = 1.3
    def __init__(self, world_x, world_y, direction, speed, damage, size,
                 travel_range=900, color=None, shape="square", path="linear",
                 amplitude=0, frequency=.035, lifetime=None, speed_decay=0,
                 orbit_center=None, orbit_radius=0, orbit_angle=0,
                 angular_speed=0, owner=None, ignore_walls=False, target=None):
        self.worldX = float(world_x)
        self.worldY = float(world_y)
        self.originX = float(world_x)
        self.originY = float(world_y)
        self.direction = direction
        self.speed = speed
        self.damage = (damage * self.DISSONANCE_DAMAGE_SCALE
                       if str(owner).startswith("dissonance") else damage)
        self.size = size
        self.remainingRange = travel_range
        self.color = color or ui.RED
        self.shape = shape
        self.path = path
        self.amplitude = amplitude
        self.frequency = frequency
        self.lifetime = lifetime
        self.speedDecay = speed_decay
        self.orbitCenter = orbit_center
        self.orbitRadius = orbit_radius
        self.orbitAngle = orbit_angle
        self.angularSpeed = angular_speed
        self.owner = owner
        # Dissonance bullets should paint complete lanes across the final arena.
        # Mines retain their deliberately local range and orbit fields retain lifetime rules.
        if (str(owner).startswith("dissonance") and path not in ("mine", "orbit")
                and lifetime is None):
            self.remainingRange = max(self.remainingRange, vH.tileSizeGlobal * 72)
        if "survival" in str(owner) or "boundary_inward" in str(owner):
            self.remainingRange = float("inf")
        self.ignoreWalls = ignore_walls
        self.target = target
        self.telegraphDuration = 1.0
        self.fuseDuration = 3.0
        self.blastRadius = vH.tileSizeGlobal * 1.5
        self.burstCount = 8
        self.burstDamage = self.damage
        self.spawnedProjectiles = []
        self.persistentHazard = path == "laser"
        self.exploded = False
        self.age = 0.0
        self.travelled = 0.0
        self.remFlag = False
        self.trail = []
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def world_rect(self):
        if self.path == "laser" and self.age >= self.telegraphDuration:
            end_x = self.worldX + cos(self.direction) * self.remainingRange
            end_y = self.worldY + sin(self.direction) * self.remainingRange
            return pygame.Rect(min(self.worldX, end_x), min(self.worldY, end_y),
                               max(self.size, abs(end_x - self.worldX)),
                               max(self.size, abs(end_y - self.worldY)))
        return pygame.Rect(self.worldX, self.worldY, self.size, self.size)

    def collides(self, rect):
        if self.path == "laser":
            if self.age < self.telegraphDuration:
                return False
            start = (self.worldX, self.worldY)
            end = (self.worldX + cos(self.direction) * self.remainingRange,
                   self.worldY + sin(self.direction) * self.remainingRange)
            return bool(rect.inflate(self.size, self.size).clipline(start, end))
        if self.path == "bomb":
            if not self.exploded:
                return False
            center_x = self.worldX + self.size / 2
            center_y = self.worldY + self.size / 2
            nearest_x = max(rect.left, min(center_x, rect.right))
            nearest_y = max(rect.top, min(center_y, rect.bottom))
            return (nearest_x - center_x) ** 2 + (nearest_y - center_y) ** 2 <= self.blastRadius ** 2
        return rect.colliderect(self.world_rect())

    def updateAndDraw(self, screen):
        seconds = vH.get_timer_step() / max(1, vH.frameRate)
        self.age += seconds

        if self.path == "laser":
            if self.age >= self.telegraphDuration and self.angularSpeed:
                self.direction += self.angularSpeed * seconds
            start = bG.world_to_screen(self.worldX, self.worldY)
            end_world = (self.worldX + cos(self.direction) * self.remainingRange,
                         self.worldY + sin(self.direction) * self.remainingRange)
            end = bG.world_to_screen(*end_world)
            if self.age < self.telegraphDuration:
                progress = self.age / max(.01, self.telegraphDuration)
                pulse = 2 + int((1 - progress) * 3)
                pygame.draw.line(screen, self.color, start, end, pulse)
                for step in range(5):
                    marker = ((start[0] * (4-step) + end[0] * step) / 4,
                              (start[1] * (4-step) + end[1] * step) / 4)
                    pygame.draw.circle(screen, ui.CREAM, marker, 3)
            else:
                width = max(8, int(self.size * (1.15 + .18 * sin(self.age * 18))))
                pygame.draw.line(screen, ui.INK, start, end, width + 8)
                pygame.draw.line(screen, self.color, start, end, width)
                pygame.draw.line(screen, ui.CREAM, start, end, max(2, width // 3))
            expired = self.lifetime is not None and self.age >= self.lifetime
            if expired:
                self.remFlag = True
            return
        if self.path == "bomb":
            if self.age < 1.0 and self.target:
                progress = min(1.0, self.age)
                self.worldX = self.originX + (self.target[0] - self.originX) * progress
                self.worldY = (self.originY + (self.target[1] - self.originY) * progress
                               - sin(progress * pi) * vH.tileSizeGlobal * 2.5)
            elif self.age >= self.fuseDuration and not self.exploded:
                self.exploded = True
                for index in range(self.burstCount):
                    self.spawnedProjectiles.append(EnemyProjectile(
                        self.worldX, self.worldY, index * 2 * pi / max(1, self.burstCount), .9,
                        self.burstDamage * .28,
                        vH.tileSizeGlobal * .38, travel_range=vH.tileSizeGlobal * 24,
                        color=self.color, shape="diamond", owner=f"{self.owner}_burst",
                        ignore_walls=True,
                    ))
            elif self.exploded and self.age >= self.fuseDuration + .18:
                self.remFlag = True
        elif self.path == "orbit" and self.orbitCenter:
            self.orbitAngle += self.angularSpeed * seconds
            center_x, center_y = self.orbitCenter
            self.worldX = center_x + cos(self.orbitAngle) * self.orbitRadius - self.size / 2
            self.worldY = center_y + sin(self.orbitAngle) * self.orbitRadius - self.size / 2
        else:
            distance = self.speed * self.HOSTILE_SPEED_SCALE * vH.get_frame_scale()
            self.travelled += distance
            self.remainingRange -= distance
            if self.path == "sine":
                lateral = sin(self.travelled * self.frequency) * self.amplitude
                self.worldX = self.originX + cos(self.direction) * self.travelled - sin(self.direction) * lateral
                self.worldY = self.originY + sin(self.direction) * self.travelled + cos(self.direction) * lateral
            else:
                self.worldX += cos(self.direction) * distance
                self.worldY += sin(self.direction) * distance
            if self.speedDecay:
                self.speed = max(0, self.speed - self.speedDecay * seconds)
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        self.trail.append((self.posX + self.size / 2, self.posY + self.size / 2))
        self.trail = self.trail[-5:]

        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        if len(self.trail) > 1:
            for index, point in enumerate(self.trail[:-1]):
                trail_size = max(2, int(self.size * (index + 1) / len(self.trail) * .22))
                pygame.draw.rect(screen, ui.INK,
                                 (point[0] - trail_size / 2, point[1] - trail_size / 2,
                                  trail_size, trail_size))
                if index >= len(self.trail) - 3:
                    core_size = max(1, trail_size // 2)
                    pygame.draw.rect(screen, self.color,
                                     (point[0] - core_size / 2, point[1] - core_size / 2,
                                      core_size, core_size))
        if self.shape in ("diamond", "mine", "bomb"):
            points = (rect.midtop, rect.midright, rect.midbottom, rect.midleft)
            shadow_points = tuple((x + 3, y + 3) for x, y in points)
            pygame.draw.polygon(screen, ui.SHADOW, shadow_points)
            pygame.draw.polygon(screen, self.color, points)
            pygame.draw.polygon(screen, ui.INK, points, max(2, int(self.size * .1)))
            if self.shape == "mine":
                pulse = max(3, int(self.size * (.12 + .05 * (1 + sin(self.age * 5)))))
                pygame.draw.rect(screen, ui.TEXT, (rect.centerx - pulse / 2, rect.centery - pulse / 2, pulse, pulse))
            elif self.shape == "bomb":
                fuse = max(0, self.fuseDuration - self.age)
                pygame.draw.circle(screen, ui.CREAM, rect.center,
                                   max(3, int(self.size * (.1 + .04 * sin(self.age * 14)))))
                if self.age >= 1.0:
                    warning = pygame.Rect(0, 0, self.blastRadius * 2, self.blastRadius * 2)
                    warning.center = rect.center
                    urgency = 1 - fuse / max(.01, self.fuseDuration - 1.0)
                    pygame.draw.ellipse(screen, ui.RED, warning, max(2, int(2 + urgency * 3)))
                    pygame.draw.arc(screen, ui.CREAM, rect.inflate(8, 8), -pi / 2,
                                    -pi / 2 + 2 * pi * max(0, urgency), 3)
                if self.exploded:
                    blast = pygame.Rect(0, 0, self.blastRadius * 2, self.blastRadius * 2)
                    blast.center = rect.center
                    pygame.draw.ellipse(screen, ui.GOLD, blast, max(5, int(self.size * .2)))
        else:
            pygame.draw.rect(screen, ui.SHADOW, rect.move(3, 3))
            pygame.draw.rect(screen, self.color, rect)
            pygame.draw.rect(screen, ui.INK, rect, max(2, int(self.size * .1)))
            pygame.draw.rect(screen, ui.lighten(self.color, 45), rect.inflate(-int(self.size * .5), -int(self.size * .5)))

        expired = self.lifetime is not None and self.age >= self.lifetime
        range_spent = self.path != "orbit" and self.remainingRange <= 0
        wall_hit = not self.ignoreWalls and bG.rect_hits_wall(self.world_rect())
        if expired or range_spent or wall_hit:
            self.remFlag = True
