import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame

import background as bG
import items
from lootCrate import LootCrate
import uiTheme as ui
import variableHolster as vH


class LootCrateTests(unittest.TestCase):
    def test_world_rect_matches_world_position_and_size(self):
        crate = LootCrate(120, 340, items.generate_drops(1, rng=__import__("random").Random(1)))
        rect = crate._world_rect()
        self.assertEqual((rect.x, rect.y), (120, 340))
        self.assertEqual(rect.width, crate.size)
        self.assertEqual(rect.height, crate.size)

    def test_tint_picks_the_highest_rarity_color(self):
        common = items.ItemDrop(items.DEFINITIONS[0], "Common")
        legendary = items.ItemDrop(items.DEFINITIONS[1], "Legendary")
        crate = LootCrate(0, 0, [common, legendary])
        self.assertEqual(crate._tint(), ui.RARITY_COLORS["Legendary"])

    def test_draw_renders_visible_pixels_inside_crate_rect(self):
        original_screen = vH.screen
        try:
            vH.screen = pygame.Surface((220, 220))
            world_x, world_y = bG.screen_to_world(100, 100)
            crate = LootCrate(world_x, world_y, [items.ItemDrop(items.DEFINITIONS[0], "Rare")])
            crate.draw()
            colors = {vH.screen.get_at((x, y))[:3]
                      for x in range(int(crate.posX), int(crate.posX + crate.size))
                      for y in range(int(crate.posY), int(crate.posY + crate.size))}
            self.assertIn(ui.INK[:3], colors)
            self.assertIn(ui.RARITY_COLORS["Rare"][:3], colors)
        finally:
            vH.screen = original_screen


if __name__ == "__main__":
    unittest.main()
