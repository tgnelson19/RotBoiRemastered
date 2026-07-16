"""Small, dependency-free persistent profile and accessibility settings store."""

import json
from pathlib import Path


PROFILE_PATH = Path("data/profile.json")

DEFAULTS = {
    "best_level": 0,
    "best_kills": 0,
    "completed_runs": 0,
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
}


def load_profile(path=PROFILE_PATH):
    profile = dict(DEFAULTS)
    try:
        saved = json.loads(Path(path).read_text(encoding="utf-8"))
        if isinstance(saved, dict):
            profile.update({key: value for key, value in saved.items() if key in DEFAULTS})
    except (OSError, ValueError, TypeError):
        pass
    return profile


profile = load_profile()


def save_profile(path=PROFILE_PATH):
    path = Path(path)
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(profile, indent=2, sort_keys=True), encoding="utf-8")
        return True
    except OSError:
        return False


def record_run(level, kills, completed=False):
    profile["best_level"] = max(int(profile["best_level"]), int(level))
    profile["best_kills"] = max(int(profile["best_kills"]), int(kills))
    if completed:
        profile["completed_runs"] = int(profile["completed_runs"]) + 1
    save_profile()


def toggle(key):
    if key in profile and isinstance(profile[key], bool):
        profile[key] = not profile[key]
        save_profile()
    return profile.get(key)
