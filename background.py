import pygame as pg
import variableHolster as vH
import character as cH

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

# Example usage:
basicRoomFile = 'backgrounds/basicRoom.csv'
basicRoomRects = loadBackgroundRects(basicRoomFile)
currRoomRects = basicRoomRects

playerPosX = 0
playerPosY = 0

tileTypes = {
            0 : ["default", pg.Color(170,170,170)],
            1 : ["wall", pg.Color(90,90,90)]
            }

def moveAndDrawBackground(roomRects):
    for i in range(len(roomRects)):
        for j in range(len(roomRects[0])):
            currRectData = roomRects[i][j]
            oldRectData = currRectData
            currRectData[1].left = playerPosX + vH.tileSizeGlobal * j
            currRectData[1].top = playerPosY + vH.tileSizeGlobal * i
            if currRectData[0] == 1:
                if cH.playerRect.colliderect(currRectData[1]):        
                    currRectData = oldRectData
                    return 1
            pg.draw.rect(vH.screen, tileTypes[roomRects[i][j][0]][1], currRectData[1])
    return 0