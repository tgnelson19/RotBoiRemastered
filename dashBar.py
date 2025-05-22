import pygame
import variableHolster as vH

#When reaches full, give a level, located in top right corner
class DashBar:

    def __init__(self):
        
        self.totalLength = vH.sW * 0.21
        self.totalHeight = vH.sH * 0.05
        self.posX = vH.sW * 0.77
        self.posY = vH.sH * 0.26

        self.dashColor = (100,100,100)
        self.outerBarColor = pygame.Color(70,70,70)
        self.fakeInnerColor = pygame.Color(20,20,20)
        self.inDel = 3
        

    def drawBar(self, screen, currPercentage):
        pygame.draw.rect(screen, self.outerBarColor, pygame.Rect(self.posX, self.posY, self.totalLength, self.totalHeight))
        pygame.draw.rect(screen, self.fakeInnerColor, pygame.Rect(self.posX + self.inDel, self.posY + self.inDel, self.totalLength - 2*self.inDel, self.totalHeight- 2*self.inDel))
        pygame.draw.rect(screen, pygame.Color(self.dashColor), pygame.Rect(self.posX + self.inDel, self.posY + self.inDel, int(self.totalLength*(currPercentage)) - 2*self.inDel, self.totalHeight - 2*self.inDel))