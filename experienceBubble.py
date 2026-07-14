from random import randint
from math import cos, sin, floor, ceil
import pygame
import background as bG
import variableHolster as vH
import characterStats as cS

#Lil' bubble that chases player when in aura and helps level up
class ExperienceBubble:    
    def __init__(self, oX, oY, value, diffDead, frameRate):
        self.size = 20 * diffDead
        self.color = pygame.Color(0,200,0)
        self.oX = oX
        self.posX = oX
        self.oY = oY
        self.posY = oY
        self.value = value
        self.direction = randint(0,360) * 0.0174533
        self.speedSpan = 40
        self.speed = 1
        self.naturalSpawn = True
        self.frameRate = frameRate

    def _world_rect(self):
        world_x = self.posX + bG.playerPosX - bG.lockX
        world_y = self.posY + bG.playerPosY - bG.lockY
        return pygame.Rect(world_x, world_y, self.size, self.size)

    def updateBubble(self, pAuraSpeed, pDX, pDY):
        if self.naturalSpawn:
            if self.speedSpan > 0:
                self.speedSpan -= vH.get_frame_scale()

            if self.speedSpan <= 0:
                self.speed = 0
            elif self.speedSpan < 20:
                self.speed = 1.25

            current_world = self._world_rect()
            dX = self.speed * cos(self.direction)
            dY = self.speed * sin(self.direction)

            next_world = current_world.copy()
            next_world.x -= dX
            if not bG.rect_hits_wall(next_world):
                self.posX -= dX
                current_world = next_world
            else:
                dX = 0

            next_world = current_world.copy()
            next_world.y -= dY
            if not bG.rect_hits_wall(next_world):
                self.posY -= dY
            else:
                dY = 0
        else:
            move_scale = vH.get_frame_scale()
            self.posX -= pAuraSpeed * cos(self.direction) * move_scale
            self.posY -= pAuraSpeed * sin(self.direction) * move_scale

        self.posX += pDX
        self.posY += pDY
        pygame.draw.rect(vH.screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))