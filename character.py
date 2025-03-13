import variableHolster as vH
import background as bG
import pygame as pg

from math import floor, ceil

playerSpeed = 3.5 / (vH.frameRate / 120)
playerSize = vH.tileSizeGlobal
playerColor = pg.Color(0,0,255)

playerRect = pg.Rect(bG.lockX, bG.lockY, playerSize, playerSize)

def movePlayer():
    global playerColor

    #Velocity at current time
    dX, dY = 0,0

    if vH.keys[pg.K_w]: dY += 1
    if vH.keys[pg.K_a]: dX += 1
    if vH.keys[pg.K_s]: dY -= 1
    if vH.keys[pg.K_d]: dX -= 1
    
    if abs(dX) + abs(dY) == 2: scalar = 0.707
    else: scalar = 1

    #Now we have velocities scaled by diagonal if needed


    #FUTURE exact position (NOT TILES) (floats)
    newABSPosX = ((bG.playerPosX) - dX * scalar * playerSpeed)
    newABSPosY = ((bG.playerPosY) - dY * scalar * playerSpeed)

    #Current Exact position in TILES (float)
    currTileX = bG.playerPosX / vH.tileSizeGlobal
    currTileY = bG.playerPosY / vH.tileSizeGlobal

    


    newTileLocXMin = floor(newABSPosX / vH.tileSizeGlobal) #Exact tile to the left
    newTileLocYMin = floor(newABSPosY / vH.tileSizeGlobal) #Exact tile to the top
    newTileLocXMax = ceil(newABSPosX / vH.tileSizeGlobal) #Exact tile to the right
    newTileLocYMax = ceil(newABSPosY / vH.tileSizeGlobal) #Exact tile to the bottom

    playerColor = pg.Color(0,0,255)

    flagX = False
    flagY = False

    # CASE: moving RIGHT
    if dX < 0: 
        if bG.currRoomRects[floor(currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].left
        elif bG.currRoomRects[ceil(currTileY)][newTileLocXMax][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].left
    # CASE: moving LEFT
    elif dX > 0:
        if bG.currRoomRects[ceil(currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].left
        elif bG.currRoomRects[floor(currTileY)][newTileLocXMin][0] == 1: flagX = True; bG.playerPosX = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].left
    
    # CASE: moving DOWN
    if dY < 0:
        if bG.currRoomRects[newTileLocYMax][floor(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMax][ceil(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[ceil(currTileY)][ceil(currTileX)][1].top
    # CASE: moving UP
    elif dY > 0:
        if bG.currRoomRects[newTileLocYMin][ceil(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].top
        elif bG.currRoomRects[newTileLocYMin][floor(currTileX)][0] == 1: flagY = True; bG.playerPosY = bG.currRoomRects[floor(currTileY)][floor(currTileX)][1].top
            
    if not flagX: bG.playerPosX = newABSPosX
    if not flagY: bG.playerPosY = newABSPosY



def drawPlayer():
    pg.draw.rect(vH.screen, playerColor, playerRect)

def drawBackground():
    bG.moveAndDisplayBackground(bG.repasteableRoomSurface)
