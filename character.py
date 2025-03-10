import variableHolster as vH
import background as bG
import pygame as pg

playerSpeed = 3.5 / (vH.frameRate / 120)
playerSize = vH.tileSizeGlobal
playerColor = pg.Color(0,0,255)
canMove = [True, True, True, True]

lockX = (vH.sW / 2) - (playerSize/2)
lockY = (vH.sH / 2) - (playerSize/2)

playerRect = pg.Rect(lockX, lockY, playerSize, playerSize)

def movePlayer():
    dX, dY = 0,0

    if vH.keys[pg.K_w]: dY -= 1
    if vH.keys[pg.K_a]: dX -= 1
    if vH.keys[pg.K_s]: dY += 1
    if vH.keys[pg.K_d]: dX += 1
    
    if abs(dX) + abs(dY) == 2: scalar = 0.707
    else: scalar = 1

    newPosX = bG.playerPosX - dX * scalar * playerSpeed
    newPosY = bG.playerPosY - dY * scalar * playerSpeed

    bG.playerPosX = newPosX
    bG.playerPosY = newPosY
    
def drawPlayer():
    pg.draw.rect(vH.screen, playerColor, playerRect)

def drawBackground():
    bG.moveAndDrawBackground(bG.basicRoomRects)
