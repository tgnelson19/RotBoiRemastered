import pygame as pg
import variableHolster as vH

import csv

def loadCSVToBG(filename):
    """Loads data from a CSV file into a list of lists (array)."""
    data_array = []
    with open(filename, 'r') as file:
        csv_reader = csv.reader(file)
        for row in csv_reader:
            data_array.append(row)
    return data_array

def loadBackgroundRects(roomFile):
    roomCSV = loadCSVToBG(roomFile)
    newRects = []
    for i in range(len(roomCSV)):
            newRects.append([])
            for j in range(len(roomCSV[0])):
                newRects[i].append([int(roomCSV[i][j]), 
                                     pg.Rect(i*vH.tileSizeGlobal, 
                                            j*vH.tileSizeGlobal, 
                                            vH.tileSizeGlobal,
                                            vH.tileSizeGlobal)])
    return newRects

def drawRepasteableBackground(roomRects):
    newSurface = pg.surface.Surface((len(roomRects[0]) * vH.tileSizeGlobal, len(roomRects)* vH.tileSizeGlobal))
    for i in range(len(roomRects)):
        for j in range(len(roomRects[0])):
            currRectData = roomRects[i][j]
            currRectData[1].left = (vH.tileSizeGlobal * j)
            currRectData[1].top = (vH.tileSizeGlobal * i)
            pg.draw.rect(newSurface, tileTypes[roomRects[i][j][0]][1], currRectData[1])
    return newSurface

def moveAndDisplayBackground(surface):
    vH.screen.blit(surface, (-playerPosX + lockX, -playerPosY + lockY))

# Example usage:

tileTypes = {
            0 : ["default", pg.Color(170,170,170)],
            1 : ["wall", pg.Color(90,90,90)]
            }

basicRoomFile = 'backgrounds/basicRoom.csv'
basicRoomRects = loadBackgroundRects(basicRoomFile)
currRoomRects = basicRoomRects

#playerPosX = ((len(currRoomRects[0]) * vH.tileSizeGlobal)/2)
#playerPosY = ((len(currRoomRects) * vH.tileSizeGlobal)/2)

lockX = (vH.sW / 2)
lockY = (vH.sH / 2)

playerPosX = 100
playerPosY = 100

repasteableRoomSurface = drawRepasteableBackground(currRoomRects)