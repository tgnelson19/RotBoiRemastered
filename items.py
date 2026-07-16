"""Data and selection rules for enemy loot drops.

This module deliberately has no pygame dependency, matching upgrades.py.
Items are placeholder loot for now (name, slot, rarity) with no stat effects --
that comes in a later milestone once the equip/unequip loop feels good to play.
"""

from dataclasses import dataclass
import random

import upgrades


SLOT_TYPES = ("weapon", "armor", "ring", "accessory")


@dataclass(frozen=True)
class ItemDefinition:
    name: str
    slot_type: str
    description: str


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


DEFINITIONS = (
    ItemDefinition("Rusty Sword", "weapon", "A worn blade, better than fists."),
    ItemDefinition("Iron Dagger", "weapon", "Light and quick to swing."),
    ItemDefinition("Hunting Bow", "weapon", "Favors distance over power."),
    ItemDefinition("Leather Vest", "armor", "Simple protection, easy to move in."),
    ItemDefinition("Chainmail", "armor", "Heavier, sturdier coverage."),
    ItemDefinition("Plate Armor", "armor", "Slow but nearly impenetrable."),
    ItemDefinition("Copper Ring", "ring", "A plain band, faintly warm."),
    ItemDefinition("Silver Band", "ring", "Polished and cool to the touch."),
    ItemDefinition("Signet Ring", "ring", "Marked with a stranger's crest."),
    ItemDefinition("Lucky Charm", "accessory", "Small, worn smooth by handling."),
    ItemDefinition("Old Locket", "accessory", "Hinges creak, but it still shuts."),
    ItemDefinition("Traveler's Badge", "accessory", "A mark of distance covered."),
)

DEFINITIONS_BY_NAME = {definition.name: definition for definition in DEFINITIONS}

# Sums to 100. Biased toward 0 so loot doesn't spam every kill -- tune freely.
DROP_COUNT_WEIGHTS = {0: 55, 1: 25, 2: 12, 3: 6, 4: 2}


def _weighted_choice(items, weights, rng):
    return rng.choices(items, weights=weights, k=1)[0]


def roll_drop_count(rng=None):
    rng = rng or random
    counts = tuple(DROP_COUNT_WEIGHTS)
    return _weighted_choice(counts, tuple(DROP_COUNT_WEIGHTS.values()), rng)


def generate_drop(rng=None):
    rng = rng or random
    definition = rng.choice(DEFINITIONS)
    return ItemDrop(definition, upgrades.roll_rarity(rng))


def generate_drops(count, rng=None):
    rng = rng or random
    return [generate_drop(rng) for _ in range(count)]
