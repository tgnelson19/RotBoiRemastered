"""Compact in-run HUD using the shared tactile block UI."""

import pygame as pg

import characterStats as cS
import upgrades
import uiTheme as ui
import variableHolster as vH


class InformationSheet:
    def __init__(self):
        self.uiScale = max(.7, min(3.2, min(vH.sW / 1024, vH.sH / 768)))
        self.totalLength = int(vH.sW * 0.25)
        self.totalHeight = int(vH.sH)
        self.posX = int(vH.sW - self.totalLength)
        self.posY = 0
        self.cardRects = []
        self.headerRect = pg.Rect(0, 0, 0, 0)
        self._build_layout()

    def _px(self, value):
        return max(1, int(round(value * self.uiScale)))

    def _build_layout(self):
        padding = self._px(10)
        width = self.totalLength - padding * 2
        header_height = max(self._px(76), int(self.totalHeight * 0.105))
        remaining = self.totalHeight - header_height - padding * 5
        heights = (int(remaining * 0.29), int(remaining * 0.36), int(remaining * 0.35))
        self.headerRect = pg.Rect(self.posX + padding, padding, width, header_height)
        self.cardRects = []
        y = self.headerRect.bottom + padding
        for height in heights:
            self.cardRects.append(pg.Rect(self.posX + padding, y, width, height))
            y += height + padding
        self.padding = padding

    def _draw_section(self, rect, number, title, accent, rows):
        hovered = rect.collidepoint(vH.mouseX, vH.mouseY)
        ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, accent if hovered else ui.BORDER, hovered=hovered)
        pg.draw.rect(vH.screen, accent, (rect.x, rect.y, self._px(6), rect.height))
        ui.draw_text(vH.screen, f"0{number}", self._px(11), accent, (rect.x + self._px(16), rect.y + self._px(12)))
        ui.draw_text(vH.screen, title.upper(), self._px(17), ui.TEXT, (rect.x + self._px(46), rect.y + self._px(8)))
        pg.draw.line(vH.screen, ui.BORDER, (rect.x + self._px(14), rect.y + self._px(34)), (rect.right - self._px(14), rect.y + self._px(34)), self._px(1))

        row_y = rect.y + self._px(46)
        row_height = self._px(22)
        for label, value, value_color in rows:
            ui.draw_text(vH.screen, label.upper(), self._px(11), ui.MUTED, (rect.x + self._px(16), row_y))
            ui.draw_text(vH.screen, value, self._px(13), value_color, (rect.right - self._px(16), row_y - self._px(1)), "topright")
            row_y += row_height

    def _draw_bar(self, rect, y, label, value, max_value, color):
        ratio = value / max_value if max_value > 0 else 0
        ui.draw_text(vH.screen, label.upper(), self._px(10), ui.MUTED, (rect.x + self._px(16), y))
        ui.draw_text(vH.screen, f"{int(value)} / {int(max_value)}", self._px(10), ui.TEXT, (rect.right - self._px(16), y), "topright")
        ui.draw_progress(vH.screen, (rect.x + self._px(16), y + self._px(16), rect.width - self._px(32), self._px(13)), ratio, color, 12)

    def _build_family(self):
        family_counts = {}
        for name, count in cS.upgradeCollection["types"].items():
            definition = upgrades.DEFINITIONS_BY_NAME.get(name)
            if definition:
                family_counts[definition.category] = family_counts.get(definition.category, 0) + count
        if not family_counts:
            return "UNSHAPED", 0
        return max(family_counts.items(), key=lambda item: item[1])

    def _draw_header(self):
        hovered = self.headerRect.collidepoint(vH.mouseX, vH.mouseY)
        ui.draw_panel(vH.screen, self.headerRect, ui.PANEL_RAISED, ui.CREAM if hovered else ui.BORDER, hovered=hovered)
        ui.draw_text(vH.screen, "ROT // RUN", self._px(21), ui.TEXT, (self.headerRect.x + self._px(14), self.headerRect.y + self._px(10)))
        ui.draw_tag(vH.screen, f"LV {cS.currentLevel:02}", (self.headerRect.x + self._px(14), self.headerRect.bottom - self._px(31)), ui.GREEN, self._px(10))
        threat_color = ui.RED if cS.currEnemyCount > cS.enemyCap * 0.7 else ui.GOLD
        tag = f"THREAT {cS.currEnemyCount:02}"
        tag_width = ui.font(self._px(10)).size(tag)[0] + self._px(12)
        ui.draw_tag(vH.screen, tag, (self.headerRect.right - self._px(14) - tag_width, self.headerRect.bottom - self._px(31)), threat_color, self._px(10))

    def _draw_upgrade_summary(self, rect, y):
        family, count = self._build_family()
        ui.draw_text(vH.screen, "RUN SHAPE", self._px(10), ui.MUTED, (rect.x + self._px(16), y))
        ui.draw_text(vH.screen, family.upper(), self._px(13), ui.PURPLE if count else ui.MUTED, (rect.right - self._px(16), y - self._px(2)), "topright")
        items = sorted(cS.upgradeCollection["types"].items(), key=lambda item: (-item[1], item[0]))[:2]
        if not items:
            ui.draw_text(vH.screen, "Draft a card to define this run", self._px(9), ui.MUTED, (rect.x + self._px(16), y + self._px(18)))
            return
        cursor_x = rect.x + self._px(16)
        for name, amount in items:
            tag = ui.draw_tag(vH.screen, f"{name} x{amount}", (cursor_x, y + self._px(22)), ui.BLUE, self._px(9))
            cursor_x = tag.right + self._px(6)
            if cursor_x > rect.right - self._px(60):
                break

    def updateCurrLevel(self):
        return None

    def drawSheet(self):
        panel_rect = pg.Rect(self.posX, 0, self.totalLength, self.totalHeight)
        pg.draw.rect(vH.screen, ui.VOID, panel_rect)
        pg.draw.rect(vH.screen, ui.INK, (self.posX, 0, self._px(7), self.totalHeight))
        pg.draw.line(vH.screen, ui.BORDER, (self.posX + self._px(7), 0), (self.posX + self._px(7), self.totalHeight), self._px(2))
        self._draw_header()

        vitality = self.cardRects[0]
        self._draw_section(vitality, 1, "Condition", ui.GREEN, [
            ("Armor", f"{cS.defense:.1f}", ui.TEXT),
            ("Move", f"{cS.playerSpeed:.2f}", ui.TEXT),
            ("Dash", "READY" if cS.currDashCooldown <= 0 else "CHARGING", ui.GREEN if cS.currDashCooldown <= 0 else ui.MUTED),
        ])
        self._draw_bar(vitality, vitality.bottom - self._px(61), "Integrity", cS.healthPoints, cS.maxHealthPoints, ui.GREEN)
        self._draw_bar(vitality, vitality.bottom - self._px(32), "Dash", max(0, cS.dashCooldownMax - cS.currDashCooldown), cS.dashCooldownMax, ui.BLUE)

        combat = self.cardRects[1]
        self._draw_section(combat, 2, "Loadout", ui.BLUE, [
            ("Damage", f"{cS.bulletDamage:.2f}", ui.CREAM),
            ("Volley", f"{cS.projectileCount:.1f}", ui.TEXT),
            ("Pierce", f"{cS.bulletPierce:.1f}", ui.TEXT),
            ("Range", f"{cS.bulletRange:.0f}", ui.TEXT),
            ("Critical", f"{cS.critChance * 100:.0f}%  x{cS.critDamage:.1f}", ui.PURPLE),
        ])

        progress = self.cardRects[2]
        self._draw_section(progress, 3, "Momentum", ui.GOLD, [
            ("Kills", f"{cS.numOfEnemiesKilled}", ui.TEXT),
            ("Magnet", f"{cS.aura:.0f}", ui.TEXT),
            ("Autofire [I]", "ON" if cS.autoFire else "OFF", ui.GREEN if cS.autoFire else ui.MUTED),
        ])
        self._draw_bar(progress, progress.bottom - self._px(67), "Experience", cS.expCount, cS.expNeededForNextLevel, ui.GOLD)
        self._draw_upgrade_summary(progress, progress.bottom - self._px(34))
