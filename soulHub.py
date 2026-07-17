"""Walkable Soul sanctuary and its interactive permanent-progression stations."""

from collections import deque
from math import hypot

import pygame as pg

import background as bG
from bossTypes import BOSS_CATALOG
import character as cH
import characterStats as cS
import gamePaths
import gameProfile
import items
import metaProgression
import uiTheme as ui
import upgrades
import variableHolster as vH


INTERACT_KEY = pg.K_f
INTERACT_RADIUS = vH.tileSizeGlobal * 2.1
overlay = None
archive_tab = 0
hub_seconds = 0.0
dummy_hits = deque()
dummy_flash = 0.0
dummy_session_best = 0.0


def _center():
    return (len(bG.currRoomRects[0]) * vH.tileSizeGlobal / 2,
            len(bG.currRoomRects) * vH.tileSizeGlobal / 2)


def _stations():
    cx, cy = _center()
    tile = vH.tileSizeGlobal
    return {
        "storage": (cx - tile * 13, cy, "PERMANENT CHEST", ui.GOLD),
        "dummy": (cx + tile * 11, cy, "DPS EFFIGY", ui.RED),
        "archive": (cx, cy - tile * 10, "ARCHIVE + MUSEUM", ui.BLUE),
        "skills": (cx, cy + tile * 10, "SOUL TREE", ui.PURPLE),
        "practice": (cx + tile * 11, cy - tile * 9, "BOSS MEMORY", ui.CREAM),
        "quests": (cx - tile * 11, cy - tile * 9, "QUEST ALTAR", ui.GREEN),
    }


def enter():
    global overlay, hub_seconds, dummy_session_best
    bG.configure_soul_hub()
    cH.resetAllStats()
    # resetAllStats uses the current map spawn and loads the permanent starting kit.
    cS.enemySpawningEnabled = False
    cS.autoFire = False
    cS.healthPoints = cS.maxHealthPoints
    overlay = None
    hub_seconds = 0.0
    dummy_session_best = 0.0
    dummy_hits.clear()


def _player_center():
    return bG.playerPosX + cS.playerSize / 2, bG.playerPosY + cS.playerSize / 2


def _near_station():
    px, py = _player_center()
    candidates = [(hypot(px - x, py - y), key) for key, (x, y, _label, _color) in _stations().items()
                  if key != "dummy"]
    distance, key = min(candidates, default=(99999, None))
    return key if distance <= INTERACT_RADIUS else None


def _draw_station(key, data):
    x, y, label, color = data
    sx, sy = bG.world_to_screen(x, y)
    radius = int(vH.tileSizeGlobal * (.62 if key != "dummy" else .8))
    pg.draw.circle(vH.screen, ui.SHADOW, (int(sx + 5), int(sy + 6)), radius)
    pg.draw.circle(vH.screen, ui.PANEL_RAISED, (int(sx), int(sy)), radius)
    pg.draw.circle(vH.screen, color, (int(sx), int(sy)), radius, 4)
    if key == "storage":
        pg.draw.rect(vH.screen, color, (sx-radius*.48, sy-radius*.22, radius*.96, radius*.55), 3)
    elif key == "dummy":
        pg.draw.line(vH.screen, color, (sx, sy-radius*.5), (sx, sy+radius*.55), 5)
        pg.draw.circle(vH.screen, color, (int(sx), int(sy-radius*.55)), int(radius*.23), 3)
    else:
        ui.draw_text(vH.screen, label[0], radius * .72, color, (sx, sy), "center")
    ui.draw_text(vH.screen, label, 8 * ui.display_scale(vH.screen), color,
                 (sx, sy + radius + 9), "midtop")


