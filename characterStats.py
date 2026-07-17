import pygame as pg
import variableHolster as vH
import gameProfile
import background as bG
from types import MappingProxyType
from informationSheet import InformationSheet
from levelingHandler import LevelingHandler

upgradeCollection = {"types": {}, "rarities": {}, "history": []}
bossAfflictions = {
    "exposure": 0.0, "decay_delay": 0.0,
    "slow": 0.0, "slow_remaining": 0.0,
    "pull": 0.0, "pull_remaining": 0.0, "pull_source": None,
}
dreamState = {
    "belief": 0.0, "clarity": 0.0, "peak_belief": 0.0,
    "decay_delay": 0.0, "false_rules": 0, "truths_read": 0,
}


def reset_upgrade_tracking():
    global upgradeCollection
    upgradeCollection = {"types": {}, "rarities": {}, "history": []}


def reset_boss_afflictions():
    bossAfflictions.update({
        "exposure": 0.0, "decay_delay": 0.0,
        "slow": 0.0, "slow_remaining": 0.0,
        "pull": 0.0, "pull_remaining": 0.0, "pull_source": None,
    })


def reset_dream_state():
    dreamState.update({
        "belief": 0.0, "clarity": 0.0, "peak_belief": 0.0,
        "decay_delay": 0.0, "false_rules": 0, "truths_read": 0,
    })


def alter_belief(amount, false_rule=False, truth=False):
    dreamState["belief"] = max(0.0, min(10.0, dreamState["belief"] + amount))
    dreamState["peak_belief"] = max(dreamState["peak_belief"], dreamState["belief"])
    dreamState["decay_delay"] = 2.0 if amount > 0 else dreamState["decay_delay"]
    if false_rule:
        dreamState["false_rules"] += 1
    if truth:
        dreamState["truths_read"] += 1
        dreamState["clarity"] = min(5.0, dreamState["clarity"] + 1.0)


def update_dream_state(seconds):
    dreamState["decay_delay"] = max(0.0, dreamState["decay_delay"] - seconds)
    dreamState["clarity"] = max(0.0, dreamState["clarity"] - seconds * .18)
    if dreamState["decay_delay"] <= 0:
        decay = .3 + dreamState["clarity"] * .12
        dreamState["belief"] = max(0.0, dreamState["belief"] - seconds * decay)


def apply_boss_affliction(kind, duration=0.0, strength=0.0, exposure=0.0, source=None):
    """Apply a reusable movement affliction without permanently changing stats."""
    bossAfflictions["exposure"] = min(10.0, bossAfflictions["exposure"] + exposure)
    bossAfflictions["decay_delay"] = max(bossAfflictions["decay_delay"], 2.25)
    if kind == "slow":
        bossAfflictions["slow"] = max(bossAfflictions["slow"], strength)
        bossAfflictions["slow_remaining"] = max(bossAfflictions["slow_remaining"], duration)
    elif kind == "pull":
        bossAfflictions["pull"] = max(bossAfflictions["pull"], strength)
        bossAfflictions["pull_remaining"] = max(bossAfflictions["pull_remaining"], duration)
        bossAfflictions["pull_source"] = source


def update_boss_afflictions(seconds):
    for name in ("slow_remaining", "pull_remaining", "decay_delay"):
        bossAfflictions[name] = max(0.0, bossAfflictions[name] - seconds)
    if bossAfflictions["slow_remaining"] <= 0:
        bossAfflictions["slow"] = 0.0
    if bossAfflictions["pull_remaining"] <= 0:
        bossAfflictions["pull"] = 0.0
        bossAfflictions["pull_source"] = None
    if bossAfflictions["decay_delay"] <= 0:
        bossAfflictions["exposure"] = max(0.0, bossAfflictions["exposure"] - seconds * .8)


def boss_movement_multiplier():
    exposure_penalty = bossAfflictions["exposure"] * .025
    return max(.58, 1.0 - exposure_penalty - bossAfflictions["slow"])


def player_build_snapshot():
    """Return an immutable summary bosses may inspect without mutating the build."""
    import upgrades
    types = dict(upgradeCollection["types"])
    categories = {}
    for name, count in types.items():
        definition = upgrades.DEFINITIONS_BY_NAME.get(name)
        if definition:
            categories[definition.category] = categories.get(definition.category, 0) + count
    offense = {key: categories.get(key, 0)
               for key in ("volley", "tempo", "precision", "power", "critical")}
    dominant = max(offense, key=offense.get) if any(offense.values()) else "power"
    stats = {
        "projectile_count": projectileCount, "pierce": bulletPierce,
        "crit_chance": critChance, "crit_damage": critDamage,
        "bullet_speed": bulletSpeed, "bullet_size": bulletSize,
    }
    return MappingProxyType({
        "types": MappingProxyType(types),
        "categories": MappingProxyType(categories),
        "stats": MappingProxyType(stats),
        "dominant_offense": dominant,
    })


