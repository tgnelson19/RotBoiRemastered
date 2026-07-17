"""Reusable enemy-owned status effects applied by player attacks."""

from dataclasses import dataclass
import random

import gameProfile
import items


@dataclass(frozen=True)
class ControlState:
    movement_multiplier: float = 1.0
    attack_delay: float = 0.0
    stunned: bool = False


def _effects(enemy):
    if not hasattr(enemy, "statusEffects"):
        enemy.statusEffects = {}
        enemy.statusDotBuffer = 0.0
        enemy.statusControlResistance = 0.0
    return enemy.statusEffects


def apply(enemy, kind, duration, potency=0.0, stacks=1):
    effects = _effects(enemy)
    is_boss = hasattr(enemy, "bossName")
    if kind == "stun":
        resistance = min(.8, enemy.statusControlResistance + (.18 if is_boss else .08))
        enemy.statusControlResistance = resistance
        duration *= (1.0 - resistance) * (.45 if is_boss else 1.0)
    current = effects.get(kind, {"remaining": 0.0, "potency": 0.0, "stacks": 0})
    current["remaining"] = max(current["remaining"], duration)
    current["potency"] = max(current["potency"], potency)
    current["stacks"] = min(8 if kind == "bleed" else 3,
                            current["stacks"] + stacks)
    effects[kind] = current
    gameProfile.increment_quest("statuses_applied")


def roll_player_hit(enemy, bullet, equipment, rng=None):
    rng = rng or random
    chances = items.status_chances(equipment)
    # Multi-shot/rapid builds retain status value without multiplying it linearly.
    import characterStats as cS
    coefficient = max(.22, 1.0 / max(1.0, cS.projectileCount) ** .5)
    if getattr(bullet, "currCrit", False):
        chances["bleed"] = chances.get("bleed", 0.0) + .025
    tuning = {
        "poison": (4.5, .007),
        "bleed": (3.2, .006),
        "slow": (2.4, .22),
        "daze": (2.2, .28),
        "stun": (.65, 1.0),
    }
    for kind, chance in chances.items():
        if rng.random() <= min(.65, chance * coefficient):
            duration, potency = tuning[kind]
            apply(enemy, kind, duration, potency)


def damage_multiplier(enemy, bullet=None):
    effects = _effects(enemy)
    multiplier = 1.0
    # Poisoned, dazed targets are "opened" and take a modest synergy bonus.
    if "poison" in effects and "daze" in effects:
        multiplier += .08
    # Bleeding targets reward precision builds without making control mandatory.
    if "bleed" in effects and getattr(bullet, "currCrit", False):
        multiplier += min(.20, effects["bleed"]["stacks"] * .025)
    return multiplier


def update(enemy, seconds):
    effects = _effects(enemy)
    if enemy.statusControlResistance > 0:
        enemy.statusControlResistance = max(0.0, enemy.statusControlResistance - seconds * .035)
    dot_per_second = 0.0
    movement = 1.0
    daze = 0.0
    stunned = False
    expired = []
    for kind, effect in effects.items():
        effect["remaining"] -= seconds
        if effect["remaining"] <= 0:
            expired.append(kind)
            continue
        if kind == "poison":
            dot_per_second += max(2.0, enemy.maxHp * effect["potency"]) * effect["stacks"]
        elif kind == "bleed":
            dot_per_second += max(1.0, enemy.maxHp * effect["potency"]) * effect["stacks"]
        elif kind == "slow":
            movement *= max(.45, 1.0 - effect["potency"])
        elif kind == "daze":
            daze = max(daze, effect["potency"])
        elif kind == "stun":
            stunned = True
    for kind in expired:
        del effects[kind]
    if dot_per_second > 0 and enemy.hp > 0:
        enemy.statusDotBuffer += dot_per_second * seconds
        damage = int(enemy.statusDotBuffer)
        if damage:
            enemy.statusDotBuffer -= damage
            enemy.take_damage(damage)
    return ControlState(movement, daze, stunned)


def summary(enemy):
    return tuple(sorted(_effects(enemy)))
