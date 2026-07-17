from copy import deepcopy
from types import SimpleNamespace
from unittest import mock
import unittest

import gameProfile
import statusEffects


class FakeEnemy:
    def __init__(self, hp=1000):
        self.hp = hp
        self.maxHp = hp

    def take_damage(self, amount, part_id="body"):
        self.hp -= amount


class StatusEffectTests(unittest.TestCase):
    def setUp(self):
        self.original = deepcopy(gameProfile.profile)
        gameProfile.profile.clear()
        gameProfile.profile.update(deepcopy(gameProfile.DEFAULTS))

    def tearDown(self):
        gameProfile.profile.clear()
        gameProfile.profile.update(self.original)

    def test_poison_deals_damage_over_time(self):
        enemy = FakeEnemy()
        statusEffects.apply(enemy, "poison", 3, .01)
        statusEffects.update(enemy, 1.0)
        self.assertLess(enemy.hp, enemy.maxHp)

    def test_slow_daze_and_stun_return_control_state(self):
        enemy = FakeEnemy()
        statusEffects.apply(enemy, "slow", 2, .25)
        statusEffects.apply(enemy, "daze", 2, .3)
        statusEffects.apply(enemy, "stun", 1, 1)
        control = statusEffects.update(enemy, .1)
        self.assertLess(control.movement_multiplier, 1)
        self.assertGreater(control.attack_delay, 0)
        self.assertTrue(control.stunned)

    def test_poison_daze_synergy_increases_hit_damage(self):
        enemy = FakeEnemy()
        statusEffects.apply(enemy, "poison", 2, .01)
        statusEffects.apply(enemy, "daze", 2, .2)
        self.assertGreater(statusEffects.damage_multiplier(enemy, SimpleNamespace(currCrit=False)), 1)


if __name__ == "__main__":
    unittest.main()
