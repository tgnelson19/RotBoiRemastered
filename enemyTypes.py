"""Enemy catalog and special enemy implementations.

Adding a regular enemy requires one ``EnemyDefinition`` registration. More complex
enemies subclass ``Enemy`` while preserving the same update, hitbox, damage, and
projectile contracts used by combat.
"""

from dataclasses import dataclass, field
from math import atan2, cos, hypot, pi, sin
import random

import pygame

import background as bG
from enemy import Enemy, HitResult
from enemyProjectile import EnemyProjectile
import uiTheme as ui
import variableHolster as vH


BASE_ENEMY_SPEED_SCALE = .66


def _normalise(x, y):
    length = max(1.0, hypot(x, y))
    return x / length, y / length, length


class WanderingRangedEnemy(Enemy):
    attackRangeTiles = 10
    awarenessRangeTiles = 17

    def __init__(self, *args, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        self.wanderAngle = self.rng.uniform(-pi, pi)
        self.wanderTimer = self.rng.randint(45, 110)
        self.attackCooldown = self.rng.randint(30, 90)
        self.attackCooldownMax = vH.frameRate * 1.45

    def _move(self, direction_x, direction_y, speed_multiplier):
        step = self.speed * speed_multiplier * vH.get_frame_scale()
        self._try_axis_move(direction_x * step, "x")
        self._try_axis_move(direction_y * step, "y")
        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        direction = atan2(player_world_y - center_y, player_world_x - center_x)
        projectile_size = max(12, self.size * .4)
        projectile_sink.append(EnemyProjectile(
            center_x - projectile_size / 2,
            center_y - projectile_size / 2,
            direction,
            speed=1.4,
            damage=self.damage * .72,
            size=projectile_size,
            travel_range=vH.tileSizeGlobal * 16,
            color=ui.RED,
            shape="diamond",
        ))

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        self.wanderTimer -= vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.size / 2)
        delta_y = player_world_y - (self.worldY + self.size / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)

        attack_range = vH.tileSizeGlobal * self.attackRangeTiles
        awareness_range = vH.tileSizeGlobal * self.awarenessRangeTiles
        if distance <= awareness_range:
            move_multiplier = .16 if distance <= attack_range else .48
            self._move(direction_x, direction_y, move_multiplier)
            if distance <= attack_range and self.attackCooldown <= 0:
                self._fire(player_world_x, player_world_y, projectile_sink)
                self.attackCooldown = self.attackCooldownMax * self.rng.uniform(.85, 1.2)
        else:
            if self.wanderTimer <= 0:
                self.wanderAngle += self.rng.uniform(-1.4, 1.4)
                self.wanderTimer = self.rng.randint(55, 135)
            self._move(cos(self.wanderAngle), sin(self.wanderAngle), .2)

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        pygame.draw.rect(screen, ui.INK, rect.inflate(-int(self.size * .35), -int(self.size * .35)))
        pygame.draw.rect(screen, ui.RED, rect.inflate(-int(self.size * .58), -int(self.size * .58)))


class ShotgunEnemy(WanderingRangedEnemy):
    attackRangeTiles = 8

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.attackCooldownMax = vH.frameRate * 2.35

    def _fire(self, player_world_x, player_world_y, projectile_sink):
        center_x = self.worldX + self.size / 2
        center_y = self.worldY + self.size / 2
        base_direction = atan2(player_world_y - center_y, player_world_x - center_x)
        pellet_count = self.rng.randint(4, 7)
        spread = self.rng.uniform(.48, .78)
        for index in range(pellet_count):
            fraction = 0 if pellet_count == 1 else index / (pellet_count - 1)
            direction = base_direction - spread / 2 + spread * fraction + self.rng.uniform(-.055, .055)
            projectile_size = self.size * self.rng.uniform(.26, .46)
            projectile_sink.append(EnemyProjectile(
                center_x - projectile_size / 2,
                center_y - projectile_size / 2,
                direction,
                speed=self.rng.uniform(1.0, 1.9),
                damage=self.damage * self.rng.uniform(.28, .52),
                size=projectile_size,
                travel_range=vH.tileSizeGlobal * self.rng.uniform(7, 11),
                color=ui.GOLD,
            ))

    def drawEnemy(self, screen):
        super().drawEnemy(screen)
        rect = pygame.Rect(self.posX, self.posY, self.size, self.size)
        for offset in (-.22, 0, .22):
            y = rect.centery + int(rect.height * offset)
            pygame.draw.line(screen, ui.GOLD, (rect.x + rect.width * .25, y), (rect.right - rect.width * .18, y), max(2, int(self.size * .07)))


