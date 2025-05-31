from enum import Enum
from os import environ
import pygame as pg

#Game States
class States(Enum):
    TITLESCREEN = 0
    GAMERUN = 1
    LEVELING = 2
    
state = States.GAMERUN

hasBeenReset = False

environ['SDL_VIDEO_CENTERED'] = '1'
pg.init()  # Initializes a window
pg.display.set_caption("RotBoiRemastered")

tileSizeGlobal = 40 #Global tile size that should hopefully not look too bad for people...
frameRate = 240 #Default maximum framerate for the game to run at

scalar = 2 #For future use for non-fullscreen gameplay
infoObject = pg.display.Info() # Gets info about native monitor res

sW, sH = (infoObject.current_w/scalar, infoObject.current_h/scalar)

numTX = sW / tileSizeGlobal #Total number of X axis tiles
numTY = sH / tileSizeGlobal #Total number of Y axis tiles

pg.display.set_mode((infoObject.current_w, infoObject.current_h), vsync=1)

if scalar == 1:
    screen = pg.display.set_mode([sW, sH], pg.FULLSCREEN)  # Makes a screen that's fullscreen
else:
    screen = pg.display.set_mode([sW, sH])  # Makes a screen that's not fullscreen

clock = pg.time.Clock()  # Main time keeper

done = False
mouseDown = False

keys = pg.key.get_pressed()

mouseX = 0
mouseY = 0

backgroundColor = pg.Color(0,0,0)

damageTextFont = pg.font.Font("media/coolveticarg.otf", 40)

def resetAllStats():
    
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