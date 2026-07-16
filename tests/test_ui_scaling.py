import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame

from informationSheet import InformationSheet
import uiTheme as ui
import variableHolster as vH


class UIScalingTests(unittest.TestCase):
    def setUp(self):
        self.original_screen = vH.screen

    def tearDown(self):
        vH.screen = self.original_screen

    def test_reference_resolution_uses_one_to_one_ui_scale(self):
        self.assertEqual(ui.display_scale(pygame.Surface((1920, 1080))), 1.0)

    def test_scale_is_height_limited_on_ultrawide_displays(self):
        standard = ui.display_scale(pygame.Surface((1920, 1080)))
        ultrawide = ui.display_scale(pygame.Surface((2560, 1080)))
        self.assertEqual(ultrawide, standard)

    def test_sidebar_remains_bounded_across_common_aspect_ratios(self):
        for resolution in ((1024, 768), (1280, 720), (1920, 1080),
                           (2560, 1080), (3440, 1440), (3840, 2160)):
            with self.subTest(resolution=resolution):
                vH.screen = pygame.Surface(resolution)
                sheet = InformationSheet()
                self.assertGreaterEqual(sheet.arena_width, resolution[0] * .58)
                self.assertLessEqual(sheet.totalLength, resolution[0] * .42)
                self.assertEqual(sheet.totalHeight, resolution[1])


if __name__ == "__main__":
    unittest.main()
