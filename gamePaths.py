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
        "bair", "sting", pygame.Color(91, 132, 74), EnemyStyle(
            speed=.70, size=1.22, health=1.65, damage=1.28,
            attack_cooldown=1.48, awareness=.92, experience=1.22,
            projectile_speed=.68, projectile_size=1.24, projectile_damage=1.28,
            colors=(pygame.Color(79, 101, 55), pygame.Color(103, 91, 55),
                    pygame.Color(54, 83, 55), pygame.Color(116, 105, 63)),
            tags=("rotton", "heavy"),
        ),
    ),
    "sight": GamePath(
        "sight", "PATH OF SIGHT", "THE QUICKENED HORIZON",
        "An exposed field of small, fragile hunters and close, rapid attacks.",
        "ishe", "chronos", pygame.Color(104, 190, 222), EnemyStyle(
            speed=1.45, size=.76, health=.56, damage=.78,
            attack_cooldown=.58, attack_range=.58, awareness=1.08,
            experience=.82, projectile_speed=1.55, projectile_size=.72,
            projectile_damage=.76, projectile_range=.48,
            colors=(pygame.Color(105, 190, 220), pygame.Color(135, 210, 230),
                    pygame.Color(228, 142, 63), pygame.Color(244, 174, 82)),
            tags=("sighted", "quick", "close_range"),
        ),
    ),
    "chemesthesis": GamePath(
        "chemesthesis", "PATH OF CHEMESTHESIS", "THE BURNING FIELD",
        "Durable carriers seed long-lived, mostly unaimed hazards everywhere.",
        "kage", "rot", pygame.Color(207, 83, 45), EnemyStyle(
            speed=.92, size=1.06, health=2.15, damage=1.02,
            attack_cooldown=.94, awareness=1.05, experience=1.34,
            projectile_speed=.62, projectile_size=1.08, projectile_damage=.88,
            projectile_range=4.0, projectile_lifetime=18.0, aimed_chance=.18,
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


def apply_enemy_identity(enemy):
    """Apply the active path's shared identity exactly once to an enemy tree."""
    path = active()
    style = path.style
    if enemy is None or path.key == "sound" or getattr(enemy, "contentPath", None) == path.key:
        return enemy
    enemy.contentPath = path.key
    enemy.speed *= style.speed
    enemy.size *= style.size
    enemy.maxHp = round(enemy.maxHp * style.health)
    enemy.hp = enemy.maxHp
    enemy.damage = round(enemy.damage * style.damage)
    enemy.expValue *= style.experience
    if style.colors:
        family = getattr(enemy, "family", path.key)
        enemy.color = style.colors[sum(map(ord, family)) % len(style.colors)]
    if hasattr(enemy, "attackCooldownMax"):
        enemy.attackCooldownMax *= style.attack_cooldown
    if hasattr(enemy, "attackCooldown"):
        enemy.attackCooldown *= style.attack_cooldown
    if hasattr(enemy, "attackRangeTiles"):
        enemy.attackRangeTiles *= style.attack_range
    enemy.awarenessRange *= style.awareness
    enemy.disengageRange *= style.awareness
    enemy.interactionTags = frozenset(
        set(getattr(enemy, "interactionTags", ())) | set(style.tags))
    for child in getattr(enemy, "spawnedEnemies", ()):
        apply_enemy_identity(child)
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
        return apply_enemy_identity(ENEMY_CATALOG.spawn(*args, **kwargs))

    def spawn_encounter(self, *args, **kwargs):
        return self._apply_group(ENEMY_CATALOG.spawn_encounter(*args, **kwargs))

    def spawn_patrol(self, *args, **kwargs):
        for factory in _EXCLUSIVE_ENCOUNTER_FACTORIES[active_key]:
            result = factory(*args, **kwargs)
            if result:
                return self._apply_group(result)
        return self._apply_group(ENEMY_CATALOG.spawn_patrol(*args, **kwargs))

    @staticmethod
    def _apply_group(result):
        if result:
            for enemy in result[1]:
                apply_enemy_identity(enemy)
        return result


ENCOUNTERS = _PathEnemyCatalog()


def tune_new_projectiles(projectiles, start_index):
    """Apply active path projectile rules to newly emitted hostile shots."""
    path = active()
    style = path.style
    if path.key == "sound":
        return
    for projectile in projectiles[start_index:]:
        if getattr(projectile, "contentPath", None) == path.key:
            continue
        projectile.contentPath = path.key
        projectile.speed *= style.projectile_speed
        projectile.size *= style.projectile_size
        projectile.damage = round(projectile.damage * style.projectile_damage)
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
