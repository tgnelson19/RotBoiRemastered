import variableHolster as vH
import background as bG
import characterStats as cS
import pygame as pg
from bullet import Bullet
from enemyTypes import ENEMY_CATALOG
from bossTypes import BOSS_CATALOG
from damageText import DamageText
from experienceBubble import ExperienceBubble
from informationSheet import InformationSheet
from levelingHandler import LevelingHandler
from math import atan, atan2, ceil, floor, pi, trunc, hypot
from random import randint
import upgrades
import uiTheme as ui
from spatialHash import SpatialHash
from progression import (FINAL_BOSS_LEVEL, MAX_LEVEL, MID_BOSS_LEVEL,
                         MINIBOSS_GATES, encounter_caps)

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

def resetAllStats():
    
    bG.playerPosX = bG.spawnX
    bG.playerPosY = bG.spawnY
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

    cS.bulletDamage = 1
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

    cS.healthPoints = 10
    cS.maxHealthPoints = 10
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
    cS.activeBoss = None
    cS.bossDebugRequested = False
    cS.bossDebugInvincible = False
    cS.beaudisEncounterStarted = False
    cS.beaudisDefeated = False
    cS.dissonanceEncounterStarted = False
    cS.gameCompleted = False
    cS.guaranteedMiniBossesSpawned = set()
    cS.enemySpawningEnabled = True

    cS.informationSheet = InformationSheet()
    
    cS.levelingHandler = LevelingHandler()
    cS.reset_upgrade_tracking()
    
    cS.newRandoUps = False
    
    cS.collectiveStats = {"Defense" : cS.defense, "Bullet Pierce" : cS.bulletPierce, "Bullet Count" : cS.projectileCount, "Spread Angle" : cS.azimuthalProjectileAngle, 
                                  "Attack Speed" : cS.attackCooldownStat, "Bullet Speed" : cS.bulletSpeed, "Bullet Range" : cS.bulletRange, "Bullet Damage" : cS.bulletDamage, 
                                  "Bullet Size" : cS.bulletSize, "Player Speed" : cS.playerSpeed, "Crit Chance" : cS.critChance, "Crit Damage" : cS.critDamage, 
                                  "Aura Size" : cS.aura, "Aura Strength" : cS.auraSpeed, "Exp Multiplier": cS.xpMult}
        
    cS.collectiveAddStats = {"Defense" : [0], "Bullet Pierce" : [0], "Bullet Count" : [0], "Spread Angle" : [0], 
                                "Attack Speed" : [0], "Bullet Speed" : [0], "Bullet Range" : [0], "Bullet Damage" : [0], 
                                "Bullet Size" : [0], "Player Speed" : [0], "Crit Chance": [0], "Crit Damage": [0],
                                "Aura Size" : [0], "Aura Strength" : [0], "Exp Multiplier": [0]}
    
    cS.collectiveMultStats = {"Defense" : [1], "Bullet Pierce" : [1], "Bullet Count" : [1], "Spread Angle" : [1], 
                                "Attack Speed" : [1], "Bullet Speed" : [1], "Bullet Range" : [1], "Bullet Damage" : [1], 
                                "Bullet Size" : [1], "Player Speed" : [1], "Crit Chance": [1], "Crit Damage": [1],
                                "Aura Size" : [1], "Aura Strength" : [1], "Exp Multiplier": [1]}
    
