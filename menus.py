"""Pause, settings, and end-of-run screens."""

import pygame as pg

import characterStats as cS
import gameProfile
import uiTheme as ui
import variableHolster as vH


_buttons = {}
_settings_tab = "gameplay"

_GAMEPLAY_OPTIONS = (
    ("casual_mode", "CASUAL ASSIST", "20% less incoming damage"),
    ("autofire", "DEFAULT AUTOFIRE", "New runs begin firing automatically"),
    ("tutorial_hints", "CONTEXT HINTS", "Show short first-run reminders"),
    ("aim_guide", "AIM GUIDE", "Draw a short aiming line"),
    ("damage_numbers", "DAMAGE NUMBERS", "Show combat damage text"),
    ("high_contrast", "HIGH CONTRAST", "Brighten hostile warnings"),
)


def _backdrop(title, subtitle):
    vH.screen.fill(ui.VOID)
    grid = max(28, int(min(vH.sW, vH.sH) / 28))
    for x in range(0, int(vH.sW), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (x, 0), (x, vH.sH))
    for y in range(0, int(vH.sH), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (0, y), (vH.sW, y))
    scale = ui.display_scale(vH.screen)
    ui.draw_text(vH.screen, title, 34 * scale, ui.TEXT, (vH.sW / 2, vH.sH * .09), "midtop")
    ui.draw_text(vH.screen, subtitle, 12 * scale, ui.CREAM, (vH.sW / 2, vH.sH * .17), "midtop")
    return scale


def _button(name, rect, label, accent=ui.CREAM, key=None, enabled=True):
    _buttons[name] = pg.Rect(rect)
    return ui.draw_button(vH.screen, rect, label, (vH.mouseX, vH.mouseY), vH.mouseDown,
                          enabled, accent, key, int(15 * ui.display_scale(vH.screen)))


def _activated(name):
    return vH.mousePressed and _buttons.get(name, pg.Rect(0, 0, 0, 0)).collidepoint(vH.mouseX, vH.mouseY)


def draw_pause():
    scale = _backdrop("RUN PAUSED", "Take a breath. Combat is fully stopped.")
    width = min(vH.sW * .68, 900 * scale)
    left = (vH.sW - width) / 2
    button_w, button_h = width * .34, 58 * scale
    _button("resume", (left, vH.sH * .25, button_w, button_h), "RESUME", ui.GREEN, "ESC")
    _button("restart", (left, vH.sH * .25 + 72 * scale, button_w, button_h), "RESTART RUN", ui.GOLD, "R")
    _button("title", (left, vH.sH * .25 + 144 * scale, button_w, button_h), "RETURN TO TITLE", ui.RED, "Q")

    settings = pg.Rect(left + width * .40, vH.sH * .25, width * .60, vH.sH * .48)
    ui.draw_panel(vH.screen, settings, ui.PANEL, ui.BLUE, shadow=6)

    tab_h = 38 * scale
    tab_w = settings.width / 2
    for index, (key, label) in enumerate((("gameplay", "GAMEPLAY"), ("options", "OPTIONS"))):
        rect = pg.Rect(settings.x + index * tab_w, settings.y, tab_w, tab_h)
        _button(f"tab_{key}", rect, label, ui.BLUE if _settings_tab == key else ui.BORDER)

    body_top = settings.y + tab_h + 12 * scale
    if _settings_tab == "gameplay":
        for index, (key, label, description) in enumerate(_GAMEPLAY_OPTIONS):
            y = body_top + index * 51 * scale
            rect = pg.Rect(settings.x + 14 * scale, y, settings.width - 28 * scale, 42 * scale)
            active = bool(gameProfile.profile[key])
            _button(key, rect, f"{label}  //  {'ON' if active else 'OFF'}",
                    ui.GREEN if active else ui.BORDER)
            ui.draw_text(vH.screen, description, 8 * scale, ui.MUTED,
                         (rect.x + 10 * scale, rect.bottom - 4 * scale), "bottomleft")
    else:
        shake_rect = pg.Rect(settings.x + 14 * scale, body_top, settings.width - 28 * scale, 42 * scale)
        _button("screen_shake", shake_rect,
                f"SCREEN SHAKE  //  {int(float(gameProfile.profile['screen_shake']) * 100)}%", ui.GOLD)
        ui.draw_text(vH.screen, "How strongly hits rattle the camera", 8 * scale, ui.MUTED,
                     (shake_rect.x + 10 * scale, shake_rect.bottom - 4 * scale), "bottomleft")

        text_size_rect = pg.Rect(settings.x + 14 * scale, body_top + 51 * scale,
                                 settings.width - 28 * scale, 42 * scale)
        levels = ui.TEXT_SIZE_LEVELS
        labels = ui.TEXT_SIZE_LABELS
        current = float(gameProfile.profile["text_size"])
        idx = min(range(len(levels)), key=lambda i: abs(levels[i] - current))
        _button("text_size", text_size_rect, f"TEXT SIZE  //  {labels[idx]}", ui.GOLD)
        ui.draw_text(vH.screen, "Scales all in-game text", 8 * scale, ui.MUTED,
                     (text_size_rect.x + 10 * scale, text_size_rect.bottom - 4 * scale), "bottomleft")

    ui.draw_text(vH.screen, "TAB toggles HUD details during play", 9 * scale, ui.MUTED,
                 (vH.sW / 2, vH.sH * .82), "center")


