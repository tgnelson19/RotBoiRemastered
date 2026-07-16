"""Twenty-level run pacing shared by encounters, enemies, and rewards."""

MID_BOSS_LEVEL = 10
FINAL_BOSS_LEVEL = 20
MAX_LEVEL = FINAL_BOSS_LEVEL

# One world miniboss introduces phase attacks in each half of the run.
MINIBOSS_GATES = (
    (5, "miniboss_arsenal"),
    (15, "miniboss_siege"),
)


def _late_progress(level):
    return max(0.0, min(1.0, (level - MID_BOSS_LEVEL) / 10.0))


def enemy_stat_scales(level):
    """Stretch the old ten-level 1.08 curve across twenty levels.

    The back half adds a controlled health/damage ramp so upgraded builds still
    meet resistance without making movement speeds or early enemies oppressive.
    Experience rises alongside that extra durability.
    """
    level = max(0, min(MAX_LEVEL, level))
    stretched = 1.08 ** (level / 2.0)
    late = _late_progress(level)
    return {
        "speed": stretched,
        "health": stretched * (1.0 + .22 * late),
        "damage": stretched * (1.0 + .12 * late),
        "experience": stretched * (1.0 + .35 * late),
    }


def encounter_caps(level):
    """Raise simultaneous pressure after Beaudis instead of only inflating HP."""
    late_levels = max(0, min(10, level - MID_BOSS_LEVEL))
    return {
        "enemy_cap": 50 + late_levels,
        "threat_cap": 36.0 + late_levels * 1.2,
        "population_threat_cap": 60.0 + late_levels * 1.8,
    }


def encounter_pacing(level):
    """Shape discrete fights rather than continuously accumulating loose enemies."""
    level = max(0, min(MAX_LEVEL, level))
    return {
        # Patrols begin as real encounters rather than pairs, then gain one body
        # every four levels until the final approach to Dissonance.
        "patrol_size": min(9, 5 + max(0, level - 1) // 4),
        # Larger patrols need fewer simultaneous map slots to preserve recovery
        # space and keep the physical cap meaningful.
        "max_world_encounters": min(5, 3 + level // 8),
        "spawn_interval_seconds": max(4.4, 7.0 - level * .13),
        "curated_chance": 0 if level < 5 else min(.48, .28 + (level - 5) * .012),
    }
