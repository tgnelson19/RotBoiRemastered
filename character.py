import variableHolster as vH
import background as bG
import characterStats as cS
import pygame as pg
from bullet import Bullet
from enemy import Enemy
from damageText import DamageText
from experienceBubble import ExperienceBubble

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
    cS.currTileX = bG.playerPosX / vH.tileSizeGlobal
    cS.currTileY = bG.playerPosY / vH.tileSizeGlobal

    newTileLocXMin = floor(newABSPosX / vH.tileSizeGlobal) #Exact tile to the left
    newTileLocYMin = floor(newABSPosY / vH.tileSizeGlobal) #Exact tile to the top
    newTileLocXMax = ceil(newABSPosX / vH.tileSizeGlobal) #Exact tile to the right
    newTileLocYMax = ceil(newABSPosY / vH.tileSizeGlobal) #Exact tile to the bottom

    playerColor = pg.Color(0,0,255)

    flagX = False
    flagY = False

    # CASE: moving RIGHT
    if cS.dX < 0: 
        if bG.currRoomRects[floor(cS.currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(cS.currTileY)][ceil(cS.currTileX)][1].left
        elif bG.currRoomRects[ceil(cS.currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(cS.currTileY)][ceil(cS.currTileX)][1].left
    # CASE: moving LEFT
    elif cS.dX > 0:
        if bG.currRoomRects[ceil(cS.currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(cS.currTileY)][floor(cS.currTileX)][1].left
        elif bG.currRoomRects[floor(cS.currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(cS.currTileY)][floor(cS.currTileX)][1].left
    
    # CASE: moving DOWN
    if cS.dY < 0:
        if bG.currRoomRects[newTileLocYMax][floor(cS.currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(cS.currTileY)][ceil(cS.currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMax][ceil(cS.currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(cS.currTileY)][ceil(cS.currTileX)][1].top
    # CASE: moving UP
    elif cS.dY > 0:
        if bG.currRoomRects[newTileLocYMin][ceil(cS.currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(cS.currTileY)][floor(cS.currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMin][floor(cS.currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(cS.currTileY)][floor(cS.currTileX)][1].top
            
    if not flagX: bG.playerPosX = newABSPosX
    else: cS.dX = 0
    if not flagY: bG.playerPosY = newABSPosY
    else: cS.dY = 0

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
            if bullet.remFlag: cS.bulletHolster.remove(bullet)

def handlingEnemyCreation():
    
    if (randint(1, cS.enemyOneInFramesChance) == 1):
        
        eSpeed = 2
        eSize = 40
        eColor = pg.Color(255,0,0)
        eDamage = 5
        eHP = 20
        eEXP = 10
        
        whichSideSpawned = randint(1,4)
        if (whichSideSpawned <=2) : 
            newXTile = randint(1, bG.currNumOfXTiles - 2)
            if (whichSideSpawned == 1):
                newYTile = 1
            else:
                newYTile = bG.currNumOfYTiles - 2
        else:
            newYTile = randint(1, bG.currNumOfYTiles - 2)
            if (whichSideSpawned == 3):
                newXTile = 1
            else:
                newXTile = bG.currNumOfXTiles - 2
        
        cS.enemyHolster.append(Enemy(
            (newXTile*vH.tileSizeGlobal) - bG.playerPosX + bG.lockX,
            (newYTile*vH.tileSizeGlobal) - bG.playerPosY + bG.lockY,
            eSpeed,
            eSize,
            eColor,
            eDamage,
            eHP,
            eEXP,
            vH.frameRate
        ))

def handlingEnemyUpdatesAndDrawing():
    for enemy in cS.enemyHolster:
        enemy.updateEnemy(bG.lockX + cS.playerSize/2, bG.lockY + cS.playerSize/2, cS.dX, cS.dY)
        enemy.drawEnemy(vH.screen)
        
def handlingDamagingEnemies():
    for bullet in cS.bulletHolster:
            originX = bullet.posX + bullet.size/2
            originY = bullet.posY + bullet.size/2
            for eman in cS.enemyHolster:
                if(originX + bullet.size/2 > eman.posX and originX - bullet.size/2< eman.posX + eman.size):
                    if(originY + bullet.size/2> eman.posY and originY - bullet.size/2< eman.posY + eman.size):
                        if (bullet not in eman.cantTouchMeList):
                            eman.cantTouchMeList.append(bullet)
                            bullet.bPierce -= 1
                            if (bullet.bPierce <= 0):
                                bullet.remFlag = True
                            eman.hp -= bullet.damage
                            if(bullet.currCrit):
                                currColor = pg.Color(128,0,128)
                            else:
                                currColor = pg.Color(200,120,0)
                            cS.damageTextList.append(DamageText(eman.posX, eman.posY, cS.damageTextSizeBase, currColor, bullet.damage, eman.size, vH.frameRate))
                            if (eman.hp <= 0):
                                cS.enemyHolster.remove(eman)
                                cS.numOfEnemiesKilled += 1
                                cS.experienceList.append(ExperienceBubble(eman.posX, eman.posY, cS.xpMult * (eman.expValue*(cS.currentStage*cS.experienceStageMod)), vH.frameRate))
                                
def updateDamageTexts():
        for dText in cS.damageTextList:
            dText.drawAndUpdateDamageText(cS.dX, cS.dY)
            if (dText.deleteMe == True):
                cS.damageTextList.remove(dText)
                
def updateExperience():
        for bubble in cS.experienceList:
            bubble.updateBubble(cS.auraSpeed, cS.dX, cS.dY)
            
def expForPlayer():
    
    for bubble in cS.experienceList:
        if(bG.lockX + cS.playerSize > bubble.posX and bG.lockX < bubble.posX + bubble.size):
            if(bG.lockY + cS.playerSize > bubble.posY and bG.lockY < bubble.posY + bubble.size):
                cS.expCount += bubble.value
                cS.experienceList.remove(bubble)

        if(bG.lockX + cS.playerSize + cS.aura > bubble.posX and bG.lockX - cS.aura < bubble.posX + bubble.size):
            if(bG.lockY + cS.playerSize + cS.aura > bubble.posY and bG.lockY - cS.aura < bubble.posY + bubble.size):

                bubble.naturalSpawn = False
                
                originX = bG.lockX + cS.playerSize/2
                originY = bG.lockY + cS.playerSize/2

                #This is direct center x, y of player

                deltaX = bubble.posX - originX
                deltaY = bubble.posY - originY

                #This is direct xhat, yhat vector towards player

                if (deltaX == 0):
                    if(deltaY > 0):
                        bubble.direction = pi/2
                    else:
                        bubble.direction = -pi/2
                else:
                    
                    if(deltaX > 0):

                        bubble.direction = atan(deltaY/deltaX)
                    else:
                        deltaX = abs(bubble.posX - originX)

                        bubble.direction = -atan(deltaY/deltaX) + pi
            else:
                bubble.naturalSpawn = True
        else:
            bubble.naturalSpawn = True
    
def drawInformationSheet():
    cS.informationSheet.drawSheet()