def _update_dummy():
    global dummy_flash, dummy_session_best
    x, y, _label, _color = _stations()["dummy"]
    size = vH.tileSizeGlobal * 1.6
    dummy_rect = pg.Rect(x - size / 2, y - size / 2, size, size)
    for bullet in cS.bulletHolster:
        if not bullet.remFlag and dummy_rect.colliderect(pg.Rect(bullet.worldX, bullet.worldY, bullet.size, bullet.size)):
            bullet.remFlag = True
            dummy_hits.append((hub_seconds, float(bullet.damage)))
            dummy_flash = .12
    while dummy_hits and hub_seconds - dummy_hits[0][0] > 5.0:
        dummy_hits.popleft()
    window = min(5.0, max(1.0, hub_seconds))
    dps = sum(hit[1] for hit in dummy_hits) / window
    dummy_session_best = max(dummy_session_best, dps)
    gameProfile.record_dummy_dps(dummy_session_best)
    dummy_flash = max(0.0, dummy_flash - min(vH.deltaMilliseconds, 50) / 1000.0)
    return dps


def _panel(title, subtitle=""):
    scale = ui.display_scale(vH.screen)
    width, height = min(vH.sW * .72, 960 * scale), min(vH.sH * .72, 650 * scale)
    rect = pg.Rect((vH.sW-width)/2, (vH.sH-height)/2, width, height)
    ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, ui.CREAM, shadow=10)
    ui.draw_text(vH.screen, title, 25 * scale, ui.TEXT, (rect.centerx, rect.y + 24*scale), "midtop")
    if subtitle:
        ui.draw_text(vH.screen, subtitle, 10 * scale, ui.MUTED, (rect.centerx, rect.y + 61*scale), "midtop")
    return rect, scale


def _draw_storage():
    rect, scale = _panel(
        "EXTRACTION CHEST",
        "Selected copies leave the chest when a run begins. Death or abandonment destroys them. C clears selection.",
    )
    storage = gameProfile.profile["storage"]
    loadout = gameProfile.profile["starting_loadout"]
    ui.draw_text(vH.screen, f"STORED  {len(storage)}  //  LOADOUT  {len(loadout)}/5", 11*scale,
                 ui.GOLD, (rect.x+28*scale, rect.y+98*scale))
    y = rect.y + 132*scale
    for index, data in enumerate(storage[:12]):
        drop = items.deserialize(data)
        if drop is None:
            continue
        row = pg.Rect(rect.x+28*scale, y+index*36*scale, rect.width-56*scale, 30*scale)
        hovered = row.collidepoint(vH.mouseX, vH.mouseY)
        pg.draw.rect(vH.screen, ui.PANEL if not hovered else ui.PANEL_RAISED, row)
        pg.draw.rect(vH.screen, ui.RARITY_COLORS.get(drop.rarity, ui.BORDER), row, 2)
        ui.draw_text(vH.screen, f"{index+1:02}  {drop.name}  //  {drop.rarity}", 10*scale,
                     ui.TEXT, (row.x+9*scale, row.centery), "midleft")
        if hovered:
            ui.draw_text(vH.screen, items.describe(drop), 8*scale, ui.CREAM,
                         (rect.centerx, rect.bottom-31*scale), "center")
        if hovered and vH.mousePressed:
            metaProgression.equip_from_storage(index)


def _draw_skills():
    rect, scale = _panel("SOUL TREE", f"SOUL TOKENS  //  {gameProfile.profile['soul_tokens']}")
    y = rect.y + 112*scale
    for index, node in enumerate(metaProgression.SKILL_NODES):
        owned = node.key in gameProfile.profile["skill_nodes"]
        row = pg.Rect(rect.x+35*scale, y+index*72*scale, rect.width-70*scale, 56*scale)
        hovered = row.collidepoint(vH.mouseX, vH.mouseY)
        ui.draw_panel(vH.screen, row, ui.PANEL, ui.GREEN if owned else ui.PURPLE, hovered=hovered)
        ui.draw_text(vH.screen, f"{index+1}. {node.name}", 13*scale, ui.TEXT, (row.x+12*scale, row.y+9*scale))
        ui.draw_text(vH.screen, node.description, 9*scale, ui.MUTED, (row.x+12*scale, row.bottom-10*scale), "bottomleft")
        ui.draw_text(vH.screen, "OWNED" if owned else f"{node.cost} TOKEN", 10*scale,
                     ui.GREEN if owned else ui.GOLD, (row.right-12*scale, row.centery), "midright")
        if hovered and vH.mousePressed:
            gameProfile.purchase_skill(node.key, node.cost)


