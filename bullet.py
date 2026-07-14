import pygame
import background as bG
import variableHolster as vH
from math import sin, cos, sqrt, degrees

#Handles basic bullet statistics used during game calculation
class Bullet:

    def __init__(
            self, 
            pX, pY,
            iDX, iDY, 
            speed, 
            direc, 
            bRange, 
            size, 
            color, 
            pierce, 
            damage, 
            currCrit, 
            sW, sH, 
            frameRate
            ):
        
        self.posX = pX
        self.posY = pY
        self.iPosX = pX
        self.iPosY = pY
        self.iDX = iDX
        self.iDY = iDY
        self.sW = sW
        self.sH = sH
        self.speed = speed
        self.direc = direc
        self.size = size
        self.color = color
        self.bRange = bRange
        self.bPierce = pierce
        self.remFlag = False
        self.frameRate = frameRate
        self.damage = damage
        self.currCrit = currCrit

    def updateAndDrawBullet(self, screen, dX, dY, playerX, playerY):
        step_scale = vH.get_frame_scale()
        self.posX += ((self.speed * cos(self.direc) - self.iDX) * step_scale + dX)
        self.posY -= ((self.speed * sin(self.direc) + self.iDY) * step_scale - dY)

        bWRTBGX = playerX + (self.posX - self.iPosX)
        bWRTBGY = playerY + (self.posY - self.iPosY)
        world_rect = pygame.Rect(bWRTBGX, bWRTBGY, self.size, self.size)
        pygame.draw.rect(screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))

        if bG.rect_hits_wall(world_rect):
            self.remFlag = True

        self.bRange -= 400 * vH.get_frame_scale() / 360
        if self.bRange <= 0:
            self.remFlag = True