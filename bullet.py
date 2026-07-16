"""World-space projectile entity."""

from math import cos, sin

import pygame

import background as bG
import uiTheme as ui
import variableHolster as vH


class Bullet:
    """A projectile whose path is independent of player and camera movement."""

    def __init__(
            self,
            world_x, world_y,
            speed,
            direction,
            bullet_range,
            size,
            color,
            pierce,
            damage,
            is_critical,
            ):
        self.worldX = world_x
        self.worldY = world_y
        self.posX, self.posY = bG.world_to_screen(world_x, world_y)
        self.speed = speed
        self.direc = direction
        self.size = size
        self.color = color
        self.bRange = bullet_range
        self.bPierce = pierce
        self.remFlag = False
        self.damage = damage
        self.currCrit = is_critical
        self.portalCooldown = 0.0

    def updateAndDrawBullet(self, screen):
        # Advance in world space using only the projectile's own velocity. Camera and
        # player movement affect the final screen projection, never the bullet path.
        distance = self.speed * vH.get_frame_scale()
        seconds = vH.get_timer_step() / max(1, vH.frameRate)
        self.portalCooldown = max(0.0, self.portalCooldown - seconds)
        self.worldX += cos(self.direc) * distance
        self.worldY -= sin(self.direc) * distance
        self.bRange -= distance

        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        world_rect = pygame.Rect(self.worldX, self.worldY, self.size, self.size)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.INK, rect.inflate(4, 4))
        pygame.draw.rect(screen, ui.PURPLE if self.currCrit else ui.CREAM, rect)
        pygame.draw.rect(screen, ui.TEXT, rect.inflate(-int(self.size * .5), -int(self.size * .5)))

        if bG.rect_hits_wall(world_rect) or self.bRange <= 0:
            self.remFlag = True
