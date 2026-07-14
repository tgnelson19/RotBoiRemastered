import unittest
import characterStats as cS
import variableHolster as vH


class DashTuningTests(unittest.TestCase):
    def test_dash_defaults_are_less_aggressive(self):
        self.assertLessEqual(cS.dashModifier, 4)
        self.assertLessEqual(cS.dashDuration, vH.frameRate * 0.15)


if __name__ == "__main__":
    unittest.main()
