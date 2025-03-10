import variableHolster as vH
import background as bG
import pygame as pg

playerSpeed = 3.5
playerSize = vH.tileSizeGlobal / 2
playerColor = pg.Color(0,0,255)

lockX = (vH.sW / 2) - (playerSize/2)
lockY = (vH.sH / 2) - (playerSize/2)

def movePlayer():
    global posX, posY
    dX, dY = 0,0

    if vH.keys[pg.K_w]: dY -= 1
    if vH.keys[pg.K_a]: dX -= 1
    if vH.keys[pg.K_s]: dY += 1
    if vH.keys[pg.K_d]: dX += 1
    
    if abs(dX) + abs(dY) == 2: scalar = 0.707
    else: scalar = 1

    bG.posX -= dX * scalar * playerSpeed
    bG.posY -= dY * scalar * playerSpeed
    
def drawPlayer():
    bG.drawBackground()
    pg.draw.rect(vH.screen, playerColor, pg.Rect(lockX, lockY, playerSize, playerSize))
    