class SnakeEnemy(Enemy):
    def __init__(self, *args, segment_count=5, segment_hp=None, rng=None, **kwargs):
        super().__init__(*args, **kwargs)
        self.rng = rng or random
        body_size = self.size
        self.headSize = body_size * 1.16
        self.segmentSize = body_size * .76
        self.size = self.headSize
        self.segmentSpacing = self.segmentSize * 1.18
        self.segments = []
        segment_hp = segment_hp or self.maxHp * .6
        for index in range(segment_count):
            segment_x = self.worldX - (index + 1) * self.segmentSpacing
            segment_y = self.worldY + self.size * .2
            safe = bG.find_nearest_open_rect(pygame.Rect(segment_x, segment_y, self.segmentSize, self.segmentSize), self.segmentSize)
            self.segments.append({
                "id": index,
                "x": float(safe.x),
                "y": float(safe.y),
                "hp": segment_hp,
                "max_hp": segment_hp,
            })
        self.attackCooldown = vH.frameRate * self.rng.uniform(1.2, 2.2)

    def updateEnemy(self, player_world_x, player_world_y, projectile_sink=None):
        projectile_sink = projectile_sink if projectile_sink is not None else []
        self.age += vH.get_timer_step()
        self.attackCooldown -= vH.get_timer_step()
        delta_x = player_world_x - (self.worldX + self.headSize / 2)
        delta_y = player_world_y - (self.worldY + self.headSize / 2)
        direction_x, direction_y, distance = _normalise(delta_x, delta_y)
        weave = sin(self.age * .075) * .32
        direction_x, direction_y = direction_x - direction_y * weave, direction_y + direction_x * weave
        direction_x, direction_y, _ = _normalise(direction_x, direction_y)
        step = self.speed * vH.get_frame_scale()
        self._try_axis_move(direction_x * step, "x")
        self._try_axis_move(direction_y * step, "y")

        previous_x = self.worldX + (self.headSize - self.segmentSize) / 2
        previous_y = self.worldY + (self.headSize - self.segmentSize) / 2
        for segment in self.segments:
            follow_x = previous_x - segment["x"]
            follow_y = previous_y - segment["y"]
            follow_distance = hypot(follow_x, follow_y)
            if follow_distance > self.segmentSpacing:
                amount = (follow_distance - self.segmentSpacing) / follow_distance
                segment["x"] += follow_x * amount
                segment["y"] += follow_y * amount
            previous_x, previous_y = segment["x"], segment["y"]

        if distance <= vH.tileSizeGlobal * 15 and self.attackCooldown <= 0:
            center_x = self.worldX + self.headSize / 2
            center_y = self.worldY + self.headSize / 2
            projectile_size = self.segmentSize * .54
            projectile_sink.append(EnemyProjectile(
                center_x - projectile_size / 2,
                center_y - projectile_size / 2,
                atan2(player_world_y - center_y, player_world_x - center_x),
                speed=.9,
                damage=self.damage * .65,
                size=projectile_size,
                travel_range=vH.tileSizeGlobal * 14,
                color=ui.PURPLE,
                shape="diamond",
            ))
            self.attackCooldown = vH.frameRate * self.rng.uniform(2.0, 3.15)

        self.posX, self.posY = bG.world_to_screen(self.worldX, self.worldY)

    def drawEnemy(self, screen):
        for segment in reversed(self.segments):
            x, y = bG.world_to_screen(segment["x"], segment["y"])
            rect = pygame.Rect(x, y, self.segmentSize, self.segmentSize)
            pygame.draw.rect(screen, ui.SHADOW, rect.move(3, 3))
            pygame.draw.rect(screen, pygame.Color(72, 145, 104), rect)
            pygame.draw.rect(screen, ui.INK, rect, max(2, int(self.segmentSize * .09)))
            if segment["hp"] < segment["max_hp"]:
                fill = pygame.Rect(rect.x, rect.y - 6, rect.width * segment["hp"] / segment["max_hp"], 4)
                pygame.draw.rect(screen, ui.GREEN, fill)

        head_rect = pygame.Rect(self.posX, self.posY, self.headSize, self.headSize)
        pygame.draw.rect(screen, ui.SHADOW, head_rect.move(5, 5))
        pygame.draw.rect(screen, ui.PURPLE, head_rect)
        pygame.draw.rect(screen, ui.INK, head_rect, max(3, int(self.headSize * .09)))
        eye_size = max(3, int(self.headSize * .12))
        pygame.draw.rect(screen, ui.TEXT, (head_rect.x + head_rect.width * .25, head_rect.y + head_rect.height * .27, eye_size, eye_size))
        pygame.draw.rect(screen, ui.TEXT, (head_rect.right - head_rect.width * .25 - eye_size, head_rect.y + head_rect.height * .27, eye_size, eye_size))
        if self.segments:
            pygame.draw.rect(screen, ui.CREAM, head_rect.inflate(8, 8), max(2, int(self.headSize * .06)))

    def get_screen_hitboxes(self):
        hitboxes = [("head", pygame.Rect(self.posX, self.posY, self.headSize, self.headSize))]
        for segment in self.segments:
            x, y = bG.world_to_screen(segment["x"], segment["y"])
            hitboxes.append((segment["id"], pygame.Rect(x, y, self.segmentSize, self.segmentSize)))
        return hitboxes

    def get_world_hitboxes(self):
        hitboxes = [("head", pygame.Rect(self.worldX, self.worldY, self.headSize, self.headSize))]
        hitboxes.extend((segment["id"], pygame.Rect(segment["x"], segment["y"], self.segmentSize, self.segmentSize)) for segment in self.segments)
        return hitboxes

    def take_damage(self, amount, part_id="head"):
        if part_id == "head":
            if self.segments:
                return HitResult(False, False, 0, True)
            self.hp -= amount
            return HitResult(True, self.hp <= 0, amount)

        segment = next((item for item in self.segments if item["id"] == part_id), None)
        if segment is None:
            return HitResult(False, False)
        segment["hp"] -= amount
        if segment["hp"] <= 0:
            self.segments.remove(segment)
        return HitResult(True, False, amount)


