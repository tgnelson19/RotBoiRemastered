from enum import Enum
from os import environ
import pygame as pg

#Game States
class States(Enum):
    TITLESCREEN = 0
    GAMERUN = 1
    LEVELING = 2
    
state = States.TITLESCREEN

hasBeenReset = False

environ['SDL_VIDEO_CENTERED'] = '1'
pg.init()  # Initializes a window
pg.display.set_caption("RotBoiRemastered")

tileSizeGlobal = 50 #Global tile size that should hopefully not look too bad for people...
frameRate = 120 #Default maximum framerate for the game to run at

scalar = 1 #For future use for non-fullscreen gameplay, it does work in it's current state however which is nice
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
mousePressed = False

REFERENCE_FPS = 240
deltaMilliseconds = 1000 / frameRate
hasFrameDelta = False
keyPressed = set()


def get_frame_scale():
    """Return elapsed time in units of the original 240 Hz simulation step.

    The upper clamp prevents a debugger pause or window drag from teleporting every
    entity on the next frame.  Before the first frame, the configured cap remains a
    useful deterministic fallback for tests and object construction.
    """
    if not hasFrameDelta or deltaMilliseconds <= 0:
        return REFERENCE_FPS / frameRate if frameRate > 0 else 1.0
    return min(deltaMilliseconds * REFERENCE_FPS / 1000, REFERENCE_FPS * 0.05)


def set_delta_time(milliseconds):
    global deltaMilliseconds, hasFrameDelta
    deltaMilliseconds = milliseconds
    hasFrameDelta = True


def get_timer_step():
    """Elapsed time in units of one configured-FPS timer tick."""
    if not hasFrameDelta:
        return 1.0
    return min(deltaMilliseconds * frameRate / 1000, frameRate * 0.05)

keys = pg.key.get_pressed()

mouseX = 0
mouseY = 0

screenShakeX = 0
screenShakeY = 0

backgroundColor = pg.Color(17,20,27)

damageTextFont = pg.font.Font("data/media/coolveticarg.otf", 40)
