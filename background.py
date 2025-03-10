import pygame as pg
import variableHolster as vH
import character as cH

bgMap = pg.image.load("images/rotmgMap.png").convert()
bgMap = pg.transform.scale_by(bgMap, 20)

bgH = bgMap.get_height()
bgW = bgMap.get_width()

posX = -10000
posY = -10000

def drawBackground():
    vH.screen.blit(bgMap, (posX, posY))