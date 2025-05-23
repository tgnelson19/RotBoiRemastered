import pygame as pg
import variableHolster as vH
import background as bG
from informationSheet import InformationSheet

highestLevel = 0

playerSpeed = 3.5
playerSize = vH.tileSizeGlobal
playerColor = pg.Color(0,0,120)

dX, dY = 0, 0

currTileX = 0
currTileY = 0

playerRect = pg.Rect(bG.lockX, bG.lockY, playerSize, playerSize)

projectileCount = 1
azimuthalProjectileAngle = 0

attackCooldownStat = 10
attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

bulletDamage = 10
bulletSpeed = 5
bulletRange = 200
bulletSize = vH.tileSizeGlobal / 2
bulletColor = pg.Color(125,125,125)
bulletPierce = 1
critChance = 0.05
critDamage = 2

aura = 50
auraSpeed = 4
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

fdX, fdY = 0, 0

bulletHolster = []
enemyHolster = []
damageTextList = []
experienceList = []

informationSheet = InformationSheet()