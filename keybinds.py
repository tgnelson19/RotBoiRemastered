"""Rebindable action -> key mapping, persisted through gameProfile.

Escape is reserved: it can never be bound to an action, and pressing it while
listening for a new key clears that action's binding instead of setting it.
"""

import pygame as pg

import gameProfile
import variableHolster as vH


ACTIONS = (
    ("move_up", "Move Up", pg.K_w),
    ("move_left", "Move Left", pg.K_a),
    ("move_down", "Move Down", pg.K_s),
    ("move_right", "Move Right", pg.K_d),
    ("dash", "Dash", pg.K_SPACE),
    ("rotate_left", "Rotate Camera Left", pg.K_q),
    ("rotate_right", "Rotate Camera Right", pg.K_e),
    ("autofire", "Toggle Autofire", pg.K_i),
    ("hud_toggle", "Toggle HUD Detail", pg.K_TAB),
    ("restart", "Restart Run (while paused)", pg.K_r),
    ("dev_level_up", "DEV: Force Level Up", pg.K_F1),
    ("dev_boss", "DEV: Force Boss Encounter", pg.K_b),
    ("dev_invincible", "DEV: Toggle Boss Invincibility", pg.K_y),
)
ACTION_DEFAULTS = {action_id: default_key for action_id, _label, default_key in ACTIONS}
ACTION_LABELS = {action_id: label for action_id, label, _default_key in ACTIONS}


def _load():
    saved = gameProfile.profile.get("keybinds")
    if not isinstance(saved, dict):
        saved = {}
    loaded = dict(ACTION_DEFAULTS)
    for action_id, key in saved.items():
        if action_id in loaded:
            loaded[action_id] = key
    return loaded


bindings = _load()


def _save():
    gameProfile.profile["keybinds"] = dict(bindings)
    gameProfile.save_profile()


def key_for(action_id):
    return bindings.get(action_id)


def pressed(action_id):
    """True if this action's bound key was pressed this frame (edge-triggered)."""
    key = bindings.get(action_id)
    return key is not None and key in vH.keyPressed


def held(action_id):
    """True if this action's bound key is currently held down."""
    key = bindings.get(action_id)
    return key is not None and bool(vH.keys[key])


def set_binding(action_id, key):
    """Bind action_id to key, clearing any other action already using it."""
    if action_id not in bindings or key == pg.K_ESCAPE:
        return
    for other_id, bound_key in bindings.items():
        if other_id != action_id and bound_key == key:
            bindings[other_id] = None
    bindings[action_id] = key
    _save()


def clear_binding(action_id):
    if action_id in bindings:
        bindings[action_id] = None
        _save()


def label_for_key(key):
    if key is None:
        return "UNBOUND"
    return pg.key.name(key).upper()