def combarinoPlayerStats():

    cS.projectileCount = (cS.collectiveStats["Bullet Count"] + sum(cS.collectiveAddStats["Bullet Count"])) * (multiply_list(cS.collectiveMultStats["Bullet Count"]))
    cS.azimuthalProjectileAngle = (cS.collectiveStats["Spread Angle"] + sum(cS.collectiveAddStats["Spread Angle"])) * (multiply_list(cS.collectiveMultStats["Spread Angle"]))
    cS.playerSpeed = (cS.collectiveStats["Player Speed"] + sum(cS.collectiveAddStats["Player Speed"])) * (multiply_list(cS.collectiveMultStats["Player Speed"]))
    cS.attackCooldownStat = (cS.collectiveStats["Attack Speed"] + sum(cS.collectiveAddStats["Attack Speed"])) * (multiply_list(cS.collectiveMultStats["Attack Speed"]))
    if(cS.attackCooldownStat <= 1): cS.attackCooldownStat = 1
    cS.bulletSpeed = (cS.collectiveStats["Bullet Speed"] + sum(cS.collectiveAddStats["Bullet Speed"])) * (multiply_list(cS.collectiveMultStats["Bullet Speed"]))
    cS.bulletRange = (cS.collectiveStats["Bullet Range"] + sum(cS.collectiveAddStats["Bullet Range"])) * (multiply_list(cS.collectiveMultStats["Bullet Range"]))
    cS.bulletSize = (cS.collectiveStats["Bullet Size"] + sum(cS.collectiveAddStats["Bullet Size"])) * (multiply_list(cS.collectiveMultStats["Bullet Size"]))
    cS.bulletDamage = (cS.collectiveStats["Bullet Damage"] + sum(cS.collectiveAddStats["Bullet Damage"])) * (multiply_list(cS.collectiveMultStats["Bullet Damage"]))
    cS.bulletPierce = (cS.collectiveStats["Bullet Pierce"] + sum(cS.collectiveAddStats["Bullet Pierce"])) * (multiply_list(cS.collectiveMultStats["Bullet Pierce"]))
    cS.defense = (cS.collectiveStats["Defense"] + sum(cS.collectiveAddStats["Defense"])) * (multiply_list(cS.collectiveMultStats["Defense"]))
    cS.critChance = (cS.collectiveStats["Crit Chance"] + sum(cS.collectiveAddStats["Crit Chance"])) * (multiply_list(cS.collectiveMultStats["Crit Chance"]))
    cS.critDamage = (cS.collectiveStats["Crit Damage"] + sum(cS.collectiveAddStats["Crit Damage"])) * (multiply_list(cS.collectiveMultStats["Crit Damage"]))
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
        cS.record_upgrade(card.name, card.rarity)
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
    input_x = int(bool(vH.keys[pg.K_a])) - int(bool(vH.keys[pg.K_d]))
    input_y = int(bool(vH.keys[pg.K_w])) - int(bool(vH.keys[pg.K_s]))
    direction_scale = 0.70710678 if input_x and input_y else 1.0
    input_x *= direction_scale
    input_y *= direction_scale

    if pg.K_SPACE in vH.keyPressed and cS.currDashCooldown <= 0 and (input_x or input_y):
        cS.dashing = True
        cS.currDashCooldown = cS.dashCooldownMax
        cS.fdX = input_x
        cS.fdY = input_y
        cS.playerInvulnerabilityTimer = max(cS.playerInvulnerabilityTimer, cS.dashDuration)
    
    if cS.currDashCooldown > 0:
        cS.currDashCooldown = max(0, cS.currDashCooldown - vH.get_timer_step())
    
    if not cS.dashing:
        movement_scale = vH.get_frame_scale()
        cS.dX = input_x * cS.playerSpeed * movement_scale
        cS.dY = input_y * cS.playerSpeed * movement_scale
    else:
        movement_scale = vH.get_frame_scale()
        cS.dX = cS.fdX * cS.dashModifier * cS.playerSpeed * movement_scale
        cS.dY = cS.fdY * cS.dashModifier * cS.playerSpeed * movement_scale
        
        if cS.currDashCooldown <= (cS.dashCooldownMax - cS.dashDuration):
            cS.dashing = False

    newABSPosX = bG.playerPosX - cS.dX
    newABSPosY = bG.playerPosY - cS.dY

    cS.currTileX = bG.playerPosX / vH.tileSizeGlobal
    cS.currTileY = bG.playerPosY / vH.tileSizeGlobal

    if not bG.rect_hits_wall(pg.Rect(newABSPosX, bG.playerPosY, cS.playerSize, cS.playerSize)):
        bG.playerPosX = newABSPosX
    else:
        cS.dX = 0

    if not bG.rect_hits_wall(pg.Rect(bG.playerPosX, newABSPosY, cS.playerSize, cS.playerSize)):
        bG.playerPosY = newABSPosY
    else:
        cS.dY = 0

    boss = cS.activeBoss
    if boss is not None and hasattr(boss, "arenaRadius"):
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

