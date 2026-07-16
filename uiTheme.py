"""Shared blocky UI primitives for RotBoi Remastered."""

import pygame as pg

import gameProfile


FONT_PATH = "data/media/coolveticarg.otf"

INK = pg.Color(12, 14, 18)
VOID = pg.Color(17, 20, 27)
PANEL = pg.Color(27, 31, 40)
PANEL_RAISED = pg.Color(37, 42, 53)
PANEL_HOVER = pg.Color(47, 53, 66)
BORDER = pg.Color(78, 87, 104)
TEXT = pg.Color(241, 237, 220)
MUTED = pg.Color(157, 164, 177)
CREAM = pg.Color(239, 211, 142)
RED = pg.Color(214, 78, 74)
GREEN = pg.Color(100, 190, 126)
BLUE = pg.Color(92, 151, 222)
GOLD = pg.Color(225, 169, 65)
PURPLE = pg.Color(175, 105, 218)
SHADOW = pg.Color(8, 9, 12)

RARITY_COLORS = {
    "Common": pg.Color(190, 195, 202),
    "Rare": BLUE,
    "Epic": PURPLE,
    "Legendary": GOLD,
    "Mythical": pg.Color(245, 241, 220),
}

_font_cache = {}

REFERENCE_WIDTH = 1920
REFERENCE_HEIGHT = 1080
MIN_DISPLAY_SCALE = .6
MAX_DISPLAY_SCALE = 2.4

TEXT_SIZE_LEVELS = (0.85, 1.0, 1.15, 1.3)
TEXT_SIZE_LABELS = ("SMALL", "NORMAL", "LARGE", "HUGE")


def display_scale(surface):
    """Return a height-aware UI scale that remains stable across aspect ratios."""
    width, height = surface.get_size()
    return max(MIN_DISPLAY_SCALE, min(
        MAX_DISPLAY_SCALE,
        min(width / REFERENCE_WIDTH, height / REFERENCE_HEIGHT),
    ))


def text_scale_multiplier():
    """User-configurable text size preference, layered on top of display_scale."""
    return float(gameProfile.profile.get("text_size", 1.0))


def font(size, italic=False, bold=False):
    size = max(9, int(round(size * text_scale_multiplier())))
    key = (size, bool(italic), bool(bold))
    if key not in _font_cache:
        typeface = pg.font.Font(FONT_PATH, size)
        typeface.set_italic(italic)
        typeface.set_bold(bold)
        _font_cache[key] = typeface
    return _font_cache[key]


def draw_text(surface, value, size, color=TEXT, position=(0, 0), anchor="topleft"):
    rendered = font(size).render(str(value), True, color)
    rect = rendered.get_rect()
    setattr(rect, anchor, (int(position[0]), int(position[1])))
    surface.blit(rendered, rect)
    return rect


def draw_panel(surface, rect, fill=PANEL, border=BORDER, shadow=5, hovered=False):
    rect = pg.Rect(rect)
    scale = display_scale(surface)
    shadow = max(0, round(shadow * scale))
    border_width = max(2, round(2 * scale))
    if shadow:
        shadow_rect = rect.move(shadow, shadow)
        pg.draw.rect(surface, SHADOW, shadow_rect)
    pg.draw.rect(surface, PANEL_HOVER if hovered else fill, rect)
    pg.draw.rect(surface, border, rect, border_width)
    pg.draw.line(surface, lighten(border, 28), rect.topleft, (rect.right - 1, rect.top), border_width)
    return rect


def draw_button(surface, rect, label, mouse_position, mouse_down=False, enabled=True,
                accent=CREAM, key_hint=None, text_size=18):
    rect = pg.Rect(rect)
    scale = display_scale(surface)
    hovered = enabled and rect.collidepoint(mouse_position)
    pressed = hovered and mouse_down
    visual_rect = rect.move(0, 3 if pressed else 0)
    fill = PANEL_HOVER if hovered else PANEL_RAISED
    if not enabled:
        fill, accent = PANEL, BORDER
    draw_panel(surface, visual_rect, fill=fill, border=accent, shadow=2 if pressed else 5)
    padding = max(6, round(8 * scale))
    hint_rect = None
    if key_hint:
        inset = padding
        hint_rect = pg.Rect(visual_rect.x + inset, visual_rect.y + inset, visual_rect.height - inset * 2, visual_rect.height - inset * 2)
        pg.draw.rect(surface, accent, hint_rect)
        draw_text(surface, key_hint, text_size * 0.72, INK, hint_rect.center, "center")
    if hint_rect:
        label_center = ((hint_rect.right + visual_rect.right) / 2, visual_rect.centery)
        available_width = (visual_rect.right - padding) - (hint_rect.right + padding)
    else:
        label_center = visual_rect.center
        available_width = visual_rect.width - padding * 2
    fitted_size = text_size
    while fitted_size > 9 and font(fitted_size).size(str(label))[0] > available_width:
        fitted_size -= 1
    draw_text(surface, label, fitted_size, TEXT if enabled else MUTED, label_center, "center")
    return hovered


def draw_progress(surface, rect, ratio, color, segments=10):
    rect = pg.Rect(rect)
    scale = display_scale(surface)
    ratio = max(0.0, min(1.0, ratio))
    pg.draw.rect(surface, INK, rect)
    border_width = max(2, round(2 * scale))
    pg.draw.rect(surface, BORDER, rect, border_width)
    inner = rect.inflate(-border_width * 2, -border_width * 2)
    fill_width = int(inner.width * ratio)
    if fill_width > 0:
        pg.draw.rect(surface, color, (inner.x, inner.y, fill_width, inner.height))
    if segments > 1:
        for index in range(1, segments):
            x = inner.x + int(inner.width * index / segments)
            pg.draw.line(surface, INK, (x, inner.y), (x, inner.bottom - 1), 1)


def draw_tag(surface, text, position, color=BLUE, text_size=11):
    scale = display_scale(surface)
    rendered = font(text_size).render(str(text).upper(), True, color)
    rect = rendered.get_rect(topleft=position).inflate(round(12 * scale), round(6 * scale))
    pg.draw.rect(surface, INK, rect)
    pg.draw.rect(surface, color, rect, max(1, round(scale)))
    surface.blit(rendered, rendered.get_rect(center=rect.center))
    return rect


def lighten(color, amount):
    color = pg.Color(color)
    return pg.Color(
        min(255, color.r + amount),
        min(255, color.g + amount),
        min(255, color.b + amount),
    )
