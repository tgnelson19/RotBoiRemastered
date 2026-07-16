import os
import unittest

os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

import pygame

import background as bG
import character as game
import characterStats as cS
import items
from lootCrate import LootCrate
import main
import variableHolster as vH


class EquipmentResetTests(unittest.TestCase):
    def test_reset_clears_equipment_and_loot_crates(self):
        cS.equipment["weapon"] = items.ItemDrop(items.DEFINITIONS[0], "Common")
        cS.lootCrateList.append(LootCrate(0, 0, items.generate_drops(1)))

        game.resetAllStats()

        self.assertEqual(cS.equipment, {
            "weapon": None, "armor": None, "ring": None,
            "accessory_1": None, "accessory_2": None,
        })
        self.assertEqual(cS.lootCrateList, [])
        self.assertFalse(vH.dragInProgress)


class EquipmentDragTests(unittest.TestCase):
    def setUp(self):
        game.resetAllStats()
        vH.mouseDown = False
        vH.mousePressed = False

    def tearDown(self):
        main._cancel_drag()
        vH.mouseDown = False
        vH.mousePressed = False

    def test_dragging_equipped_item_to_empty_space_unequips_to_new_crate(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        cS.equipment["weapon"] = drop

        sheet.drawSheet()
        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertTrue(vH.dragInProgress)
        self.assertEqual(sheet.dragging_source, ("equipment", "weapon"))

        vH.mousePressed = False
        vH.mouseX = weapon_rect.centerx + 500
        vH.mouseY = weapon_rect.centery + 500
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertIsNone(cS.equipment["weapon"])
        self.assertFalse(vH.dragInProgress)
        self.assertEqual(len(cS.lootCrateList), 1)
        self.assertEqual(cS.lootCrateList[0].items, [drop])
        self.assertEqual((cS.lootCrateList[0].worldX, cS.lootCrateList[0].worldY),
                         (bG.playerPosX, bG.playerPosY))

    def test_unequip_while_standing_over_a_crate_fills_its_next_slot(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        cS.equipment["weapon"] = drop
        existing = items.ItemDrop(items.DEFINITIONS_BY_NAME["Copper Ring"], "Rare")
        crate = LootCrate(bG.playerPosX, bG.playerPosY, [existing])
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()
        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()

        vH.mousePressed = False
        vH.mouseX = weapon_rect.centerx + 500
        vH.mouseY = weapon_rect.centery + 500
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertIsNone(cS.equipment["weapon"])
        self.assertEqual(len(cS.lootCrateList), 1)  # no new crate spawned
        self.assertEqual(crate.items, [existing, drop])

    def test_unequip_while_standing_over_a_full_crate_spawns_a_new_one(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        cS.equipment["weapon"] = drop
        full_items = items.generate_drops(sheet.CRATE_SLOT_COUNT, rng=__import__("random").Random(3))
        crate = LootCrate(bG.playerPosX, bG.playerPosY, full_items)
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()
        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()

        vH.mousePressed = False
        vH.mouseX = weapon_rect.centerx + 500
        vH.mouseY = weapon_rect.centery + 500
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertIsNone(cS.equipment["weapon"])
        self.assertEqual(crate.items, full_items)  # untouched, still full
        self.assertEqual(len(cS.lootCrateList), 2)  # a fresh crate for the unequipped item
        new_crate = next(c for c in cS.lootCrateList if c is not crate)
        self.assertEqual(new_crate.items, [drop])
        self.assertEqual((new_crate.worldX, new_crate.worldY), (bG.playerPosX, bG.playerPosY))

    def test_dragging_loot_item_onto_matching_slot_equips_it(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Copper Ring"], "Rare")
        crate = LootCrate(bG.playerPosX, bG.playerPosY, [drop])
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()
        loot_rect = sheet._loot_panel_slot_rects[0]
        vH.mouseX, vH.mouseY = loot_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertTrue(vH.dragInProgress)

        ring_rect = sheet._equipment_slot_rects["ring"]
        vH.mousePressed = False
        vH.mouseX, vH.mouseY = ring_rect.center
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertEqual(cS.equipment["ring"], drop)
        self.assertNotIn(crate, cS.lootCrateList)
        self.assertIsNone(sheet.nearby_crate)

    def test_dropping_onto_mismatched_slot_type_is_rejected(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Copper Ring"], "Rare")
        crate = LootCrate(bG.playerPosX, bG.playerPosY, [drop])
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()
        loot_rect = sheet._loot_panel_slot_rects[0]
        vH.mouseX, vH.mouseY = loot_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()

        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mousePressed = False
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertIsNone(cS.equipment["weapon"])
        self.assertEqual(crate.items, [drop])
        self.assertIn(crate, cS.lootCrateList)

    def test_click_without_moving_does_not_unequip(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Signet Ring"], "Epic")
        cS.equipment["ring"] = drop

        sheet.drawSheet()
        ring_rect = sheet._equipment_slot_rects["ring"]
        vH.mouseX, vH.mouseY = ring_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertTrue(vH.dragInProgress)

        vH.mousePressed = False
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertEqual(cS.equipment["ring"], drop)
        self.assertEqual(cS.lootCrateList, [])
        self.assertFalse(vH.dragInProgress)

    def test_loot_panel_always_shows_four_slots_with_trailing_empties(self):
        sheet = cS.informationSheet
        drops = items.generate_drops(2, rng=__import__("random").Random(4))
        crate = LootCrate(bG.playerPosX, bG.playerPosY, drops)
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()

        self.assertEqual(len(sheet._loot_panel_slot_rects), sheet.CRATE_SLOT_COUNT)
        # The two trailing (empty) slots must not be draggable.
        empty_rect = sheet._loot_panel_slot_rects[2]
        vH.mouseX, vH.mouseY = empty_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertFalse(vH.dragInProgress)
        self.assertIsNone(sheet.dragging_item)

    def test_dragging_onto_occupied_equipment_slot_swaps_items(self):
        sheet = cS.informationSheet
        sword = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        bow = items.ItemDrop(items.DEFINITIONS_BY_NAME["Hunting Bow"], "Rare")
        cS.equipment["weapon"] = sword

        crate = LootCrate(bG.playerPosX, bG.playerPosY, [bow])
        cS.lootCrateList.append(crate)
        sheet.nearby_crate = crate

        sheet.drawSheet()
        loot_rect = sheet._loot_panel_slot_rects[0]
        vH.mouseX, vH.mouseY = loot_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertTrue(vH.dragInProgress)

        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mousePressed = False
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertEqual(cS.equipment["weapon"], bow)
        self.assertEqual(crate.items, [sword])
        self.assertIn(crate, cS.lootCrateList)

    def test_dragging_between_two_equipped_accessory_slots_swaps_items(self):
        sheet = cS.informationSheet
        charm = items.ItemDrop(items.DEFINITIONS_BY_NAME["Lucky Charm"], "Common")
        locket = items.ItemDrop(items.DEFINITIONS_BY_NAME["Old Locket"], "Rare")
        cS.equipment["accessory_1"] = charm
        cS.equipment["accessory_2"] = locket

        sheet.drawSheet()
        acc1_rect = sheet._equipment_slot_rects["accessory_1"]
        vH.mouseX, vH.mouseY = acc1_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()

        acc2_rect = sheet._equipment_slot_rects["accessory_2"]
        vH.mousePressed = False
        vH.mouseX, vH.mouseY = acc2_rect.center
        vH.mouseDown = False
        sheet.drawSheet()

        self.assertEqual(cS.equipment["accessory_1"], locket)
        self.assertEqual(cS.equipment["accessory_2"], charm)
        self.assertEqual(cS.lootCrateList, [])

    def test_dragging_onto_mismatched_equipment_slot_is_rejected(self):
        sheet = cS.informationSheet
        sword = items.ItemDrop(items.DEFINITIONS_BY_NAME["Rusty Sword"], "Common")
        vest = items.ItemDrop(items.DEFINITIONS_BY_NAME["Leather Vest"], "Rare")
        cS.equipment["weapon"] = sword
        cS.equipment["armor"] = vest

        sheet.drawSheet()
        weapon_rect = sheet._equipment_slot_rects["weapon"]
        vH.mouseX, vH.mouseY = weapon_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()

        armor_rect = sheet._equipment_slot_rects["armor"]
        vH.mousePressed = False
        vH.mouseX, vH.mouseY = armor_rect.center
        vH.mouseDown = False
        sheet.drawSheet()

        # weapon/armor require different slot_type -- rejected, not swapped;
        # the dragged sword unequips to a new crate instead.
        self.assertIsNone(cS.equipment["weapon"])
        self.assertEqual(cS.equipment["armor"], vest)
        self.assertEqual(len(cS.lootCrateList), 1)
        self.assertEqual(cS.lootCrateList[0].items, [sword])

    def test_cancel_drag_clears_flags_without_mutating_state(self):
        sheet = cS.informationSheet
        drop = items.ItemDrop(items.DEFINITIONS_BY_NAME["Lucky Charm"], "Epic")
        cS.equipment["accessory_1"] = drop

        sheet.drawSheet()
        slot_rect = sheet._equipment_slot_rects["accessory_1"]
        vH.mouseX, vH.mouseY = slot_rect.center
        vH.mouseDown = True
        vH.mousePressed = True
        sheet.drawSheet()
        self.assertTrue(vH.dragInProgress)

        main._cancel_drag()

        self.assertFalse(vH.dragInProgress)
        self.assertIsNone(sheet.dragging_item)
        self.assertIsNone(sheet.dragging_source)
        self.assertEqual(cS.equipment["accessory_1"], drop)
        self.assertEqual(cS.lootCrateList, [])


if __name__ == "__main__":
    unittest.main()
