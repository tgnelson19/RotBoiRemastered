from enum import Enum
from os import environ
import pygame as pg

#Game States
class States(Enum):
    TITLESCREEN = 0
    GAMERUN = 1
    LEVELING = 2
    
state = States.GAMERUN

environ['SDL_VIDEO_CENTERED'] = '1'
pg.init()  # Initializes a window
pg.display.set_caption("RotBoiRemastered")

tileSizeGlobal = 40 #Global tile size that should hopefully not look too bad for people...
frameRate = 240 #Default maximum framerate for the game to run at

scalar = 1.5 #For future use for non-fullscreen gameplay
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