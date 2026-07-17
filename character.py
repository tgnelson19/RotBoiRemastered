import variableHolster as vH
import background as bG
import characterStats as cS
import pygame as pg
from bullet import Bullet
from bossTypes import BOSS_CATALOG
from damageText import DamageText
from experienceBubble import ExperienceBubble
from enemyProjectile import EnemyProjectile
from lootCrate import LootCrate
import items
import keybinds
from informationSheet import InformationSheet
from levelingHandler import LevelingHandler
from math import atan, atan2, ceil, floor, pi, trunc, hypot
from random import randint
import upgrades
import uiTheme as ui
import gameProfile
import gamePaths
from spatialHash import SpatialHash
from progression import (FINAL_BOSS_LEVEL, MAX_LEVEL, MID_BOSS_LEVEL,
                         MINIBOSS_GATES, encounter_caps, encounter_pacing)

# helper functions for repeated game calculations

def multiply_list(values):
    result = 1
    for num in values:
        result *= num
    return result


def _combine_stat(stat_name):
    base_value = cS.collectiveStats[stat_name]
    additive = sum(cS.collectiveAddStats[stat_name])
    multiplicative = multiply_list(cS.collectiveMultStats[stat_name])
    return (base_value + additive) * multiplicative


def _is_overlap(x1, y1, size1, x2, y2, size2):
    return (x1 + size1 > x2 and x1 < x2 + size2 and
            y1 + size1 > y2 and y1 < y2 + size2)


def _direction_to_target(origin_x, origin_y, target_x, target_y):
    return atan2(origin_y - target_y, target_x - origin_x)

#
#   Warning, all stats added to character stats needs to be included in reset here in order to be reset
#   This is bad design, but I am working on figuring out a way to reset the stats without using this
#   Also note that some stats are located in different sheets and must be accounted for as well
#
#   FOR QUICK STAT MODIFICATIONS CHANGE THEM HERE
#

titleFont = pg.font.Font("data/media/coolveticarg.otf", int(vH.tileSizeGlobal*(2/3)))
textColor = (245,245,220)

MAX_LOOT_CRATES = 40
CRATE_INTERACT_RADIUS = 24

def resetAllStats():
    
    bG.playerPosX = bG.spawnX
    bG.playerPosY = bG.spawnY
    bG.set_camera_angle(0)
    vH.screenShakeX = 0
    vH.screenShakeY = 0
    
    cS.playerSpeed = 2.1
    cS.playerSize = vH.tileSizeGlobal * .75
    cS.playerColor = pg.Color(0,0,120)

    cS.dX, cS.dY = 0, 0

    cS.currTileX = 0
    cS.currTileY = 0

    cS.playerRect = pg.Rect(bG.lockX, bG.lockY, cS.playerSize, cS.playerSize)

    cS.projectileCount = 1
    cS.azimuthalProjectileAngle = pi/8

    cS.attackCooldownStat = 40
    cS.attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

    cS.bulletDamage = 100
    cS.bulletSpeed = 4
    cS.bulletRange = 250
    cS.bulletSize = vH.tileSizeGlobal / 2
    cS.bulletColor = pg.Color(125,125,125)
    cS.bulletPierce = 1
    cS.critChance = 0.05
    cS.critDamage = 2

    cS.aura = 50
    cS.auraSpeed = 2
    # Twenty applications reach approximately the old ten-level spawn cadence.
    cS.levelMod = 1.04
    cS.xpMult = 1
    cS.currentLevel = 0
    cS.pendingLevelUps = 0
    cS.expCount = 0
    cS.expNeededForNextLevel = 40
    cS.baseExpNeededForNextLevel = 40
    cS.levelScaleIncreaseFunction = 1.15

    cS.healthPoints = 1000
    cS.maxHealthPoints = 1000
    cS.vitality = 25
    cS.healthRecoveryBuffer = 0.0
    cS.defense = 0
    cS.currEnemyCount = 0
    cS.enemyCap = 50
    cS.enemyThreatCap = 36.0
    cS.enemyPopulationThreatCap = 60.0
    cS.playerInvulnerabilityTimer = 0
    cS.playerInvulnerabilityMax = vH.frameRate * 0.55
    cS.gracePeriod = vH.frameRate * 1.25

    cS.enemyOneInFramesChance = 220
    cS.enemySpawnTimer = vH.frameRate * 1.0
    cS.encounterSpawnCooldown = 0

    cS.numOfEnemiesKilled = 0
    cS.currentStage = 1
    cS.xpMult = 1
    cS.experienceStageMod = 1.1

    cS.dashDuration = vH.frameRate * 0.15
    cS.dashing = False

    cS.dashModifier = 4

    cS.dashCooldownMax = vH.frameRate * 1
    cS.currDashCooldown = 0

    cS.fdX, cS.fdY = 0, 0

    cS.bulletHolster = []
    cS.enemyHolster = []
    cS.damageTextList = []
    cS.experienceList = []
    cS.enemyProjectileHolster = []
    cS.lootCrateList = []
    cS.equipment = {"weapon": None, "armor": None, "ring": None, "accessory_1": None, "accessory_2": None}
    vH.dragInProgress = False
    cS.activeBoss = None
    cS.bossDebugRequested = False
    cS.bossDebugInvincible = False
    cS.beaudisEncounterStarted = False
    cS.beaudisDefeated = False
    cS.dissonanceEncounterStarted = False
    cS.gameCompleted = False
    cS.runTimeSeconds = 0.0
    cS.runOutcome = "DEFEATED"
    cS.currentBounty = None
    cS.lastUpgrade = None
    cS.lastUpgradeAt = -100.0
    cS.guaranteedMiniBossesSpawned = set()
    cS.enemySpawningEnabled = True
    cS.autoFire = bool(gameProfile.profile["autofire"])

    cS.informationSheet = InformationSheet()
    bG.lockX = cS.informationSheet.arena_width / 2

    cS.levelingHandler = LevelingHandler()
    cS.reset_upgrade_tracking()
    cS.reset_boss_afflictions()
    cS.reset_dream_state()
    
    cS.newRandoUps = False
    
    cS.collectiveStats = {"Defense" : cS.defense, "Health" : cS.maxHealthPoints, "Vitality" : cS.vitality, "Bullet Pierce" : cS.bulletPierce, "Bullet Count" : cS.projectileCount, "Spread Angle" : cS.azimuthalProjectileAngle, 
                                  "Attack Speed" : cS.attackCooldownStat, "Bullet Speed" : cS.bulletSpeed, "Bullet Range" : cS.bulletRange, "Bullet Damage" : cS.bulletDamage, 
                                  "Bullet Size" : cS.bulletSize, "Player Speed" : cS.playerSpeed, "Crit Chance" : cS.critChance, "Crit Damage" : cS.critDamage, 
                                  "Aura Size" : cS.aura, "Aura Strength" : cS.auraSpeed, "Exp Multiplier": cS.xpMult}
        
    cS.collectiveAddStats = {"Defense" : [0], "Health" : [0], "Vitality" : [0], "Bullet Pierce" : [0], "Bullet Count" : [0], "Spread Angle" : [0], 
                                "Attack Speed" : [0], "Bullet Speed" : [0], "Bullet Range" : [0], "Bullet Damage" : [0], 
                                "Bullet Size" : [0], "Player Speed" : [0], "Crit Chance": [0], "Crit Damage": [0],
                                "Aura Size" : [0], "Aura Strength" : [0], "Exp Multiplier": [0]}
    
    cS.collectiveMultStats = {"Defense" : [1], "Health" : [1], "Vitality" : [1], "Bullet Pierce" : [1], "Bullet Count" : [1], "Spread Angle" : [1], 
                                "Attack Speed" : [1], "Bullet Speed" : [1], "Bullet Range" : [1], "Bullet Damage" : [1], 
                                "Bullet Size" : [1], "Player Speed" : [1], "Crit Chance": [1], "Crit Damage": [1],
                                "Aura Size" : [1], "Aura Strength" : [1], "Exp Multiplier": [1]}
    
