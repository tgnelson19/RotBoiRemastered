import variableHolster as vH
import background as bG
import characterStats as cS
import pygame as pg
from bullet import Bullet
from enemy import Enemy
from damageText import DamageText
from experienceBubble import ExperienceBubble
from informationSheet import InformationSheet
from levelingHandler import LevelingHandler
from math import pi

from math import floor, ceil, pi, atan, trunc
from random import randint

#
#   Warning, all stats added to character stats needs to be included in reset here in order to be reset
#   This is bad design, but I am working on figuring out a way to reset the stats without using this
#   Also note that some stats are located in different sheets and must be accounted for as well
#
#   FOR QUICK STAT MODIFICATIONS CHANGE THEM HERE
#

titleFont = pg.font.Font("media/coolveticarg.otf", int(vH.tileSizeGlobal*(2/3)))
textColor = (245,245,220)

def resetAllStats():
    
    bG.playerPosX = 200
    bG.playerPosY = 200
    
    cS.playerSpeed = 2.5
    cS.playerSize = vH.tileSizeGlobal
    cS.playerColor = pg.Color(0,0,120)

    cS.dX, dY = 0, 0

    cS.currTileX = 0
    cS.currTileY = 0

    cS.playerRect = pg.Rect(bG.lockX, bG.lockY, cS.playerSize, cS.playerSize)

    cS.projectileCount = 1
    cS.azimuthalProjectileAngle = pi/8

    cS.attackCooldownStat = 40
    cS.attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

    cS.bulletDamage = 1
    cS.bulletSpeed = 4
    cS.bulletRange = 250
    cS.bulletSize = vH.tileSizeGlobal / 2
    cS.bulletColor = pg.Color(125,125,125)
    cS.bulletPierce = 1
    cS.critChance = 0.05
    cS.critDamage = 2

    cS.aura = 50
    cS.auraSpeed = 2
    cS.levelMod = 1.1
    cS.xpMult = 1
    cS.currentLevel = 0
    cS.expCount = 0
    cS.expNeededForNextLevel = 25
    cS.baseExpNeededForNextLevel = 25
    cS.levelScaleIncreaseFunction = 1.2

    cS.healthPoints = 10
    cS.maxHealthPoints = 10
    cS.defense = 0

    cS.enemyOneInFramesChance = 360

    cS.numOfEnemiesKilled = 0
    cS.currentStage = 1
    cS.xpMult = 1
    cS.experienceStageMod = 1.1

    cS.dashDuration = vH.frameRate * 0.1
    cS.dashing = False

    cS.dashModifier = 4

    cS.dashCooldownMax = vH.frameRate * 1
    cS.currDashCooldown = 0

    cS.fdX, cS.fdY = 0, 0

    cS.bulletHolster = []
    cS.enemyHolster = []
    cS.damageTextList = []
    cS.experienceList = []

    cS.informationSheet = InformationSheet()
    
    cS.levelingHandler = LevelingHandler()
    
    cS.newRandoUps = False
    
    cS.collectiveStats = {"Defense" : cS.defense, "Bullet Pierce" : cS.bulletPierce, "Bullet Count" : cS.projectileCount, "Spread Angle" : cS.azimuthalProjectileAngle, 
                                  "Attack Speed" : cS.attackCooldownStat, "Bullet Speed" : cS.bulletSpeed, "Bullet Range" : cS.bulletRange, "Bullet Damage" : cS.bulletDamage, 
                                  "Bullet Size" : cS.bulletSize, "Player Speed" : cS.playerSpeed, "Crit Chance" : cS.critChance, "Crit Damage" : cS.critDamage, 
                                  "Aura Size" : cS.aura, "Aura Strength" : cS.auraSpeed, "Exp Multiplier": cS.xpMult}
        
    cS.collectiveAddStats = {"Defense" : [0], "Bullet Pierce" : [0], "Bullet Count" : [0], "Spread Angle" : [0], 
                                "Attack Speed" : [0], "Bullet Speed" : [0], "Bullet Range" : [0], "Bullet Damage" : [0], 
                                "Bullet Size" : [0], "Player Speed" : [0], "Crit Chance": [0], "Crit Damage": [0],
                                "Aura Size" : [0], "Aura Strength" : [0], "Exp Multiplier": [0]}
    
    cS.collectiveMultStats = {"Defense" : [1], "Bullet Pierce" : [1], "Bullet Count" : [1], "Spread Angle" : [1], 
                                "Attack Speed" : [1], "Bullet Speed" : [1], "Bullet Range" : [1], "Bullet Damage" : [1], 
                                "Bullet Size" : [1], "Player Speed" : [1], "Crit Chance": [1], "Crit Damage": [1],
                                "Aura Size" : [1], "Aura Strength" : [1], "Exp Multiplier": [1]}
    
def multiply_list(list):
        result = 1
        for num in list:
            result *= num
        return result
    
