import os
import unittest
from unittest import mock

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame as pg

import character as game
import gameProfile
import keybinds
import menus
import variableHolster as vH


class KeybindRebindUITests(unittest.TestCase):
    def setUp(self):
        self._original_bindings = dict(keybinds.bindings)
        self._save_patch = mock.patch.object(gameProfile, "save_profile", return_value=True)
        self._save_patch.start()

        game.resetAllStats()
        vH.state = vH.States.PAUSED
        vH.pauseReturnState = vH.States.GAMERUN
        menus._settings_tab = "keybinds"
        menus._rebinding_action = None
        vH.mouseX, vH.mouseY = -100, -100
        vH.mouseDown = False
        vH.mousePressed = False
        vH.keyPressed = set()

    def tearDown(self):
        menus._rebinding_action = None
        self._save_patch.stop()
        keybinds.bindings.clear()
        keybinds.bindings.update(self._original_bindings)

    def _click_keybind_row(self, action_id):
        menus.draw_pause()
        rect = menus._buttons[f"keybind_{action_id}"]
        vH.mouseX, vH.mouseY = rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        menus.handle_pause()
        vH.mousePressed = False
        vH.mouseDown = False

    def test_clicking_a_row_enters_listening_mode(self):
        self._click_keybind_row("dash")
        self.assertEqual(menus._rebinding_action, "dash")

    def test_pressing_a_key_while_listening_rebinds_the_action(self):
        self._click_keybind_row("dash")
        vH.keyPressed = {pg.K_LSHIFT}
        menus.handle_pause()

        self.assertIsNone(menus._rebinding_action)
        self.assertEqual(keybinds.key_for("dash"), pg.K_LSHIFT)

    def test_pressing_escape_while_listening_clears_instead_of_binding(self):
        self._click_keybind_row("dash")
        vH.keyPressed = {pg.K_ESCAPE}
        menus.handle_pause()

        self.assertIsNone(menus._rebinding_action)
        self.assertIsNone(keybinds.key_for("dash"))

    def test_escape_while_listening_does_not_also_resume_the_game(self):
        self._click_keybind_row("hud_toggle")
        vH.keyPressed = {pg.K_ESCAPE}
        menus.handle_pause()

        self.assertEqual(vH.state, vH.States.PAUSED)

    def test_rebinding_to_a_key_already_used_elsewhere_clears_the_other_action(self):
        keybinds.set_binding("move_up", pg.K_UP)
        self._click_keybind_row("dash")
        vH.keyPressed = {pg.K_UP}
        menus.handle_pause()

        self.assertEqual(keybinds.key_for("dash"), pg.K_UP)
        self.assertIsNone(keybinds.key_for("move_up"))


if __name__ == "__main__":
    unittest.main()
