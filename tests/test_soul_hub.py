import unittest

import background as bG
import gamePaths
import soulHub
import variableHolster as vH


class SoulHubMapTests(unittest.TestCase):
    def test_soul_hub_is_compact_and_spawn_is_open(self):
        room = bG.generate_soul_battleground()
        self.assertEqual((len(room), len(room[0])), (33, 45))
        center = room[len(room)//2][len(room[0])//2][0]
        self.assertNotIn(center, bG.SOLID_TILES)

    def test_soul_hub_has_walls_and_walkable_tiles(self):
        room = bG.generate_soul_battleground()
        tiles = {cell[0] for row in room for cell in row}
        self.assertTrue(bG.SOLID_TILES.intersection(tiles))
        self.assertTrue({0, 2, 3}.intersection(tiles))

    def test_hub_can_enter_and_render_a_frame(self):
        vH.keyPressed.clear()
        vH.mouseDown = False
        vH.mousePressed = False
        try:
            soulHub.enter()
            soulHub.run()
            self.assertEqual(len(bG.currRoomRects), 33)
        finally:
            bG.configure_battleground(gamePaths.active_key)


if __name__ == "__main__":
    unittest.main()
