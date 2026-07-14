import unittest
import variableHolster as vH


class FrameRateScalingTests(unittest.TestCase):
    def test_get_frame_scale_uses_reference_fps(self):
        original_frame_rate = vH.frameRate

        try:
            vH.frameRate = 120
            self.assertAlmostEqual(vH.get_frame_scale(), 2.0)

            vH.frameRate = 360
            self.assertAlmostEqual(vH.get_frame_scale(), 2/3)
        finally:
            vH.frameRate = original_frame_rate


if __name__ == "__main__":
    unittest.main()