def handlingBulletCreation():

    if (cS.attackCooldownTimer <= 0 and (cS.autoFire or vH.mouseDown)):
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
            
            originX, originY = bG.lockX + (cS.playerSize / 2), bG.lockY + (cS.playerSize / 2)
            direction = _direction_to_target(originX, originY, vH.mouseX, vH.mouseY)

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
            boss_key = "beaudis"
        else:
            boss_key = "dissonance"
            if natural_dissonance_requested and natural_encounter:
                cS.dissonanceEncounterStarted = True
        cS.enemyHolster.clear()
        cS.enemyProjectileHolster.clear()
        cS.damageTextList.clear()
        cS.experienceList.clear()
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
            cS.enemyHolster.append(ENEMY_CATALOG.spawn(
                cS.currentLevel, key=key,
                min_distance_tiles=outside_awareness_tiles,
            ))
            cS.guaranteedMiniBossesSpawned.add(key)
    cS.currEnemyCount = len(cS.enemyHolster)

    cS.enemySpawnTimer -= vH.get_timer_step()
    current_threat = sum(getattr(enemy, "threatCost", 1.0) for enemy in cS.enemyHolster)
    if (cS.currEnemyCount < cS.enemyCap and current_threat < cS.enemyPopulationThreatCap
            and cS.enemySpawnTimer <= 0):
        cS.enemySpawnTimer = max(1, cS.enemyOneInFramesChance * randint(65, 135) / 100)
        batch_size = 1
        if randint(1, 100) <= 55:
            batch_size += 1
        if cS.currentLevel >= 6 and randint(1, 100) <= 35:
            batch_size += 1
        if cS.currentLevel >= 12 and randint(1, 100) <= 40:
            batch_size += 1
        if cS.currentLevel >= 17 and randint(1, 100) <= 30:
            batch_size += 1

        for _ in range(min(batch_size, cS.enemyCap - len(cS.enemyHolster))):
            remaining_threat = cS.enemyPopulationThreatCap - sum(
                getattr(enemy, "threatCost", 1.0) for enemy in cS.enemyHolster
            )
            enemy = ENEMY_CATALOG.spawn(cS.currentLevel, max_threat=remaining_threat,
                                        existing=cS.enemyHolster)
            if enemy is None:
                break
            cS.enemyHolster.append(enemy)
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
    prioritized = sorted(
        cS.enemyHolster,
        key=lambda enemy: (
            getattr(enemy, "awarenessState", "alerted") == "wandering",
            hypot((enemy.worldX + enemy.size / 2) - player_center[0],
                  (enemy.worldY + enemy.size / 2) - player_center[1]),
        ),
    )
    for enemy in prioritized:
        cost = getattr(enemy, "threatCost", 1.0)
        is_boss = enemy is cS.activeBoss
        enemy.engagementAllowed = is_boss or pressure_used + cost <= cS.enemyThreatCap
        if enemy.engagementAllowed:
            pressure_used += cost

    spawned_enemies = []
    for enemy in cS.enemyHolster:
        enemy.updateEnemy(
            bG.playerPosX + cS.playerSize/2,
            bG.playerPosY + cS.playerSize/2,
            cS.enemyProjectileHolster,
        )
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
        spawned_enemies.extend(getattr(enemy, "spawnedEnemies", ()))
        if hasattr(enemy, "spawnedEnemies"):
            enemy.spawnedEnemies.clear()

    for enemy in spawned_enemies:
        current_threat = sum(getattr(item, "threatCost", 1.0) for item in cS.enemyHolster)
        if len(cS.enemyHolster) >= cS.enemyCap:
            break
        if current_threat + getattr(enemy, "threatCost", 1.0) > cS.enemyPopulationThreatCap:
            continue
        cS.enemyHolster.append(enemy)
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
        for eman in enemy_grid.query(bullet_rect):
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
                    cS.damageTextList.append(DamageText(hitbox.x, hitbox.y, currColor, display_value, hitbox.width, vH.frameRate))
                    if result.killed:
                        dead_enemies.add(eman)

    for enemy in cS.enemyHolster:
        if enemy.is_dead():
            dead_enemies.add(enemy)

    for enemy in dead_enemies:
        cS.numOfEnemiesKilled += 1
        cS.experienceList.append(ExperienceBubble(
            enemy.posX, enemy.posY,
            cS.xpMult * (enemy.expValue * (cS.currentStage * cS.experienceStageMod)),
            enemy.difficulty, vH.frameRate, celebration=enemy is cS.activeBoss,
        ))
        if enemy is cS.activeBoss:
            if getattr(enemy, "bossName", "") == "BEAUDIS":
                cS.beaudisDefeated = True
            elif getattr(enemy, "bossName", "") == "DISSONANCE":
                cS.gameCompleted = True
            cS.activeBoss = None
            cS.enemySpawningEnabled = not cS.gameCompleted
            vH.screenShakeX = 0
            vH.screenShakeY = 0
            cS.enemyProjectileHolster[:] = [
                projectile for projectile in cS.enemyProjectileHolster
                if not str(projectile.owner or "").startswith(("beaudis", "dissonance"))
            ]
    if dead_enemies:
        cS.enemyHolster[:] = [enemy for enemy in cS.enemyHolster if enemy not in dead_enemies]
        cS.currEnemyCount = len(cS.enemyHolster)
    cS.bulletHolster[:] = [bullet for bullet in cS.bulletHolster if not bullet.remFlag]

