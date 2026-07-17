"""Data, modifiers, sets, persistence, and selection rules for loot items."""

from dataclasses import dataclass
import random

import upgrades


SLOT_TYPES = ("weapon", "armor", "ring", "accessory")


@dataclass(frozen=True)
class ItemModifier:
    stat: str
    value: float
    mode: str = "additive"


@dataclass(frozen=True)
class ItemDefinition:
    name: str
    slot_type: str
    description: str
    modifiers: tuple = ()
    set_name: str | None = None
    status: tuple | None = None  # (kind, base proc chance)


@dataclass(frozen=True)
class ItemDrop:
    definition: ItemDefinition
    rarity: str

    @property
    def name(self):
        return self.definition.name

    @property
    def slot_type(self):
        return self.definition.slot_type


M = ItemModifier
DEFINITIONS = (
    ItemDefinition("Rusty Sword", "weapon", "Heavy critical strikes at a slower cadence.",
                   (M("Bullet Damage", 20), M("Crit Chance", .05), M("Crit Damage", .25), M("Attack Speed", 1.12, "multiplicative")), "Rustbound", ("bleed", .07)),
    ItemDefinition("Iron Dagger", "weapon", "Quick attacks trade reach for reliable bleeding.",
                   (M("Attack Speed", .84, "multiplicative"), M("Crit Chance", .08), M("Bullet Damage", -12), M("Bullet Range", -.12, "multiplicative")), "Wayfarer", ("bleed", .12)),
    ItemDefinition("Hunting Bow", "weapon", "Fast, distant shots with less raw impact.",
                   (M("Bullet Range", 1.25, "multiplicative"), M("Bullet Speed", 1.18, "multiplicative"), M("Bullet Pierce", .2), M("Bullet Damage", -.10, "multiplicative")), "Wayfarer", ("slow", .06)),
    ItemDefinition("Leather Vest", "armor", "Mobility and light protection.",
                   (M("Defense", 35), M("Player Speed", .12), M("Health", -.06, "multiplicative")), "Wayfarer"),
    ItemDefinition("Chainmail", "armor", "Steady protection with a small movement cost.",
                   (M("Defense", 90), M("Health", 80), M("Player Speed", -.08)), "Rustbound"),
    ItemDefinition("Plate Armor", "armor", "Exceptional defense, deliberately cumbersome.",
                   (M("Defense", 170), M("Health", 150), M("Player Speed", -.18), M("Attack Speed", 1.08, "multiplicative")), "Bulwark"),
    ItemDefinition("Copper Ring", "ring", "Learns quickly at the cost of damage.",
                   (M("Exp Multiplier", .15), M("Bullet Damage", -.06, "multiplicative")), "Scholar"),
    ItemDefinition("Silver Band", "ring", "Extends and hastens projectiles.",
                   (M("Bullet Speed", 1.15, "multiplicative"), M("Bullet Range", 1.15, "multiplicative"), M("Bullet Size", -.08, "multiplicative")), "Scholar", ("daze", .05)),
    ItemDefinition("Signet Ring", "ring", "Powerful criticals that occur less often.",
                   (M("Crit Damage", .40), M("Crit Chance", -.03)), "Rustbound", ("stun", .02)),
    ItemDefinition("Lucky Charm", "accessory", "A little luck and a little vulnerability.",
                   (M("Crit Chance", .04), M("Defense", -30)), "Wayfarer", ("poison", .06)),
    ItemDefinition("Old Locket", "accessory", "Recovery and vitality slow the firing rhythm.",
                   (M("Vitality", 4), M("Health", 100), M("Attack Speed", 1.08, "multiplicative")), "Bulwark"),
    ItemDefinition("Traveler's Badge", "accessory", "Speed and range over protection.",
                   (M("Player Speed", .18), M("Bullet Range", 1.10, "multiplicative"), M("Defense", -35)), "Wayfarer"),
)

DEFINITIONS_BY_NAME = {definition.name: definition for definition in DEFINITIONS}
DROP_COUNT_WEIGHTS = {0: 55, 1: 25, 2: 12, 3: 6, 4: 2}


def rarity_scale(rarity):
    return upgrades.RARITY_MULTIPLIERS.get(rarity, 1.0) ** .5


def modifiers_for(drop):
    scale = rarity_scale(drop.rarity)
    for modifier in drop.definition.modifiers:
        if modifier.mode == "multiplicative":
            yield modifier.stat, 1 + (modifier.value - 1) * scale, modifier.mode
        else:
            yield modifier.stat, modifier.value * scale, modifier.mode


def equipment_adjustments(equipment):
    additive, multiplicative = {}, {}
    equipped = [drop for drop in equipment.values() if drop is not None]
    for drop in equipped:
        for stat, value, mode in modifiers_for(drop):
            target = multiplicative if mode == "multiplicative" else additive
            target.setdefault(stat, []).append(value)
    set_counts = {}
    for drop in equipped:
        if drop.definition.set_name:
            set_counts[drop.definition.set_name] = set_counts.get(drop.definition.set_name, 0) + 1
    # Two-piece bonuses create an immediately useful set foundation.
    if set_counts.get("Wayfarer", 0) >= 2:
        additive.setdefault("Player Speed", []).append(.12)
    if set_counts.get("Rustbound", 0) >= 2:
        additive.setdefault("Crit Damage", []).append(.25)
    if set_counts.get("Bulwark", 0) >= 2:
        additive.setdefault("Defense", []).append(75)
    if set_counts.get("Scholar", 0) >= 2:
        additive.setdefault("Exp Multiplier", []).append(.12)
    return additive, multiplicative


def status_chances(equipment):
    chances = {}
    for drop in equipment.values():
        if drop is not None and drop.definition.status:
            kind, chance = drop.definition.status
            chances[kind] = chances.get(kind, 0.0) + chance * rarity_scale(drop.rarity)
    # Rustbound turns bleed into a critical synergy; applied by the effect module.
    return chances


def describe(drop):
    lines = [f"{drop.rarity} {drop.name}", drop.definition.description]
    for stat, value, mode in modifiers_for(drop):
        if mode == "multiplicative":
            percent = (value - 1) * 100
            lines.append(f"{percent:+.0f}% {stat}")
        else:
            lines.append(f"{value:+.2g} {stat}")
    if drop.definition.status:
        kind, chance = drop.definition.status
        lines.append(f"{chance * rarity_scale(drop.rarity) * 100:.1f}% {kind.title()} on hit")
    if drop.definition.set_name:
        lines.append(f"Set: {drop.definition.set_name} (2 pieces)")
    return " | ".join(lines)


def serialize(drop):
    return {"name": drop.name, "rarity": drop.rarity}


def deserialize(data):
    if not isinstance(data, dict) or data.get("name") not in DEFINITIONS_BY_NAME:
        return None
    rarity = data.get("rarity", "Common")
    if rarity not in upgrades.RARITY_WEIGHTS:
        rarity = "Common"
    return ItemDrop(DEFINITIONS_BY_NAME[data["name"]], rarity)


def _weighted_choice(values, weights, rng):
    return rng.choices(values, weights=weights, k=1)[0]


def roll_drop_count(rng=None):
    rng = rng or random
    return _weighted_choice(tuple(DROP_COUNT_WEIGHTS), tuple(DROP_COUNT_WEIGHTS.values()), rng)


def generate_drop(rng=None):
    rng = rng or random
    return ItemDrop(rng.choice(DEFINITIONS), upgrades.roll_rarity(rng))


def generate_drops(count, rng=None):
    rng = rng or random
    return [generate_drop(rng) for _ in range(count)]