def combarinoPlayerStats():
    cS.projectileCount = (cS.collectiveStats["Bullet Count"] + sum(cS.collectiveAddStats["Bullet Count"])) * (multiply_list(cS.collectiveMultStats["Bullet Count"]))
    cS.azimuthalProjectileAngle = (cS.collectiveStats["Spread Angle"] + sum(cS.collectiveAddStats["Spread Angle"])) * (multiply_list(cS.collectiveMultStats["Spread Angle"]))
    cS.playerSpeed = (cS.collectiveStats["Player Speed"] + sum(cS.collectiveAddStats["Player Speed"])) * (multiply_list(cS.collectiveMultStats["Player Speed"]))
    cS.attackCooldownStat = (cS.collectiveStats["Attack Speed"] + sum(cS.collectiveAddStats["Attack Speed"])) * (multiply_list(cS.collectiveMultStats["Attack Speed"]))
    if(cS.attackCooldownStat <= 1): cS.attackCooldownStat = 1
    cS.bulletSpeed = (cS.collectiveStats["Bullet Speed"] + sum(cS.collectiveAddStats["Bullet Speed"])) * (multiply_list(cS.collectiveMultStats["Bullet Speed"]))
    cS.bulletRange = (cS.collectiveStats["Bullet Range"] + sum(cS.collectiveAddStats["Bullet Range"])) * (multiply_list(cS.collectiveMultStats["Bullet Range"]))
    cS.bulletSize = (cS.collectiveStats["Bullet Size"] + sum(cS.collectiveAddStats["Bullet Size"])) * (multiply_list(cS.collectiveMultStats["Bullet Size"]))
    cS.bulletDamage = (cS.collectiveStats["Bullet Damage"] + sum(cS.collectiveAddStats["Bullet Damage"])) * (multiply_list(cS.collectiveMultStats["Bullet Damage"]))
    cS.bulletPierce = (cS.collectiveStats["Bullet Pierce"] + sum(cS.collectiveAddStats["Bullet Pierce"])) * (multiply_list(cS.collectiveMultStats["Bullet Pierce"]))
    cS.defense = (cS.collectiveStats["Defense"] + sum(cS.collectiveAddStats["Defense"])) * (multiply_list(cS.collectiveMultStats["Defense"]))
    cS.critChance = (cS.collectiveStats["Crit Chance"] + sum(cS.collectiveAddStats["Crit Chance"])) * (multiply_list(cS.collectiveMultStats["Crit Chance"]))
    cS.critDamage = (cS.collectiveStats["Crit Damage"] + sum(cS.collectiveAddStats["Crit Damage"])) * (multiply_list(cS.collectiveMultStats["Crit Damage"]))
    cS.aura = (cS.collectiveStats["Aura Size"] + sum(cS.collectiveAddStats["Aura Size"])) * (multiply_list(cS.collectiveMultStats["Aura Size"]))
    cS.auraSpeed = (cS.collectiveStats["Aura Strength"] + sum(cS.collectiveAddStats["Aura Strength"])) * (multiply_list(cS.collectiveMultStats["Aura Strength"]))
    cS.xpMult = (cS.collectiveStats["Exp Multiplier"]+ sum(cS.collectiveAddStats["Exp Multiplier"])) * (multiply_list(cS.collectiveMultStats["Exp Multiplier"]))

