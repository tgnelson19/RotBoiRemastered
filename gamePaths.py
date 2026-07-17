"""Data-driven content paths layered over the shared run systems.

Paths own maps, encounter identity, projectile identity, and boss rosters.  New
path-exclusive encounter builders can be registered here without changing the
leveling, statistics, HUD, or core game loop.
"""

from dataclasses import dataclass
from math import pi
import random

import pygame

import background as bG
from enemyTypes import ENEMY_CATALOG


@dataclass(frozen=True)
class EnemyStyle:
    speed: float = 1.0
    size: float = 1.0
    health: float = 1.0
    damage: float = 1.0
    attack_cooldown: float = 1.0
    attack_range: float = 1.0
    awareness: float = 1.0
    experience: float = 1.0
    projectile_speed: float = 1.0
    projectile_size: float = 1.0
    projectile_damage: float = 1.0
    projectile_range: float = 1.0
    projectile_lifetime: float | None = None
    aimed_chance: float = 1.0
    late_health: float = 1.0
    late_damage: float = 1.0
    late_attack_rate: float = 1.0
    late_projectile_speed: float = 1.0
    late_projectile_damage: float = 1.0
    colors: tuple = ()
    tags: tuple = ()


@dataclass(frozen=True)
class GamePath:
    key: str
    title: str
    subtitle: str
    description: str
    mid_boss: str
    final_boss: str
    accent: pygame.Color
    style: EnemyStyle


PATHS = {
    "sound": GamePath(
        "sound", "PATH OF SOUND", "THE DISSONANCE",
        "The original arena: fast patterns, echoes, and open ground.",
        "beaudis", "dissonance", pygame.Color(207, 191, 151), EnemyStyle(),
    ),
    "touch": GamePath(
        "touch", "PATH OF TOUCH", "THE WEIGHT BELOW",
        "A cramped prison sewer of heavy bodies and slow, punishing shots.",
        "bair", "rot", pygame.Color(91, 132, 74), EnemyStyle(
            speed=.72, size=1.18, health=1.45, damage=1.20,
            attack_cooldown=1.35, awareness=.92, experience=1.18,
            projectile_speed=.68, projectile_size=1.24, projectile_damage=1.28,
            late_health=1.18, late_damage=1.12, late_attack_rate=.68,
            late_projectile_speed=1.08, late_projectile_damage=1.06,
            colors=(pygame.Color(79, 101, 55), pygame.Color(103, 91, 55),
                    pygame.Color(54, 83, 55), pygame.Color(116, 105, 63)),
            tags=("rotton", "heavy"),
        ),
    ),
    "sight": GamePath(
        "sight", "PATH OF SIGHT", "THE QUICKENED HORIZON",
        "An exposed field of small, fragile hunters and close, rapid attacks.",
        "ishe", "chronos", pygame.Color(104, 190, 222), EnemyStyle(
            speed=1.38, size=.78, health=.62, damage=.82,
            attack_cooldown=.68, attack_range=.60, awareness=1.08,
            experience=.82, projectile_speed=1.55, projectile_size=.72,
            projectile_damage=.76, projectile_range=.48,
            late_health=1.12, late_damage=1.20, late_attack_rate=.72,
            late_projectile_speed=1.08, late_projectile_damage=1.12,
            colors=(pygame.Color(105, 190, 220), pygame.Color(135, 210, 230),
                    pygame.Color(228, 142, 63), pygame.Color(244, 174, 82)),
            tags=("sighted", "quick", "close_range"),
        ),
    ),
    "chemesthesis": GamePath(
        "chemesthesis", "PATH OF CHEMESTHESIS", "THE BURNING FIELD",
        "Durable carriers seed long-lived, mostly unaimed hazards everywhere.",
        "kage", "ache", pygame.Color(207, 83, 45), EnemyStyle(
            speed=.94, size=1.05, health=1.55, damage=.92,
            attack_cooldown=1.12, awareness=1.05, experience=1.25,
            projectile_speed=.64, projectile_size=1.08, projectile_damage=.88,
            projectile_range=2.6, projectile_lifetime=12.0, aimed_chance=.22,
            late_health=1.15, late_damage=1.18, late_attack_rate=.62,
            late_projectile_speed=1.10, late_projectile_damage=1.12,
            colors=(pygame.Color(171, 62, 36), pygame.Color(211, 91, 38),
                    pygame.Color(92, 120, 50), pygame.Color(126, 48, 39)),
            tags=("chemesthetic", "minefield", "durable"),
        ),
    ),
    "phantasia": GamePath(
        "phantasia", "PATH OF PHANTASIA", "THE ORNATE DREAM",
        "Broad dream courts surround a few extravagant, feature-heavy ruins.",
        "hypno", "malady", pygame.Color(190, 83, 175), EnemyStyle(
            speed=1.02, size=1.03, health=1.12, damage=1.02,
            attack_cooldown=.96, experience=1.08,
            projectile_size=1.08, projectile_range=1.15,
            late_health=1.20, late_damage=1.18, late_attack_rate=.65,
            late_projectile_speed=1.12, late_projectile_damage=1.10,
            colors=(pygame.Color(117, 48, 121), pygame.Color(161, 57, 147),
                    pygame.Color(202, 85, 174), pygame.Color(91, 48, 119)),
            tags=("phantasian", "ornate"),
        ),
    ),
}

