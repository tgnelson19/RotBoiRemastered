import variableHolster as vH
import background as bG
import characterStats as cS
import pygame as pg
from bullet import Bullet

from math import floor, ceil, pi, atan, trunc
from random import randint

def movePlayer():
    global playerColor

    #Velocity at current time
    cS.dX, cS.dY = 0,0

    if vH.keys[pg.K_w]: cS.dY += 1
    if vH.keys[pg.K_a]: cS.dX += 1
    if vH.keys[pg.K_s]: cS.dY -= 1
    if vH.keys[pg.K_d]: cS.dX -= 1
    
    if abs(cS.dX) + abs(cS.dY) == 2: scalar = 0.707
    else: scalar = 1

    #Now we have velocities scaled by diagonal if needed
    cS.dX, cS.dY = cS.dX * scalar * cS.playerSpeed * (120/vH.frameRate), cS.dY * scalar * cS.playerSpeed * (120/vH.frameRate)

    #FUTURE exact position (NOT TILES) (floats)
    newABSPosX = ((bG.playerPosX) - cS.dX)
    newABSPosY = ((bG.playerPosY) - cS.dY)

    #Current Exact position in TILES (float)
    currTileX = bG.playerPosX / vH.tileSizeGlobal
    currTileY = bG.playerPosY / vH.tileSizeGlobal

    


    newTileLocXMin = floor(newABSPosX / vH.tileSizeGlobal) #Exact tile to the left
    newTileLocYMin = floor(newABSPosY / vH.tileSizeGlobal) #Exact tile to the top
    newTileLocXMax = ceil(newABSPosX / vH.tileSizeGlobal) #Exact tile to the right
    newTileLocYMax = ceil(newABSPosY / vH.tileSizeGlobal) #Exact tile to the bottom

    playerColor = pg.Color(0,0,255)

    flagX = False
    flagY = False

    # CASE: moving RIGHT
    if cS.dX < 0: 
        if bG.currRoomRects[floor(currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].left
        elif bG.currRoomRects[ceil(currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].left
    # CASE: moving LEFT
    elif cS.dX > 0:
        if bG.currRoomRects[ceil(currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].left
        elif bG.currRoomRects[floor(currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].left
    
    # CASE: moving DOWN
    if cS.dY < 0:
        if bG.currRoomRects[newTileLocYMax][floor(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMax][ceil(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].top
    # CASE: moving UP
    elif cS.dY > 0:
        if bG.currRoomRects[newTileLocYMin][ceil(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMin][floor(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].top
            
    if not flagX: bG.playerPosX = newABSPosX
    if not flagY: bG.playerPosY = newABSPosY

def drawPlayer():
    pg.draw.rect(vH.screen, cS.playerColor, cS.playerRect)

def drawBackground():
    bG.moveAndDisplayBackground(bG.repasteableRoomSurface)

def handlingBulletCreation():

        if (cS.attackCooldownTimer <= 0 and vH.mouseDown):
            cS.attackCooldownTimer = cS.attackCooldownStat

            currCrit = False
            currCritChance = floor(cS.critChance)
            chance = randint(1, 100)
            if (chance <= 100*(cS.critChance - trunc(cS.critChance))): currCrit = True; currCritChance = floor(cS.critChance) + 1
            currDamage = cS.bulletDamage * (cS.critDamage **(currCritChance))

            currProjectileCount = floor(cS.projectileCount)
            chance = randint(1, 100)
            if (chance <= 100*(cS.projectileCount - trunc(cS.projectileCount))): currProjectileCount = floor(cS.projectileCount) + 1

            currPierce = floor(cS.bulletPierce)
            chance = randint(1, 100)
            if (chance <= 100*(cS.bulletPierce - trunc(cS.bulletPierce))): currPierce = floor(cS.bulletPierce) + 1

            for bNum in range(0,int(currProjectileCount)):
                
                originX, originY = bG.lockX + (cS.playerSize / 2), bG.lockY + (cS.playerSize / 2)
                deltaX, deltaY = vH.mouseX - originX, vH.mouseY - originY
                direction = 0

                if (deltaX == 0):
                    if(deltaY > 0): direction = 0
                    else: direction = -pi
                else:
                    if(deltaX > 0): direction = -atan(deltaY/deltaX)
                    else: deltaX = abs(vH.mouseX - originX); direction = atan(deltaY/deltaX) + pi

                if(currProjectileCount != 1):
                    dirDelta = -(cS.azimuthalProjectileAngle / 2)
                    direction += dirDelta + bNum*(cS.azimuthalProjectileAngle / (currProjectileCount-1))

                cS.bulletHolster.append(Bullet(bG.lockX + (cS.playerSize / 2) - (cS.bulletSize / 2),
                                                bG.lockY + (cS.playerSize / 2) - (cS.bulletSize / 2),
                                                cS.dX,
                                                cS.dY,
                                                cS.bulletSpeed,
                                                direction,
                                                cS.bulletRange,
                                                cS.bulletSize,
                                                cS.bulletColor,
                                                currPierce,
                                                currDamage,
                                                currCrit,
                                                vH.sW,
                                                vH.sH,
                                                vH.frameRate))

        elif(cS.attackCooldownTimer > 0):
            cS.attackCooldownTimer -= 1 * (120/vH.frameRate)


def handlingBulletUpdating():

        for bullet in cS.bulletHolster:
            bullet.updateAndDrawBullet(vH.screen, cS.dX, cS.dY, bG.playerPosX, bG.playerPosY)

            # currX = bullet.posX / vH.tileSizeGlobal #Current Position in tiles
            # currY = bullet.posY / vH.tileSizeGlobal #Current Position in tiles

            # try:
            #     if(noNoZone[int(currX + bullet.bulletSize)][int(currY + bullet.bulletSize)] == "wall"):
            #         bulletHolster.remove(bullet)
            #     elif (bullet.remFlag == True):
            #         bulletHolster.remove(bullet)

            # except IndexError:
            #     bulletHolster.remove(bullet)
            if bullet.remFlag: cS.bulletHolster.remove(bullet)