def _draw_archive():
    tabs = ("RECORDS", "BESTIARY", "CARDS", "ITEMS", "MUSEUM")
    rect, scale = _panel("ARCHIVE", "1-5 changes collection // Research grows when foes are seen and defeated")
    tab = tabs[archive_tab]
    ui.draw_text(vH.screen, "   //   ".join(f"{i+1} {name}" for i, name in enumerate(tabs)),
                 10*scale, ui.BLUE, (rect.centerx, rect.y+92*scale), "center")
    y = rect.y + 132*scale
    if tab == "RECORDS":
        lines = [
            f"Best run level: {gameProfile.profile['best_level']}",
            f"Best kills: {gameProfile.profile['best_kills']}",
            f"Completed runs: {gameProfile.profile['completed_runs']}",
            f"Best dummy DPS: {gameProfile.profile['best_dummy_dps']:.1f}",
        ]
        for path in gamePaths.PATHS.values():
            lines.append(f"{path.title}: {metaProgression.mastery_title(path.key)} ({gameProfile.profile['path_mastery'].get(path.key, 0)} clears)")
    elif tab == "BESTIARY":
        records = list(gameProfile.profile["boss_research"].items()) + list(gameProfile.profile["enemy_research"].items())
        lines = [f"{key.replace('_',' ').title()}: research {min(3, int(data.get('seen',0)>0)+int(data.get('defeated',0)>0)+int(data.get('defeated',0)>=5))}/3  // defeated {data.get('defeated',0)}"
                 for key, data in records[:18]] or ["No creatures recorded yet."]
    elif tab == "CARDS":
        found = set(gameProfile.profile["discovered_cards"])
        lines = [f"{'FOUND' if card.name in found else '?????'}  //  {card.name if card.name in found else card.category.title()}"
                 for card in upgrades.DEFINITIONS]
    elif tab == "ITEMS":
        found = set(gameProfile.profile["discovered_items"])
        lines = [f"{'FOUND' if definition.name in found else '?????'}  //  {definition.name if definition.name in found else definition.slot_type.title()}"
                 for definition in items.DEFINITIONS]
    else:
        artifacts = gameProfile.profile["museum_artifacts"]
        lines = [f"Path relic: {name.replace('_echo','').title()} Echo" for name in artifacts]
        lines += [f"Recovered item: {name}" for name in gameProfile.profile["discovered_items"][:10]]
        lines = lines or ["Empty plinths wait for completed paths and recovered items."]
    for index, line in enumerate(lines[:19]):
        ui.draw_text(vH.screen, line, 10*scale, ui.TEXT if index % 2 == 0 else ui.CREAM,
                     (rect.x+38*scale, y+index*23*scale))


def _draw_quests():
    metaProgression.complete_ready_quests()
    rect, scale = _panel("QUESTS", "Permanent objectives award Soul tokens when completed")
    y = rect.y + 120*scale
    progress = gameProfile.profile["quest_progress"]
    completed = gameProfile.profile["completed_quests"]
    for index, (quest_id, name, description, counter, target) in enumerate(metaProgression.QUESTS):
        value = min(target, int(progress.get(counter, 0)))
        row = pg.Rect(rect.x+38*scale, y+index*92*scale, rect.width-76*scale, 72*scale)
        ui.draw_panel(vH.screen, row, ui.PANEL, ui.GREEN if quest_id in completed else ui.GOLD)
        ui.draw_text(vH.screen, name, 14*scale, ui.TEXT, (row.x+12*scale, row.y+10*scale))
        ui.draw_text(vH.screen, description, 9*scale, ui.MUTED, (row.x+12*scale, row.y+34*scale))
        ui.draw_text(vH.screen, "COMPLETE" if quest_id in completed else f"{value}/{target}", 11*scale,
                     ui.GREEN if quest_id in completed else ui.GOLD, (row.right-12*scale, row.centery), "midright")


