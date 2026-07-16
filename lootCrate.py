"""A stationary world-space container of loot dropped by a defeated enemy."""

import pygame

import background as bG
import uiTheme as ui
import upgrades
import variableHolster as vH


RARITY_ORDER = tuple(upgrades.RARITY_WEIGHTS)


class LootCrate:
    def __init__(self, worldX, worldY, drops):
        self.worldX = worldX
        self.worldY = worldY
        self.size = vH.tileSizeGlobal * .6
        self.items = list(drops)
        self.posX, self.posY = bG.world_to_screen(worldX, worldY)

    def _world_rect(self):
        return pygame.Rect(self.worldX, self.worldY, self.size, self.size)

    def _tint(self):
        if not self.items:
            return ui.BORDER
        best = max(self.items, key=lambda item: RARITY_ORDER.index(item.rarity))
        return ui.RARITY_COLORS.get(best.rarity, ui.BORDER)

    def draw(self):
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)
        rect = pygame.Rect(int(self.posX), int(self.posY), int(self.size), int(self.size))
        accent = self._tint()
        border = max(2, int(self.size * .08))
        shadow_rect = (rect.x + 4, rect.bottom - rect.height * .18, rect.width, rect.height * .18)
        pygame.draw.ellipse(vH.screen, ui.SHADOW, shadow_rect)
        pygame.draw.rect(vH.screen, ui.INK, rect)
        pygame.draw.rect(vH.screen, accent, rect, border)
        lid_y = rect.y + rect.height * .35
        pygame.draw.line(vH.screen, accent, (rect.x, lid_y), (rect.right, lid_y), max(2, int(self.size * .06)))
        pygame.draw.line(vH.screen, accent, (rect.centerx, rect.y), (rect.centerx, rect.bottom),
                         max(1, int(self.size * .04)))
