"""Friendly, outcome-first run sidebar with collectible upgrade icon cards."""

from math import cos, floor, hypot, radians, sin

import pygame as pg

import background as bG
import characterStats as cS
import gameProfile
from lootCrate import LootCrate
from progression import FINAL_BOSS_LEVEL, MID_BOSS_LEVEL, MINIBOSS_GATES
import itemCards
import items
import statCards
import upgrades
import uiTheme as ui
import variableHolster as vH


EQUIPMENT_SLOT_TYPES = {
    "weapon": "weapon",
    "armor": "armor",
    "ring": "ring",
    "accessory_1": "accessory",
    "accessory_2": "accessory",
}

BUILD_NAMES = {
    "volley": ("BULLET STORM", "More shots fill more of the arena."),
    "critical": ("CRITICAL STRIKER", "Critical hits create sudden bursts of power."),
    "harvest": ("EXPERIENCE MAGNET", "Fast collection keeps the upgrades coming."),
    "survival": ("ARMORED RUNNER", "Defense and movement keep danger manageable."),
    "tempo": ("RAPID FIRE", "A steady stream of shots controls nearby space."),
    "precision": ("LONGSHOT", "Fast, far-reaching shots reward clean aim."),
    "power": ("HEAVY GUNNER", "Each projectile lands with extra weight."),
}