selected_key = "sound"
active_key = "sound"
_projectile_rng = random.Random(9071)


def selected():
    return PATHS[selected_key]


def active():
    return PATHS[active_key]


def select(key):
    global selected_key
    if key not in PATHS:
        raise KeyError(f"Unknown game path: {key}")
    selected_key = key


def cycle(direction):
    keys = tuple(PATHS)
    select(keys[(keys.index(selected_key) + direction) % len(keys)])


def activate_selected():
    global active_key
    active_key = selected_key
    bG.configure_battleground(active_key)


def boss_key(midpoint):
    path = active()
    return path.mid_boss if midpoint else path.final_boss


def is_touch():
    return active_key == "touch"


def _late_pressure(level):
    """Return the non-boss escalation from the midpoint through level 19."""
    return max(0.0, min(1.0, (float(level or 0) - 10.0) / 9.0))


def _toward_one(multiplier, pressure):
    return 1.0 + (multiplier - 1.0) * pressure


def apply_enemy_identity(enemy, level=0):
    """Apply the active path's shared identity exactly once to an enemy tree."""
    path = active()
    style = path.style
    if enemy is None or path.key == "sound" or getattr(enemy, "contentPath", None) == path.key:
        return enemy
    enemy.contentPath = path.key
    pressure = _late_pressure(level)
    enemy.speed *= style.speed
    enemy.size *= style.size
    enemy.maxHp = round(enemy.maxHp * style.health
                        * _toward_one(style.late_health, pressure))
    enemy.hp = enemy.maxHp
    enemy.damage = round(enemy.damage * style.damage
                         * _toward_one(style.late_damage, pressure))
    enemy.expValue *= style.experience
    if style.colors:
        family = getattr(enemy, "family", path.key)
        enemy.color = style.colors[sum(map(ord, family)) % len(style.colors)]
    if hasattr(enemy, "attackCooldownMax"):
        enemy.attackCooldownMax *= (style.attack_cooldown
                                    * _toward_one(style.late_attack_rate, pressure))
    if hasattr(enemy, "attackCooldown"):
        enemy.attackCooldown *= (style.attack_cooldown
                                 * _toward_one(style.late_attack_rate, pressure))
    if hasattr(enemy, "attackRangeTiles"):
        enemy.attackRangeTiles *= style.attack_range
    enemy.awarenessRange *= style.awareness
    enemy.disengageRange *= style.awareness
    enemy.interactionTags = frozenset(
        set(getattr(enemy, "interactionTags", ())) | set(style.tags))
    for child in getattr(enemy, "spawnedEnemies", ()):
        apply_enemy_identity(child, level)
    return enemy


