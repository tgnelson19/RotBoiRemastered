import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame as pg

import background as bG
import main as game
import variableHolster as vH


class CameraInputTests(unittest.TestCase):
    def setUp(self):
        self.original_state = vH.state
        self.original_keys = vH.keys
        self.original_delta = vH.deltaMilliseconds
        self.original_angle = bG.cameraAngleDegrees
        vH.state = vH.States.GAMERUN
        vH.deltaMilliseconds = 1000 / 120

    def tearDown(self):
        vH.state = self.original_state
        vH.keys = self.original_keys
        vH.deltaMilliseconds = self.original_delta
        bG.set_camera_angle(self.original_angle)

    def _held_keys(self, key):
        return type("HeldKeys", (), {
            "__getitem__": lambda self, requested: requested == key,
        })()

    def test_holding_e_rotates_counter_clockwise_at_180_degrees_per_second(self):
        bG.set_camera_angle(0)
        vH.keys = self._held_keys(pg.K_e)
        for _ in range(120):
            game.update_camera_controls()
        self.assertAlmostEqual(bG.cameraAngleDegrees, 180)

    def test_holding_q_rotates_clockwise(self):
        bG.set_camera_angle(0)
        vH.keys = self._held_keys(pg.K_q)
        game.update_camera_controls()
        self.assertAlmostEqual(bG.cameraAngleDegrees, 358.5)

    def test_opposite_keys_cancel_each_other(self):
        bG.set_camera_angle(27)
        vH.keys = type("BothKeys", (), {
            "__getitem__": lambda self, requested: requested in (pg.K_q, pg.K_e),
        })()
        game.update_camera_controls()
        self.assertAlmostEqual(bG.cameraAngleDegrees, 27)


if __name__ == "__main__":
    unittest.main()