def combarinoPlayerStats():
    previous_max_health = cS.maxHealthPoints

    cS.projectileCount = (cS.collectiveStats["Bullet Count"] + sum(cS.collectiveAddStats["Bullet Count"])) * (multiply_list(cS.collectiveMultStats["Bullet Count"]))
    cS.azimuthalProjectileAngle = (cS.collectiveStats["Spread Angle"] + sum(cS.collectiveAddStats["Spread Angle"])) * (multiply_list(cS.collectiveMultStats["Spread Angle"]))
    cS.playerSpeed = (cS.collectiveStats["Player Speed"] + sum(cS.collectiveAddStats["Player Speed"])) * (multiply_list(cS.collectiveMultStats["Player Speed"]))
    cS.attackCooldownStat = (cS.collectiveStats["Attack Speed"] + sum(cS.collectiveAddStats["Attack Speed"])) * (multiply_list(cS.collectiveMultStats["Attack Speed"]))
    if(cS.attackCooldownStat <= 1): cS.attackCooldownStat = 1
    cS.bulletSpeed = (cS.collectiveStats["Bullet Speed"] + sum(cS.collectiveAddStats["Bullet Speed"])) * (multiply_list(cS.collectiveMultStats["Bullet Speed"]))
    cS.bulletRange = (cS.collectiveStats["Bullet Range"] + sum(cS.collectiveAddStats["Bullet Range"])) * (multiply_list(cS.collectiveMultStats["Bullet Range"]))
    cS.bulletSize = (cS.collectiveStats["Bullet Size"] + sum(cS.collectiveAddStats["Bullet Size"])) * (multiply_list(cS.collectiveMultStats["Bullet Size"]))
    cS.bulletDamage = round(_combine_stat("Bullet Damage"))
    cS.bulletPierce = (cS.collectiveStats["Bullet Pierce"] + sum(cS.collectiveAddStats["Bullet Pierce"])) * (multiply_list(cS.collectiveMultStats["Bullet Pierce"]))
    cS.defense = round(_combine_stat("Defense"))
    cS.maxHealthPoints = max(1, round(_combine_stat("Health")))
    cS.healthPoints = min(cS.maxHealthPoints, cS.healthPoints + max(0, cS.maxHealthPoints - previous_max_health))
    cS.vitality = max(0, round(_combine_stat("Vitality")))
    cS.critChance = (cS.collectiveStats["Crit Chance"] + sum(cS.collectiveAddStats["Crit Chance"])) * (multiply_list(cS.collectiveMultStats["Crit Chance"]))
    cS.critDamage = round(_combine_stat("Crit Damage"))
    cS.aura = (cS.collectiveStats["Aura Size"] + sum(cS.collectiveAddStats["Aura Size"])) * (multiply_list(cS.collectiveMultStats["Aura Size"]))
    cS.auraSpeed = (cS.collectiveStats["Aura Strength"] + sum(cS.collectiveAddStats["Aura Strength"])) * (multiply_list(cS.collectiveMultStats["Aura Strength"]))
    cS.xpMult = (cS.collectiveStats["Exp Multiplier"]+ sum(cS.collectiveAddStats["Exp Multiplier"])) * (multiply_list(cS.collectiveMultStats["Exp Multiplier"]))

def handleLevelingProcess():
    
    if (not cS.newRandoUps):
        cS.levelingHandler.randomizeLevelUp()
        cS.newRandoUps = True

    cS.levelingHandler.drawCards()
    
    pDecision = cS.levelingHandler.PlayerClicked()

    if (pDecision != "none"):
        card = cS.levelingHandler.selected_card
        cS.record_upgrade(card.name, card.rarity, card.math_type)
        cS.lastUpgrade = (card.name, card.rarity)
        cS.lastUpgradeAt = cS.runTimeSeconds
        modifier = upgrades.card_modifier(card)
        if card.math_type == "additive":
            cS.collectiveAddStats[card.name].append(modifier)
        else:
            cS.collectiveMultStats[card.name].append(modifier)
        combarinoPlayerStats()
        cS.newRandoUps = False
        cS.pendingLevelUps = max(0, cS.pendingLevelUps - 1)
        if cS.pendingLevelUps > 0:
            vH.state = vH.States.LEVELING
        else:
            cS.gracePeriod = vH.frameRate * 2
            vH.state = vH.States.GAMERUN

def movePlayer():
    cS.update_boss_afflictions(vH.get_timer_step() / max(1, vH.frameRate))
    cS.update_dream_state(vH.get_timer_step() / max(1, vH.frameRate))
    input_x = int(keybinds.held("move_left")) - int(keybinds.held("move_right")) - vH.controllerMoveX
    input_y = int(keybinds.held("move_up")) - int(keybinds.held("move_down")) - vH.controllerMoveY
    direction_scale = 0.70710678 if input_x and input_y else 1.0
    input_x *= direction_scale
    input_y *= direction_scale
    # WASD remains relative to the monitor after a camera turn.  The converted
    # values below are still the historical camera deltas used by this function.
    input_x, input_y = bG.screen_vector_to_world(input_x, input_y)

    if keybinds.pressed("dash") and cS.currDashCooldown <= 0 and (input_x or input_y):
        cS.dashing = True
        cS.currDashCooldown = cS.dashCooldownMax
        cS.fdX = input_x
        cS.fdY = input_y
        cS.playerInvulnerabilityTimer = max(cS.playerInvulnerabilityTimer, cS.dashDuration)
    
    if cS.currDashCooldown > 0:
        cS.currDashCooldown = max(0, cS.currDashCooldown - vH.get_timer_step())
    
    if not cS.dashing:
        movement_scale = vH.get_frame_scale()
        affliction_scale = cS.boss_movement_multiplier()
        cS.dX = input_x * cS.playerSpeed * movement_scale * affliction_scale
        cS.dY = input_y * cS.playerSpeed * movement_scale * affliction_scale
    else:
        movement_scale = vH.get_frame_scale()
        cS.dX = cS.fdX * cS.dashModifier * cS.playerSpeed * movement_scale
        cS.dY = cS.fdY * cS.dashModifier * cS.playerSpeed * movement_scale
        
        if cS.currDashCooldown <= (cS.dashCooldownMax - cS.dashDuration):
            cS.dashing = False

    pull_source = cS.bossAfflictions["pull_source"]
    if pull_source and cS.bossAfflictions["pull_remaining"] > 0 and not cS.dashing:
        player_x = bG.playerPosX + cS.playerSize / 2
        player_y = bG.playerPosY + cS.playerSize / 2
        pull_x, pull_y = pull_source[0] - player_x, pull_source[1] - player_y
        pull_distance = max(1.0, hypot(pull_x, pull_y))
        force = cS.bossAfflictions["pull"] * vH.get_frame_scale()
        cS.dX -= pull_x / pull_distance * force
        cS.dY -= pull_y / pull_distance * force

    newABSPosX = bG.playerPosX - cS.dX
    newABSPosY = bG.playerPosY - cS.dY

    cS.currTileX = bG.playerPosX / vH.tileSizeGlobal
    cS.currTileY = bG.playerPosY / vH.tileSizeGlobal

    boss = cS.activeBoss
    boss_obstacles = (boss.movement_obstacles() if boss is not None
                      and hasattr(boss, "movement_obstacles") else ())

    next_x_rect = pg.Rect(newABSPosX, bG.playerPosY, cS.playerSize, cS.playerSize)
    if (not bG.rect_hits_wall(next_x_rect)
            and not any(next_x_rect.colliderect(obstacle) for obstacle in boss_obstacles)):
        bG.playerPosX = newABSPosX
    else:
        cS.dX = 0

    next_y_rect = pg.Rect(bG.playerPosX, newABSPosY, cS.playerSize, cS.playerSize)
    if (not bG.rect_hits_wall(next_y_rect)
            and not any(next_y_rect.colliderect(obstacle) for obstacle in boss_obstacles)):
        bG.playerPosY = newABSPosY
    else:
        cS.dY = 0

    if boss is not None and hasattr(boss, "constrain_player_position"):
        bG.playerPosX, bG.playerPosY = boss.constrain_player_position(
            bG.playerPosX, bG.playerPosY, cS.playerSize)
    elif boss is not None and hasattr(boss, "arenaRadius"):
        arena_x, arena_y = boss._arena_center()
        player_x = bG.playerPosX + cS.playerSize / 2
        player_y = bG.playerPosY + cS.playerSize / 2
        delta_x, delta_y = player_x - arena_x, player_y - arena_y
        distance = hypot(delta_x, delta_y)
        limit = boss.arenaRadius - cS.playerSize * .7
        if distance > limit:
            bG.playerPosX = arena_x + delta_x / distance * limit - cS.playerSize / 2
            bG.playerPosY = arena_y + delta_y / distance * limit - cS.playerSize / 2

    cS.playerRect.topleft = (bG.lockX, bG.lockY)

