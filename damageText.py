import pygame
import variableHolster as vH
import uiTheme as ui
import background as bG

#Simple text that floats above the target with a certain value
class DamageText:

    def __init__(self, entX, entY, color, value, objSize, framerate):
        self.worldX = entX
        self.worldY = entY
        self.posX, self.posY = bG.world_to_screen(entX, entY)
        self.color = color
        self.value = value
        self.lifetimeMax = framerate
        self.frameRate = framerate
        self.lifetime = framerate / 2
        self.objSize = objSize
        self.deleteMe = False
        self.deltaVal = 10

    def drawAndUpdateDamageText(self, pDX, pDY):
        speedMod = 1
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

        if self.lifetime > 0:
            self.lifetime -= vH.get_frame_scale() * 2
            self.deltaVal += vH.get_frame_scale() * speedMod
        if self.lifetime <= 0:
            self.deleteMe = True

        label = str(round(self.value)) if isinstance(self.value, (int, float)) else str(self.value)
        shadow_render = vH.damageTextFont.render(label, True, ui.INK)
        textRender = vH.damageTextFont.render(label, True, self.color)
        textRect = textRender.get_rect(center=(self.posX + self.objSize / 2, self.posY - self.deltaVal))
        vH.screen.blit(shadow_render, textRect.move(3, 3))
        vH.screen.blit(textRender, textRect)