def updateDamageTexts():
    for dText in cS.damageTextList[:]:
        dText.drawAndUpdateDamageText(cS.dX, cS.dY)
        if dText.deleteMe:
            cS.damageTextList.remove(dText)
                
def updateExperience():
    for bubble in cS.experienceList[:]:
        bubble.updateBubble(cS.auraSpeed, cS.dX, cS.dY)
            
def expForPlayer():
    player_rect = pg.Rect(bG.lockX, bG.lockY, cS.playerSize, cS.playerSize)

    for bubble in cS.experienceList[:]:
        bubble_rect = pg.Rect(bubble.posX, bubble.posY, bubble.size, bubble.size)

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
            originX = bG.lockX + cS.playerSize / 2
            originY = bG.lockY + cS.playerSize / 2
            deltaX = bubble.posX - originX
            deltaY = bubble.posY - originY

            if deltaX == 0:
                bubble.direction = pi/2 if deltaY > 0 else -pi/2
            else:
                bubble.direction = atan(deltaY / deltaX) if deltaX > 0 else -atan(deltaY / abs(deltaX)) + pi
        else:
            bubble.naturalSpawn = True
            
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
            if not getattr(projectile, "persistentHazard", False):
                projectile.remFlag = True
            trueDMG = max(projectile.damage - cS.defense, 0)
            cS.damageTextList.append(DamageText(
                bG.lockX, bG.lockY, ui.RED, trueDMG, vH.tileSizeGlobal, vH.frameRate,
            ))
            cS.healthPoints -= trueDMG
            cS.playerInvulnerabilityTimer = cS.playerInvulnerabilityMax
            if cS.healthPoints <= 0:
                vH.state = vH.States.TITLESCREEN
                cS.highestLevel = max(cS.highestLevel, cS.currentLevel)
            return

    for eman in cS.enemyHolster:
        collided_hitbox = next(
            (hitbox for _, hitbox in eman.get_screen_hitboxes() if player_rect.colliderect(hitbox)),
            None,
        )
        if collided_hitbox:
            trueDMG = max(eman.damage - cS.defense, 0)
            cS.damageTextList.append(DamageText(bG.lockX, bG.lockY, ui.RED, trueDMG, vH.tileSizeGlobal, vH.frameRate))
            cS.healthPoints -= trueDMG
            cS.playerInvulnerabilityTimer = cS.playerInvulnerabilityMax

            # Separate the bodies immediately so the next readable threat comes from
            # a new approach, not an enemy hidden inside the player rectangle.
            delta_x = collided_hitbox.centerx - player_rect.centerx
            delta_y = collided_hitbox.centery - player_rect.centery
            distance = max(1, hypot(delta_x, delta_y))
            eman.apply_knockback(
                delta_x / distance * vH.tileSizeGlobal * 0.8,
                delta_y / distance * vH.tileSizeGlobal * 0.8,
            )
            if cS.healthPoints <= 0:
                vH.state = vH.States.TITLESCREEN
                cS.highestLevel = max(cS.highestLevel, cS.currentLevel)
            break
    
def drawInformationSheet():
    if vH.mouseX < vH.sW * 0.75:
        center = (int(vH.mouseX), int(vH.mouseY))
        color = ui.CREAM if (cS.autoFire or vH.mouseDown) else ui.TEXT
        pg.draw.rect(vH.screen, ui.INK, (center[0] - 3, center[1] - 3, 6, 6))
        pg.draw.rect(vH.screen, color, (center[0] - 3, center[1] - 3, 6, 6), 1)
        gap, length = 7, 8
        pg.draw.line(vH.screen, color, (center[0] - gap - length, center[1]), (center[0] - gap, center[1]), 2)
        pg.draw.line(vH.screen, color, (center[0] + gap, center[1]), (center[0] + gap + length, center[1]), 2)
        pg.draw.line(vH.screen, color, (center[0], center[1] - gap - length), (center[0], center[1] - gap), 2)
        pg.draw.line(vH.screen, color, (center[0], center[1] + gap), (center[0], center[1] + gap + length), 2)
    drawBossHealthBar()
    drawRunCompleteBanner()
    cS.informationSheet.drawSheet()


