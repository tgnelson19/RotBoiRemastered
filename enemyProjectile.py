"""Projectiles fired by enemies, including reusable boss path primitives."""

from math import ceil, cos, hypot, pi, sin

import pygame

import background as bG
import uiTheme as ui
import variableHolster as vH
import gameProfile


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
        boss_scale = 100 if str(owner).startswith(("beaudis", "dissonance")) else 1
        dissonance_scale = self.DISSONANCE_DAMAGE_SCALE if str(owner).startswith("dissonance") else 1
        self.damage = round(damage * boss_scale * dissonance_scale)
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
        self.illusory = False
        self.truthMarked = False
        self.beliefGain = 0.0
        self.clarityGain = 0.0
        # Dissonance bullets should paint complete lanes across the final arena.
        # Mines retain their deliberately local range and orbit fields retain lifetime rules.
        if (str(owner).startswith("dissonance") and path not in ("mine", "orbit")
                and lifetime is None):
            self.remainingRange = max(self.remainingRange, vH.tileSizeGlobal * 72)
        # Malady's moving shots belong to the arena, not to an arbitrary range
        # budget. Character projectile handling removes them at the boss court's
        # boundary, so they remain threatening until they visibly leave it.
        if (str(owner).startswith("malady_phantasia")
                and path not in ("pool", "bomb", "orbit", "laser")):
            self.remainingRange = float("inf")
        # Rot's deliberately sluggish volleys still belong to the whole arena.
        # Their range ends at the encounter boundary, not after an arbitrary
        # number of tiles; stationary sludge and orbiting clots keep their own
        # lifetime rules.
        if (str(owner).startswith("rot_touch")
                and path not in ("pool", "bomb", "orbit", "laser", "mine")):
            self.remainingRange = float("inf")
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
        self.splitCount = 0
        self.splitAt = None
        self.splitGeneration = 0
        self.persistentHazard = path == "laser"
        self.exploded = False
        self.age = 0.0
        self.travelled = 0.0
        self.remFlag = False
        self.trail = []
        self.lightningTravel = 0.0
        self.lightningPoints = self._build_lightning_points() if path == "lightning" else []
        if path == "lightning":
            self.persistentHazard = True
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _build_lightning_points(self):
        """Build a stable, alternating convex beam instead of frame-random noise."""
        segment = max(vH.tileSizeGlobal * 1.15, self.size * 3.2)
        count = max(4, int(ceil(self.remainingRange / segment)))
        along_x, along_y = cos(self.direction), sin(self.direction)
        side_x, side_y = -along_y, along_x
        points = [(self.worldX, self.worldY)]
        for index in range(1, count + 1):
            distance = min(self.remainingRange, index * segment)
            bend = 0.0
            if index < count:
                bend = ((-1 if index % 2 else 1) * vH.tileSizeGlobal
                        * (.48 + .22 * sin(index * 1.73 + self.direction)))
            points.append((self.worldX + along_x * distance + side_x * bend,
                           self.worldY + along_y * distance + side_y * bend))
        return points

    def _lightning_visible_points(self, distance=None):
        if not self.lightningPoints:
            return []
        budget = self.lightningTravel if distance is None else distance
        visible = [self.lightningPoints[0]]
        for start, end in zip(self.lightningPoints, self.lightningPoints[1:]):
            length = hypot(end[0] - start[0], end[1] - start[1])
            if budget >= length:
                visible.append(end)
                budget -= length
                continue
            if budget > 0:
                fraction = budget / max(1e-6, length)
                visible.append((start[0] + (end[0] - start[0]) * fraction,
                                start[1] + (end[1] - start[1]) * fraction))
            break
        return visible

    def world_rect(self):
        if self.path == "lightning" and self.age >= self.telegraphDuration:
            points = self._lightning_visible_points()
            if len(points) < 2:
                return pygame.Rect(self.worldX, self.worldY, self.size, self.size)
            xs, ys = [point[0] for point in points], [point[1] for point in points]
            return pygame.Rect(min(xs), min(ys), max(self.size, max(xs) - min(xs)),
                               max(self.size, max(ys) - min(ys)))
        if self.path == "laser" and self.age >= self.telegraphDuration:
            end_x = self.worldX + cos(self.direction) * self.remainingRange
            end_y = self.worldY + sin(self.direction) * self.remainingRange
            return pygame.Rect(min(self.worldX, end_x), min(self.worldY, end_y),
                               max(self.size, abs(end_x - self.worldX)),
                               max(self.size, abs(end_y - self.worldY)))
        return pygame.Rect(self.worldX, self.worldY, self.size, self.size)

    def collides(self, rect):
        if self.illusory:
            return False
        if self.path == "pool":
            if self.age < self.telegraphDuration:
                return False
            hazard = pygame.Rect(self.worldX, self.worldY, self.size, self.size)
            return rect.colliderect(hazard.inflate(-self.size*.12, -self.size*.12))
        if self.path == "laser":
            if self.age < self.telegraphDuration:
                return False
            start = (self.worldX, self.worldY)
            end = (self.worldX + cos(self.direction) * self.remainingRange,
                   self.worldY + sin(self.direction) * self.remainingRange)
            return bool(rect.inflate(self.size, self.size).clipline(start, end))
        if self.path == "lightning":
            if self.age < self.telegraphDuration:
                return False
            points = self._lightning_visible_points()
            hazard = rect.inflate(self.size, self.size)
            return any(hazard.clipline(start, end)
                       for start, end in zip(points, points[1:]))
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

        if self.path == "pool":
            self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
            rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
            lifetime = self.lifetime if self.lifetime is not None else 8.0
            appearing = min(1.0, self.age / max(.01, self.telegraphDuration))
            fading = min(1.0, max(0.0, lifetime - self.age) / .7)
            scale = max(.08, min(appearing, fading))
            visible = rect.inflate(-rect.width * (1 - scale), -rect.height * (1 - scale))
            pygame.draw.rect(screen, ui.SHADOW, visible.move(7, 8))
            pygame.draw.rect(screen, ui.INK, visible.inflate(5, 5))
            tile = max(4, int(visible.width / 7))
            for row in range(7):
                for column in range(7):
                    # A stable checker/noise field keeps the hazard blocky while
                    # the alternating brightness makes it appear to flow.
                    pulse = sin(self.age*4.2 + row*1.7 + column*2.3)
                    if (row + column + int(self.age*3)) % 4 == 0:
                        continue
                    cell = pygame.Rect(visible.x + column*tile,
                                       visible.y + row*tile, tile+1, tile+1)
                    cell = cell.clip(visible)
                    color = (ui.lighten(self.color, 38) if pulse > .45
                             else self.color.lerp(ui.VOID, .24 if pulse < -.45 else .08))
                    pygame.draw.rect(screen, color, cell)
                    if (row*7 + column) % 6 == 0:
                        pygame.draw.rect(screen, ui.INK, cell, 1)
            if self.age < self.telegraphDuration:
                progress = self.age / max(.01, self.telegraphDuration)
                warning = visible.inflate(12, 12)
                pygame.draw.rect(screen, ui.CREAM, warning, 3)
                fill = warning.copy()
                fill.width = warning.width * progress
                pygame.draw.line(screen, self.color, fill.bottomleft,
                                 fill.bottomright, 5)
            if self.age >= lifetime:
                self.remFlag = True
            return

        if self.path == "lightning":
            if self.age < self.telegraphDuration:
                world_points = self.lightningPoints
            else:
                # Unlike an instantaneous laser, the active head visibly travels
                # through every bend, giving the player time to read the route.
                self.lightningTravel += (self.speed * vH.tileSizeGlobal * seconds)
                world_points = self._lightning_visible_points()
            points = [bG.world_to_screen(*point) for point in world_points]
            if len(points) > 1:
                if self.age < self.telegraphDuration:
                    pygame.draw.lines(screen, self.color, False, points, 3)
                    for point in points[1:-1]:
                        pygame.draw.circle(screen, ui.CREAM, point, 3)
                else:
                    width = max(7, int(self.size * (1.0 + .2 * sin(self.age * 22))))
                    pygame.draw.lines(screen, ui.INK, False, points, width + 7)
                    pygame.draw.lines(screen, self.color, False, points, width)
                    pygame.draw.lines(screen, ui.CREAM, False, points, max(2, width // 3))
                    pygame.draw.circle(screen, ui.CREAM, points[-1], max(3, width // 2))
            if self.lifetime is not None and self.age >= self.lifetime:
                self.remFlag = True
            return

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
                core_color = ui.MUTED if self.illusory else ui.CREAM
                pygame.draw.line(screen, core_color, start, end, max(2, width // 3))
                if self.truthMarked:
                    pygame.draw.circle(screen, ui.CREAM, start, max(3, width // 3))
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
            comfort_scale = .88 if gameProfile.profile["casual_mode"] else 1.0
            distance = self.speed * self.HOSTILE_SPEED_SCALE * comfort_scale * vH.get_frame_scale()
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
            if (self.splitCount > 1 and self.splitAt is not None
                    and self.travelled >= self.splitAt and not self.exploded):
                self.exploded = True
                spread = .8 + .12 * self.splitGeneration
                for index in range(self.splitCount):
                    fraction = .5 if self.splitCount == 1 else index / (self.splitCount - 1)
                    child = EnemyProjectile(
                        self.worldX, self.worldY,
                        self.direction - spread / 2 + spread * fraction,
                        self.speed * 1.08, self.damage * .58, self.size * .72,
                        travel_range=max(vH.tileSizeGlobal * 5, self.remainingRange),
                        color=self.color, shape="diamond", owner=self.owner,
                        ignore_walls=self.ignoreWalls,
                    )
                    if self.splitGeneration > 0:
                        child.splitCount = self.splitCount
                        child.splitAt = max(vH.tileSizeGlobal * 2.5,
                                            self.remainingRange * .42)
                        child.splitGeneration = self.splitGeneration - 1
                    self.spawnedProjectiles.append(child)
                self.remFlag = True
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
        if gameProfile.profile["high_contrast"]:
            pygame.draw.rect(screen, ui.CREAM, rect.inflate(4, 4), max(2, int(self.size * .08)))
        if self.truthMarked:
            pygame.draw.circle(screen, ui.CREAM, rect.center, max(2, int(self.size * .1)))
        elif self.illusory:
            pygame.draw.circle(screen, ui.MUTED, rect.center, max(3, int(self.size * .22)), 2)

        expired = self.lifetime is not None and self.age >= self.lifetime
        range_spent = self.path != "orbit" and self.remainingRange <= 0
        wall_hit = not self.ignoreWalls and bG.rect_hits_wall(self.world_rect())
        if expired or range_spent or wall_hit:
            self.remFlag = True
