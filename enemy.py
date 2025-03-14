import pygame
from math import pi, atan, cos, sin
from bullet import Bullet

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

        self.posX -= (self.speed*cos(self.direction) * (120/self.frameRate)) - pDX
        self.posY -= (self.speed*sin(self.direction) * (120/self.frameRate)) - pDY