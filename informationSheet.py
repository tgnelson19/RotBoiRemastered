import pygame as pg
import variableHolster as vH
import characterStats as cS
from levelBar import LevelBar
from dashBar import DashBar
from hpBar import HPBar

class InformationSheet:

    def __init__(self):
        
        self.totalLength = vH.sW * 0.25
        self.totalHeight = vH.sH
        self.posX = vH.sW * 0.75
        self.posY = 0

        self.backOfSheetColor = pg.Color(40,40,40)
        self.inDel = 3
        
        self.levelBar = LevelBar()
        self.dashBar = DashBar()
        self.hpBar = HPBar()
        
        self.informationFont = pg.font.Font("media/coolveticarg.otf", 10)
        
        self.levelBarTextColor = pg.Color(200,200,200)
        
        self.levelBarTextRender = vH.damageTextFont.render("Level : " + str(cS.currentLevel) , True, self.levelBarTextColor)
        self.levelBarTextRenderRect = self.levelBarTextRender.get_rect(center = (self.levelBar.posX + self.levelBar.totalLength/2, self.levelBar.posY + self.levelBar.totalHeight/2))
        
        self.dashBarTextRender = vH.damageTextFont.render("Dash Cooldown" , True, self.levelBarTextColor)
        self.dashBarTextRenderRect = self.dashBarTextRender.get_rect(center = (self.dashBar.posX + self.dashBar.totalLength/2, self.dashBar.posY + self.dashBar.totalHeight/2))
        
        self.hpBarTextRender = vH.damageTextFont.render("Hit Points" , True, self.levelBarTextColor)
        self.hpBarTextRenderRect = self.hpBarTextRender.get_rect(center = (self.hpBar.posX + self.hpBar.totalLength/2, self.hpBar.posY + self.hpBar.totalHeight/2))

    def updateCurrLevel(self):
        self.levelBarTextRender = vH.damageTextFont.render("Level : " + str(cS.currentLevel) , True, self.levelBarTextColor)
        self.levelBarTextRenderRect = self.levelBarTextRender.get_rect(center = (self.levelBar.posX + self.levelBar.totalLength/2, self.levelBar.posY + self.levelBar.totalHeight/2))

    def drawSheet(self):
        pg.draw.rect(vH.screen, self.backOfSheetColor, pg.Rect(self.posX, self.posY, self.totalLength, self.totalHeight))
        self.levelBar.drawBar(vH.screen, cS.expCount / cS.expNeededForNextLevel)
        self.dashBar.drawBar(vH.screen, 1-(cS.currDashCooldown / cS.dashCooldownMax))
        self.hpBar.drawBar(vH.screen, cS.healthPoints/cS.maxHealthPoints)
        
        vH.screen.blit(self.levelBarTextRender, self.levelBarTextRenderRect)
        vH.screen.blit(self.dashBarTextRender, self.dashBarTextRenderRect)
        vH.screen.blit(self.hpBarTextRender, self.hpBarTextRenderRect)