def _practice_boss(key):
    for path in gamePaths.PATHS.values():
        if key in (path.mid_boss, path.final_boss):
            gamePaths.select(path.key)
            gamePaths.activate_selected()
            break
    cH.resetAllStats()
    cS.practiceBossKey = key
    cS.practiceMode = True
    cS.bossDebugRequested = False
    vH.state = vH.States.GAMERUN


def _draw_practice():
    rect, scale = _panel("BOSS MEMORY", "Defeated or encountered bosses become available for practice")
    known = [key for key in BOSS_CATALOG.definitions if key in gameProfile.profile["boss_research"]]
    if not known:
        ui.draw_text(vH.screen, "No boss memories have been recorded yet.", 13*scale, ui.MUTED, rect.center, "center")
        return
    y = rect.y + 118*scale
    for index, key in enumerate(known[:10]):
        definition = BOSS_CATALOG.definitions[key]
        row = pg.Rect(rect.x+40*scale, y+index*43*scale, rect.width-80*scale, 34*scale)
        hovered = row.collidepoint(vH.mouseX, vH.mouseY)
        ui.draw_panel(vH.screen, row, ui.PANEL, ui.RED, hovered=hovered)
        ui.draw_text(vH.screen, f"{index+1}. {definition.display_name}", 11*scale, ui.TEXT,
                     (row.x+10*scale, row.centery), "midleft")
        if hovered and vH.mousePressed:
            _practice_boss(key)


def _draw_overlay():
    if overlay == "storage":
        _draw_storage()
    elif overlay == "skills":
        _draw_skills()
    elif overlay == "archive":
        _draw_archive()
    elif overlay == "quests":
        _draw_quests()
    elif overlay == "practice":
        _draw_practice()


def run():
    global overlay, archive_tab, hub_seconds
    hub_seconds += min(vH.deltaMilliseconds, 50) / 1000.0
    cH.drawBackground()
    if overlay is None:
        cH.movePlayer()
        cH.handlingBulletCreation()
        cH.handlingBulletUpdating()
    dps = _update_dummy()
    for key, station in _stations().items():
        _draw_station(key, station)
    cH.drawPlayer()

    scale = ui.display_scale(vH.screen)
    ui.draw_text(vH.screen, "THE SOUL", 23*scale, ui.TEXT, (24*scale, 20*scale))
    ui.draw_text(vH.screen, "WANDER // FIRE AT THE EFFIGY // F INTERACT // ESC RETURN", 9*scale,
                 ui.MUTED, (24*scale, 53*scale))
    ui.draw_text(vH.screen, f"DPS {dps:,.1f}  //  SESSION BEST {dummy_session_best:,.1f}  //  ALL-TIME {gameProfile.profile['best_dummy_dps']:,.1f}",
                 10*scale, ui.RED, (vH.sW-24*scale, 25*scale), "topright")
    nearby = _near_station() if overlay is None else None
    if nearby:
        label = _stations()[nearby][2]
        ui.draw_tag(vH.screen, f"F  //  OPEN {label}", (vH.sW/2-90*scale, vH.sH-50*scale),
                    _stations()[nearby][3], int(10*scale))
    if overlay is not None:
        _draw_overlay()

    if INTERACT_KEY in vH.keyPressed and overlay is None and nearby:
        overlay = nearby
    if pg.K_c in vH.keyPressed and overlay == "storage":
        metaProgression.clear_loadout()
    if overlay == "archive":
        for index, key in enumerate((pg.K_1, pg.K_2, pg.K_3, pg.K_4, pg.K_5)):
            if key in vH.keyPressed:
                archive_tab = index
    if pg.K_ESCAPE in vH.keyPressed:
        if overlay is not None:
            overlay = None
        else:
            vH.state = vH.States.TITLESCREEN
            vH.hasBeenReset = False
