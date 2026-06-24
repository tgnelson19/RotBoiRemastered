import pygame
from math import pi, atan, cos, sin
from bullet import Bullet
import background as bG
from math import floor, ceil
import variableHolster as vH
import characterStats as cS

#Collects basic enemy variables that are used for game calculations
class Enemy:

    def __init__(self, posX, posY, speed, size, color, damage, hp, expValue, difficulty, frameRate):
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
        self.difficulty = difficulty

    def drawEnemy(self, screen):
        pygame.draw.rect(screen, self.color, pygame.Rect(self.posX, self.posY, self.size, self.size))

    def _world_rect(self):
        world_x = self.posX + bG.playerPosX - bG.lockX
        world_y = self.posY + bG.playerPosY - bG.lockY
        return pygame.Rect(world_x, world_y, self.size, self.size)

    def updateEnemy(self, playerX, playerY, pDX, pDY):
        
        # Logic for a basic crawler enemy that simply runs towards the player
        originX, originY = playerX, playerY

        # This is direct center x, y of player
        deltaX, deltaY = self.posX - originX, self.posY - originY

        # This is direct xhat, yhat vector towards player
        if deltaX == 0:
            self.direction = pi/2 if deltaY > 0 else -pi/2
        else:
            if deltaX > 0:
                self.direction = atan(deltaY / deltaX)
            else:
                deltaX = abs(self.posX - originX)
                self.direction = -atan(deltaY / deltaX) + pi

        dX = self.speed * cos(self.direction) * (120 / self.frameRate)
        dY = self.speed * sin(self.direction) * (120 / self.frameRate)

        current_world = self._world_rect()

        # Try the X move; if it increases overlap with walls and the enemy is currently overlapping,
        # allow the move only if it reduces the number of overlapped wall tiles. This prevents getting
        # permanently stuck inside walls while still preventing walking through solid tiles.
        next_world = current_world.copy()
        next_world.x -= dX
        if not bG.rect_hits_wall(next_world):
            self.posX -= dX
            current_world = next_world
        else:
            # If currently overlapping walls, see if this X move reduces overlap
            curr_overlap = bG.count_overlapping_walls(current_world)
            next_overlap = bG.count_overlapping_walls(next_world)
            if curr_overlap > 0 and next_overlap < curr_overlap:
                self.posX -= dX
                current_world = next_world
            else:
                dX = 0

        next_world = current_world.copy()
        next_world.y -= dY
        if not bG.rect_hits_wall(next_world):
            self.posY -= dY
        else:
            curr_overlap = bG.count_overlapping_walls(current_world)
            next_overlap = bG.count_overlapping_walls(next_world)
            if curr_overlap > 0 and next_overlap < curr_overlap:
                self.posY -= dY
            else:
                dY = 0

        self.posX += pDX
        self.posY += pDY
        