def drawPlayer():
    flash_on = cS.playerInvulnerabilityTimer > 0 and int(cS.playerInvulnerabilityTimer / 4) % 2 == 0
    color = pg.Color(235, 245, 255) if flash_on else cS.playerColor
    shadow = cS.playerRect.move(4, 4)
    pg.draw.rect(vH.screen, ui.SHADOW, shadow)
    pg.draw.rect(vH.screen, color, cS.playerRect)
    pg.draw.rect(vH.screen, ui.CREAM if cS.dashing else ui.INK, cS.playerRect, 3)
    inset = cS.playerRect.inflate(-int(cS.playerSize * .42), -int(cS.playerSize * .42))
    pg.draw.rect(vH.screen, ui.lighten(color, 45), inset)

def drawBackground():
    bG.moveAndDisplayBackground(bG.repasteableRoomSurface)
    bG.drawRaisedScenery(bG.currRoomRects)

def handlingBulletCreation():

    controller_firing = hypot(vH.controllerAimX, vH.controllerAimY) > .3
    if (cS.attackCooldownTimer <= 0 and not vH.dragInProgress
            and (cS.autoFire or vH.mouseDown or controller_firing)):
        cS.attackCooldownTimer = cS.attackCooldownStat
        currCrit = False
        currCritChance = floor(cS.critChance)
        chance = randint(1, 100)
        
        if (chance <= 100*(cS.critChance - trunc(cS.critChance))): currCrit = True; currCritChance = floor(cS.critChance) + 1
        currDamage = cS.bulletDamage * (cS.critDamage **(currCritChance))

        currProjectileCount = floor(cS.projectileCount)
        chance = randint(1, 100)
        if (chance <= 100*(cS.projectileCount - trunc(cS.projectileCount))): currProjectileCount = floor(cS.projectileCount) + 1

        currPierce = floor(cS.bulletPierce)
        chance = randint(1, 100)
        if (chance <= 100*(cS.bulletPierce - trunc(cS.bulletPierce))): currPierce = floor(cS.bulletPierce) + 1

        for bNum in range(0,int(currProjectileCount)):
            
            originX = bG.playerPosX + cS.playerSize / 2
            originY = bG.playerPosY + cS.playerSize / 2
            screenOriginX = bG.lockX + cS.playerSize / 2
            screenOriginY = bG.lockY + cS.playerSize / 2
            if hypot(vH.controllerAimX, vH.controllerAimY) > .3:
                targetDX, targetDY = bG.screen_vector_to_world(
                    vH.controllerAimX * vH.sW, vH.controllerAimY * vH.sH,
                )
            else:
                targetDX, targetDY = bG.screen_vector_to_world(
                    vH.mouseX - screenOriginX, vH.mouseY - screenOriginY,
                )
            targetX, targetY = originX + targetDX, originY + targetDY
            direction = _direction_to_target(originX, originY, targetX, targetY)

            if(currProjectileCount != 1):
                dirDelta = -(cS.azimuthalProjectileAngle / 2)
                direction += dirDelta + bNum*(cS.azimuthalProjectileAngle / (currProjectileCount-1))

            cS.bulletHolster.append(Bullet(bG.playerPosX + (cS.playerSize / 2) - (cS.bulletSize / 2),
                                            bG.playerPosY + (cS.playerSize / 2) - (cS.bulletSize / 2),
                                            cS.bulletSpeed,
                                            direction,
                                            cS.bulletRange,
                                            cS.bulletSize,
                                            cS.bulletColor,
                                            currPierce,
                                            currDamage,
                                            currCrit))

    elif(cS.attackCooldownTimer > 0):
        cS.attackCooldownTimer = max(0, cS.attackCooldownTimer - vH.get_timer_step())


def handlingBulletUpdating():
    for bullet in cS.bulletHolster:
        bullet.updateAndDrawBullet(vH.screen)
    cS.bulletHolster[:] = [bullet for bullet in cS.bulletHolster if not bullet.remFlag]

def handlingEnemyCreation():
    natural_beaudis_requested = (
        cS.currentLevel >= MID_BOSS_LEVEL
        and not cS.beaudisEncounterStarted
        and cS.activeBoss is None
    )
    natural_dissonance_requested = (
        cS.currentLevel >= FINAL_BOSS_LEVEL
        and cS.beaudisDefeated
        and not cS.dissonanceEncounterStarted
        and cS.activeBoss is None
    )
    if cS.bossDebugRequested or natural_beaudis_requested or natural_dissonance_requested:
        natural_encounter = not cS.bossDebugRequested
        if natural_beaudis_requested and natural_encounter:
            cS.beaudisEncounterStarted = True
            boss_key = gamePaths.boss_key(midpoint=True)
        else:
            boss_key = gamePaths.boss_key(midpoint=False)
            if natural_dissonance_requested and natural_encounter:
                cS.dissonanceEncounterStarted = True
        cS.enemyHolster.clear()
        cS.enemyProjectileHolster.clear()
        cS.damageTextList.clear()
        cS.experienceList.clear()
        cS.lootCrateList.clear()
        cS.informationSheet.nearby_crate = None
        boss = BOSS_CATALOG.spawn(boss_key)
        if boss_key == "dissonance":
            arena_x, arena_y = boss._arena_center()
            boss.worldX, boss.worldY = arena_x - boss.size / 2, arena_y - boss.size / 2
            boss.posX, boss.posY = bG.world_to_screen(boss.worldX, boss.worldY)
            player_rect = bG.find_nearest_open_rect(
                pg.Rect(arena_x - cS.playerSize / 2,
                        arena_y + vH.tileSizeGlobal * 9.6 - cS.playerSize / 2,
                        cS.playerSize, cS.playerSize), cS.playerSize,
            )
            bG.playerPosX, bG.playerPosY = player_rect.x, player_rect.y
        cS.enemyHolster.append(boss)
        cS.activeBoss = boss
        # Boss practice now respects the normal damage rules unless the player
        # explicitly toggles invincibility with Y.
        cS.bossDebugInvincible = False
        cS.currEnemyCount = 1
        cS.enemySpawningEnabled = False
        cS.bossDebugRequested = False
        cS.gracePeriod = vH.frameRate * 2
        return

    if not cS.enemySpawningEnabled:
        return

    caps = encounter_caps(cS.currentLevel)
    cS.enemyCap = caps["enemy_cap"]
    cS.enemyThreatCap = caps["threat_cap"]
    cS.enemyPopulationThreatCap = caps["population_threat_cap"]

    # Mini-bosses enter the ordinary world once per run. They do not clear the
    # map, reposition the player, disable spawning, or create a boss arena, so a
    # player who never explores toward them can leave them behind.
    for unlock_level, key in MINIBOSS_GATES:
        if (cS.currentLevel >= unlock_level
                and key not in cS.guaranteedMiniBossesSpawned
                and len(cS.enemyHolster) < cS.enemyCap):
            outside_awareness_tiles = ceil((vH.sH * .625) / vH.tileSizeGlobal) + 2
            cS.enemyHolster.append(gamePaths.ENCOUNTERS.spawn(
                cS.currentLevel, key=key,
                min_distance_tiles=outside_awareness_tiles,
            ))
            cS.guaranteedMiniBossesSpawned.add(key)
    cS.currEnemyCount = len(cS.enemyHolster)

    cS.enemySpawnTimer -= vH.get_timer_step()
    cS.encounterSpawnCooldown = max(0, cS.encounterSpawnCooldown - vH.get_timer_step())
    current_threat = sum(getattr(enemy, "threatCost", 1.0) for enemy in cS.enemyHolster)
    pacing = encounter_pacing(cS.currentLevel)
    world_encounters = {enemy.encounter.id for enemy in cS.enemyHolster
                        if getattr(enemy, "encounter", None) is not None}
    if (len(world_encounters) < pacing["max_world_encounters"]
            and cS.currEnemyCount < cS.enemyCap
            and current_threat < cS.enemyPopulationThreatCap
            and cS.enemySpawnTimer <= 0):
        cS.enemySpawnTimer = (vH.frameRate * pacing["spawn_interval_seconds"]
                              * randint(85, 115) / 100)
        remaining_threat = cS.enemyPopulationThreatCap - current_threat
        encounter = None
        if (cS.currentLevel >= 5 and cS.encounterSpawnCooldown <= 0
                and randint(1, 100) <= pacing["curated_chance"] * 100):
            encounter = gamePaths.ENCOUNTERS.spawn_encounter(
                cS.currentLevel, remaining_threat, cS.enemyHolster)
        if encounter is None:
            encounter = gamePaths.ENCOUNTERS.spawn_patrol(
                cS.currentLevel, remaining_threat, cS.enemyHolster)
        if encounter:
            package, group = encounter
            group_threat = sum(getattr(enemy, "threatCost", 1.0) for enemy in group)
            if (len(cS.enemyHolster) + len(group) <= cS.enemyCap
                    and current_threat + group_threat <= cS.enemyPopulationThreatCap):
                cS.enemyHolster.extend(group)
                if not package.key.startswith("patrol_"):
                    cS.encounterSpawnCooldown = vH.frameRate * 18
                cS.currEnemyCount = len(cS.enemyHolster)
                return
        cS.currEnemyCount = len(cS.enemyHolster)

