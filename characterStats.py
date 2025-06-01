import pygame as pg
import variableHolster as vH
import background as bG
from informationSheet import InformationSheet
from levelingHandler import LevelingHandler

highestLevel = 0

playerSpeed = 2.5
playerSize = vH.tileSizeGlobal
playerColor = pg.Color(0,0,120)

dX, dY = 0, 0

currTileX = 0
currTileY = 0

playerRect = pg.Rect(bG.lockX, bG.lockY, playerSize, playerSize)

projectileCount = 2
azimuthalProjectileAngle = 200

attackCooldownStat = 40
attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

bulletDamage = 1
bulletSpeed = 4
bulletRange = 150
bulletSize = vH.tileSizeGlobal / 2
bulletColor = pg.Color(125,125,125)
bulletPierce = 1
critChance = 0.05
critDamage = 2

aura = 50
auraSpeed = 2
levelMod = 1.1
xpMult = 1
currentLevel = 0
expCount = 0
expNeededForNextLevel = 50
baseExpNeededForNextLevel = 50
levelScaleIncreaseFunction = 1.2

healthPoints = 10
maxHealthPoints = 10
defense = 0

enemyOneInFramesChance = 360

numOfEnemiesKilled = 0
currentStage = 1
xpMult = 1
experienceStageMod = 1.1

dashDuration = vH.frameRate * 0.1
dashing = False

dashModifier = 4

dashCooldownMax = vH.frameRate * 1
currDashCooldown = 0

autoFire = False
autoFlop = False

fdX, fdY = 0, 0

bulletHolster = []
enemyHolster = []
damageTextList = []
experienceList = []

informationSheet = InformationSheet()

levelingHandler = LevelingHandler()

newRandoUps = False

collectiveStats = {"Defense" : defense, "Bullet Pierce" : bulletPierce, "Bullet Count" : projectileCount, "Spread Angle" : azimuthalProjectileAngle, 
                                  "Attack Speed" : attackCooldownStat, "Bullet Speed" : bulletSpeed, "Bullet Range" : bulletRange, "Bullet Damage" : bulletDamage, 
                                  "Bullet Size" : bulletSize, "Player Speed" : playerSpeed, "Crit Chance" : critChance, "Crit Damage" : critDamage, 
                                  "Aura Size" : aura, "Aura Strength" : auraSpeed, "Exp Multiplier": xpMult}
        
collectiveAddStats = {"Defense" : [0], "Bullet Pierce" : [0], "Bullet Count" : [0], "Spread Angle" : [0], 
                            "Attack Speed" : [0], "Bullet Speed" : [0], "Bullet Range" : [0], "Bullet Damage" : [0], 
                            "Bullet Size" : [0], "Player Speed" : [0], "Crit Chance": [0], "Crit Damage": [0],
                            "Aura Size" : [0], "Aura Strength" : [0], "Exp Multiplier": [0]}

collectiveMultStats = {"Defense" : [1], "Bullet Pierce" : [1], "Bullet Count" : [1], "Spread Angle" : [1], 
                            "Attack Speed" : [1], "Bullet Speed" : [1], "Bullet Range" : [1], "Bullet Damage" : [1], 
                            "Bullet Size" : [1], "Player Speed" : [1], "Crit Chance": [1], "Crit Damage": [1],
                            "Aura Size" : [1], "Aura Strength" : [1], "Exp Multiplier": [1]}

gracePeriod = 10