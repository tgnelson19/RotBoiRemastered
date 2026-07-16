from random import randint, uniform
from math import cos, sin, floor, ceil
import pygame
import background as bG
import variableHolster as vH
import characterStats as cS
import uiTheme as ui

#Lil' bubble that chases player when in aura and helps level up
class ExperienceBubble:    
    def __init__(self, oX, oY, value, diffDead, frameRate, celebration=False):
        self.size = 20 * diffDead
        self.color = pygame.Color(0,200,0)
        self.oX = oX
        self.worldX = oX
        self.oY = oY
        self.worldY = oY
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        self.value = value
        self.direction = randint(0,360) * 0.0174533
        self.speedSpan = 40
        self.speed = 1
        self.naturalSpawn = True
        self.frameRate = frameRate
        self.celebrationParticles = []
        if celebration:
            for index in range(56):
                angle = index * 6.283185 / 56 + uniform(-.08, .08)
                speed = uniform(1.5, 5.2)
                self.celebrationParticles.append({
                    "x": self.size / 2, "y": self.size / 2,
                    "vx": cos(angle) * speed, "vy": sin(angle) * speed,
                    "life": uniform(.7, 1.8), "size": randint(2, 10),
                    "color": ui.CREAM if index % 4 == 0 else ui.PURPLE,
                })

    def _world_rect(self):
        return pygame.Rect(self.worldX, self.worldY, self.size, self.size)

    def updateBubble(self, pAuraSpeed, pDX, pDY):
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        dt = vH.get_timer_step() / max(1, vH.frameRate)
        for particle in self.celebrationParticles:
            particle["x"] += particle["vx"] * vH.get_frame_scale()
            particle["y"] += particle["vy"] * vH.get_frame_scale()
            particle["vy"] += .08 * vH.get_frame_scale()
            particle["life"] -= dt
            size = max(1, int(particle["size"] * min(1, particle["life"] * 1.5)))
            pygame.draw.rect(vH.screen, particle["color"],
                             (self.posX + particle["x"], self.posY + particle["y"], size, size))
        self.celebrationParticles[:] = [p for p in self.celebrationParticles if p["life"] > 0]
        if self.naturalSpawn:
            if self.speedSpan > 0:
                self.speedSpan -= vH.get_frame_scale()

            if self.speedSpan <= 0:
                self.speed = 0
            elif self.speedSpan < 20:
                self.speed = 1.25

            current_world = self._world_rect()
            move_scale = vH.get_frame_scale()
            dX = self.speed * cos(self.direction) * move_scale
            dY = self.speed * sin(self.direction) * move_scale

            next_world = current_world.copy()
            next_world.x -= dX
            if not bG.rect_hits_wall(next_world):
                self.worldX -= dX
                current_world = next_world
            else:
                dX = 0

            next_world = current_world.copy()
            next_world.y -= dY
            if not bG.rect_hits_wall(next_world):
                self.worldY -= dY
            else:
                dY = 0
        else:
            move_scale = vH.get_frame_scale()
            self.worldX -= pAuraSpeed * cos(self.direction) * move_scale
            self.worldY -= pAuraSpeed * sin(self.direction) * move_scale

        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        center = rect.center
        points = ((center[0], rect.top), (rect.right, center[1]), (center[0], rect.bottom), (rect.left, center[1]))
        shadow_points = tuple((x + 3, y + 3) for x, y in points)
        pygame.draw.polygon(vH.screen, ui.SHADOW, shadow_points)
        pygame.draw.polygon(vH.screen, ui.GREEN, points)
        pygame.draw.polygon(vH.screen, ui.INK, points, 2)
