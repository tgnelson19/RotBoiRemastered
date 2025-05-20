import pygame
from math import pi, atan, cos, sin
from bullet import Bullet
import background as bG
from math import floor, ceil
import variableHolster as vH
import characterStats as cS

#Collects basic enemy variables that are used for game calculations
class Enemy:

    def __init__(self, posX, posY, speed, size, color, damage, hp, expValue, frameRate):
        self.posX = posX
        self.posY = posY
        self.speed = speed
        self.size = size
        self.color = color
        self.damage = damage
        self.hp = hp
        self.direction = 0
        self.frameRate = frameRate
        self.cantTouchMeList = []
        self.expValue = expValue

    def drawEnemy(self, screen):
        pygame.draw.rect(screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))

    def updateEnemy(self, playerX, playerY, pDX, pDY):
        
        #Logic for a basic crawler enemy
        
        originX, originY = playerX, playerY

        #This is direct center x, y of player

        deltaX, deltaY = self.posX - originX, self.posY - originY

        #This is direct xhat, yhat vector towards player

        if (deltaX == 0):
            if(deltaY > 0): self.direction = pi/2
            else: self.direction = -pi/2
        else:
            if(deltaX > 0): self.direction = atan(deltaY/deltaX)
            else: deltaX = abs(self.posX - originX); self.direction = -atan(deltaY/deltaX) + pi
            
        dX = (self.speed*cos(self.direction) * (120/self.frameRate))
        dY = (self.speed*sin(self.direction) * (120/self.frameRate))
        
        flagX, flagY = False, False
            
        currPosXG = (self.posX - bG.lockX)
        currPosYG = (self.posY - bG.lockY)
        
        postPDX = cS.currTileX + (currPosXG/vH.tileSizeGlobal)
        postPDY = cS.currTileY + (currPosYG/vH.tileSizeGlobal)
        
        #Current exact position
        newABSPosX = (postPDX * vH.tileSizeGlobal) - dX
        newABSPosY = (postPDY * vH.tileSizeGlobal) - dY
        
        newTileLocXMin = floor(newABSPosX / vH.tileSizeGlobal) #Exact tile to the left
        newTileLocYMin = floor(newABSPosY / vH.tileSizeGlobal) #Exact tile to the top
        newTileLocXMax = ceil(newABSPosX / vH.tileSizeGlobal) #Exact tile to the right
        newTileLocYMax = ceil(newABSPosY / vH.tileSizeGlobal) #Exact tile to the bottom
        
        try:
                # CASE: moving RIGHT
            if dX < 0: 
                if bG.currRoomRects[floor(postPDY)][newTileLocXMax][0] == 1: flagX = True
                elif bG.currRoomRects[ceil(postPDY)][newTileLocXMax][0] == 1: flagX = True
            # CASE: moving LEFT
            elif dX > 0:
                if bG.currRoomRects[ceil(postPDY)][newTileLocXMin][0] == 1: flagX = True
                elif bG.currRoomRects[floor(postPDY)][newTileLocXMin][0] == 1: flagX = True
            
            # CASE: moving DOWN
            if dY < 0:
                if bG.currRoomRects[newTileLocYMax][floor(postPDX)][0] == 1: flagY = True
                elif bG.currRoomRects[newTileLocYMax][ceil(postPDX)][0] == 1: flagY = True
            # CASE: moving UP
            elif dY > 0:
                if bG.currRoomRects[newTileLocYMin][ceil(postPDX)][0] == 1: flagY = True
                elif bG.currRoomRects[newTileLocYMin][floor(postPDX)][0] == 1: flagY = True
                
        except IndexError:
            flagX, flagY = True, True

        if not flagX: self.posX -= dX
        else: dX = 0
        if not flagY: self.posY -= dY
        else: dY = 0
        
        self.posX += pDX
        self.posY += pDY
        
        
        
        
        
        
        
        

        
        