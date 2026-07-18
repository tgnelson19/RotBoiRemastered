"""Small, UI-independent rules for Soul progression and persistent collections."""

from dataclasses import dataclass

import gameProfile
import items


@dataclass(frozen=True)
class SkillNode:
    key: str
    name: str
    description: str
    cost: int
    stat: str | None = None
    value: float = 0.0
    mode: str = "additive"


SKILL_NODES = (
    SkillNode("tempered_soul", "Tempered Soul", "+2% damage in every run", 1,
              "Bullet Damage", 1.02, "multiplicative"),
    SkillNode("quick_memory", "Quick Memory", "+2% movement speed", 1,
              "Player Speed", 1.02, "multiplicative"),
    SkillNode("deep_reserve", "Deep Reserve", "+30 maximum health", 1,
              "Health", 30),
    SkillNode("patient_hands", "Patient Hands", "+2% critical chance", 1,
              "Crit Chance", .02),
    SkillNode("wide_grasp", "Wide Grasp", "+8 experience attraction range", 1,
              "Aura Size", 8),
)
SKILL_NODES_BY_KEY = {node.key: node for node in SKILL_NODES}


QUESTS = (
    ("first_steps", "First Steps", "Defeat 50 enemies", "enemies_defeated", 50),
    ("curator", "Curator", "Find 8 distinct items", "items_found", 8),
    ("pathwalker", "Pathwalker", "Complete 2 paths", "path_clears", 2),
    ("afflictor", "Afflictor", "Apply 100 status effects", "statuses_applied", 100),
)


def stat_adjustments():
    additive, multiplicative = {}, {}
    for key in gameProfile.profile["skill_nodes"]:
        node = SKILL_NODES_BY_KEY.get(key)
        if node is None or node.stat is None:
            continue
        target = multiplicative if node.mode == "multiplicative" else additive
        target.setdefault(node.stat, []).append(node.value)
    return additive, multiplicative


def complete_ready_quests():
    changed = False
    completed = gameProfile.profile["completed_quests"]
    progress = gameProfile.profile["quest_progress"]
    for quest_id, _name, _description, counter, target in QUESTS:
        if quest_id not in completed and int(progress.get(counter, 0)) >= target:
            completed.append(quest_id)
            gameProfile.profile["soul_tokens"] += 1
            changed = True
    if changed:
        gameProfile.save_profile()
    return changed


def store_drop(drop):
    serialized = items.serialize(drop)
    storage = gameProfile.profile["storage"]
    # Every extracted copy matters.  Storage is an inventory, not a discovery
    # checklist; deduplicating identical rolls made one item effectively infinite.
    storage.append(serialized)
    gameProfile.discover_item(drop.name)


def extract_equipment(equipment):
    for drop in equipment.values():
        if drop is not None:
            store_drop(drop)
    gameProfile.save_profile()


def empty_equipment():
    return {"weapon": None, "armor": None, "ring": None,
            "accessory_1": None, "accessory_2": None}


def starting_equipment():
    """Preview the selected next-run kit without changing persistent storage."""
    result = empty_equipment()
    for slot, serialized in gameProfile.profile["starting_loadout"].items():
        if slot in result:
            result[slot] = items.deserialize(serialized)
    return result


def begin_run():
    """Withdraw the selected kit from storage and return its runtime equipment.

    Withdrawal happens before combat.  Extraction deposits surviving equipped
    items again; defeat, restart, or abandonment intentionally leaves them lost.
    Permanent skill nodes and other profile progression are never touched here.
    """
    equipment = empty_equipment()
    storage = gameProfile.profile["storage"]
    for slot, serialized in tuple(gameProfile.profile["starting_loadout"].items()):
        if slot not in equipment:
            continue
        try:
            storage_index = storage.index(serialized)
        except ValueError:
            continue
        drop = items.deserialize(storage.pop(storage_index))
        if drop is not None:
            equipment[slot] = drop
    gameProfile.profile["starting_loadout"] = {}
    gameProfile.save_profile()
    return equipment


def equip_from_storage(index):
    storage = gameProfile.profile["storage"]
    if not 0 <= index < len(storage):
        return False
    drop = items.deserialize(storage[index])
    if drop is None:
        return False
    loadout = gameProfile.profile["starting_loadout"]
    compatible = [drop.slot_type] if drop.slot_type != "accessory" else ["accessory_1", "accessory_2"]
    target = next((slot for slot in compatible if slot not in loadout), compatible[0])
    serialized = items.serialize(drop)
    # A selected loadout may not promise more identical copies than storage owns.
    already_selected = sum(1 for slot, value in loadout.items()
                           if slot != target and value == serialized)
    if already_selected >= storage.count(serialized):
        return False
    loadout[target] = serialized
    gameProfile.save_profile()
    return True


def clear_loadout():
    gameProfile.profile["starting_loadout"] = {}
    gameProfile.save_profile()


def mastery_title(path_key):
    count = int(gameProfile.profile["path_mastery"].get(path_key, 0))
    if count >= 5:
        return "Master"
    if count >= 3:
        return "Adept"
    if count >= 1:
        return "Initiate"
    return "Unwalked"
