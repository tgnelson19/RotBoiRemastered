import os
import unittest
from unittest import mock

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame as pg

import gameProfile
import keybinds
import variableHolster as vH


class KeybindsTests(unittest.TestCase):
    def setUp(self):
        self._original_bindings = dict(keybinds.bindings)
        self._save_patch = mock.patch.object(gameProfile, "save_profile", return_value=True)
        self._save_patch.start()

    def tearDown(self):
        self._save_patch.stop()
        keybinds.bindings.clear()
        keybinds.bindings.update(self._original_bindings)

    def test_set_binding_updates_the_action(self):
        keybinds.set_binding("dash", pg.K_LSHIFT)
        self.assertEqual(keybinds.key_for("dash"), pg.K_LSHIFT)

    def test_set_binding_clears_any_other_action_using_that_key(self):
        keybinds.set_binding("move_up", pg.K_UP)
        keybinds.set_binding("dash", pg.K_UP)
        self.assertIsNone(keybinds.key_for("move_up"))
        self.assertEqual(keybinds.key_for("dash"), pg.K_UP)

    def test_set_binding_refuses_escape(self):
        original = keybinds.key_for("dash")
        keybinds.set_binding("dash", pg.K_ESCAPE)
        self.assertEqual(keybinds.key_for("dash"), original)

    def test_clear_binding_unbinds_the_action(self):
        keybinds.clear_binding("autofire")
        self.assertIsNone(keybinds.key_for("autofire"))

    def test_pressed_reads_from_key_pressed_set(self):
        keybinds.set_binding("dash", pg.K_LCTRL)
        original = vH.keyPressed
        try:
            vH.keyPressed = {pg.K_LCTRL}
            self.assertTrue(keybinds.pressed("dash"))
            vH.keyPressed = set()
            self.assertFalse(keybinds.pressed("dash"))
        finally:
            vH.keyPressed = original

    def test_pressed_is_false_when_unbound(self):
        keybinds.clear_binding("dash")
        original = vH.keyPressed
        try:
            vH.keyPressed = {pg.K_SPACE}
            self.assertFalse(keybinds.pressed("dash"))
        finally:
            vH.keyPressed = original

    def test_held_reads_from_keys_array(self):
        keybinds.set_binding("move_up", pg.K_UP)
        original_keys = vH.keys
        try:
            vH.keys = type("HeldKeys", (), {
                "__getitem__": lambda self, requested: requested == pg.K_UP,
            })()
            self.assertTrue(keybinds.held("move_up"))
            self.assertFalse(keybinds.held("move_down"))
        finally:
            vH.keys = original_keys

    def test_held_is_false_when_unbound_without_indexing_none(self):
        keybinds.clear_binding("move_up")
        original_keys = vH.keys
        try:
            vH.keys = type("Boom", (), {
                "__getitem__": lambda self, requested: (_ for _ in ()).throw(
                    AssertionError("should not index with None")),
            })()
            self.assertFalse(keybinds.held("move_up"))
        finally:
            vH.keys = original_keys

    def test_set_binding_persists_to_profile(self):
        keybinds.set_binding("dash", pg.K_LSHIFT)
        self.assertEqual(gameProfile.profile["keybinds"]["dash"], pg.K_LSHIFT)


if __name__ == "__main__":
    unittest.main()