# Optional path-only encounter factories receive the same arguments as
# EnemyCatalog.spawn_patrol and return (encounter, enemies) or None.  Keeping the
# registry path-scoped prevents experimental enemies from leaking into Sound.
_EXCLUSIVE_ENCOUNTER_FACTORIES = {key: [] for key in PATHS}


def register_exclusive_encounter(path_key, factory):
    if path_key not in PATHS:
        raise KeyError(f"Unknown game path: {path_key}")
    _EXCLUSIVE_ENCOUNTER_FACTORIES[path_key].append(factory)


class _PathEnemyCatalog:
    """Adapter around the mature shared catalog with path-only extension hooks."""

    @property
    def definitions(self):
        return ENEMY_CATALOG.definitions

    def spawn(self, *args, **kwargs):
        level = args[0] if args else kwargs.get("level", 0)
        return apply_enemy_identity(ENEMY_CATALOG.spawn(*args, **kwargs), level)

    def spawn_encounter(self, *args, **kwargs):
        level = args[0] if args else kwargs.get("level", 0)
        return self._apply_group(ENEMY_CATALOG.spawn_encounter(*args, **kwargs), level)

    def spawn_patrol(self, *args, **kwargs):
        level = args[0] if args else kwargs.get("level", 0)
        for factory in _EXCLUSIVE_ENCOUNTER_FACTORIES[active_key]:
            result = factory(*args, **kwargs)
            if result:
                return self._apply_group(result, level)
        return self._apply_group(ENEMY_CATALOG.spawn_patrol(*args, **kwargs), level)

    @staticmethod
    def _apply_group(result, level=0):
        if result:
            for enemy in result[1]:
                apply_enemy_identity(enemy, level)
        return result


ENCOUNTERS = _PathEnemyCatalog()


def tune_new_projectiles(projectiles, start_index, level=0, late_pressure=True):
    """Apply active path projectile rules to newly emitted hostile shots."""
    path = active()
    style = path.style
    if path.key == "sound":
        return
    pressure = _late_pressure(level) if late_pressure else 0.0
    for projectile in projectiles[start_index:]:
        if getattr(projectile, "contentPath", None) == path.key:
            continue
        projectile.contentPath = path.key
        projectile.speed *= (style.projectile_speed
                             * _toward_one(style.late_projectile_speed, pressure))
        projectile.size *= style.projectile_size
        projectile.damage = round(
            projectile.damage * style.projectile_damage
            * _toward_one(style.late_projectile_damage, pressure))
        projectile.remainingRange *= style.projectile_range
        if style.projectile_lifetime is not None:
            projectile.lifetime = max(projectile.lifetime or 0,
                                      style.projectile_lifetime)
        if style.aimed_chance < 1 and _projectile_rng.random() > style.aimed_chance:
            # Chemesthesis shots preserve their source but seed the room rather
            # than tracking the player, producing a persistent minefield.
            projectile.direction += _projectile_rng.uniform(-pi, pi)
        if style.colors:
            projectile.color = style.colors[-1]


def tune_encounter_caps(caps, level):
    """Turn late non-Sound rooms into bounded horde encounters."""
    result = dict(caps)
    pressure = _late_pressure(level) if active_key != "sound" else 0.0
    result["enemy_cap"] += round(8 * pressure)
    result["threat_cap"] *= 1.0 + .18 * pressure
    result["population_threat_cap"] *= 1.0 + .15 * pressure
    return result


def tune_encounter_pacing(pacing, level):
    result = dict(pacing)
    pressure = _late_pressure(level) if active_key != "sound" else 0.0
    result["patrol_size"] += round(pressure)
    result["max_world_encounters"] += round(pressure)
    result["spawn_interval_seconds"] *= 1.0 - .22 * pressure
    result["curated_chance"] = min(.65, result["curated_chance"] + .12 * pressure)
    return result


def projectile_cap(level, boss_active=False):
    """Bound hostile draw/update cost while preserving a late-run bullet hell."""
    if boss_active:
        return 150
    base = {"sound": 280, "touch": 280, "sight": 380,
            "chemesthesis": 340, "phantasia": 340}.get(active_key, 300)
    return base + round(40 * _late_pressure(level))
