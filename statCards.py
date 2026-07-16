"""Resolution-independent stat symbols and collectible mini upgrade cards."""

import pygame as pg

import uiTheme as ui


def _line(surface, color, start, end, width):
    pg.draw.line(surface, color, start, end, max(1, int(width)))


def draw_stat_symbol(surface, stat_name, rect, color=ui.TEXT):
    """Draw a compact silhouette for every upgrade stat using Pygame primitives."""
    rect = pg.Rect(rect)
    cx, cy = rect.center
    unit = max(1, min(rect.width, rect.height) / 20)
    stroke = max(1, round(unit * 1.7))
    name = stat_name.lower()

    def bullet(x=cx, y=cy, scale=1.0):
        length, radius = 10 * unit * scale, 3.5 * unit * scale
        body = pg.Rect(x - length * .45, y - radius, length * .65, radius * 2)
        pg.draw.rect(surface, color, body, border_radius=max(1, round(radius)))
        pg.draw.polygon(surface, color, ((body.right - unit, body.top),
                                         (x + length * .55, y),
                                         (body.right - unit, body.bottom)))

    if name == "defense":
        points = ((cx, cy - 8 * unit), (cx + 7 * unit, cy - 5 * unit),
                  (cx + 6 * unit, cy + 3 * unit), (cx, cy + 9 * unit),
                  (cx - 6 * unit, cy + 3 * unit), (cx - 7 * unit, cy - 5 * unit))
        pg.draw.polygon(surface, color, points, stroke)
        _line(surface, color, (cx, cy - 6 * unit), (cx, cy + 6 * unit), stroke)
    elif name in ("health", "vitality"):
        _line(surface, color, (cx - 7 * unit, cy), (cx + 7 * unit, cy), stroke)
        _line(surface, color, (cx, cy - 7 * unit), (cx, cy + 7 * unit), stroke)
        if name == "vitality":
            pg.draw.circle(surface, color, (cx, cy), round(9 * unit), stroke)
    elif name == "bullet pierce":
        bullet(cx - 2 * unit, cy)
        for x in (cx + 5 * unit, cx + 8 * unit):
            _line(surface, color, (x, cy - 7 * unit), (x, cy + 7 * unit), stroke)
    elif name == "bullet count":
        for offset in (-5, 0, 5):
            bullet(cx - 1 * unit, cy + offset * unit, .72)
    elif name == "spread angle":
        origin = (cx - 7 * unit, cy)
        for offset in (-7, 0, 7):
            _line(surface, color, origin, (cx + 8 * unit, cy + offset * unit), stroke)
    elif name == "attack speed":
        pg.draw.circle(surface, color, (cx, cy), round(8 * unit), stroke)
        _line(surface, color, (cx, cy), (cx + 5 * unit, cy - 4 * unit), stroke)
        for offset in (-4, 0, 4):
            _line(surface, color, (cx - 11 * unit, cy + offset * unit),
                  (cx - 7 * unit, cy + offset * unit), stroke)
    elif name == "bullet speed":
        bullet(cx + 2 * unit, cy)
        for offset, length in ((-5, 5), (0, 8), (5, 5)):
            _line(surface, color, (cx - (length + 3) * unit, cy + offset * unit),
                  (cx - 3 * unit, cy + offset * unit), stroke)
    elif name == "bullet range":
        bullet(cx - 4 * unit, cy)
        _line(surface, color, (cx + 2 * unit, cy), (cx + 9 * unit, cy), stroke)
        _line(surface, color, (cx + 7 * unit, cy - 3 * unit), (cx + 10 * unit, cy), stroke)
        _line(surface, color, (cx + 7 * unit, cy + 3 * unit), (cx + 10 * unit, cy), stroke)
    elif name == "bullet damage":
        bullet(cx - 3 * unit, cy)
        for angle in range(0, 360, 45):
            direction = pg.Vector2(1, 0).rotate(angle)
            _line(surface, color, (cx + direction.x * 4 * unit, cy + direction.y * 4 * unit),
                  (cx + direction.x * 8 * unit, cy + direction.y * 8 * unit), stroke)
    elif name == "bullet size":
        bullet(cx - 2 * unit, cy + 2 * unit, 1.35)
        _line(surface, color, (cx + 7 * unit, cy + 6 * unit), (cx + 7 * unit, cy - 7 * unit), stroke)
        pg.draw.polygon(surface, color, ((cx + 7 * unit, cy - 9 * unit),
                                         (cx + 3 * unit, cy - 4 * unit),
                                         (cx + 11 * unit, cy - 4 * unit)))
    elif name == "player speed":
        pg.draw.circle(surface, color, (cx + 2 * unit, cy - 6 * unit), round(2.5 * unit))
        _line(surface, color, (cx + 1 * unit, cy - 3 * unit), (cx - 2 * unit, cy + 2 * unit), stroke)
        _line(surface, color, (cx - 2 * unit, cy + 2 * unit), (cx + 4 * unit, cy + 7 * unit), stroke)
        _line(surface, color, (cx - 2 * unit, cy + 2 * unit), (cx - 7 * unit, cy + 8 * unit), stroke)
        for offset in (-5, 0, 5):
            _line(surface, color, (cx - 10 * unit, cy + offset * unit),
                  (cx - 6 * unit, cy + offset * unit), stroke)
    elif name == "crit chance":
        pg.draw.circle(surface, color, (cx, cy), round(8 * unit), stroke)
        pg.draw.circle(surface, color, (cx, cy), round(3 * unit), stroke)
        _line(surface, color, (cx + 2 * unit, cy - 2 * unit),
              (cx + 9 * unit, cy - 9 * unit), stroke)
    elif name == "crit damage":
        points = []
        for index in range(16):
            radius = (9 if index % 2 == 0 else 4) * unit
            point = pg.Vector2(0, -radius).rotate(index * 22.5)
            points.append((cx + point.x, cy + point.y))
        pg.draw.polygon(surface, color, points, stroke)
    elif name == "aura size":
        for radius in (3, 6, 9):
            pg.draw.circle(surface, color, (cx, cy), round(radius * unit), stroke)
    elif name == "aura strength":
        pg.draw.circle(surface, color, (cx, cy), round(3 * unit), stroke)
        for angle in (0, 90, 180, 270):
            direction = pg.Vector2(0, -1).rotate(angle)
            start = (cx + direction.x * 9 * unit, cy + direction.y * 9 * unit)
            end = (cx + direction.x * 4 * unit, cy + direction.y * 4 * unit)
            _line(surface, color, start, end, stroke)
            side = direction.rotate(28)
            pg.draw.polygon(surface, color, (end,
                (end[0] + side.x * 3 * unit, end[1] + side.y * 3 * unit),
                (end[0] + direction.rotate(-28).x * 3 * unit,
                 end[1] + direction.rotate(-28).y * 3 * unit)))
    elif name == "exp multiplier":
        gem = ((cx, cy - 9 * unit), (cx + 7 * unit, cy - 3 * unit),
               (cx + 5 * unit, cy + 7 * unit), (cx - 5 * unit, cy + 7 * unit),
               (cx - 7 * unit, cy - 3 * unit))
        pg.draw.polygon(surface, color, gem, stroke)
        _line(surface, color, (cx - 6 * unit, cy - 3 * unit),
              (cx + 6 * unit, cy - 3 * unit), stroke)
    else:
        pg.draw.circle(surface, color, (cx, cy), round(7 * unit), stroke)
        _line(surface, color, (cx, cy - 4 * unit), (cx, cy + 4 * unit), stroke)


