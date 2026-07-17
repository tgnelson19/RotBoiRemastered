"""Data and selection rules for the run's upgrade-card draft.

This module deliberately has no pygame dependency.  Keeping the rules separate from
the card renderer makes balance changes testable and gives future shops, rewards,
and starting decks one shared source of truth.
"""

from dataclasses import dataclass
import random


@dataclass(frozen=True)
class UpgradeDefinition:
    name: str
    category: str
    additive: float
    multiplicative: float
    description: str


@dataclass(frozen=True)
class UpgradeCard:
    definition: UpgradeDefinition
    rarity: str
    math_type: str

    @property
    def name(self):
        return self.definition.name


RARITY_MULTIPLIERS = {
    "Common": 1.0,
    "Rare": 1.6,
    "Epic": 2.4,
    "Legendary": 4.0,
    "Mythical": 7.0,
}

# Explicit probabilities are easier to reason about and tune than a chain of
# independent "one in N" rolls.  The entries sum to 100.
RARITY_WEIGHTS = {
    "Common": 69.0,
    "Rare": 21.0,
    "Epic": 7.0,
    "Legendary": 2.5,
    "Mythical": 0.5,
}


DEFINITIONS = (
    UpgradeDefinition("Defense", "survival", 100, 0.12, "Reduce incoming damage"),
    UpgradeDefinition("Health", "survival", 100, 0.10, "Increase current and maximum health"),
    UpgradeDefinition("Vitality", "survival", 5, 0.12, "Recover health continuously"),
    UpgradeDefinition("Bullet Pierce", "volley", 0.25, 0.12, "Shots pass through more foes"),
    UpgradeDefinition("Bullet Count", "volley", 0.25, 0.12, "Fire additional projectiles"),
    UpgradeDefinition("Spread Angle", "volley", 0.314159, 0.12, "Widen the firing arc"),
    UpgradeDefinition("Attack Speed", "tempo", -1, -0.04, "Shorten time between attacks"),
    UpgradeDefinition("Bullet Speed", "precision", 3, 0.18, "Shots reach targets sooner"),
    UpgradeDefinition("Bullet Range", "precision", 75, 0.18, "Shots travel farther"),
    UpgradeDefinition("Bullet Damage", "power", 25, 0.16, "Increase every hit"),
    UpgradeDefinition("Bullet Size", "power", 4, 0.12, "Make shots easier to land"),
    UpgradeDefinition("Player Speed", "survival", 0.2, 0.16, "Improve repositioning"),
    UpgradeDefinition("Crit Chance", "critical", 0.08, 0.04, "Land critical hits more often"),
    UpgradeDefinition("Crit Damage", "critical", 0.25, 0.10, "Critical hits deal more damage"),
    UpgradeDefinition("Aura Size", "harvest", 8, 0.14, "Collect experience from farther away"),
    UpgradeDefinition("Aura Strength", "harvest", 0.8, 0.14, "Pull experience in faster"),
    UpgradeDefinition("Exp Multiplier", "harvest", 0.2, 0.16, "Gain more experience per foe"),
)

DEFINITIONS_BY_NAME = {definition.name: definition for definition in DEFINITIONS}


def _weighted_choice(items, weights, rng):
    return rng.choices(items, weights=weights, k=1)[0]


def roll_rarity(rng=None):
    rng = rng or random
    names = tuple(RARITY_WEIGHTS)
    return _weighted_choice(names, tuple(RARITY_WEIGHTS.values()), rng)


def _category_counts(upgrade_collection):
    counts = {}
    for name, count in upgrade_collection.get("types", {}).items():
        definition = DEFINITIONS_BY_NAME.get(name)
        if definition:
            counts[definition.category] = counts.get(definition.category, 0) + count
    return counts


def generate_offer(upgrade_collection=None, count=3, rng=None):
    """Return distinct cards, gently weighted toward the run's existing synergies.

    The weighting is intentionally modest: a build becomes more coherent without
    making off-build pivots disappear.  Each offer always contains distinct stats.
    """
    rng = rng or random
    upgrade_collection = upgrade_collection or {"types": {}}
    category_counts = _category_counts(upgrade_collection)
    available = list(DEFINITIONS)
    cards = []

    for _ in range(min(count, len(available))):
        weights = [1.0 + category_counts.get(item.category, 0) * 0.45 for item in available]
        definition = _weighted_choice(available, weights, rng)
        available.remove(definition)
        math_type = _weighted_choice(("additive", "multiplicative"), (0.62, 0.38), rng)
        cards.append(UpgradeCard(definition, roll_rarity(rng), math_type))

    return cards


def card_modifier(card):
    """Return the value appended to the additive or multiplicative stat stack."""
    rarity = RARITY_MULTIPLIERS[card.rarity]
    if card.math_type == "additive":
        return card.definition.additive * rarity
    return 1 + card.definition.multiplicative * rarity


def format_card_value(card):
    modifier = card_modifier(card)
    if card.math_type == "additive":
        sign = "+" if modifier >= 0 else ""
        return f"{sign}{modifier:.3g}"
    return f"x{modifier:.3g}"