def drawRunCompleteBanner():
    if not cS.gameCompleted:
        return
    scale = ui.display_scale(vH.screen)
    arena_width = vH.sW * .75
    width = min(arena_width * .58, 680 * scale)
    rect = pg.Rect((arena_width - width) / 2, 22 * scale, width, 76 * scale)
    ui.draw_panel(vH.screen, rect, ui.PANEL_RAISED, ui.CREAM, shadow=7)
    ui.draw_text(vH.screen, "DISSONANCE ENDED", 24 * scale, ui.CREAM,
                 (rect.centerx, rect.y + 10 * scale), "midtop")
    ui.draw_text(vH.screen, "LEVEL 20 // RUN COMPLETE", 11 * scale, ui.PURPLE,
                 (rect.centerx, rect.bottom - 12 * scale), "midbottom")


def drawBossHealthBar():
    boss = cS.activeBoss
    if boss is None or boss.hp <= 0:
        return
    if getattr(boss, "entranceRemaining", 0) > 1.0:
        return
    scale = ui.display_scale(vH.screen)
    arena_width = vH.sW * .75
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

def runTheTitleScreen():
    vH.screen.fill(ui.VOID)
    grid = max(28, int(min(vH.sW, vH.sH) / 28))
    for x in range(0, int(vH.sW), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (x, 0), (x, vH.sH))
    for y in range(0, int(vH.sH), grid):
        pg.draw.line(vH.screen, pg.Color(23, 27, 35), (0, y), (vH.sW, y))

    scale = min(vH.sW, vH.sH)
    ui_scale = max(.7, min(3.2, min(vH.sW / 1024, vH.sH / 768)))
    content_width = min(int(vH.sW * .68), int(980 * ui_scale))
    left = int((vH.sW - content_width) / 2)
    ui.draw_text(vH.screen, "ROTBOI", scale * .095, ui.TEXT, (vH.sW / 2, vH.sH * .12), "midtop")
    ui.draw_text(vH.screen, "R E M A S T E R E D", scale * .026, ui.CREAM, (vH.sW / 2, vH.sH * .245), "midtop")
    ui.draw_text(vH.screen, "BUILD THE VOLLEY. BREAK THE ROOM. DO IT AGAIN.", scale * .019, ui.MUTED, (vH.sW / 2, vH.sH * .305), "midtop")

    play_rect = pg.Rect(left + content_width * .22, vH.sH * .39, content_width * .56, max(62 * ui_scale, scale * .078))
    hovered = ui.draw_button(vH.screen, play_rect, "START RUN", (vH.mouseX, vH.mouseY), vH.mouseDown, True, ui.CREAM, "SPACE", int(scale * .025))

    controls_rect = pg.Rect(left, vH.sH * .55, content_width, max(132 * ui_scale, vH.sH * .18))
    ui.draw_panel(vH.screen, controls_rect, ui.PANEL, ui.BORDER, shadow=6)
    ui.draw_text(vH.screen, "FIELD MANUAL", scale * .018, ui.TEXT, (controls_rect.x + 18 * ui_scale, controls_rect.y + 14 * ui_scale))
    controls = (("WASD", "MOVE"), ("MOUSE", "AIM + FIRE"), ("SPACE", "DASH"), ("I", "AUTOFIRE"))
    cell_width = (controls_rect.width - 36 * ui_scale) / 4
    for index, (key, action) in enumerate(controls):
        center_x = controls_rect.x + 18 * ui_scale + cell_width * (index + .5)
        key_rect = pg.Rect(0, 0, min(82 * ui_scale, cell_width - 12 * ui_scale), 34 * ui_scale)
        key_rect.center = (center_x, controls_rect.centery - 3)
        pg.draw.rect(vH.screen, ui.INK, key_rect)
        pg.draw.rect(vH.screen, ui.BLUE, key_rect, 2)
        ui.draw_text(vH.screen, key, scale * .014, ui.BLUE, key_rect.center, "center")
        ui.draw_text(vH.screen, action, scale * .011, ui.MUTED, (center_x, key_rect.bottom + 11 * ui_scale), "midtop")

    record_label = "NO RUNS LOGGED" if cS.highestLevel <= 0 else f"BEST RUN  //  LEVEL {cS.highestLevel:02}"
    ui.draw_tag(vH.screen, record_label, (left, int(vH.sH * .80)), ui.GOLD if cS.highestLevel else ui.BORDER, int(scale * .012))
    ui.draw_text(vH.screen, "ESC  QUIT", scale * .012, ui.MUTED, (left + content_width, vH.sH * .805), "topright")

    if pg.K_SPACE in vH.keyPressed or (hovered and vH.mousePressed):
        vH.state = vH.States.GAMERUN