def handleLevelingProcess():
    
    if (not cS.newRandoUps):
        cS.levelingHandler.randomizeLevelUp()
        cS.newRandoUps = True

    cS.levelingHandler.drawCards()
    
    pDecision = cS.levelingHandler.PlayerClicked()

    if (pDecision == "leftCard"):
        if (cS.levelingHandler.leftCardUpgradeMath == "addative"):
            cS.collectiveAddStats[cS.levelingHandler.leftCardUpgradeType].append(cS.levelingHandler.upgradeRarity[cS.levelingHandler.leftCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesAdd[cS.levelingHandler.leftCardUpgradeType])
        if (cS.levelingHandler.leftCardUpgradeMath == "multiplicative"):
            cS.collectiveMultStats[cS.levelingHandler.leftCardUpgradeType].append(1 + cS.levelingHandler.upgradeRarity[cS.levelingHandler.leftCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesMult[cS.levelingHandler.leftCardUpgradeType])

    elif (pDecision == "midCard"):
        if (cS.levelingHandler.midCardUpgradeMath == "addative"):
            cS.collectiveAddStats[cS.levelingHandler.midCardUpgradeType].append(cS.levelingHandler.upgradeRarity[cS.levelingHandler.midCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesAdd[cS.levelingHandler.midCardUpgradeType])
        if (cS.levelingHandler.midCardUpgradeMath == "multiplicative"):
            cS.collectiveMultStats[cS.levelingHandler.midCardUpgradeType].append(1 + cS.levelingHandler.upgradeRarity[cS.levelingHandler.midCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesMult[cS.levelingHandler.midCardUpgradeType])

    elif (pDecision == "rightCard"):
        if (cS.levelingHandler.rightCardUpgradeMath == "addative"):
            cS.collectiveAddStats[cS.levelingHandler.rightCardUpgradeType].append(cS.levelingHandler.upgradeRarity[cS.levelingHandler.rightCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesAdd[cS.levelingHandler.rightCardUpgradeType])
        if (cS.levelingHandler.rightCardUpgradeMath == "multiplicative"):
            cS.collectiveMultStats[cS.levelingHandler.rightCardUpgradeType].append(1 + cS.levelingHandler.upgradeRarity[cS.levelingHandler.rightCardUpgradeRarity] * cS.levelingHandler.upgradeBasicTypesMult[cS.levelingHandler.rightCardUpgradeType])

    if (pDecision != "none"):
        combarinoPlayerStats()
        cS.newRandoUps = False
        cS.gracePeriod = vH.frameRate * 2
        vH.state = vH.States.GAMERUN

def movePlayer():
    global playerColor
    
    if vH.keys[pg.K_SPACE] and cS.currDashCooldown == 0: cS.dashing = True; cS.currDashCooldown = cS.dashCooldownMax; cS.fdX = cS.dX; cS.fdY = cS.dY
    
    if cS.currDashCooldown > 0: cS.currDashCooldown -= 1
    
    if(not cS.dashing):

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
    
    else:
        if abs(cS.dX) + abs(cS.dY) == 2: scalar = 0.707
        else: scalar = 1
        
        #Now we have velocities scaled by diagonal if needed
        cS.dX, cS.dY = cS.fdX * scalar * cS.dashModifier * cS.playerSpeed * (120/vH.frameRate), cS.fdY * scalar * cS.dashModifier * cS.playerSpeed * (120/vH.frameRate)
        
        if cS.currDashCooldown <= (cS.dashCooldownMax - cS.dashDuration):
            cS.dashing = False

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

    if (cS.attackCooldownTimer <= 0 and (cS.autoFire or vH.mouseDown)):
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
        
        eSpeed = 1 * (cS.levelMod ** cS.currentLevel)
        eSize = vH.tileSizeGlobal
        eColor = pg.Color(255,0,0)
        eDamage = 1 * (cS.levelMod ** cS.currentLevel)
        eHP = 3 * (cS.levelMod ** cS.currentLevel)
        eEXP = 3 * (cS.levelMod ** cS.currentLevel)
        
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
                            cS.damageTextList.append(DamageText(eman.posX, eman.posY, currColor, bullet.damage, eman.size, vH.frameRate))
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
                
                while (cS.expCount >= cS.expNeededForNextLevel):
                    cS.currentLevel += 1
                    cS.expCount -= cS.expNeededForNextLevel
                    cS.informationSheet.updateCurrLevel()
                    cS.expNeededForNextLevel *= cS.levelScaleIncreaseFunction
                    cS.healthPoints = cS.maxHealthPoints
                    cS.enemyOneInFramesChance = cS.enemyOneInFramesChance / (cS.levelMod)
                    vH.state = vH.States.LEVELING
                
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
            
def hurtPlayer():
    for eman in cS.enemyHolster:
        if(bG.lockX + cS.playerSize > eman.posX and bG.lockX < eman.posX + eman.size):
            if(bG.lockY + cS.playerSize > eman.posY and bG.lockY < eman.posY + eman.size):
                cS.numOfEnemiesKilled += 1
                cS.enemyHolster.remove(eman)
                cS.experienceList.append(ExperienceBubble(eman.posX, eman.posY, cS.xpMult* (eman.expValue*(cS.currentStage*cS.experienceStageMod)), vH.frameRate))
                trueDMG = eman.damage - cS.defense
                if (trueDMG < 0):
                    trueDMG = 0
                cS.damageTextList.append(DamageText(bG.lockX, bG.lockY, pg.Color(200,100,0), trueDMG, vH.tileSizeGlobal, vH.frameRate))
                
                cS.healthPoints -= trueDMG
                if (cS.healthPoints <= 0):
                    vH.state = vH.States.TITLESCREEN
                    cS.highestLevel = cS.currentLevel
    
def drawInformationSheet():
    cS.informationSheet.drawSheet()
    
def runTheTitleScreen():
    
    #Displays title texts on title screen
    textRender = titleFont.render("RbR : Press Space To Play", True, textColor)
    textRect = textRender.get_rect(center = (vH.sW/2, vH.sH/2))
    vH.screen.blit(textRender, textRect)

    textRender = titleFont.render("WASD to Move, Mouse to Shoot, I to Autofire, O for light/dark mode", True, textColor)
    textRect = textRender.get_rect(center = (vH.sW/2, vH.sH*(2/3)))
    vH.screen.blit(textRender, textRect)

    textRender = titleFont.render("Highest Level So Far: " + str(cS.highestLevel), True, textColor)
    textRect = textRender.get_rect(center = (vH.sW/2, vH.sH*(4/5)))
    vH.screen.blit(textRender, textRect)

    if (vH.keys[pg.K_SPACE]):
        vH.state = vH.States.GAMERUN
        cS.highestLevel = 0
        