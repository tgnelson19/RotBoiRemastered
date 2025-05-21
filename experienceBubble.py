from random import randint
from math import cos, sin, floor, ceil
import pygame
import background as bG
import variableHolster as vH
import characterStats as cS

#Lil' bubble that chases player when in aura and helps level up
class ExperienceBubble:    
    def __init__(self, oX, oY, value, frameRate):
        self.size = 20
        self.color = pygame.Color(0,200,0)
        self.oX = oX
        self.posX = oX
        self.oY = oY
        self.posY = oY
        self.value = value
        self.direction = randint(0,360) * 0.0174533
        self.speedSpan = 40
        self.speed = 2.5
        self.naturalSpawn = True
        self.frameRate = frameRate

    def updateBubble(self, pAuraSpeed, pDX, pDY):

        if(self.naturalSpawn):

            if (self.speedSpan > 0):
                self.speedSpan -= 1 * (120/self.frameRate)
                
            if (self.speedSpan <= 0):
                self.speed = 0
            elif(self.speedSpan < 20):
                self.speed = 1.25

            flagX, flagY = False, False
            
            currPosXG = (self.posX - bG.lockX)
            currPosYG = (self.posY - bG.lockY)
            
            postPDX = cS.currTileX + (currPosXG/vH.tileSizeGlobal)
            postPDY = cS.currTileY + (currPosYG/vH.tileSizeGlobal)
            
            dX = self.speed*cos(self.direction)
            dY = self.speed*sin(self.direction)
            
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
        
        else:
            self.posX -= pAuraSpeed*cos(self.direction) * (120/self.frameRate)
            self.posY -= pAuraSpeed*sin(self.direction) * (120/self.frameRate)  
        


        pygame.draw.rect(vH.screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))