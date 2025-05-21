import pygame as pg
import variableHolster as vH
import characterStats as cS

class InformationSheet:

    def __init__(self):
        
        self.totalLength = vH.sW * 0.25
        self.totalHeight = vH.sH
        self.posX = vH.sW * 0.75
        self.posY = 0

        self.backOfSheetColor = pg.Color(40,40,40)
        self.inDel = 3

    def drawSheet(self):
        pg.draw.rect(vH.screen, self.backOfSheetColor, pg.Rect(self.posX, self.posY, self.totalLength, self.totalHeight))