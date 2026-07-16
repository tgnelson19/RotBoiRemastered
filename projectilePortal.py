"""Boss-owned projectile emitters with independent movement and firing patterns."""

from math import atan2, cos, pi, sin

import pygame

import background as bG
from enemyProjectile import EnemyProjectile
import uiTheme as ui
import variableHolster as vH


class ProjectilePortal:
    """An orbiting arena entity that fires a configurable inward shotgun."""

    def __init__(self, center, radius, angle, angular_speed=.35, fire_interval=1.7,
                 pellet_count=5, spread=.72, owner="dissonance_portal", color=None,
                 polarity=1, movement_path="orbit"):
        self.orbitCenter = center
        self.radius = radius
        self.angle = angle
        self.angularSpeed = angular_speed
        self.fireInterval = fire_interval
        self.fireCooldown = fire_interval * ((angle % (2 * pi)) / (2 * pi))
        self.pelletCount = pellet_count
        self.spread = spread
        self.owner = owner
        self.color = color or ui.PURPLE
        self.polarity = 1 if polarity >= 0 else -1
        self.movementPath = movement_path
        self.size = vH.tileSizeGlobal * .9
        self.maxHp = 600
        self.hp = self.maxHp
        self.hitsTaken = 0
        self.hitsToDisable = 3
        self.phaseDisabled = False
        self.runeStrokes = ()
        self.disabledRemaining = 0.0
        self.regenerationTime = 5.0
        self.trail = []
        self.telegraphTimer = 0.0
        self.telegraphKind = "inward"
        self.telegraphTarget = center
        self.showTether = True
        self.worldX = 0.0
        self.worldY = 0.0
        self.remFlag = False
        self.burstQueue = []
        self._place()

    def _place(self):
        center_x, center_y = self.orbitCenter
        if self.movementPath == "figure8":
            offset_x = cos(self.angle) * self.radius
            offset_y = sin(self.angle * 2) * self.radius * .48
        elif self.movementPath == "square":
            phase = (self.angle / (2 * pi)) % 1 * 4
            side, progress = int(phase), phase % 1
            corners = ((-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1))
            start, end = corners[side], corners[side + 1]
            offset_x = (start[0] + (end[0] - start[0]) * progress) * self.radius
            offset_y = (start[1] + (end[1] - start[1]) * progress) * self.radius
        elif self.movementPath == "tornado":
            breathing_radius = self.radius * (.72 + .28 * sin(self.angle * 1.7))
            offset_x = cos(self.angle) * breathing_radius
            offset_y = sin(self.angle) * breathing_radius * .62
        elif self.movementPath == "wave":
            offset_x = cos(self.angle) * self.radius
            offset_y = sin(self.angle * 3) * self.radius * .38
        else:
            offset_x = cos(self.angle) * self.radius
            offset_y = sin(self.angle) * self.radius
        self.worldX = center_x + offset_x - self.size / 2
        self.worldY = center_y + offset_y - self.size / 2
        point = (self.worldX + self.size / 2, self.worldY + self.size / 2)
        if not self.trail or abs(point[0] - self.trail[-1][0]) + abs(point[1] - self.trail[-1][1]) > 3:
            self.trail.append(point)
            self.trail = self.trail[-7:]

    @property
    def active(self):
        return not self.remFlag and self.disabledRemaining <= 0

    @property
    def blocks_shots(self):
        return self.active and not self.phaseDisabled

    def reset_for_phase(self, rune_strokes=()):
        """Restore interception and full firepower for a newly started phase."""
        self.hitsTaken = 0
        self.phaseDisabled = False
        self.disabledRemaining = 0.0
        self.hp = self.maxHp
        self.runeStrokes = rune_strokes
        self.burstQueue.clear()

    def take_damage(self, amount):
        if not self.blocks_shots:
            return False
        self.hitsTaken += 1
        self.hp = round(self.maxHp * max(0, self.hitsToDisable - self.hitsTaken) / self.hitsToDisable)
        if self.hitsTaken >= self.hitsToDisable:
            self.phaseDisabled = True
            return True
        return False

    def update_status(self, dt):
        self.telegraphTimer = max(0.0, self.telegraphTimer - dt)
        if self.disabledRemaining > 0:
            self.disabledRemaining = max(0.0, self.disabledRemaining - dt)
            if self.disabledRemaining <= 0:
                self.hp = self.maxHp
                self.fireCooldown = max(self.fireCooldown, .5)

    def update(self, projectile_sink, dt):
        if self.remFlag:
            return
        if not self.active:
            return
        self.angle += self.angularSpeed * dt
        self._place()
        self.update_bursts(projectile_sink, dt)
        self.fireCooldown -= dt
        if self.fireCooldown > 0:
            if self.fireCooldown <= .32:
                self.telegraphTimer = max(self.telegraphTimer, self.fireCooldown)
                self.telegraphKind = "shotgun"
                self.telegraphTarget = self.orbitCenter
            return

        # The standard portal shotgun is a three-beat phrase.  Each wave changes
        # density, speed, and silhouette, making the volley readable as one attack.
        self.fire_pattern_burst(
            projectile_sink, self.orbitCenter,
            ((self.pelletCount, self.spread, .92, .4),
             (max(3, self.pelletCount - 2), self.spread * .72, 1.18, .42),
             (self.pelletCount + 2, self.spread * 1.12, 1.42, .28)),
            wave_interval=.14, owner_suffix="shot",
        )
        self.fireCooldown += self.fireInterval

    def update_bursts(self, projectile_sink, dt):
        """Advance queued follow-up waves without coupling them to portal movement."""
        if not self.active:
            return
        remaining = []
        for burst in self.burstQueue:
            burst[0] -= dt
            if burst[0] <= 0:
                self._fire_wave(projectile_sink, *burst[1:])
            else:
                remaining.append(burst)
        self.burstQueue = remaining

    def _fire_wave(self, projectile_sink, target, pellet_count, spread, speed,
                   size_scale, damage, color, owner_suffix, direction_offset=0):
        if not self.active:
            return
        portal_x, portal_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        direction = atan2(target[1] - portal_y, target[0] - portal_x) + direction_offset
        distance = ((target[0] - portal_x) ** 2 + (target[1] - portal_y) ** 2) ** .5
        pellet_count = max(1, (pellet_count + 1) // 2) if self.phaseDisabled else pellet_count
        for index in range(pellet_count):
            offset = (index - (pellet_count - 1) / 2) * spread / max(1, pellet_count - 1)
            shot_size = vH.tileSizeGlobal * size_scale
            projectile_sink.append(EnemyProjectile(
                portal_x - shot_size / 2, portal_y - shot_size / 2,
                direction + offset, speed, damage, shot_size,
                travel_range=max(vH.tileSizeGlobal * 72, distance), color=color or self.color,
                shape="diamond", owner=f"{self.owner}_{owner_suffix}", ignore_walls=True,
            ))

    def fire_pattern_burst(self, projectile_sink, target, waves,
                           wave_interval=.13, damage=.85, color=None,
                           owner_suffix="pattern_burst", direction_offset=0):
        """Fire the first wave now and queue coordinated variable follow-ups.

        Wave entries are ``(pellets, spread, speed, size_scale)``.  Keeping this
        vocabulary on the emitter lets every phase reuse it without duplicating
        timing machinery.
        """
        if not self.active or not waves:
            return
        self.telegraphTimer = .32
        self.telegraphKind = "fan"
        self.telegraphTarget = target
        first, *followups = waves
        self._fire_wave(projectile_sink, target, *first, damage, color,
                        owner_suffix, direction_offset)
        for index, wave in enumerate(followups, 1):
            self.burstQueue.append([
                wave_interval * index, target, *wave, damage, color,
                owner_suffix, direction_offset,
            ])

    def fire_toward(self, projectile_sink, target, pellet_count=None, spread=None,
                    speed=1.15, damage=.85, color=None, owner_suffix="shot"):
        """Fire a volley at an arbitrary world point without changing the orbit."""
        if not self.active:
            return
        pellet_count = pellet_count if pellet_count is not None else self.pelletCount
        if self.phaseDisabled:
            pellet_count = max(1, (pellet_count + 1) // 2)
        spread = spread if spread is not None else self.spread
        portal_x, portal_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        direction = atan2(target[1] - portal_y, target[0] - portal_x)
        self.telegraphTimer = .28
        self.telegraphKind = "line" if pellet_count <= 2 else "fan"
        self.telegraphTarget = target
        distance = ((target[0] - portal_x) ** 2 + (target[1] - portal_y) ** 2) ** .5
        for index in range(pellet_count):
            offset = (index - (pellet_count - 1) / 2) * spread / max(1, pellet_count - 1)
            shot_size = vH.tileSizeGlobal * .4
            projectile_sink.append(EnemyProjectile(
                portal_x - shot_size / 2, portal_y - shot_size / 2,
                direction + offset, speed, damage, shot_size,
                travel_range=max(vH.tileSizeGlobal * 72, distance), color=color or self.color,
                shape="diamond", owner=f"{self.owner}_{owner_suffix}", ignore_walls=True,
            ))

    def fire_speed_burst(self, projectile_sink, target, count=4, color=None,
                         owner_suffix="speed_burst"):
        """Launch a readable comet train: fast leader, progressively slower tail."""
        portal_x, portal_y = self.worldX + self.size / 2, self.worldY + self.size / 2
        direction = atan2(target[1] - portal_y, target[0] - portal_x)
        speeds = (1.35, 1.05, .78, .52, .36)
        count = max(1, (count + 1) // 2) if self.phaseDisabled else count
        for index in range(max(1, min(count, len(speeds)))):
            shot_size = vH.tileSizeGlobal * (.34 + index * .035)
            projectile_sink.append(EnemyProjectile(
                portal_x - shot_size / 2, portal_y - shot_size / 2,
                direction, speeds[index], .85, shot_size,
                travel_range=float("inf"), color=color or self.color,
                shape="diamond", owner=f"{self.owner}_{owner_suffix}",
                ignore_walls=True,
            ))

    def draw(self, screen):
        if self.remFlag:
            return
        screen_x, screen_y = bG.world_to_screen(self.worldX, self.worldY)
        rect = pygame.Rect(screen_x, screen_y, self.size, self.size)
        trail_points = [bG.world_to_screen(x, y) for x, y in self.trail]
        if len(trail_points) > 1:
            pygame.draw.lines(screen, ui.INK, False, trail_points, 5)
            pygame.draw.lines(screen, self.color, False, trail_points, 2)
        if not self.active:
            pygame.draw.ellipse(screen, ui.SHADOW, rect.move(3, 4))
            pygame.draw.ellipse(screen, ui.MUTED, rect, max(2, int(self.size * .1)))
            repair = self.disabledRemaining / self.regenerationTime
            pygame.draw.arc(screen, self.color, rect.inflate(8, 8), -pi / 2,
                            -pi / 2 + 2 * pi * (1 - repair), 3)
            pygame.draw.line(screen, ui.INK, rect.topleft, rect.bottomright, 3)
            pygame.draw.line(screen, ui.INK, rect.topright, rect.bottomleft, 3)
            return
        pulse = sin(self.angle * 3) * self.size * .06
        outer = rect.inflate(pulse, pulse)
        pygame.draw.ellipse(screen, ui.SHADOW, outer.move(4, 5))
        pygame.draw.ellipse(screen, self.color, outer)
        pygame.draw.ellipse(screen, ui.INK, outer, max(3, int(self.size * .11)))
        for ring_index in range(2):
            ring = outer.inflate(-self.size * (.16 + ring_index * .15),
                                 -self.size * (.16 + ring_index * .15))
            start = self.angle * (2.2 if ring_index == 0 else -1.7)
            pygame.draw.arc(screen, ui.lighten(self.color, 35), ring,
                            start, start + pi * 1.25, 2)
        inner = outer.inflate(-self.size * .36, -self.size * .36)
        pygame.draw.ellipse(screen, ui.VOID, inner)
        if self.runeStrokes:
            rune_scale = inner.width * .34
            for stroke in self.runeStrokes:
                points = [(inner.centerx + x * rune_scale,
                           inner.centery + y * rune_scale) for x, y in stroke]
                if len(points) > 1:
                    pygame.draw.lines(screen, ui.INK, False, points, 4)
                    pygame.draw.lines(screen, ui.CREAM, False, points, 2)
        if self.telegraphTimer > 0:
            # A compact charge halo communicates imminent fire without painting
            # additional aiming lanes over an already dense bullet field.
            charge = min(1.0, self.telegraphTimer / .32)
            charge_ring = outer.inflate(self.size * (.18 + charge * .18),
                                        self.size * (.18 + charge * .18))
            pygame.draw.arc(screen, ui.INK, charge_ring, -pi / 2,
                            -pi / 2 + 2 * pi * (1 - charge), 6)
            pygame.draw.arc(screen, ui.CREAM, charge_ring, -pi / 2,
                            -pi / 2 + 2 * pi * (1 - charge), 2)
        # Three rune motes counter-rotate around the mouth, making even distant
        # emitters readable and giving every path a little clockwork personality.
        for mote_index in range(3):
            mote_angle = -self.angle * 2.1 + mote_index * 2 * pi / 3
            mote_radius = outer.width * .56
            mote = (outer.centerx + cos(mote_angle) * mote_radius,
                    outer.centery + sin(mote_angle) * mote_radius)
            pygame.draw.circle(screen, ui.INK, mote, max(3, int(self.size * .075)) + 2)
            pygame.draw.circle(screen, ui.CREAM, mote, max(3, int(self.size * .075)))
        polarity_color = ui.BLUE if self.polarity > 0 else ui.RED
        pygame.draw.line(screen, polarity_color,
                         (inner.centerx - inner.width * .22, inner.centery),
                         (inner.centerx + inner.width * .22, inner.centery), 2)
        if self.polarity > 0:
            pygame.draw.line(screen, polarity_color,
                             (inner.centerx, inner.centery - inner.height * .22),
                             (inner.centerx, inner.centery + inner.height * .22), 2)
        if self.showTether:
            pygame.draw.line(screen, ui.MUTED, inner.center, self.orbitCenter_screen(), 2)
            tether_end = self.orbitCenter_screen()
            for index in range(3):
                progress = (self.angle * .18 + index / 3) % 1
                packet_x = inner.centerx + (tether_end[0] - inner.centerx) * progress
                packet_y = inner.centery + (tether_end[1] - inner.centery) * progress
                packet = pygame.Rect(int(packet_x) - 2, int(packet_y) - 2, 4, 4)
                pygame.draw.rect(screen, ui.INK, packet.inflate(2, 2))
                pygame.draw.rect(screen, self.color, packet)
        hp_ratio = max(0.0, self.hp / self.maxHp)
        pygame.draw.arc(screen, ui.CREAM, outer.inflate(5, 5), -pi / 2,
                        -pi / 2 + 2 * pi * hp_ratio, 2)
        if self.phaseDisabled:
            pygame.draw.arc(screen, ui.MUTED, outer.inflate(9, 9), 0, 2 * pi, 3)
        # Aiming telegraphs intentionally stay hidden; portal-to-portal rune-cannon
        # connections are drawn by Dissonance and remain as the formation's visual link.

    def orbitCenter_screen(self):
        return bG.world_to_screen(*self.orbitCenter)