def handlingBossDebugControls():
    boss = cS.activeBoss
    if boss is None:
        return
    phase_keys = (pg.K_1, pg.K_2, pg.K_3, pg.K_4, pg.K_5,
                  pg.K_6, pg.K_7, pg.K_8, pg.K_9)
    for phase, key in enumerate(phase_keys, 1):
        if key in vH.keyPressed:
            boss.debug_set_phase(phase)
            cS.enemyProjectileHolster.clear()
            return
    if pg.K_r in vH.keyPressed:
        boss.debug_set_phase(boss.phase)
        cS.enemyProjectileHolster.clear()
    if pg.K_l in vH.keyPressed:
        boss.debugPhaseLocked = not boss.debugPhaseLocked
    if pg.K_f in vH.keyPressed and not boss.isStaggered:
        boss.stagger = boss.maxStagger - boss.minimumStaggerPerHit
        boss.take_damage(1)
    if pg.K_c in vH.keyPressed and hasattr(boss, "runeCannonCooldown"):
        boss.runeCannonCooldown = 0

def handlingEnemyUpdatesAndDrawing():
    player_center = (bG.playerPosX + cS.playerSize / 2,
                     bG.playerPosY + cS.playerSize / 2)
    pressure_used = 0.0
    encounters = []
    seen_encounters = set()
    ungrouped = []
    for enemy in cS.enemyHolster:
        encounter = getattr(enemy, "encounter", None)
        if encounter is None:
            ungrouped.append(enemy)
        elif encounter.id not in seen_encounters:
            seen_encounters.add(encounter.id)
            encounters.append(encounter)

    encounters.sort(key=lambda item: (
        item.state != "engaged", item.distance_to(*player_center)))
    for encounter in encounters:
        wants_pressure = (encounter.state == "engaged"
                          or encounter.distance_to(*player_center) <= encounter.activationRange)
        allowed = not wants_pressure or pressure_used + encounter.threat_cost <= cS.enemyThreatCap
        encounter.update(*player_center, allowed)
        encounter.draw(vH.screen)
        if encounter.engagementAllowed:
            pressure_used += encounter.threat_cost

    for enemy in sorted(ungrouped, key=lambda item: hypot(
            item.worldX + item.size / 2 - player_center[0],
            item.worldY + item.size / 2 - player_center[1])):
        cost = getattr(enemy, "threatCost", 1.0)
        is_boss = enemy is cS.activeBoss
        enemy.engagementAllowed = is_boss or pressure_used + cost <= cS.enemyThreatCap
        if enemy.engagementAllowed:
            pressure_used += cost

    spawned_groups = []
    for enemy in cS.enemyHolster:
        projectile_start = len(cS.enemyProjectileHolster)
        enemy.updateEnemy(
            bG.playerPosX + cS.playerSize/2,
            bG.playerPosY + cS.playerSize/2,
            cS.enemyProjectileHolster,
        )
        gamePaths.tune_new_projectiles(cS.enemyProjectileHolster, projectile_start)
        enemy.drawEnemy(vH.screen)
        if getattr(enemy, "transitionCleanupRequested", False):
            cleanup_owner = getattr(enemy, "transitionCleanupOwner", None)
            if cleanup_owner:
                cS.enemyProjectileHolster[:] = [
                    projectile for projectile in cS.enemyProjectileHolster
                    if projectile.owner != cleanup_owner
                ]
            else:
                cS.enemyProjectileHolster.clear()
            enemy.transitionCleanupRequested = False
        pending = list(getattr(enemy, "spawnedEnemies", ()))
        if pending:
            spawned_groups.append((enemy, pending, getattr(enemy, "atomicSpawnGroup", False)))
        if hasattr(enemy, "spawnedEnemies"):
            enemy.spawnedEnemies.clear()

    rejected_atomic_owners = set()
    for owner, group, atomic in spawned_groups:
        current_threat = sum(getattr(item, "threatCost", 1.0) for item in cS.enemyHolster)
        group_threat = sum(getattr(item, "threatCost", 1.0) for item in group)
        if atomic and (len(cS.enemyHolster) + len(group) > cS.enemyCap
                       or current_threat + group_threat > cS.enemyPopulationThreatCap):
            rejected_atomic_owners.add(owner)
            continue
        for enemy in group:
            gamePaths.apply_enemy_identity(enemy)
            current_threat = sum(getattr(item, "threatCost", 1.0) for item in cS.enemyHolster)
            if len(cS.enemyHolster) >= cS.enemyCap:
                break
            if current_threat + getattr(enemy, "threatCost", 1.0) > cS.enemyPopulationThreatCap:
                break
            if owner.encounter is not None and enemy.encounter is None:
                enemy.encounter = owner.encounter
                enemy.encounterSlot = len(owner.encounter.members)
                enemy.combatSide = -1 if enemy.encounterSlot % 2 else 1
                owner.encounter.members.append(enemy)
            cS.enemyHolster.append(enemy)
    if rejected_atomic_owners:
        cS.enemyHolster[:] = [enemy for enemy in cS.enemyHolster
                              if enemy not in rejected_atomic_owners]
    cS.currEnemyCount = len(cS.enemyHolster)


def handlingEnemyProjectileUpdating():
    boss = cS.activeBoss
    spawned_projectiles = []
    for projectile in cS.enemyProjectileHolster:
        if boss is not None and hasattr(boss, "arenaRadius"):
            center_x, center_y = boss._arena_center()
            projectile_x = projectile.worldX + projectile.size / 2
            projectile_y = projectile.worldY + projectile.size / 2
            if hypot(projectile_x - center_x, projectile_y - center_y) > boss.arenaRadius * 1.04:
                projectile.remFlag = True
            if getattr(boss, "dying", False):
                projectile.remFlag = True
        projectile.updateAndDraw(vH.screen)
        spawned_projectiles.extend(getattr(projectile, "spawnedProjectiles", ()))
        if hasattr(projectile, "spawnedProjectiles"):
            projectile.spawnedProjectiles.clear()
    cS.enemyProjectileHolster[:] = [
        projectile for projectile in cS.enemyProjectileHolster if not projectile.remFlag
    ]
    gamePaths.tune_new_projectiles(spawned_projectiles, 0)
    cS.enemyProjectileHolster.extend(spawned_projectiles)
    if boss is not None and len(cS.enemyProjectileHolster) > 150:
        overflow = len(cS.enemyProjectileHolster) - 150
        for projectile in cS.enemyProjectileHolster[:overflow]:
            projectile.remFlag = True
        