def record_upgrade(upgrade_type, rarity, math_type=None):
    global upgradeCollection
    if upgrade_type not in upgradeCollection["types"]:
        upgradeCollection["types"][upgrade_type] = 0
    upgradeCollection["types"][upgrade_type] += 1

    if rarity not in upgradeCollection["rarities"]:
        upgradeCollection["rarities"][rarity] = 0
    upgradeCollection["rarities"][rarity] += 1
    if math_type:
        upgradeCollection["history"].append({
            "name": upgrade_type,
            "rarity": rarity,
            "math_type": math_type,
        })


enemyCap = 50
enemyThreatCap = 36.0
enemyPopulationThreatCap = 60.0
currEnemyCount = 0

highestLevel = int(gameProfile.profile["best_level"])
runTimeSeconds = 0.0
runOutcome = "DEFEATED"
currentBounty = None
lastUpgrade = None
lastUpgradeAt = -100.0

playerSpeed = 2.1
playerSize = vH.tileSizeGlobal * .75
playerColor = pg.Color(0,0,120)

dX, dY = 0, 0

currTileX = 0
currTileY = 0

playerRect = pg.Rect(bG.lockX, bG.lockY, playerSize, playerSize)

projectileCount = 2
azimuthalProjectileAngle = 200

attackCooldownStat = 40
attackCooldownTimer = 0 #Number of frames before next bullet can be fired (Yes, I know, I don't care)

bulletDamage = 100
bulletSpeed = 4
bulletRange = 150
bulletSize = vH.tileSizeGlobal / 2
bulletColor = pg.Color(125,125,125)
bulletPierce = 1
critChance = 0.05
critDamage = 2

aura = 80
auraSpeed = 2
levelMod = 1.04
xpMult = 1
currentLevel = 0
pendingLevelUps = 0
expCount = 0
expNeededForNextLevel = 40
baseExpNeededForNextLevel = 40
levelScaleIncreaseFunction = 1.15

healthPoints = 1000
maxHealthPoints = 1000
vitality = 10
healthRecoveryBuffer = 0.0
defense = 0

enemyOneInFramesChance = 220
enemySpawnTimer = 0
encounterSpawnCooldown = 0

numOfEnemiesKilled = 0
currentStage = 1
xpMult = 1
experienceStageMod = 1.1

dashDuration = vH.frameRate * 0.15
dashing = False

dashModifier = 4

dashCooldownMax = vH.frameRate * 1
currDashCooldown = 0

autoFire = bool(gameProfile.profile["autofire"])
autoFlop = False

fdX, fdY = 0, 0

bulletHolster = []
enemyHolster = []
damageTextList = []
experienceList = []
enemyProjectileHolster = []
lootCrateList = []
equipment = {"weapon": None, "armor": None, "ring": None, "accessory_1": None, "accessory_2": None}
activeBoss = None
bossDebugRequested = False
bossDebugInvincible = False
beaudisEncounterStarted = False
beaudisDefeated = False
dissonanceEncounterStarted = False
gameCompleted = False
practiceBossKey = None
practiceMode = False
guaranteedMiniBossesSpawned = set()
enemySpawningEnabled = True

informationSheet = InformationSheet()

levelingHandler = LevelingHandler()

newRandoUps = False

collectiveStats = {"Defense" : defense, "Health" : maxHealthPoints, "Vitality" : vitality, "Bullet Pierce" : bulletPierce, "Bullet Count" : projectileCount, "Spread Angle" : azimuthalProjectileAngle, 
                                  "Attack Speed" : attackCooldownStat, "Bullet Speed" : bulletSpeed, "Bullet Range" : bulletRange, "Bullet Damage" : bulletDamage, 
                                  "Bullet Size" : bulletSize, "Player Speed" : playerSpeed, "Crit Chance" : critChance, "Crit Damage" : critDamage, 
                                  "Aura Size" : aura, "Aura Strength" : auraSpeed, "Exp Multiplier": xpMult}
        
collectiveAddStats = {"Defense" : [0], "Health" : [0], "Vitality" : [0], "Bullet Pierce" : [0], "Bullet Count" : [0], "Spread Angle" : [0], 
                            "Attack Speed" : [0], "Bullet Speed" : [0], "Bullet Range" : [0], "Bullet Damage" : [0], 
                            "Bullet Size" : [0], "Player Speed" : [0], "Crit Chance": [0], "Crit Damage": [0],
                            "Aura Size" : [0], "Aura Strength" : [0], "Exp Multiplier": [0]}

collectiveMultStats = {"Defense" : [1], "Health" : [1], "Vitality" : [1], "Bullet Pierce" : [1], "Bullet Count" : [1], "Spread Angle" : [1], 
                            "Attack Speed" : [1], "Bullet Speed" : [1], "Bullet Range" : [1], "Bullet Damage" : [1], 
                            "Bullet Size" : [1], "Player Speed" : [1], "Crit Chance": [1], "Crit Damage": [1],
                            "Aura Size" : [1], "Aura Strength" : [1], "Exp Multiplier": [1]}

gracePeriod = 10
playerInvulnerabilityTimer = 0
playerInvulnerabilityMax = vH.frameRate * 0.55