@dataclass(frozen=True)
class EnemyDefinition:
    key: str
    enemy_class: type
    weight: float
    min_level: int
    speed: float
    size: float
    damage: float
    health: float
    experience: float
    color: pygame.Color
    options: dict = field(default_factory=dict)


class EnemyCatalog:
    def __init__(self):
        self.definitions = {}

    def register(self, definition):
        if definition.key in self.definitions:
            raise ValueError(f"Enemy type already registered: {definition.key}")
        self.definitions[definition.key] = definition

    def available(self, level):
        return [definition for definition in self.definitions.values() if level >= definition.min_level]

    def choose(self, level, rng=None):
        rng = rng or random
        available = self.available(level)
        return rng.choices(available, weights=[item.weight for item in available], k=1)[0]

    def create(self, key, world_x, world_y, level, rng=None):
        rng = rng or random
        definition = self.definitions[key]
        level_scale = 1.08 ** level
        variation = rng.uniform(.9, 1.12)
        difficulty = rng.uniform(.92, 1.25)
        size = vH.tileSizeGlobal * definition.size / variation
        options = dict(definition.options)
        options.setdefault("archetype", definition.key)
        if definition.enemy_class is not Enemy:
            options["rng"] = rng
        if definition.enemy_class is SnakeEnemy:
            options.pop("archetype", None)
            options["segment_count"] = min(8, 5 + level // 3)
        return definition.enemy_class(
            world_x, world_y,
            BASE_ENEMY_SPEED_SCALE * level_scale * definition.speed * variation,
            size,
            definition.color,
            .9 * level_scale * definition.damage / variation,
            2.2 * level_scale * definition.health / variation,
            2.4 * level_scale * definition.experience * difficulty,
            difficulty,
            **options,
        )

    def spawn(self, level, rng=None, key=None):
        rng = rng or random
        definition = self.definitions[key] if key else self.choose(level, rng)
        # Find a fitting spawn using the definition's nominal body size.
        nominal_size = vH.tileSizeGlobal * definition.size
        spawn_rect = bG.find_spawn_rect(nominal_size)
        return self.create(definition.key, spawn_rect.x, spawn_rect.y, level, rng)


ENEMY_CATALOG = EnemyCatalog()


def _register_defaults():
    entries = (
        EnemyDefinition("runner", Enemy, 22, 0, 1.42, .58, .72, .62, .8, pygame.Color(221, 76, 73)),
        EnemyDefinition("drifter", Enemy, 30, 0, 1.0, .76, 1.0, 1.0, 1.0, pygame.Color(184, 66, 75)),
        EnemyDefinition("skirmisher", Enemy, 18, 0, 1.08, .82, .92, 1.18, 1.3, pygame.Color(68, 151, 142)),
        EnemyDefinition("bulwark", Enemy, 12, 0, .58, 1.18, 1.52, 2.65, 2.1, pygame.Color(200, 132, 56)),
        EnemyDefinition("ranged_wanderer", WanderingRangedEnemy, 10, 0, .62, .82, .85, 1.45, 1.7, pygame.Color(82, 126, 190)),
        EnemyDefinition("shotgunner", ShotgunEnemy, 5, 0, .56, .96, 1.0, 2.0, 2.4, pygame.Color(188, 112, 61)),
        EnemyDefinition("snake", SnakeEnemy, 3, 0, .92, .86, 1.15, 2.15, 5.2, pygame.Color(142, 83, 184)),
    )
    for definition in entries:
        ENEMY_CATALOG.register(definition)


_register_defaults()
