import pygame as pg
import variableHolster as vH

import csv


def loadCSVToBG(filename):
    """Loads data from a CSV file into a list of lists (array)."""
    data_array = [] # The background as displayed in the editor is stored as a 2D array of integers, where each integer corresponds to a tile type.
    with open(filename, 'r') as file:
        csv_reader = csv.reader(file)
        for row in csv_reader:
            data_array.append(row)
    return data_array

def loadBackgroundRects(roomFile):
    roomCSV = loadCSVToBG(roomFile)
    newRects = []
    for rowIndex, row in enumerate(roomCSV):
        newRects.append([])
        for colIndex, cell in enumerate(row):
            newRects[rowIndex].append([
                int(cell),
                pg.Rect(colIndex * vH.tileSizeGlobal,
                        rowIndex * vH.tileSizeGlobal,
                        vH.tileSizeGlobal,
                        vH.tileSizeGlobal)
            ])
    return newRects

def drawRepasteableBackground(roomRects):
    newSurface = pg.surface.Surface((len(roomRects[0]) * vH.tileSizeGlobal, len(roomRects) * vH.tileSizeGlobal))
    for rowIndex in range(len(roomRects)):
        for colIndex in range(len(roomRects[0])):
            currRectData = roomRects[rowIndex][colIndex]
            currRectData[1].left = (vH.tileSizeGlobal * colIndex)
            currRectData[1].top = (vH.tileSizeGlobal * rowIndex)
            pg.draw.rect(newSurface, tileTypes[currRectData[0]][1], currRectData[1])
    return newSurface

def screen_to_world(screen_x, screen_y):
    """Convert a screen coordinate into a world coordinate."""
    return screen_x - lockX + playerPosX, screen_y - lockY + playerPosY

def world_to_screen(world_x, world_y):
    """Convert a world coordinate into a screen coordinate."""
    return world_x - playerPosX + lockX, world_y - playerPosY + lockY

def rect_hits_wall(world_rect):
    """Return True if any tile overlapped by the world rect is a wall."""
    left = int(world_rect.left // vH.tileSizeGlobal)
    top = int(world_rect.top // vH.tileSizeGlobal)
    right = int((world_rect.right - 1) // vH.tileSizeGlobal)
    bottom = int((world_rect.bottom - 1) // vH.tileSizeGlobal)

    if left < 0 or top < 0 or bottom >= len(currRoomRects) or right >= len(currRoomRects[0]):
        return True

    for tileY in range(top, bottom + 1):
        for tileX in range(left, right + 1):
            if currRoomRects[tileY][tileX][0] == 1:
                return True
    return False


def count_overlapping_walls(world_rect):
    """Return the number of wall tiles overlapped by world_rect.

    Out-of-bounds areas count as a large number so callers treat them as impassable.
    """
    left = int(world_rect.left // vH.tileSizeGlobal)
    top = int(world_rect.top // vH.tileSizeGlobal)
    right = int((world_rect.right - 1) // vH.tileSizeGlobal)
    bottom = int((world_rect.bottom - 1) // vH.tileSizeGlobal)

    if left < 0 or top < 0 or bottom >= len(currRoomRects) or right >= len(currRoomRects[0]):
        return 10**6

    count = 0
    for tileY in range(top, bottom + 1):
        for tileX in range(left, right + 1):
            if currRoomRects[tileY][tileX][0] == 1:
                count += 1
    return count

def moveAndDisplayBackground(surface):
    vH.screen.blit(surface, (-playerPosX + lockX, -playerPosY + lockY))

# Example usage:

tileTypes = {
            0 : ["default", pg.Color(170,170,170)],
            1 : ["wall", pg.Color(90,90,90)]
            }

basicRoomFile = 'data/backgrounds/basicRoom.csv'
basicRoomRects = loadBackgroundRects(basicRoomFile)
currRoomRects = basicRoomRects

#playerPosX = ((len(currRoomRects[0]) * vH.tileSizeGlobal)/2)
#playerPosY = ((len(currRoomRects) * vH.tileSizeGlobal)/2)

lockX = ((vH.sW * 0.75) / 2)
lockY = (vH.sH / 2)

playerPosX = 200
playerPosY = 200

currNumOfXTiles = len(currRoomRects[0])
currNumOfYTiles = len(currRoomRects)

repasteableRoomSurface = drawRepasteableBackground(currRoomRects)