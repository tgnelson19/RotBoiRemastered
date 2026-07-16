import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import background as bG
import variableHolster as vH


class CameraRotationTests(unittest.TestCase):
    def setUp(self):
        self.original_position = bG.playerPosX, bG.playerPosY
        self.original_angle = bG.cameraAngleDegrees
        self.original_shake = vH.screenShakeX, vH.screenShakeY
        bG.playerPosX, bG.playerPosY = 500, 650
        vH.screenShakeX = vH.screenShakeY = 0

    def tearDown(self):
        bG.playerPosX, bG.playerPosY = self.original_position
        vH.screenShakeX, vH.screenShakeY = self.original_shake
        bG.set_camera_angle(self.original_angle)

    def test_e_turn_projects_world_right_toward_screen_up(self):
        bG.set_camera_quarter_turns(1)
        screen_x, screen_y = bG.world_to_screen(600, 650)
        self.assertAlmostEqual(screen_x, bG.lockX)
        self.assertAlmostEqual(screen_y, bG.lockY - 100)

    def test_q_turn_projects_world_right_toward_screen_down(self):
        bG.set_camera_quarter_turns(-1)
        screen_x, screen_y = bG.world_to_screen(600, 650)
        self.assertAlmostEqual(screen_x, bG.lockX)
        self.assertAlmostEqual(screen_y, bG.lockY + 100)

    def test_screen_and_world_conversion_are_inverses_at_every_turn(self):
        for angle in (0, 17.5, 45, 93, 181.25, 319):
            bG.set_camera_angle(angle)
            screen_point = bG.world_to_screen(731.5, 412.25)
            world_point = bG.screen_to_world(*screen_point)
            self.assertAlmostEqual(world_point[0], 731.5)
            self.assertAlmostEqual(world_point[1], 412.25)

    def test_screen_relative_movement_rotates_back_to_world(self):
        bG.set_camera_quarter_turns(1)
        # At the E orientation, screen-right lies along world-down.
        world_x, world_y = bG.screen_vector_to_world(10, 0)
        self.assertAlmostEqual(world_x, 0)
        self.assertAlmostEqual(world_y, 10)

    def test_arbitrary_angle_rotates_fluidly(self):
        bG.set_camera_angle(45)
        screen_x, screen_y = bG.world_to_screen(600, 650)
        self.assertAlmostEqual(screen_x, bG.lockX + 70.710678, places=5)
        self.assertAlmostEqual(screen_y, bG.lockY - 70.710678, places=5)

    def test_degree_rotation_wraps_cleanly(self):
        bG.set_camera_angle(355)
        bG.rotate_camera(10)
        self.assertAlmostEqual(bG.cameraAngleDegrees, 5)

    def test_raised_wall_height_always_points_screen_up(self):
        for angle in (0, 27, 90, 146, 233, 315):
            bG.set_camera_angle(angle)
            ground, cap = bG._wall_screen_geometry(10, 12, bG.WALL_HEIGHT)
            for ground_point, cap_point in zip(ground, cap):
                self.assertAlmostEqual(cap_point[0], ground_point[0])
                self.assertAlmostEqual(
                    cap_point[1], ground_point[1] - bG.WALL_HEIGHT,
                )

    def test_decorations_remain_axis_aligned_at_every_camera_angle(self):
        for angle in (0, 27, 90, 146, 233, 315):
            bG.set_camera_angle(angle)
            rect = bG._decoration_screen_rect(10, 12)
            anchor = bG.world_to_screen(
                10.5 * vH.tileSizeGlobal, 12.5 * vH.tileSizeGlobal,
            )
            self.assertEqual(rect.size, (vH.tileSizeGlobal, vH.tileSizeGlobal))
            self.assertAlmostEqual(rect.centerx, round(anchor[0]), delta=1)
            self.assertAlmostEqual(rect.centery, round(anchor[1]), delta=1)


if __name__ == "__main__":
    unittest.main()