def draw_upgrade_card(surface, rect, stat_name, rarity, math_type, hovered=False):
    """Draw a rarity-backed mini card with a stat icon and scaling corner mark."""
    rect = pg.Rect(rect)
    rarity_color = ui.RARITY_COLORS.get(rarity, ui.BORDER)
    shadow = rect.move(max(2, rect.width // 12), max(2, rect.width // 12))
    pg.draw.rect(surface, ui.SHADOW, shadow, border_radius=max(2, rect.width // 8))
    fill = ui.lighten(rarity_color, 24) if hovered else rarity_color
    pg.draw.rect(surface, fill, rect, border_radius=max(2, rect.width // 8))
    pg.draw.rect(surface, ui.INK, rect, max(2, rect.width // 14),
                 border_radius=max(2, rect.width // 8))
    inner = rect.inflate(-rect.width * .18, -rect.height * .22)
    draw_stat_symbol(surface, stat_name, inner, ui.INK)

    mark_center = (rect.right - rect.width * .18, rect.y + rect.height * .17)
    mark = rect.width * .10
    stroke = max(2, rect.width // 14)
    if math_type == "multiplicative":
        _line(surface, ui.TEXT, (mark_center[0] - mark, mark_center[1] - mark),
              (mark_center[0] + mark, mark_center[1] + mark), stroke)
        _line(surface, ui.TEXT, (mark_center[0] + mark, mark_center[1] - mark),
              (mark_center[0] - mark, mark_center[1] + mark), stroke)
    else:
        _line(surface, ui.TEXT, (mark_center[0] - mark, mark_center[1]),
              (mark_center[0] + mark, mark_center[1]), stroke)
        _line(surface, ui.TEXT, (mark_center[0], mark_center[1] - mark),
              (mark_center[0], mark_center[1] + mark), stroke)
    return rect