def handle_pause():
    global _settings_tab
    import character as game

    if pg.K_ESCAPE in vH.keyPressed or _activated("resume"):
        vH.state = vH.pauseReturnState
        return
    if pg.K_r in vH.keyPressed or _activated("restart"):
        game.resetAllStats()
        vH.state = vH.States.GAMERUN
        return
    if pg.K_q in vH.keyPressed or _activated("title"):
        vH.state = vH.States.TITLESCREEN
        vH.hasBeenReset = False
        return

    if _activated("tab_gameplay"):
        _settings_tab = "gameplay"
    elif _activated("tab_options"):
        _settings_tab = "options"

    if _settings_tab == "gameplay":
        for key, _label, _description in _GAMEPLAY_OPTIONS:
            if _activated(key):
                gameProfile.toggle(key)
                if key == "autofire":
                    cS.autoFire = bool(gameProfile.profile[key])
    else:
        if _activated("screen_shake"):
            levels = (0.0, .35, .65, 1.0)
            current = float(gameProfile.profile["screen_shake"])
            gameProfile.profile["screen_shake"] = levels[(min(range(len(levels)), key=lambda i: abs(levels[i] - current)) + 1) % len(levels)]
            gameProfile.save_profile()
        if _activated("text_size"):
            levels = ui.TEXT_SIZE_LEVELS
            current = float(gameProfile.profile["text_size"])
            idx = min(range(len(levels)), key=lambda i: abs(levels[i] - current))
            gameProfile.profile["text_size"] = levels[(idx + 1) % len(levels)]
            gameProfile.save_profile()


def draw_results():
    completed = cS.runOutcome == "RUN COMPLETE"
    accent = ui.CREAM if completed else ui.RED
    scale = _backdrop(cS.runOutcome, "The build is saved to your run record.")
    width = min(vH.sW * .62, 820 * scale)
    panel = pg.Rect((vH.sW - width) / 2, vH.sH * .25, width, vH.sH * .38)
    ui.draw_panel(vH.screen, panel, ui.PANEL_RAISED, accent, shadow=8)
    stats = (("LEVEL", f"{cS.currentLevel:02}"), ("KILLS", str(cS.numOfEnemiesKilled)),
             ("TIME", f"{int(cS.runTimeSeconds // 60):02}:{int(cS.runTimeSeconds % 60):02}"),
             ("UPGRADES", str(sum(cS.upgradeCollection['types'].values()))))
    cell = panel.width / len(stats)
    for index, (label, value) in enumerate(stats):
        x = panel.x + cell * (index + .5)
        ui.draw_text(vH.screen, value, 29 * scale, accent, (x, panel.y + 55 * scale), "center")
        ui.draw_text(vH.screen, label, 9 * scale, ui.MUTED, (x, panel.y + 89 * scale), "center")
    families = {}
    for name, count in cS.upgradeCollection["types"].items():
        import upgrades
        definition = upgrades.DEFINITIONS_BY_NAME.get(name)
        if definition:
            families[definition.category] = families.get(definition.category, 0) + count
    shape = max(families, key=families.get).upper() if families else "UNSHAPED"
    ui.draw_text(vH.screen, f"RUN SHAPE  //  {shape}", 13 * scale, ui.PURPLE,
                 (panel.centerx, panel.bottom - 50 * scale), "center")
    _button("retry", (panel.x, panel.bottom + 24 * scale, panel.width * .48, 58 * scale),
            "PLAY AGAIN", ui.GREEN, "ENTER")
    _button("results_title", (panel.right - panel.width * .48, panel.bottom + 24 * scale,
                               panel.width * .48, 58 * scale), "TITLE SCREEN", ui.RED, "ESC")


def handle_results():
    import character as game

    if pg.K_RETURN in vH.keyPressed or _activated("retry"):
        game.resetAllStats()
        vH.state = vH.States.GAMERUN
    elif pg.K_ESCAPE in vH.keyPressed or _activated("results_title"):
        vH.state = vH.States.TITLESCREEN
        vH.hasBeenReset = False