def handlingDamagingEnemies():
    enemy_grid = SpatialHash(max(64, int(vH.tileSizeGlobal * 2)))
    for enemy in cS.enemyHolster:
        for _, hitbox in enemy.get_screen_hitboxes():
            enemy_grid.insert(enemy, hitbox)

    dead_enemies = set()
    for bullet in cS.bulletHolster:
        bullet_rect = pg.Rect(bullet.posX, bullet.posY, bullet.size, bullet.size)
        candidates = list(enemy_grid.query(bullet_rect))
        candidates.sort(key=lambda enemy: 0 if any(
            part_id == "shield" and bullet_rect.colliderect(hitbox)
            for part_id, hitbox in enemy.get_screen_hitboxes()) else 1)
        for eman in candidates:
            if eman in dead_enemies:
                continue
            collided_part = next(
                ((part_id, hitbox) for part_id, hitbox in eman.get_screen_hitboxes() if bullet_rect.colliderect(hitbox)),
                None,
            )
            if collided_part:
                if bullet not in eman.cantTouchMeList:
                    part_id, hitbox = collided_part
                    if (str(part_id).startswith("portal:") and hasattr(eman, "route_player_bullet")
                            and eman.route_player_bullet(bullet, int(str(part_id).split(":")[1]))):
                        continue
                    eman.cantTouchMeList.append(bullet)
                    bullet.bPierce -= 1
                    if bullet.bPierce <= 0:
                        bullet.remFlag = True
                    result = eman.take_damage(bullet.damage, part_id)
                    if getattr(eman, "transitionCleanupRequested", False):
                        cleanup_owner = getattr(eman, "transitionCleanupOwner", None)
                        if cleanup_owner:
                            cS.enemyProjectileHolster[:] = [
                                projectile for projectile in cS.enemyProjectileHolster
                                if projectile.owner != cleanup_owner
                            ]
                        else:
                            cS.enemyProjectileHolster.clear()
                        eman.transitionCleanupRequested = False
                    currColor = ui.PURPLE if bullet.currCrit else ui.GOLD
                    portal_hit = str(part_id).startswith("portal:")
                    portal = (eman.projectilePortals[int(str(part_id).split(":")[1])]
                              if portal_hit else None)
                    display_value = ("SURVIVE" if getattr(eman, "survivalActive", False)
                                      and not str(part_id).startswith("portal:")
                                     else "DISABLED" if portal_hit and portal.phaseDisabled
                                     else bullet.damage) if result.applied else (
                        "SURVIVE" if getattr(eman, "survivalActive", False)
                        else "BLOCK" if portal_hit
                        else "STAGGER" if hasattr(eman, "stagger") else "BLOCK"
                    )
                    text_world_x, text_world_y = bG.screen_to_world(hitbox.x, hitbox.y)
                    cS.damageTextList.append(DamageText(
                        text_world_x, text_world_y, currColor, display_value,
                        hitbox.width, vH.frameRate,
                    ))
                    if result.killed:
                        dead_enemies.add(eman)

    for enemy in cS.enemyHolster:
        if enemy.is_dead():
            dead_enemies.add(enemy)

    for enemy in dead_enemies:
        cS.numOfEnemiesKilled += 1
        cS.experienceList.append(ExperienceBubble(
            enemy.worldX, enemy.worldY,
            cS.xpMult * (enemy.expValue * (cS.currentStage * cS.experienceStageMod)),
            enemy.difficulty, vH.frameRate, celebration=enemy is cS.activeBoss,
        ))
        volatile_count = getattr(enemy, "volatileBurst", 0)
        if volatile_count:
            center_x = enemy.worldX + enemy.size / 2
            center_y = enemy.worldY + enemy.size / 2
            for index in range(volatile_count):
                cS.enemyProjectileHolster.append(EnemyProjectile(
                    center_x, center_y, index * 2 * pi / volatile_count,
                    .72, enemy.damage * .22, enemy.size * .18,
                    travel_range=vH.tileSizeGlobal * 4.5,
                    color=ui.RED, shape="diamond", owner="volatile_enemy",
                ))
        drop_count = items.roll_drop_count()
        if drop_count:
            cS.lootCrateList.append(LootCrate(
                enemy.worldX, enemy.worldY, items.generate_drops(drop_count),
            ))
            if len(cS.lootCrateList) > MAX_LOOT_CRATES:
                evictable = next((crate for crate in cS.lootCrateList
                                  if crate is not cS.informationSheet.nearby_crate), None)
                if evictable:
                    cS.lootCrateList.remove(evictable)
        if enemy is cS.activeBoss:
            content_key = getattr(enemy, "contentKey", "")
            if content_key == gamePaths.active().mid_boss:
                cS.beaudisDefeated = True
            elif content_key == gamePaths.active().final_boss:
                cS.gameCompleted = True
                cS.runOutcome = "RUN COMPLETE"
                gameProfile.record_run(cS.currentLevel, cS.numOfEnemiesKilled, completed=True)
            cS.activeBoss = None
            cS.enemySpawningEnabled = not cS.gameCompleted
            vH.screenShakeX = 0
            vH.screenShakeY = 0
            # Boss encounters begin from a cleared arena, so no ordinary hostile
            # projectile needs to survive the boss's defeat.
            cS.enemyProjectileHolster.clear()
    if dead_enemies:
        cS.enemyHolster[:] = [enemy for enemy in cS.enemyHolster if enemy not in dead_enemies]
        cS.currEnemyCount = len(cS.enemyHolster)
    cS.bulletHolster[:] = [bullet for bullet in cS.bulletHolster if not bullet.remFlag]

def updateDamageTexts():
    if not gameProfile.profile["damage_numbers"]:
        cS.damageTextList.clear()
        return
    for dText in cS.damageTextList[:]:
        dText.drawAndUpdateDamageText(cS.dX, cS.dY)
        if dText.deleteMe:
            cS.damageTextList.remove(dText)
                
def updateExperience():
    for bubble in cS.experienceList[:]:
        bubble.updateBubble(cS.auraSpeed, cS.dX, cS.dY)
            
def expForPlayer():
    player_rect = pg.Rect(bG.playerPosX, bG.playerPosY,
                          cS.playerSize, cS.playerSize)

    for bubble in cS.experienceList[:]:
        if hasattr(bubble, "_world_rect"):
            bubble_rect = bubble._world_rect()
        else:
            # Keep simple test/debug bubble doubles compatible with the older
            # screen-space contract.
            bubble_x, bubble_y = bG.screen_to_world(bubble.posX, bubble.posY)
            bubble_rect = pg.Rect(bubble_x, bubble_y, bubble.size, bubble.size)

        if player_rect.colliderect(bubble_rect):
            cS.expCount += bubble.value

            while (cS.currentLevel < MAX_LEVEL
                   and cS.expCount >= cS.expNeededForNextLevel):
                cS.currentLevel += 1
                cS.pendingLevelUps += 1
                cS.expCount -= cS.expNeededForNextLevel
                cS.informationSheet.updateCurrLevel()
                cS.expNeededForNextLevel *= cS.levelScaleIncreaseFunction
                cS.healthPoints = cS.maxHealthPoints
                cS.enemyOneInFramesChance /= cS.levelMod
                vH.state = vH.States.LEVELING
            if cS.currentLevel >= MAX_LEVEL:
                cS.expCount = min(cS.expCount, cS.expNeededForNextLevel)
                
            cS.experienceList.remove(bubble)
            continue

        aura_rect = player_rect.inflate(2 * (cS.aura + bubble.size), 2 * (cS.aura + bubble.size))
        if aura_rect.colliderect(bubble_rect):
            bubble.naturalSpawn = False
            originX = bG.playerPosX + cS.playerSize / 2
            originY = bG.playerPosY + cS.playerSize / 2
            deltaX = getattr(bubble, "worldX", bubble_rect.x) - originX
            deltaY = getattr(bubble, "worldY", bubble_rect.y) - originY

            if deltaX == 0:
                bubble.direction = pi/2 if deltaY > 0 else -pi/2
            else:
                bubble.direction = atan(deltaY / deltaX) if deltaX > 0 else -atan(deltaY / abs(deltaX)) + pi
        else:
            bubble.naturalSpawn = True

def debugForceLevelUp():
    """Dev/testing hotkey: spawn one level's worth of experience on the player.

    Reuses the real pickup path in expForPlayer() rather than duplicating the
    level-up transition, so this behaves exactly like a natural level up.
    """
    cS.experienceList.append(ExperienceBubble(
        bG.playerPosX, bG.playerPosY, cS.expNeededForNextLevel, 1, vH.frameRate,
    ))

def updateLootCrates():
    old_clip = vH.screen.get_clip()
    vH.screen.set_clip(bG.gameplay_viewport_rect())
    for crate in cS.lootCrateList:
        crate.draw()
    vH.screen.set_clip(old_clip)

def crateInteractionForPlayer():
    dragging_source = cS.informationSheet.dragging_source
    if dragging_source is not None and dragging_source[0] == "crate":
        return
    player_rect = pg.Rect(bG.playerPosX, bG.playerPosY, cS.playerSize, cS.playerSize)
    nearest, nearest_distance = None, None
    for crate in cS.lootCrateList:
        if not crate.items:
            continue
        aura_rect = player_rect.inflate(2 * (CRATE_INTERACT_RADIUS + crate.size),
                                        2 * (CRATE_INTERACT_RADIUS + crate.size))
        if aura_rect.colliderect(crate._world_rect()):
            distance = hypot(crate.worldX - bG.playerPosX, crate.worldY - bG.playerPosY)
            if nearest_distance is None or distance < nearest_distance:
                nearest, nearest_distance = crate, distance
    cS.informationSheet.nearby_crate = nearest

