import pygame as pg
import variableHolster as vH
import background as bG

playerSpeed = 3.5
playerSize = vH.tileSizeGlobal
playerColor = pg.Color(0,0,255)

dX, dY = 0, 0

playerRect = pg.Rect(bG.lockX, bG.lockY, playerSize, playerSize)

projectileCount = 1
azimuthalProjectileAngle = 0

attackCooldownStat = 20
attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

bulletDamage = 1
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
expNeededForNextLevel = 50
baseExpNeededForNextLevel = 50
levelScaleIncreaseFunction = 1.2

healthPoints = 10
maxHealthPoints = 10
defense = 0

bulletHolster = []