class InformationSheet:
    def __init__(self):
        self.uiScale = ui.display_scale(vH.screen)
        self.mode = gameProfile.profile.get("hud_mode", "compact")
        self.tooltip = None
        self.nearby_crate = None
        self.dragging_item = None
        self.dragging_source = None
        self._equipment_slot_rects = {}
        self._loot_panel_slot_rects = []
        self._build_layout()

    def _px(self, value):
        return max(1, int(round(value * self.uiScale)))

    def _build_layout(self):
        screen_width, screen_height = vH.screen.get_size()
        self._layoutSize = (screen_width, screen_height)
        ratio = .15 if self.mode == "compact" else .24
        minimum = self._px(220 if self.mode == "compact" else 300)
        maximum = self._px(320 if self.mode == "compact" else 440)
        self.totalLength = max(minimum, min(maximum, int(screen_width * ratio)))
        # Never consume more than 42% of a narrow display.
        self.totalLength = min(self.totalLength, int(screen_width * .42))
        self.totalHeight = screen_height
        self.posX = screen_width - self.totalLength
        self.padding = self._px(9)

    def _sync_layout(self):
        next_scale = ui.display_scale(vH.screen)
        if next_scale != self.uiScale or self._layoutSize != vH.screen.get_size():
            self.uiScale = next_scale
            self._build_layout()
            bG.lockX = self.arena_width / 2

    @property
    def arena_width(self):
        return self.posX

    def toggle_mode(self):
        self.mode = "expanded" if self.mode == "compact" else "compact"
        gameProfile.profile["hud_mode"] = self.mode
        gameProfile.save_profile()
        self._build_layout()
        bG.lockX = self.arena_width / 2

    def _panel(self, y, height, accent=ui.BORDER, fill=ui.PANEL_RAISED):
        rect = pg.Rect(self.posX + self.padding, y,
                       self.totalLength - self.padding * 2, height)
        ui.draw_panel(vH.screen, rect, fill, accent, shadow=3)
        return rect

    def _bar(self, rect, y, label, value, maximum, color, value_text):
        ui.draw_text(vH.screen, label, self._px(9), ui.MUTED,
                     (rect.x + self._px(11), y))
        ui.draw_text(vH.screen, value_text, self._px(9), ui.TEXT,
                     (rect.right - self._px(11), y), "topright")
        ratio = value / maximum if maximum else 0
        ui.draw_progress(vH.screen, (rect.x + self._px(11), y + self._px(14),
                         rect.width - self._px(22), self._px(11)), ratio, color, 10)

    def _family_counts(self):
        counts = {}
        for name, count in cS.upgradeCollection["types"].items():
            definition = upgrades.DEFINITIONS_BY_NAME.get(name)
            if definition:
                counts[definition.category] = counts.get(definition.category, 0) + count
        return sorted(counts.items(), key=lambda item: (-item[1], item[0]))

    def _build_identity(self):
        families = self._family_counts()
        if not families:
            return "FRESH START", "Your first picks will shape this run.", "No weakness yet"
        family = families[0][0]
        title, strength = BUILD_NAMES.get(family, (family.upper(), "A flexible set of upgrades."))
        if cS.defense < 1 and cS.playerSpeed < 2.5:
            caution = "Fragile if cornered"
        elif cS.bulletDamage < 1.35 and cS.projectileCount >= 2:
            caution = "Relies on shot volume"
        elif cS.bulletRange < vH.tileSizeGlobal * 5:
            caution = "Best at close range"
        else:
            caution = "No clear weakness"
        return title, strength, caution

    def _combat_values(self):
        attacks_per_second = vH.frameRate / max(1, cS.attackCooldownStat)
        guaranteed_crits = floor(cS.critChance)
        bonus_chance = cS.critChance - guaranteed_crits
        expected_crit = (cS.critDamage ** guaranteed_crits
                         * ((1 - bonus_chance) + bonus_chance * cS.critDamage))
        dps = cS.bulletDamage * cS.projectileCount * attacks_per_second * expected_crit
        return attacks_per_second, dps

    def _rating(self, value, baseline, inverse=False):
        if value <= 0:
            return "None"
        ratio = baseline / max(.001, value) if inverse else value / max(.001, baseline)
        if ratio >= 2.0:
            return "Exceptional"
        if ratio >= 1.45:
            return "Very strong"
        if ratio >= 1.12:
            return "Strong"
        return "Normal"

    def _shot_text(self):
        whole = floor(cS.projectileCount)
        chance = round((cS.projectileCount - whole) * 100)
        return f"{whole} shots + {chance}% bonus" if chance else f"{whole} shot{'s' if whole != 1 else ''}"

    def _pierce_text(self):
        # A pierce value of one allows the initial target plus one pass-through.
        whole = floor(cS.bulletPierce) + 1
        chance = round((cS.bulletPierce - floor(cS.bulletPierce)) * 100)
        return f"Hits {whole} + {chance}% extra" if chance else f"Hits up to {whole} enemies"

    def _pressure(self):
        if cS.gameCompleted:
            return "RUN COMPLETE", ui.CREAM, 0
        if cS.activeBoss is not None:
            return "BOSS", ui.RED, 1
        threat = sum(getattr(enemy, "threatCost", 1.0)
                     for enemy in cS.enemyHolster if not enemy.is_dead())
        ratio = min(1, threat / max(1, cS.enemyThreatCap))
        if not cS.enemyHolster:
            return "CALM", ui.GREEN, ratio
        if any(getattr(enemy, "combatRole", "") == "elite" for enemy in cS.enemyHolster):
            return "ELITE NEARBY", ui.PURPLE, ratio
        if ratio >= .72:
            return "DANGEROUS", ui.RED, ratio
        return "ACTIVE", ui.GOLD, ratio

    def _bounty_details(self):
        bounty = cS.currentBounty
        if not bounty:
            return "Explore the arena", "No active target"
        dx = bounty["world"][0] - (bG.playerPosX + cS.playerSize / 2)
        dy = bounty["world"][1] - (bG.playerPosY + cS.playerSize / 2)
        tiles = hypot(dx, dy) / max(1, vH.tileSizeGlobal)
        target = bounty["target"]
        members = getattr(target, "members", None)
        count = len([member for member in members if not member.is_dead()]) if members else 1
        distance = "Target nearby" if tiles < 8 else f"About {tiles:.0f} tiles away"
        return bounty["label"].title(), f"{count} hostile{'s' if count != 1 else ''}  •  {distance}"

    def _next_milestone(self):
        gates = [(level, name.replace("miniboss_", "").title())
                 for level, name in MINIBOSS_GATES]
        gates += [(MID_BOSS_LEVEL, "Beaudis"), (FINAL_BOSS_LEVEL, "Dissonance")]
        gates.sort()
        return next(((level, name) for level, name in gates if level > cS.currentLevel),
                    (FINAL_BOSS_LEVEL, "Complete"))

    def _draw_header(self):
        rect = self._panel(self.padding, self._px(62), ui.CREAM)
        ui.draw_text(vH.screen, f"LEVEL {cS.currentLevel:02}", self._px(17), ui.TEXT,
                     (rect.x + self._px(11), rect.y + self._px(9)))
        pressure, color, _ = self._pressure()
        ui.draw_text(vH.screen, pressure, self._px(9), color,
                     (rect.right - self._px(11), rect.y + self._px(14)), "topright")
        ui.draw_text(vH.screen, "Tab: build details" if self.mode == "compact" else "Tab: compact view",
                     self._px(8), ui.MUTED, (rect.x + self._px(11), rect.bottom - self._px(15)))
        return rect.bottom + self.padding

    def _draw_status(self, y):
        health_color = ui.GREEN if cS.healthPoints > cS.maxHealthPoints * .3 else ui.RED
        rect = self._panel(y, self._px(112), health_color)
        self._bar(rect, rect.y + self._px(9), "HEALTH", cS.healthPoints, cS.maxHealthPoints,
                  health_color, f"{cS.healthPoints} / {cS.maxHealthPoints}")
        dash_value = max(0, cS.dashCooldownMax - cS.currDashCooldown)
        dash_text = "READY" if cS.currDashCooldown <= 0 else f"{cS.currDashCooldown / vH.frameRate:.1f} sec"
        self._bar(rect, rect.y + self._px(39), "DASH", dash_value, cS.dashCooldownMax,
                  ui.BLUE, dash_text)
        percent = cS.expCount / max(1, cS.expNeededForNextLevel) * 100
        self._bar(rect, rect.y + self._px(69), "NEXT PICK", cS.expCount,
                  cS.expNeededForNextLevel, ui.GOLD, f"{percent:.0f}%")
        if percent >= 82:
            ui.draw_text(vH.screen, "Next pick soon", self._px(8), ui.GOLD,
                         (rect.right - self._px(11), rect.bottom - self._px(11)), "bottomright")
        return rect.bottom + self.padding

    def _draw_inventory(self, y):
        header_h = self._px(24)
        hub_h = self._px(140)
        height = header_h + hub_h + self._px(12)
        rect = self._panel(y, height, ui.BORDER)
        ui.draw_text(vH.screen, "EQUIPMENT", self._px(9), ui.MUTED,
                     (rect.x + self._px(10), rect.y + self._px(8)))

        hub_x = rect.centerx
        hub_y = rect.y + header_h + hub_h / 2
        radius_x = rect.width * .28
        radius_y = hub_h * .38
        slot_size = self._px(38)

        slots = (
            ("WEAPON", "weapon", 90),
            ("RING", "ring", 18),
            ("ACC 2", "accessory_2", -54),
            ("ACC 1", "accessory_1", -126),
            ("ARMOR", "armor", 162),
        )
        self._equipment_slot_rects = {}
        for label, key, angle_degrees in slots:
            angle = radians(angle_degrees)
            center = (hub_x + cos(angle) * radius_x, hub_y - sin(angle) * radius_y)
            slot_rect = pg.Rect(0, 0, slot_size, slot_size)
            slot_rect.center = center
            self._equipment_slot_rects[key] = slot_rect
            item = cS.equipment[key]
            dragging_this = (self.dragging_source is not None
                             and self.dragging_source[0] == "equipment"
                             and self.dragging_source[1] == key)
            if item is not None and not dragging_this:
                itemCards.draw_item_card(vH.screen, slot_rect, item.slot_type, item.rarity,
                                         hovered=slot_rect.collidepoint(vH.mouseX, vH.mouseY))
                if slot_rect.collidepoint(vH.mouseX, vH.mouseY):
                    self.tooltip = items.describe(item)
            else:
                pg.draw.rect(vH.screen, ui.INK, slot_rect)
                pg.draw.rect(vH.screen, ui.BORDER, slot_rect, self._px(2))
            ui.draw_text(vH.screen, label, self._px(7), ui.MUTED,
                         (center[0], slot_rect.bottom + self._px(3)), "midtop")
        return rect.bottom + self.padding

    CRATE_SLOT_COUNT = 4

    def _draw_loot_panel(self, y):
        crate = self.nearby_crate
        if crate is None or not crate.items:
            return y
        header_h = self._px(24)
        slot_size = self._px(38)
        height = header_h + slot_size + self._px(20)
        rect = self._panel(y, height, ui.CREAM)
        ui.draw_text(vH.screen, "NEARBY LOOT", self._px(9), ui.CREAM,
                     (rect.x + self._px(10), rect.y + self._px(8)))

        gap = self._px(10)
        total_width = self.CRATE_SLOT_COUNT * slot_size + (self.CRATE_SLOT_COUNT - 1) * gap
        start_x = rect.centerx - total_width / 2
        slot_y = rect.y + header_h + self._px(4)
        self._loot_panel_slot_rects = []
        for index in range(self.CRATE_SLOT_COUNT):
            slot_rect = pg.Rect(start_x + index * (slot_size + gap), slot_y, slot_size, slot_size)
            self._loot_panel_slot_rects.append(slot_rect)
            if index >= len(crate.items):
                pg.draw.rect(vH.screen, ui.INK, slot_rect)
                pg.draw.rect(vH.screen, ui.BORDER, slot_rect, self._px(2))
                continue
            item = crate.items[index]
            dragging_this = (self.dragging_source is not None
                             and self.dragging_source[0] == "crate"
                             and self.dragging_source[1] is crate
                             and self.dragging_source[2] == index)
            if not dragging_this:
                itemCards.draw_item_card(vH.screen, slot_rect, item.slot_type, item.rarity,
                                         hovered=slot_rect.collidepoint(vH.mouseX, vH.mouseY))
                if slot_rect.collidepoint(vH.mouseX, vH.mouseY):
                    self.tooltip = items.describe(item)
            else:
                pg.draw.rect(vH.screen, ui.INK, slot_rect)
                pg.draw.rect(vH.screen, ui.BORDER, slot_rect, self._px(2))
        return rect.bottom + self.padding

    def _handle_equipment_drag(self):
        if self.dragging_item is None:
            if vH.mousePressed:
                for key, rect in self._equipment_slot_rects.items():
                    if rect.collidepoint(vH.mouseX, vH.mouseY) and cS.equipment[key] is not None:
                        self.dragging_item = cS.equipment[key]
                        self.dragging_source = ("equipment", key)
                        vH.dragInProgress = True
                        return
                for index, rect in enumerate(self._loot_panel_slot_rects):
                    if rect.collidepoint(vH.mouseX, vH.mouseY) and self.nearby_crate is not None \
                            and index < len(self.nearby_crate.items):
                        self.dragging_item = self.nearby_crate.items[index]
                        self.dragging_source = ("crate", self.nearby_crate, index)
                        vH.dragInProgress = True
                        return
            return

        slot_size = self._px(38)
        icon_rect = pg.Rect(0, 0, slot_size, slot_size)
        icon_rect.center = (vH.mouseX, vH.mouseY)
        itemCards.draw_item_card(vH.screen, icon_rect, self.dragging_item.slot_type,
                                 self.dragging_item.rarity, hovered=True)

        if not vH.mouseDown:
            self._resolve_drop((vH.mouseX, vH.mouseY))
            self.dragging_item = None
            self.dragging_source = None
            vH.dragInProgress = False

    def _resolve_drop(self, mouse_pos):
        item = self.dragging_item
        source_kind = self.dragging_source[0]

        target_key = None
        for key, rect in self._equipment_slot_rects.items():
            if rect.collidepoint(mouse_pos) and EQUIPMENT_SLOT_TYPES[key] == item.slot_type:
                target_key = key
                break

        if target_key is not None:
            if source_kind == "equipment":
                _, source_key = self.dragging_source
                if source_key == target_key:
                    return  # released back over its own slot -- treat as a cancelled drag
                # Swap: works whether the target slot is occupied or empty.
                cS.equipment[source_key], cS.equipment[target_key] = (
                    cS.equipment[target_key], cS.equipment[source_key])
            else:
                _, crate, index = self.dragging_source
                displaced = cS.equipment[target_key]
                cS.equipment[target_key] = item
                if displaced is not None:
                    crate.items[index] = displaced
                else:
                    del crate.items[index]
                    if not crate.items:
                        if crate in cS.lootCrateList:
                            cS.lootCrateList.remove(crate)
                        if self.nearby_crate is crate:
                            self.nearby_crate = None
            import character
            character.combarinoPlayerStats()
            return

        if source_kind == "equipment":
            _, source_key = self.dragging_source
            cS.equipment[source_key] = None
            crate = self.nearby_crate
            if crate is not None and len(crate.items) < self.CRATE_SLOT_COUNT:
                crate.items.append(item)
            else:
                cS.lootCrateList.append(LootCrate(bG.playerPosX, bG.playerPosY, [item]))
            import character
            character.combarinoPlayerStats()
        # source_kind == "crate" and invalid drop target: no-op, item stays put.

    def _draw_build(self, y):
        height = self._px(106 if self.mode == "compact" else 134)
        rect = self._panel(y, height, ui.PURPLE)
        title, strength, caution = self._build_identity()
        ui.draw_text(vH.screen, title, self._px(14), ui.PURPLE,
                     (rect.x + self._px(11), rect.y + self._px(9)))
        ui.draw_text(vH.screen, strength, self._px(8), ui.TEXT,
                     (rect.x + self._px(11), rect.y + self._px(31)))
        ui.draw_text(vH.screen, caution, self._px(8), ui.MUTED,
                     (rect.x + self._px(11), rect.y + self._px(49)))
        families = self._family_counts()
        if families:
            max_pips = 5
            for index, (family, count) in enumerate(families[:2 if self.mode == "compact" else 4]):
                row_y = rect.y + self._px(68 + index * 16)
                ui.draw_text(vH.screen, family.title(), self._px(8), ui.MUTED,
                             (rect.x + self._px(11), row_y))
                for pip in range(max_pips):
                    pip_rect = pg.Rect(rect.right - self._px(11 + (max_pips - pip) * 11),
                                       row_y + self._px(1), self._px(7), self._px(7))
                    pg.draw.rect(vH.screen, ui.PURPLE if pip < count else ui.INK, pip_rect)
                    pg.draw.rect(vH.screen, ui.BORDER, pip_rect, 1)
        return rect.bottom + self.padding

    def _stat_row(self, rect, y, symbol, label, value, rating=None, help_text=None):
        icon_rect = pg.Rect(rect.x + self._px(10), y - self._px(3), self._px(24), self._px(24))
        pg.draw.rect(vH.screen, ui.INK, icon_rect, border_radius=self._px(3))
        statCards.draw_stat_symbol(vH.screen, symbol, icon_rect.inflate(-self._px(4), -self._px(4)), ui.CREAM)
        label_rect = ui.draw_text(vH.screen, label, self._px(8), ui.MUTED,
                                  (icon_rect.right + self._px(7), y))
        ui.draw_text(vH.screen, value, self._px(9), ui.TEXT,
                     (icon_rect.right + self._px(7), y + self._px(12)))
        if rating:
            ui.draw_text(vH.screen, rating, self._px(7), ui.GREEN,
                         (rect.right - self._px(10), y + self._px(1)), "topright")
        hover_rect = pg.Rect(icon_rect.x, y - self._px(4), rect.right - icon_rect.x, self._px(31))
        if help_text and hover_rect.collidepoint(vH.mouseX, vH.mouseY):
            self.tooltip = help_text
        return label_rect

    def _draw_stats(self, y):
        attacks, _ = self._combat_values()
        rows = [
            ("Bullet Damage", "Damage", f"{cS.bulletDamage} / hit",
             self._rating(cS.bulletDamage, 100), "The exact damage dealt by a normal projectile hit."),
            ("Attack Speed", "Fire rate", f"{attacks:.2f} / sec",
             self._rating(attacks, vH.frameRate / 40), "The exact number of volleys fired each second."),
            ("Bullet Count", "Projectiles", self._shot_text(), None,
             "Fractional projectile count becomes a chance to fire one bonus shot."),
            ("Crit Chance", "Critical", f"{cS.critChance * 100:.0f}% for x{cS.critDamage:.1f}", None,
             "Critical chance and the damage multiplier applied when it succeeds."),
        ]
        if self.mode == "expanded":
            rows += [
                ("Bullet Pierce", "Piercing", self._pierce_text(), None,
                 "How many enemies one projectile can damage before disappearing."),
                ("Defense", "Defense", f"Blocks {cS.defense} damage", self._rating(cS.defense, 100),
                 "Flat damage removed from every incoming hit."),
                ("Vitality", "Vitality", f"{cS.vitality} HP / sec", self._rating(cS.vitality, 10),
                 "Health recovered continuously each second."),
                ("Bullet Range", "Range", f"{cS.bulletRange / vH.tileSizeGlobal:.1f} tiles",
                 self._rating(cS.bulletRange, 250), "Approximate projectile travel distance."),
            ]
        height = self._px(29 + len(rows) * 31)
        rect = self._panel(y, height, ui.BLUE)
        ui.draw_text(vH.screen, "YOUR WEAPON", self._px(9), ui.BLUE,
                     (rect.x + self._px(10), rect.y + self._px(8)))
        for index, row in enumerate(rows):
            self._stat_row(rect, rect.y + self._px(29 + index * 31), *row)
        return rect.bottom + self.padding

    def _draw_objective(self, y):
        rect = self._panel(y, self._px(80), ui.GOLD)
        pressure, color, ratio = self._pressure()
        name, detail = self._bounty_details()
        ui.draw_text(vH.screen, name[:32], self._px(11), color,
                     (rect.x + self._px(10), rect.y + self._px(8)))
        ui.draw_text(vH.screen, detail, self._px(8), ui.TEXT,
                     (rect.x + self._px(10), rect.y + self._px(29)))
        ui.draw_progress(vH.screen, (rect.x + self._px(10), rect.y + self._px(47),
                         rect.width - self._px(20), self._px(8)), ratio, color, 8)
        level, milestone = self._next_milestone()
        ui.draw_text(vH.screen, f"Next: level {level} • {milestone}", self._px(7), ui.MUTED,
                     (rect.x + self._px(10), rect.bottom - self._px(9)), "bottomleft")
        return rect.bottom + self.padding

    def _draw_recent_table(self, minimum_y):
        available = self.totalHeight - minimum_y - self.padding
        height = max(self._px(76), min(self._px(112), available))
        y = self.totalHeight - height - self.padding
        rect = self._panel(y, height, ui.CREAM, ui.PANEL)
        ui.draw_text(vH.screen, "RECENT PICKS", self._px(8), ui.MUTED,
                     (rect.x + self._px(10), rect.y + self._px(7)))
        table_y = rect.y + self._px(25)
        pg.draw.rect(vH.screen, ui.INK,
                     (rect.x + self._px(7), table_y, rect.width - self._px(14), rect.bottom - table_y - self._px(7)))
        pg.draw.line(vH.screen, ui.GOLD, (rect.x + self._px(7), table_y),
                     (rect.right - self._px(7), table_y), self._px(2))
        history = cS.upgradeCollection.get("history", [])[-5:]
        if not history:
            ui.draw_text(vH.screen, "Your upgrade cards will collect here.", self._px(7), ui.MUTED,
                         (rect.centerx, table_y + (rect.bottom - table_y) / 2), "center")
            return
        gap = self._px(5)
        max_card_w = (rect.width - self._px(24) - (len(history) - 1) * gap) / len(history)
        card_h = min(self._px(58), rect.bottom - table_y - self._px(10), max_card_w / .72)
        card_w = int(card_h * .72)
        total = len(history) * card_w + (len(history) - 1) * gap
        start_x = rect.centerx - total / 2
        for index, entry in enumerate(history):
            card_rect = pg.Rect(start_x + index * (card_w + gap), table_y + self._px(5), card_w, card_h)
            hovered = card_rect.collidepoint(vH.mouseX, vH.mouseY)
            statCards.draw_upgrade_card(vH.screen, card_rect, entry["name"], entry["rarity"],
                                        entry["math_type"], hovered)
            if hovered:
                mode = "Flat increase" if entry["math_type"] == "additive" else "Multiplicative increase"
                self.tooltip = f"{entry['rarity']} {entry['name']} • {mode}"

    def _draw_tooltip(self):
        if not self.tooltip:
            return
        width = min(self._px(250), int(vH.sW * .24))
        font = ui.font(self._px(8))
        words, lines, line = self.tooltip.split(), [], ""
        for word in words:
            candidate = f"{line} {word}".strip()
            if font.size(candidate)[0] > width - self._px(18) and line:
                lines.append(line)
                line = word
            else:
                line = candidate
        lines.append(line)
        rect = pg.Rect(vH.mouseX - width - self._px(10), vH.mouseY + self._px(10), width,
                       self._px(14 + len(lines) * 13))
        rect.clamp_ip(pg.Rect(self.posX, 0, self.totalLength, self.totalHeight))
        ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, ui.CREAM, shadow=4)
        for index, text in enumerate(lines):
            ui.draw_text(vH.screen, text, self._px(8), ui.TEXT,
                         (rect.x + self._px(9), rect.y + self._px(7 + index * 13)))

    def updateCurrLevel(self):
        return None

    def drawSheet(self):
        self._sync_layout()
        self.tooltip = None
        pg.draw.rect(vH.screen, ui.VOID, (self.posX, 0, self.totalLength, self.totalHeight))
        pg.draw.rect(vH.screen, ui.INK, (self.posX, 0, self._px(6), self.totalHeight))
        y = self._draw_header()
        y = self._draw_status(y)
        y = self._draw_inventory(y)
        if self.nearby_crate is not None and y + self._px(70) < self.totalHeight - self._px(82):
            y = self._draw_loot_panel(y)
        y = self._draw_build(y)
        y = self._draw_stats(y)
        if self.mode == "compact" and y + self._px(90) < self.totalHeight - self._px(82):
            y = self._draw_objective(y)
        self._draw_recent_table(y)
        self._handle_equipment_drag()
        if not vH.dragInProgress:
            self._draw_tooltip()