HOSTILE_MIN_DAMAGE = 25
HOSTILE_DAMAGE_FLOOR_RATIO = .1


def hostile_damage_after_defense(raw_damage, defense):
    """Apply defense while preserving a small chip-damage floor for hostile hits."""
    raw_damage = max(0.0, float(raw_damage))
    if raw_damage <= 0:
        return 0
    return round(max(raw_damage - defense,
               min(raw_damage, max(HOSTILE_MIN_DAMAGE,
                                   raw_damage * HOSTILE_DAMAGE_FLOOR_RATIO))))


def recoverPlayerHealth():
    """Apply vitality continuously while keeping stored and displayed HP integral."""
    boss = cS.activeBoss
    if boss is not None and getattr(boss, "vitalitySuppressed", False):
        cS.healthRecoveryBuffer = 0.0
        return
    if cS.healthPoints >= cS.maxHealthPoints or cS.vitality <= 0:
        cS.healthRecoveryBuffer = 0.0
        return
    cS.healthRecoveryBuffer += cS.vitality * vH.get_timer_step() / max(1, vH.frameRate)
    recovered = int(cS.healthRecoveryBuffer)
    if recovered:
        cS.healthPoints = min(cS.maxHealthPoints, cS.healthPoints + recovered)
        cS.healthRecoveryBuffer -= recovered


def hurtPlayer():
    timer_step = vH.get_timer_step()
    cS.playerInvulnerabilityTimer = max(0, cS.playerInvulnerabilityTimer - timer_step)
    cS.gracePeriod = max(0, cS.gracePeriod - timer_step)
    if cS.bossDebugInvincible:
        cS.healthPoints = cS.maxHealthPoints
        return
    if cS.playerInvulnerabilityTimer > 0 or cS.gracePeriod > 0:
        return

    player_rect = pg.Rect(bG.lockX, bG.lockY, cS.playerSize, cS.playerSize)
    player_world_rect = pg.Rect(bG.playerPosX, bG.playerPosY, cS.playerSize, cS.playerSize)

    for projectile in cS.enemyProjectileHolster:
        if projectile.collides(player_world_rect):
            belief_gain = getattr(projectile, "beliefGain", 0.0)
            clarity_gain = getattr(projectile, "clarityGain", 0.0)
            if belief_gain or clarity_gain:
                cS.alter_belief(belief_gain - clarity_gain,
                                false_rule=belief_gain >= 1.0,
                                truth=clarity_gain > 0)
            affliction = getattr(projectile, "affliction", None)
            if affliction:
                cS.apply_boss_affliction(
                    affliction, getattr(projectile, "afflictionDuration", 0.0),
                    getattr(projectile, "afflictionStrength", 0.0),
                    getattr(projectile, "exposure", 0.0),
                    getattr(projectile, "afflictionSource", None),
                )
            if not getattr(projectile, "persistentHazard", False):
                projectile.remFlag = True
            trueDMG = hostile_damage_after_defense(projectile.damage, cS.defense)
            if gameProfile.profile["casual_mode"]:
                trueDMG = round(trueDMG * .8)
            cS.damageTextList.append(DamageText(
                bG.playerPosX, bG.playerPosY, ui.RED, trueDMG,
                vH.tileSizeGlobal, vH.frameRate,
            ))
            cS.healthPoints = max(0, cS.healthPoints - trueDMG)
            cS.playerInvulnerabilityTimer = cS.playerInvulnerabilityMax
            if cS.healthPoints <= 0:
                cS.runOutcome = "DEFEATED"
                gameProfile.record_run(cS.currentLevel, cS.numOfEnemiesKilled)
                vH.state = vH.States.RESULTS
                cS.highestLevel = max(cS.highestLevel, cS.currentLevel)
            return

    for eman in cS.enemyHolster:
        collided_hitbox = next(
            (hitbox for _, hitbox in eman.get_screen_hitboxes() if player_rect.colliderect(hitbox)),
            None,
        )
        if collided_hitbox:
            trueDMG = hostile_damage_after_defense(eman.damage, cS.defense)
            if gameProfile.profile["casual_mode"]:
                trueDMG = round(trueDMG * .8)
            cS.damageTextList.append(DamageText(
                bG.playerPosX, bG.playerPosY, ui.RED, trueDMG,
                vH.tileSizeGlobal, vH.frameRate,
            ))
            cS.healthPoints = max(0, cS.healthPoints - trueDMG)
            cS.playerInvulnerabilityTimer = cS.playerInvulnerabilityMax

            # Separate the bodies immediately so the next readable threat comes from
            # a new approach, not an enemy hidden inside the player rectangle.
            delta_x = collided_hitbox.centerx - player_rect.centerx
            delta_y = collided_hitbox.centery - player_rect.centery
            distance = max(1, hypot(delta_x, delta_y))
            knockback_x, knockback_y = bG.screen_vector_to_world(
                delta_x / distance * vH.tileSizeGlobal * 0.8,
                delta_y / distance * vH.tileSizeGlobal * 0.8,
            )
            eman.apply_knockback(
                knockback_x, knockback_y,
            )
            if cS.healthPoints <= 0:
                cS.runOutcome = "DEFEATED"
                gameProfile.record_run(cS.currentLevel, cS.numOfEnemiesKilled)
                vH.state = vH.States.RESULTS
                cS.highestLevel = max(cS.highestLevel, cS.currentLevel)
            break


def selectBountyTarget():
    """Return the highest-value live target or patrol as a world-space bounty."""
    if cS.activeBoss is not None and not cS.activeBoss.is_dead():
        boss = cS.activeBoss
        return {
            "world": (boss.worldX + boss.size / 2, boss.worldY + boss.size / 2),
            "score": float("inf"),
            "label": getattr(boss, "bossName", "BOSS"),
            "target": boss,
        }

    candidates = []
    seen_encounters = set()
    for enemy in cS.enemyHolster:
        if enemy.is_dead():
            continue
        encounter = getattr(enemy, "encounter", None)
        if encounter is not None:
            if encounter.id in seen_encounters:
                continue
            seen_encounters.add(encounter.id)
            living = [member for member in encounter.members if not member.is_dead()]
            if not living:
                continue
            center_x = sum(member.worldX + member.size / 2 for member in living) / len(living)
            center_y = sum(member.worldY + member.size / 2 for member in living) / len(living)
            reward = sum(member.expValue + getattr(member, "storedExperience", 0)
                         for member in living)
            threat = sum(getattr(member, "threatCost", 1.0) for member in living)
            candidates.append({
                "world": (center_x, center_y),
                "score": reward + threat * 4,
                "label": encounter.key.replace("_", " ").upper(),
                "target": encounter,
            })
        else:
            reward = enemy.expValue + getattr(enemy, "storedExperience", 0)
            elite_bonus = 500 if getattr(enemy, "combatRole", "") == "elite" else 0
            candidates.append({
                "world": (enemy.worldX + enemy.size / 2,
                          enemy.worldY + enemy.size / 2),
                "score": reward + getattr(enemy, "threatCost", 1.0) * 4 + elite_bonus,
                "label": getattr(enemy, "bossName", enemy.family).replace("_", " ").upper(),
                "target": enemy,
            })
    return max(candidates, key=lambda item: item["score"], default=None)


