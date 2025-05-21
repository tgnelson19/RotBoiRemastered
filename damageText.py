import pygame
import variableHolster as vH

#Simple text that floats above the target with a certain value
class DamageText:

    def __init__(self, entX, entY, textBaseSize, color, value, objSize, framerate):
        self.posX = entX
        self.posY = entY
        self.color = color
        self.value = value
        self.textSize = textBaseSize
        self.lifetimeMax = framerate
        self.frameRate = framerate
        self.lifetime = framerate
        self.objSize = objSize
        self.deleteMe = False
        self.deltaVal = 10
        self.font = pygame.font.Font("media/coolveticarg.otf", int(self.textSize))

    def drawAndUpdateDamageText(self, pDX, pDY):
        
        speedMod = 1
        
        self.posX += pDX
        self.posY += pDY

        if (self.lifetime > 0):
            self.lifetime -= (120/self.frameRate)*2
            self.deltaVal += (120/self.frameRate)*(speedMod)
        if (self.lifetime <= 0):
            self.deleteMe = True
        
        textRender = self.font.render("- " + str(format(self.value, '.3g')), True, self.color)
        textRect = textRender.get_rect(center = (self.posX + self.objSize/2, self.posY - self.deltaVal))
        vH.screen.blit(textRender, textRect)