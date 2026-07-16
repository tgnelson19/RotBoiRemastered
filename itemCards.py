"""Resolution-independent icons and mini cards for equippable loot items."""

import pygame as pg

import uiTheme as ui


def _line(surface, color, start, end, width):
    pg.draw.line(surface, color, start, end, max(1, int(width)))


def draw_item_symbol(surface, slot_type, rect, color=ui.TEXT):
    """Draw a compact silhouette for an item's equipment slot type."""
    rect = pg.Rect(rect)
    cx, cy = rect.center
    unit = max(1, min(rect.width, rect.height) / 20)
    stroke = max(1, round(unit * 1.7))
    name = slot_type.lower()

    if name == "weapon":
        tip = pg.Vector2(cx + 6 * unit, cy - 9 * unit)
        guard_point = pg.Vector2(cx - 1 * unit, cy + 2 * unit)
        pommel = pg.Vector2(cx - 6 * unit, cy + 8 * unit)
        _line(surface, color, tip, guard_point, stroke)
        _line(surface, color, guard_point, pommel, max(1, round(stroke * .7)))
        blade_dir = (guard_point - tip).normalize()
        perp = pg.Vector2(-blade_dir.y, blade_dir.x) * 4 * unit
        _line(surface, color, guard_point - perp, guard_point + perp, stroke)
        pg.draw.circle(surface, color, (round(pommel.x), round(pommel.y)), max(1, round(unit * 1.5)))
    elif name == "armor":
        body = pg.Rect(0, 0, 13 * unit, 15 * unit)
        body.center = (cx, cy + 1 * unit)
        pg.draw.rect(surface, color, body, stroke, border_radius=round(3 * unit))
        _line(surface, color, (cx, cy - 6 * unit), (cx - 3 * unit, cy - 1 * unit), max(1, round(stroke * .7)))
        _line(surface, color, (cx, cy - 6 * unit), (cx + 3 * unit, cy - 1 * unit), max(1, round(stroke * .7)))
    elif name == "ring":
        pg.draw.circle(surface, color, (cx, cy + 2 * unit), round(7 * unit), stroke)
        gem = ((cx, cy - 9 * unit), (cx + 3 * unit, cy - 5 * unit),
               (cx, cy - 2 * unit), (cx - 3 * unit, cy - 5 * unit))
        pg.draw.polygon(surface, color, gem, stroke)
    elif name == "accessory":
        pg.draw.circle(surface, color, (cx, cy - 7 * unit), round(2.5 * unit), stroke)
        gem = ((cx, cy - 2 * unit), (cx + 5 * unit, cy + 4 * unit),
               (cx, cy + 9 * unit), (cx - 5 * unit, cy + 4 * unit))
        pg.draw.polygon(surface, color, gem, stroke)
    else:
        pg.draw.circle(surface, color, (cx, cy), round(7 * unit), stroke)


def draw_item_card(surface, rect, slot_type, rarity, hovered=False):
    """Draw a rarity-backed mini card with an item slot-type icon."""
    rect = pg.Rect(rect)
    rarity_color = ui.RARITY_COLORS.get(rarity, ui.BORDER)
    shadow = rect.move(max(2, rect.width // 12), max(2, rect.width // 12))
    pg.draw.rect(surface, ui.SHADOW, shadow, border_radius=max(2, rect.width // 8))
    fill = ui.lighten(rarity_color, 24) if hovered else rarity_color
    pg.draw.rect(surface, fill, rect, border_radius=max(2, rect.width // 8))
    pg.draw.rect(surface, ui.INK, rect, max(2, rect.width // 14),
                 border_radius=max(2, rect.width // 8))
    inner = rect.inflate(-rect.width * .18, -rect.height * .22)
    draw_item_symbol(surface, slot_type, inner, ui.INK)
    return rect