def _bounty_arrow_geometry(target_screen, viewport):
    """Return a short, fat arrow polygon on the viewport edge."""
    origin_x, origin_y = bG.lockX, bG.lockY
    delta_x = target_screen[0] - origin_x
    delta_y = target_screen[1] - origin_y
    distance = hypot(delta_x, delta_y)
    if distance < 1:
        return None
    direction_x, direction_y = delta_x / distance, delta_y / distance
    intersections = []
    if direction_x > 0:
        intersections.append((viewport.right - origin_x) / direction_x)
    elif direction_x < 0:
        intersections.append((viewport.left - origin_x) / direction_x)
    if direction_y > 0:
        intersections.append((viewport.bottom - origin_y) / direction_y)
    elif direction_y < 0:
        intersections.append((viewport.top - origin_y) / direction_y)
    positive = [value for value in intersections if value > 0]
    if not positive:
        return None
    edge_distance = min(positive)
    tip_x = origin_x + direction_x * edge_distance
    tip_y = origin_y + direction_y * edge_distance
    perpendicular_x, perpendicular_y = -direction_y, direction_x
    length, head_length = 38, 17
    shaft_half, head_half = 6, 13
    tail_x, tail_y = tip_x - direction_x * length, tip_y - direction_y * length
    neck_x, neck_y = tip_x - direction_x * head_length, tip_y - direction_y * head_length
    points = (
        (tail_x + perpendicular_x * shaft_half, tail_y + perpendicular_y * shaft_half),
        (neck_x + perpendicular_x * shaft_half, neck_y + perpendicular_y * shaft_half),
        (neck_x + perpendicular_x * head_half, neck_y + perpendicular_y * head_half),
        (tip_x, tip_y),
        (neck_x - perpendicular_x * head_half, neck_y - perpendicular_y * head_half),
        (neck_x - perpendicular_x * shaft_half, neck_y - perpendicular_y * shaft_half),
        (tail_x - perpendicular_x * shaft_half, tail_y - perpendicular_y * shaft_half),
    )
    return points, (tip_x, tip_y), (direction_x, direction_y)


def drawBountyIndicator():
    bounty = cS.currentBounty or selectBountyTarget()
    if bounty is None:
        return
    target_screen = bG.world_to_screen(*bounty["world"])
    arena_width = cS.informationSheet.arena_width
    # The marker is navigation for off-screen targets only.  Once the target's
    # center enters the playable view, the enemy itself is the clearer cue.
    if pg.Rect(0, 0, arena_width, vH.screen.get_height()).collidepoint(target_screen):
        return
    top_margin = 112 if cS.activeBoss is not None else 44
    viewport = pg.Rect(34, top_margin, max(1, arena_width - 68),
                       max(1, int(vH.sH) - top_margin - 42))
    geometry = _bounty_arrow_geometry(target_screen, viewport)
    if geometry is None:
        return
    points, tip, direction = geometry
    shadow = [(x + 4, y + 5) for x, y in points]
    pg.draw.polygon(vH.screen, ui.SHADOW, shadow)
    pg.draw.polygon(vH.screen, ui.RED, points)
    pg.draw.polygon(vH.screen, ui.INK, points, 4)
    # A compact inward label gives the marker meaning without covering the biome.
    label_position = (tip[0] - direction[0] * 52,
                      tip[1] - direction[1] * 52)
    ui.draw_text(vH.screen, "BOUNTY", 9 * ui.display_scale(vH.screen), ui.RED,
                 label_position, "center")
    
def drawInformationSheet():
    cS.currentBounty = selectBountyTarget()
    if vH.mouseX < cS.informationSheet.arena_width and not vH.dragInProgress:
        center = (int(vH.mouseX), int(vH.mouseY))
        color = ui.CREAM if (cS.autoFire or vH.mouseDown) else ui.TEXT
        pg.draw.rect(vH.screen, ui.INK, (center[0] - 3, center[1] - 3, 6, 6))
        pg.draw.rect(vH.screen, color, (center[0] - 3, center[1] - 3, 6, 6), 1)
        gap, length = 7, 8
        pg.draw.line(vH.screen, color, (center[0] - gap - length, center[1]), (center[0] - gap, center[1]), 2)
        pg.draw.line(vH.screen, color, (center[0] + gap, center[1]), (center[0] + gap + length, center[1]), 2)
        pg.draw.line(vH.screen, color, (center[0], center[1] - gap - length), (center[0], center[1] - gap), 2)
        pg.draw.line(vH.screen, color, (center[0], center[1] + gap), (center[0], center[1] + gap + length), 2)
        if gameProfile.profile["aim_guide"]:
            origin = (int(bG.lockX + cS.playerSize / 2), int(bG.lockY + cS.playerSize / 2))
            dx, dy = center[0] - origin[0], center[1] - origin[1]
            distance = max(1, hypot(dx, dy))
            guide_length = min(distance, vH.tileSizeGlobal * 3)
            end = (origin[0] + dx / distance * guide_length,
                   origin[1] + dy / distance * guide_length)
            pg.draw.line(vH.screen, ui.CREAM, origin, end, 1)
    drawBossHealthBar()
    drawRunCompleteBanner()
    drawLowHealthWarning()
    cS.informationSheet.drawSheet()
    drawTutorialHint()


def drawLowHealthWarning():
    ratio = cS.healthPoints / max(1, cS.maxHealthPoints)
    if ratio > .3:
        return
    arena_width = cS.informationSheet.arena_width
    alpha = max(0, min(255, int(35 + (1 - ratio / .3) * 65)))
    overlay = pg.Surface((arena_width, int(vH.sH)), pg.SRCALPHA)
    border = max(8, int(22 * ui.display_scale(vH.screen)))
    pg.draw.rect(overlay, (*ui.RED[:3], alpha), overlay.get_rect(), border)
    vH.screen.blit(overlay, (0, 0))


def drawRunCompleteBanner():
    if not cS.gameCompleted:
        return
    scale = ui.display_scale(vH.screen)
    arena_width = cS.informationSheet.arena_width
    width = min(arena_width * .58, 680 * scale)
    rect = pg.Rect((arena_width - width) / 2, 22 * scale, width, 76 * scale)
    ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, ui.CREAM, shadow=7)
    ending = f"{gamePaths.active().final_boss.upper()} ENDED"
    ui.draw_text(vH.screen, ending, 24 * scale, ui.CREAM,
                 (rect.centerx, rect.y + 10 * scale), "midtop")
    ui.draw_text(vH.screen, "LEVEL 20 // RUN COMPLETE", 11 * scale, ui.PURPLE,
                 (rect.centerx, rect.bottom - 12 * scale), "midbottom")
    ui.draw_text(vH.screen, "ENTER  VIEW RESULTS", 9 * scale, ui.TEXT,
                 (rect.centerx, rect.bottom + 12 * scale), "midtop")
    if pg.K_RETURN in vH.keyPressed:
        vH.state = vH.States.RESULTS


def drawTutorialHint():
    if not gameProfile.profile["tutorial_hints"] or cS.runTimeSeconds > 42:
        return
    hints = (
        (0, 8, "WASD MOVE  //  MOUSE AIM  //  PRESS I FOR AUTOFIRE"),
        (8, 16, "SPACE DASHES IN YOUR MOVEMENT DIRECTION AND BRIEFLY AVOIDS DAMAGE"),
        (16, 25, "FOLLOW THE RED BOUNTY ARROW TO HIGH-VALUE PATROLS"),
        (25, 34, "Q / E ROTATE THE ARENA  //  MOVEMENT STAYS SCREEN-RELATIVE"),
        (34, 42, "TAB OPENS DETAILS  //  ESC PAUSES AND OPENS COMFORT SETTINGS"),
    )
    text = next((text for start, end, text in hints if start <= cS.runTimeSeconds < end), None)
    if not text:
        return
    scale = ui.display_scale(vH.screen)
    width = min(cS.informationSheet.arena_width * .72, 760 * scale)
    rect = pg.Rect((cS.informationSheet.arena_width - width) / 2,
                   vH.sH - 58 * scale, width, 38 * scale)
    ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, ui.BLUE, shadow=4)
    ui.draw_text(vH.screen, text, 9 * scale, ui.TEXT, rect.center, "center")


