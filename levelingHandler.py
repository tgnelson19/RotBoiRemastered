"""Upgrade-card presentation and input handling."""

import pygame

import upgrades
import uiTheme as ui
import variableHolster as vH


class LevelingHandler:
    CARD_KEYS = (pygame.K_1, pygame.K_2, pygame.K_3)

    def __init__(self):
        self.frameRate = vH.frameRate
        self.sW, self.sH = vH.screen.get_size()
        self.cardColor = ui.PANEL
        self.cardHoverColor = ui.PANEL_HOVER
        self.baseColor = ui.TEXT
        self.rerolls = 2
        self.tileSize = min(self.sW, self.sH) / 20
        self.uiScale = ui.display_scale(vH.screen)
        font_path = "data/media/coolveticarg.otf"
        self.titleFont = pygame.font.Font(font_path, int(self.tileSize * 0.9))
        self.descFont = pygame.font.Font(font_path, int(self.tileSize * 0.45))
        self.smallFont = pygame.font.Font(font_path, int(self.tileSize * 0.34))
        self.update_layout()
        self.firstClick = True
        self.upgradeRarityColors = ui.RARITY_COLORS
        self.upgradeRarity = dict(upgrades.RARITY_MULTIPLIERS)
        self.upgradeTypesList = list(upgrades.DEFINITIONS_BY_NAME)
        self.upgradeBasicTypesAdd = {
            item.name: item.additive for item in upgrades.DEFINITIONS
        }
        self.upgradeBasicTypesMult = {
            item.name: item.multiplicative for item in upgrades.DEFINITIONS
        }
        self.cards = upgrades.generate_offer(count=3)
        self.selected_card = None
        self.randomizing = False
        self._sync_legacy_fields()

    def _px(self, value):
        return max(1, int(round(value * self.uiScale)))

    def update_layout(self):
        card_width = (self.sW - self.tileSize * 5) / 3
        card_height = self.sH * 0.62
        card_y = (self.sH - card_height) / 2 - self.tileSize * 0.35
        self.rerollButton = pygame.Rect(
            self.sW // 2 - self.tileSize * 2.1,
            self.sH * 0.87,
            self.tileSize * 4.2,
            self.tileSize,
        )
        self.leftCard = pygame.Rect(self.tileSize * 2, card_y, card_width, card_height)
        self.midCard = pygame.Rect(self.tileSize * 3 + card_width, card_y, card_width, card_height)
        self.rightCard = pygame.Rect(self.tileSize * 4 + 2 * card_width, card_y, card_width, card_height)
        self.card_rects = (self.leftCard, self.midCard, self.rightCard)

    def _sync_legacy_fields(self):
        """Keep the original public fields available while old callers migrate."""
        sides = ("left", "mid", "right")
        for side, card in zip(sides, self.cards):
            setattr(self, f"{side}CardUpgradeRarity", card.rarity)
            setattr(self, f"{side}CardUpgradeMath", card.math_type)
            setattr(self, f"{side}CardUpgradeType", card.name)

    def _draw_centered(self, font, text, color, x, y):
        rendered = font.render(text, True, color)
        vH.screen.blit(rendered, rendered.get_rect(center=(x, y)))

    def _projected_value(self, card, cS):
        base = cS.collectiveStats[card.name]
        additive = sum(cS.collectiveAddStats[card.name])
        multiplicative = 1
        for value in cS.collectiveMultStats[card.name]:
            multiplicative *= value
        modifier = upgrades.card_modifier(card)
        current = (base + additive) * multiplicative
        projected = ((base + additive + modifier) * multiplicative
                     if card.math_type == "additive"
                     else current * modifier)
        return current, projected

    def _recommendation(self, card, cS):
        owned_categories = {}
        for name, count in cS.upgradeCollection["types"].items():
            definition = upgrades.DEFINITIONS_BY_NAME.get(name)
            if definition:
                owned_categories[definition.category] = owned_categories.get(definition.category, 0) + count
        if cS.healthPoints <= cS.maxHealthPoints * .45 and card.definition.category == "survival":
            return "SAFE PICK", ui.GREEN
        if owned_categories.get(card.definition.category, 0) >= 2:
            return "BUILD MATCH", ui.PURPLE
        return None, None

    def drawCards(self):
        import characterStats as cS

        vH.screen.fill(ui.VOID)
        grid_size = max(24, int(self.tileSize * 0.55))
        for x in range(0, int(self.sW), grid_size):
            pygame.draw.line(vH.screen, pygame.Color(23, 27, 35), (x, 0), (x, self.sH))
        for y in range(0, int(self.sH), grid_size):
            pygame.draw.line(vH.screen, pygame.Color(23, 27, 35), (0, y), (self.sW, y))

        ui.draw_text(vH.screen, "LEVEL SECURED", self.tileSize * 0.34, ui.GREEN, (self.sW / 2, self.tileSize * 0.3), "midtop")
        ui.draw_text(vH.screen, "CHOOSE ONE // SHAPE THE RUN", self.tileSize * 0.72, ui.TEXT, (self.sW / 2, self.tileSize * 0.75), "midtop")
        ui.draw_text(vH.screen, "Every card stays with you until the run ends.", self.tileSize * 0.3, ui.MUTED, (self.sW / 2, self.tileSize * 1.65), "midtop")
        if cS.pendingLevelUps > 1:
            ui.draw_tag(vH.screen, f"{cS.pendingLevelUps} DRAFTS QUEUED",
                        (self.tileSize * 2, self.tileSize * 1.25), ui.GOLD, int(self.tileSize * .21))

        mouse_position = (vH.mouseX, vH.mouseY)
        for index, (rect, card) in enumerate(zip(self.card_rects, self.cards)):
            hovered = rect.collidepoint(mouse_position)
            accent = self.upgradeRarityColors[card.rarity]
            pressed = hovered and vH.mouseDown
            visual_rect = rect.move(0, self._px(2) if pressed else (-self._px(7) if hovered else 0))
            ui.draw_panel(vH.screen, visual_rect, self.cardColor, accent if hovered else ui.BORDER, shadow=3 if pressed else 7, hovered=hovered)
            pygame.draw.rect(vH.screen, accent, (visual_rect.x, visual_rect.y, visual_rect.width, self._px(9)))
            key_rect = pygame.Rect(visual_rect.x + self._px(18), visual_rect.y + self._px(24), self._px(38), self._px(38))
            pygame.draw.rect(vH.screen, ui.INK, key_rect)
            pygame.draw.rect(vH.screen, accent, key_rect, self._px(2))
            ui.draw_text(vH.screen, str(index + 1), self.tileSize * 0.4, accent, key_rect.center, "center")
            rarity_width = ui.font(int(self.tileSize * .23)).size(card.rarity.upper())[0]
            ui.draw_tag(vH.screen, card.rarity, (visual_rect.right - self._px(22) - rarity_width, visual_rect.y + self._px(29)), accent, int(self.tileSize * .23))
            ui.draw_text(vH.screen, card.definition.category.upper() + " CARD", self.tileSize * 0.24, ui.MUTED, (visual_rect.centerx, visual_rect.y + self.tileSize * 1.55), "center")
            ui.draw_text(vH.screen, card.name, self.tileSize * 0.58, ui.TEXT, (visual_rect.centerx, visual_rect.y + self.tileSize * 2.15), "center")
            pygame.draw.line(vH.screen, accent, (visual_rect.x + self._px(28), visual_rect.y + self.tileSize * 2.72), (visual_rect.right - self._px(28), visual_rect.y + self.tileSize * 2.72), self._px(2))
            ui.draw_text(vH.screen, card.definition.description, self.tileSize * 0.38, self.baseColor, (visual_rect.centerx, visual_rect.centery - self.tileSize * 0.2), "center")
            mode = "Flat bonus" if card.math_type == "additive" else "Scaling bonus"
            mode_width = ui.font(int(self.tileSize * .2)).size(mode.upper())[0]
            ui.draw_tag(vH.screen, mode, (visual_rect.centerx - mode_width / 2, visual_rect.centery + self.tileSize * 0.65), ui.BLUE, int(self.tileSize * .2))
            ui.draw_text(vH.screen, upgrades.format_card_value(card), self.tileSize * 0.78, accent, (visual_rect.centerx, visual_rect.bottom - self.tileSize * 1.25), "center")
            current, projected = self._projected_value(card, cS)
            direction = "LOWER IS FASTER" if card.name == "Attack Speed" else "PROJECTED STAT"
            ui.draw_text(vH.screen, f"{direction}  //  {current:.2f} → {projected:.2f}",
                         self.tileSize * .19, ui.GREEN if projected != current else ui.MUTED,
                         (visual_rect.centerx, visual_rect.bottom - self.tileSize * .76), "center")
            owned = cS.upgradeCollection["types"].get(card.name, 0)
            ui.draw_text(vH.screen, f"OWNED  {owned}", self.tileSize * 0.22, ui.MUTED, (visual_rect.centerx, visual_rect.bottom - self.tileSize * 0.45), "center")
            recommendation, recommendation_color = self._recommendation(card, cS)
            if recommendation:
                ui.draw_tag(vH.screen, recommendation,
                            (visual_rect.x + self._px(18), visual_rect.bottom - self.tileSize * .62),
                            recommendation_color, int(self.tileSize * .18))

        ui.draw_button(
            vH.screen, self.rerollButton, f"RUN REROLLS  //  {self.rerolls} LEFT", mouse_position,
            vH.mouseDown, self.rerolls > 0, ui.RED, "R", int(self.tileSize * 0.31),
        )

    def PlayerClicked(self):
        if pygame.K_r in vH.keyPressed and self.rerolls > 0:
            self.randomizeLevelUp()
            self.rerolls -= 1
            return "none"

        for index, key in enumerate(self.CARD_KEYS):
            if key in vH.keyPressed:
                self.selected_card = self.cards[index]
                return ("leftCard", "midCard", "rightCard")[index]

        if not vH.mouseDown:
            self.firstClick = False
        if vH.mouseDown and not self.firstClick:
            if self.rerollButton.collidepoint(vH.mouseX, vH.mouseY):
                self.firstClick = True
                if self.rerolls > 0:
                    self.randomizeLevelUp()
                    self.rerolls -= 1
                return "none"
            for index, rect in enumerate(self.card_rects):
                clickable_rect = rect.inflate(0, self._px(14)).move(0, -self._px(7))
                if clickable_rect.collidepoint(vH.mouseX, vH.mouseY):
                    self.firstClick = True
                    self.selected_card = self.cards[index]
                    return ("leftCard", "midCard", "rightCard")[index]
        return "none"

    def randomizeLevelUp(self):
        # Import locally to avoid a module cycle during characterStats initialization.
        import characterStats as cS

        self.cards = upgrades.generate_offer(cS.upgradeCollection, count=3)
        self.selected_card = None
        self._sync_legacy_fields()
        self.randomizing = True
