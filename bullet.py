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

        self.posX = self.posX + ((self.speed*cos(self.direc) - self.iDX) * (120/self.frameRate) + dX)
        self.posY = self.posY - ((self.speed*sin(self.direc) + self.iDY) * (120/self.frameRate) - dY)

        bWRTBGX = (playerX + (self.posX - self.iPosX))
        bWRTBGY = (playerY + (self.posY - self.iPosY))

        currTileX = int((bWRTBGX + self.size/2) / vH.tileSizeGlobal)
        currTileY = int((bWRTBGY + self.size/2) / vH.tileSizeGlobal)

        pygame.draw.rect(screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))

        if bG.currRoomRects[currTileY][currTileX][0] == 1: self.remFlag = True

        self.bRange -= (400/vH.frameRate)
        if self.bRange <= 0:
            self.remFlag = True