def drawBossHealthBar():
    boss = cS.activeBoss
    if boss is None or boss.hp <= 0:
        return
    if getattr(boss, "entranceRemaining", 0) > 1.0:
        return
    exposure = cS.bossAfflictions["exposure"]
    if exposure > .05:
        intensity = min(1.0, exposure / 10.0)
        veil = pg.Surface(vH.screen.get_size(), pg.SRCALPHA)
        edge = max(18, int(min(vH.screen.get_size()) * .055))
        alpha = int(18 + intensity * 62)
        color = (126, 48, 32, alpha)
        pg.draw.rect(veil, color, (0, 0, vH.screen.get_width(), edge))
        pg.draw.rect(veil, color, (0, vH.screen.get_height() - edge,
                                  vH.screen.get_width(), edge))
        pg.draw.rect(veil, color, (0, edge, edge, vH.screen.get_height() - edge * 2))
        pg.draw.rect(veil, color, (vH.screen.get_width() - edge, edge, edge,
                                  vH.screen.get_height() - edge * 2))
        vH.screen.blit(veil, (0, 0))
    scale = ui.display_scale(vH.screen)
    arena_width = cS.informationSheet.arena_width
    width = min(arena_width * .62, 720 * scale)
    height = 88 * scale
    rect = pg.Rect((arena_width - width) / 2, 16 * scale, width, height)
    accent = boss.phaseAccent
    ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, accent, shadow=6)
    ui.draw_text(vH.screen, boss.bossName, 20 * scale, ui.TEXT, (rect.x + 14 * scale, rect.y + 8 * scale))
    phase_text = (f"SURVIVE // {boss.survivalRemaining:04.1f}s"
                  if getattr(boss, "survivalActive", False)
                  else f"PHASE {boss.phase} // {boss.phaseLabel}")
    ui.draw_text(vH.screen, phase_text, 10 * scale, accent,
                 (rect.right - 14 * scale, rect.y + 13 * scale), "topright")
    hp_rect = pg.Rect(rect.x + 14 * scale, rect.y + 39 * scale, rect.width - 28 * scale, 12 * scale)
    ui.draw_progress(vH.screen, hp_rect, max(0.0, min(1.0, boss.hp / boss.maxHp)), accent, 18)
    stagger_color = ui.CREAM if boss.isStaggered else ui.GOLD
    stagger_rect = pg.Rect(rect.x + 14 * scale, rect.y + 64 * scale, rect.width - 28 * scale, 10 * scale)
    stagger_ratio = (boss.staggerRemaining / boss.staggerDuration
                     if boss.isStaggered else boss.stagger / boss.maxStagger)
    ui.draw_progress(vH.screen, stagger_rect, max(0.0, min(1.0, stagger_ratio)), stagger_color, 12)
    stagger_label = (f"PERFECT BREAK // {boss.staggerRemaining:.1f}s"
                     if boss.isStaggered and boss.perfectStagger
                     else f"STAGGERED // {boss.staggerRemaining:.1f}s" if boss.isStaggered
                     else f"RECOVERING // {boss.staggerRecoveryRemaining:.1f}s"
                     if boss.staggerRecoveryRemaining > 0
                     else f"RUNE BROKEN // {boss.runeSilenceRemaining:.1f}s"
                     if boss.runeSilenceRemaining > 0
                     else f"STAGGER // TIER {min(3, int(boss.stagger / boss.maxStagger * 4))}")
    ui.draw_text(vH.screen, stagger_label, 9 * scale, stagger_color,
                 (stagger_rect.x, stagger_rect.y - 2 * scale), "bottomleft")
    if exposure > .05:
        affliction = "PULLED" if cS.bossAfflictions["pull_remaining"] > 0 else (
            "SLOWED" if cS.bossAfflictions["slow_remaining"] > 0 else "EXPOSED")
        ui.draw_text(vH.screen, f"{affliction} // {exposure:.1f}", 9 * scale, ui.RED,
                     (rect.right - 14 * scale, rect.bottom + 5 * scale), "topright")
    belief = cS.dreamState["belief"]
    if belief > .05:
        label = "CLARITY" if cS.dreamState["clarity"] > belief * .35 else "BELIEF"
        color = ui.BLUE if label == "CLARITY" else ui.PURPLE
        ui.draw_text(vH.screen, f"{label} // {belief:.1f}", 9 * scale, color,
                     (rect.x + 14 * scale, rect.bottom + 5 * scale), "topleft")

def runTheTitleScreen():
    vH.screen.fill(ui.VOID)
    grid = max(28, int(min(vH.sW, vH.sH) / 28))
    for x in range(0, int(vH.sW), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (x, 0), (x, vH.sH))
    for y in range(0, int(vH.sH), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (0, y), (vH.sW, y))

    scale = min(vH.sW, vH.sH)
    ui_scale = ui.display_scale(vH.screen)
    content_width = min(int(vH.sW * .68), int(980 * ui_scale))
    left = int((vH.sW - content_width) / 2)
    ui.draw_text(vH.screen, "ROTBOI", scale * .095, ui.TEXT, (vH.sW / 2, vH.sH * .12), "midtop")
    ui.draw_text(vH.screen, "R E M A S T E R E D", scale * .026, ui.CREAM, (vH.sW / 2, vH.sH * .245), "midtop")
    ui.draw_text(vH.screen, "CHOOSE WHAT THE ROT REMEMBERS.", scale * .019, ui.MUTED, (vH.sW / 2, vH.sH * .305), "midtop")

    selector_y = vH.sH * .365
    gap = 8 * ui_scale
    path_list = list(gamePaths.PATHS.values())
    selector_width = (content_width - gap * (len(path_list) - 1)) / len(path_list)
    path_hovers = {}
    for index, path in enumerate(path_list):
        rect = pg.Rect(left + index * (selector_width + gap), selector_y,
                       selector_width, max(50 * ui_scale, scale * .058))
        is_selected = path.key == gamePaths.selected_key
        path_hovers[path.key] = ui.draw_button(
            vH.screen, rect, path.title, (vH.mouseX, vH.mouseY), vH.mouseDown,
            True, path.accent if is_selected else ui.BORDER,
            None, int(scale * .014),
        )

    if pg.K_LEFT in vH.keyPressed or pg.K_a in vH.keyPressed:
        gamePaths.cycle(-1)
    elif pg.K_RIGHT in vH.keyPressed or pg.K_d in vH.keyPressed:
        gamePaths.cycle(1)
    elif vH.mousePressed:
        for key, hovered_path in path_hovers.items():
            if hovered_path:
                gamePaths.select(key)
                break

    path = gamePaths.selected()
    ui.draw_text(vH.screen, f"{path.subtitle}  //  {path.description}", scale * .013,
                 path.accent, (vH.sW / 2, vH.sH * .455), "midtop")
    play_rect = pg.Rect(left + content_width * .27, vH.sH * .495, content_width * .46,
                        max(54 * ui_scale, scale * .064))
    hovered = ui.draw_button(vH.screen, play_rect, f"ENTER {path.title}",
                             (vH.mouseX, vH.mouseY), vH.mouseDown, True,
                             path.accent, "SPACE", int(scale * .019))

    controls_rect = pg.Rect(left, vH.sH * .615, content_width, max(116 * ui_scale, vH.sH * .15))
    ui.draw_panel(vH.screen, controls_rect, ui.PANEL, ui.BORDER, shadow=6)
    ui.draw_text(vH.screen, "FIELD MANUAL", scale * .018, ui.TEXT, (controls_rect.x + 18 * ui_scale, controls_rect.y + 14 * ui_scale))
    controls = (("WASD", "MOVE"), ("MOUSE", "AIM + FIRE"),
                ("SPACE", "DASH"), ("Q / E", "ROTATE"), ("I", "AUTOFIRE"))
    cell_width = (controls_rect.width - 36 * ui_scale) / len(controls)
    for index, (key, action) in enumerate(controls):
        center_x = controls_rect.x + 18 * ui_scale + cell_width * (index + .5)
        key_rect = pg.Rect(0, 0, min(82 * ui_scale, cell_width - 12 * ui_scale), 34 * ui_scale)
        key_rect.center = (center_x, controls_rect.centery - 3)
        pg.draw.rect(vH.screen, ui.INK, key_rect)
        pg.draw.rect(vH.screen, ui.BLUE, key_rect, 2)
        ui.draw_text(vH.screen, key, scale * .014, ui.BLUE, key_rect.center, "center")
        ui.draw_text(vH.screen, action, scale * .011, ui.MUTED, (center_x, key_rect.bottom + 11 * ui_scale), "midtop")

    best_level = max(cS.highestLevel, int(gameProfile.profile["best_level"]))
    record_label = "NO RUNS LOGGED" if best_level <= 0 else f"BEST RUN  //  LEVEL {best_level:02}  //  {int(gameProfile.profile['best_kills'])} KILLS"
    ui.draw_tag(vH.screen, record_label, (left, int(vH.sH * .81)), ui.GOLD if best_level else ui.BORDER, int(scale * .012))
    ui.draw_text(vH.screen, "A / D  SELECT PATH    ESC  QUIT", scale * .012, ui.MUTED, (left + content_width, vH.sH * .815), "topright")

    if pg.K_SPACE in vH.keyPressed or (hovered and vH.mousePressed):
        gamePaths.activate_selected()
        resetAllStats()
        vH.state = vH.States.GAMERUN
    elif pg.K_ESCAPE in vH.keyPressed:
        vH.done = True
