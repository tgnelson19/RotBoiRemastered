"""Versioned local profile, accessibility settings, and meta progression."""

from copy import deepcopy
import json
import os
from pathlib import Path
import sys
import tempfile


_default_profile_path = (Path(tempfile.gettempdir()) / "rotboi_pytest_profile.json"
                         if any("pytest" in argument.lower() for argument in sys.argv)
                         else Path("data/profile.json"))
PROFILE_PATH = Path(os.environ.get("ROTBOI_PROFILE_PATH", _default_profile_path))
PROFILE_VERSION = 2

DEFAULTS = {
    "version": PROFILE_VERSION,
    "best_level": 0,
    "best_kills": 0,
    "completed_runs": 0,
    "best_dummy_dps": 0.0,
    "autofire": True,
    "casual_mode": True,
    "tutorial_hints": True,
    "screen_shake": 0.65,
    "damage_numbers": True,
    "aim_guide": False,
    "high_contrast": False,
    "hud_mode": "compact",
    "text_size": 1.0,
    "keybinds": {},
    "soul_tokens": 0,
    "skill_nodes": [],
    "path_mastery": {},
    "completed_paths": [],
    "enemy_research": {},
    "boss_research": {},
    "discovered_cards": [],
    "discovered_items": [],
    "storage": [],
    "starting_loadout": {},
    "museum_artifacts": [],
    "quest_progress": {},
    "completed_quests": [],
}


def _merge_defaults(saved):
    result = deepcopy(DEFAULTS)
    if not isinstance(saved, dict):
        return result
    for key, default in DEFAULTS.items():
        value = saved.get(key, default)
        if isinstance(default, dict) and isinstance(value, dict):
            result[key].update(value)
        elif isinstance(default, list) and isinstance(value, list):
            result[key] = list(value)
        elif key in saved:
            result[key] = value
    result["version"] = PROFILE_VERSION
    return result


def load_profile(path=PROFILE_PATH):
    try:
        saved = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError, TypeError):
        saved = {}
    return _merge_defaults(saved)


profile = load_profile()


def save_profile(path=PROFILE_PATH):
    path = Path(path)
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(profile, indent=2, sort_keys=True), encoding="utf-8")
        return True
    except OSError:
        return False


def record_run(level, kills, completed=False, path_key=None):
    profile["best_level"] = max(int(profile["best_level"]), int(level))
    profile["best_kills"] = max(int(profile["best_kills"]), int(kills))
    if completed:
        profile["completed_runs"] = int(profile["completed_runs"]) + 1
        if path_key:
            mastery = profile["path_mastery"]
            mastery[path_key] = int(mastery.get(path_key, 0)) + 1
            if path_key not in profile["completed_paths"]:
                profile["completed_paths"].append(path_key)
                profile["soul_tokens"] = int(profile["soul_tokens"]) + 1
                artifact = f"{path_key}_echo"
                if artifact not in profile["museum_artifacts"]:
                    profile["museum_artifacts"].append(artifact)
            increment_quest("path_clears")
    save_profile()


def increment_quest(key, amount=1):
    progress = profile["quest_progress"]
    progress[key] = int(progress.get(key, 0)) + int(amount)


def discover_card(name):
    if name not in profile["discovered_cards"]:
        profile["discovered_cards"].append(name)
        save_profile()


def discover_item(name):
    if name not in profile["discovered_items"]:
        profile["discovered_items"].append(name)
        increment_quest("items_found")
        save_profile()


def research_enemy(key, defeated=False, boss=False):
    collection = profile["boss_research"] if boss else profile["enemy_research"]
    entry = collection.setdefault(str(key), {"seen": 0, "defeated": 0})
    field = "defeated" if defeated else "seen"
    if defeated:
        entry[field] = int(entry.get(field, 0)) + 1
    elif int(entry.get(field, 0)) == 0:
        entry[field] = 1
    else:
        return
    if defeated:
        increment_quest("enemies_defeated")
    # Avoid writing the profile on every ordinary kill while still preserving
    # each bestiary research threshold promptly.
    if (not defeated or entry[field] in (1, 5, 10, 25, 50, 100)
            or entry[field] % 100 == 0):
        save_profile()


def record_dummy_dps(value):
    value = round(float(value), 1)
    previous = float(profile["best_dummy_dps"])
    if value >= previous + max(1.0, previous * .01):
        profile["best_dummy_dps"] = value
        save_profile()


def purchase_skill(node_id, cost):
    if node_id in profile["skill_nodes"] or int(profile["soul_tokens"]) < cost:
        return False
    profile["soul_tokens"] = int(profile["soul_tokens"]) - cost
    profile["skill_nodes"].append(node_id)
    save_profile()
    return True


def toggle(key):
    if key in profile and isinstance(profile[key], bool):
        profile[key] = not profile[key]
        save_profile()
    return profile.get(key)
