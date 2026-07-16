import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame

import background as bG
from experienceBubble import ExperienceBubble
import uiTheme as ui
import variableHolster as vH


class ExperienceVisualTests(unittest.TestCase):
    def test_experience_has_animated_gold_orbit_and_bright_core(self):
        original_screen = vH.screen
        try:
            vH.screen = pygame.Surface((220, 220))
            world_x, world_y = bG.screen_to_world(100, 100)
            bubble = ExperienceBubble(world_x, world_y, 1, 1, vH.frameRate)
            bubble.updateBubble(0, 0, 0)

            self.assertEqual(bubble.pickupKind, "experience")
            self.assertGreater(bubble.visualAge, 0)
            colors = {vH.screen.get_at((x, y))[:3]
                      for x in range(70, 140) for y in range(70, 140)}
            self.assertIn(ui.GOLD[:3], colors)
            self.assertIn(ui.TEXT[:3], colors)
        finally:
            vH.screen = original_screen


if __name__ == "__main__":
    unittest.main()
