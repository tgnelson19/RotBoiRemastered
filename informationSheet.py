import pygame as pg
import variableHolster as vH
import characterStats as cS


class InformationSheet:

    def __init__(self):
        self.totalLength = vH.sW * 0.25
        self.totalHeight = vH.sH
        self.posX = vH.sW * 0.75
        self.posY = 0

        self.panelColor = pg.Color(20, 24, 32)
        self.panelEdgeColor = pg.Color(90, 100, 120)
        self.cardColor = pg.Color(30, 35, 46)
        self.textColor = pg.Color(240, 242, 248)
        self.mutedTextColor = pg.Color(170, 178, 196)
        self.accentColor = pg.Color(198, 85, 95)
        self.accentGreen = pg.Color(90, 180, 115)
        self.accentBlue = pg.Color(100, 145, 230)
        self.shadowColor = pg.Color(0, 0, 0, 70)

        self.titleFont = pg.font.Font("data/media/coolveticarg.otf", 22)
        self.headerFont = pg.font.Font("data/media/coolveticarg.otf", 15)
        self.valueFont = pg.font.Font("data/media/coolveticarg.otf", 13)
        self.smallFont = pg.font.Font("data/media/coolveticarg.otf", 11)

        self.cardRects = []
        self.headerRect = pg.Rect(0, 0, 0, 0)
        self._build_layout()

    def _build_layout(self):
        padding = max(18, int(vH.sW * 0.012))
        header_height = 84
        card_width = self.totalLength - (padding * 2)
        card_height = max(150, (self.totalHeight - header_height - (padding * 4)) // 3)

        self.headerRect = pg.Rect(self.posX + padding, self.posY + padding, card_width, header_height)
        self.cardRects = []
        for index in range(3):
            card_y = self.posY + header_height + padding * (index + 1) + (card_height * index)
            self.cardRects.append(pg.Rect(self.posX + padding, card_y, card_width, card_height))

    def _draw_card(self, rect, title, accent, rows):
        shadow_rect = rect.copy()
        shadow_rect.x += 4
        shadow_rect.y += 4
        pg.draw.rect(vH.screen, self.shadowColor, shadow_rect, border_radius=12)
        pg.draw.rect(vH.screen, self.cardColor, rect, border_radius=12)
        pg.draw.rect(vH.screen, accent, pg.Rect(rect.x, rect.y, rect.width, 3), border_radius=12)

        title_render = self.headerFont.render(title.upper(), True, self.textColor)
        vH.screen.blit(title_render, (rect.x + 12, rect.y + 8))

        row_y = rect.y + 36
        row_gap = 20
        for label, value in rows:
            label_render = self.valueFont.render(label, True, self.mutedTextColor)
            value_render = self.valueFont.render(value, True, self.textColor)
            vH.screen.blit(label_render, (rect.x + 12, row_y))
            vH.screen.blit(value_render, (rect.x + rect.width - 12 - value_render.get_width(), row_y))
            row_y += row_gap

    def _draw_bar(self, rect, y, label, value, max_value, color):
        label_render = self.smallFont.render(label, True, self.mutedTextColor)
        vH.screen.blit(label_render, (rect.x + 12, y))

        bar_x = rect.x + 12
        bar_y = y + 16
        bar_width = rect.width - 24
        bar_height = 10
        pg.draw.rect(vH.screen, self.shadowColor, pg.Rect(bar_x + 1, bar_y + 1, bar_width, bar_height), border_radius=6)
        pg.draw.rect(vH.screen, pg.Color(50, 54, 64), pg.Rect(bar_x, bar_y, bar_width, bar_height), border_radius=6)

        if max_value > 0:
            fill_ratio = max(0, min(1, value / max_value))
            fill_width = int(bar_width * fill_ratio)
        else:
            fill_width = 0

        pg.draw.rect(vH.screen, color, pg.Rect(bar_x, bar_y, fill_width, bar_height), border_radius=6)

        value_text = self.smallFont.render(f"{int(value)}/{int(max_value)}", True, self.textColor)
        vH.screen.blit(value_text, (rect.x + rect.width - 12 - value_text.get_width(), y + 13))

    def _draw_header(self):
        shadow_rect = self.headerRect.copy()
        shadow_rect.x += 4
        shadow_rect.y += 4
        pg.draw.rect(vH.screen, self.shadowColor, shadow_rect, border_radius=14)
        pg.draw.rect(vH.screen, self.cardColor, self.headerRect, border_radius=14)
        pg.draw.rect(vH.screen, self.accentColor, pg.Rect(self.headerRect.x, self.headerRect.y, self.headerRect.width, 3), border_radius=14)

        title_render = self.titleFont.render("PLAYER HUD", True, self.textColor)
        vH.screen.blit(title_render, (self.headerRect.x + 12, self.headerRect.y + 10))

        level_render = self.smallFont.render(f"Lv. {cS.currentLevel}", True, self.accentGreen)
        enemy_render = self.smallFont.render(f"Threat {cS.currEnemyCount}", True, self.accentBlue)
        vH.screen.blit(level_render, (self.headerRect.x + 12, self.headerRect.y + 48))
        vH.screen.blit(enemy_render, (self.headerRect.x + self.headerRect.width - 12 - enemy_render.get_width(), self.headerRect.y + 48))

    def _draw_upgrade_summary(self, rect, y):
        label_render = self.valueFont.render("Collected", True, self.mutedTextColor)
        vH.screen.blit(label_render, (rect.x + 12, y))

        type_items = sorted(cS.upgradeCollection["types"].items(), key=lambda item: item[1], reverse=True)[:3]
        rarity_items = sorted(cS.upgradeCollection["rarities"].items(), key=lambda item: item[1], reverse=True)[:3]

        if not type_items and not rarity_items:
            summary_text = self.smallFont.render("No upgrades yet", True, self.textColor)
            vH.screen.blit(summary_text, (rect.x + 12, y + 24))
            return

        type_line = ", ".join(f"{name}:{count}" for name, count in type_items) if type_items else "-"
        rarity_line = ", ".join(f"{name}:{count}" for name, count in rarity_items) if rarity_items else "-"

        type_render = self.smallFont.render("Types: " + type_line, True, self.textColor)
        rarity_render = self.smallFont.render("Rarities: " + rarity_line, True, self.textColor)
        vH.screen.blit(type_render, (rect.x + 12, y + 24))
        vH.screen.blit(rarity_render, (rect.x + 12, y + 44))

    def updateCurrLevel(self):
        return None

    def drawSheet(self):
        panel_rect = pg.Rect(self.posX, self.posY, self.totalLength, self.totalHeight)
        shadow_rect = panel_rect.copy()
        shadow_rect.x += 4
        shadow_rect.y += 4
        pg.draw.rect(vH.screen, self.shadowColor, shadow_rect, border_radius=18)
        pg.draw.rect(vH.screen, self.panelColor, panel_rect, border_radius=18)
        pg.draw.rect(vH.screen, self.panelEdgeColor, panel_rect, 2, border_radius=18)

        self._draw_header()

        self._draw_card(
            self.cardRects[0],
            "Vitality",
            self.accentGreen,
            [
                ("HP", f"{int(cS.healthPoints)}/{int(cS.maxHealthPoints)}"),
                ("Dash", "Ready" if cS.currDashCooldown <= 0 else f"{cS.currDashCooldown:.0f}"),
                ("Speed", f"{cS.playerSpeed:.2f}"),
            ],
        )
        self._draw_bar(self.cardRects[0], self.cardRects[0].y + 120, "HP", cS.healthPoints, cS.maxHealthPoints, self.accentGreen)
        self._draw_bar(self.cardRects[0], self.cardRects[0].y + 148, "Dash", max(0, cS.dashCooldownMax - cS.currDashCooldown), cS.dashCooldownMax, self.accentBlue)

        self._draw_card(
            self.cardRects[1],
            "Combat",
            self.accentBlue,
            [
                ("Damage", f"{cS.bulletDamage:.2f}"),
                ("Pierce", f"{cS.bulletPierce:.0f}"),
                ("Range", f"{cS.bulletRange:.0f}"),
                ("Count", f"{cS.projectileCount:.0f}"),
                ("Crit", f"{cS.critChance * 100:.0f}%"),
                ("Crit Dmg", f"{cS.critDamage:.2f}"),
            ],
        )

        self._draw_card(
            self.cardRects[2],
            "Progress",
            self.accentColor,
            [
                ("Level", f"{cS.currentLevel}"),
                ("XP", f"{int(cS.expCount)}/{int(cS.expNeededForNextLevel)}"),
                ("Aura", f"{cS.aura:.0f}"),
            ],
        )
        self._draw_bar(self.cardRects[2], self.cardRects[2].y + 124, "XP", cS.expCount, cS.expNeededForNextLevel, self.accentColor)
        self._draw_upgrade_summary(self.cardRects[2], self.cardRects[2].y + 152)