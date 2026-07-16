import pygame as pg
import variableHolster as vH
import csv
from math import hypot
from random import randint
import uiTheme as ui

_open_tile_cache = {}

def loadCSVToBG(filename):
    """Loads data from a CSV file into a list of lists (array)."""
    data_array = [] # The background as displayed in the editor is stored as a 2D array of integers, where each integer corresponds to a tile type.
    with open(filename, 'r') as file:
        csv_reader = csv.reader(file)
        for row in csv_reader:
            data_array.append(row)
    return data_array

def loadBackgroundRects(roomFile):
    roomCSV = loadCSVToBG(roomFile)
    newRects = []
    for rowIndex, row in enumerate(roomCSV):
        newRects.append([])
        for colIndex, cell in enumerate(row):
            newRects[rowIndex].append([
                int(cell),
                pg.Rect(colIndex * vH.tileSizeGlobal,
                        rowIndex * vH.tileSizeGlobal,
                        vH.tileSizeGlobal,
                        vH.tileSizeGlobal)
            ])
    return newRects

def drawRepasteableBackground(roomRects):
    newSurface = pg.surface.Surface((len(roomRects[0]) * vH.tileSizeGlobal, len(roomRects) * vH.tileSizeGlobal))
    for rowIndex in range(len(roomRects)):
        for colIndex in range(len(roomRects[0])):
            currRectData = roomRects[rowIndex][colIndex]
            currRectData[1].left = (vH.tileSizeGlobal * colIndex)
            currRectData[1].top = (vH.tileSizeGlobal * rowIndex)
            pg.draw.rect(newSurface, tileTypes[currRectData[0]][1], currRectData[1])
            if currRectData[0] in SOLID_TILES:
                pg.draw.line(newSurface, pg.Color(78, 83, 91), currRectData[1].topleft, currRectData[1].topright, 3)
                pg.draw.line(newSurface, ui.INK, currRectData[1].bottomleft, currRectData[1].bottomright, 3)
                if currRectData[0] == 4:
                    mid_y = currRectData[1].centery
                    pg.draw.line(newSurface, pg.Color(42, 50, 62), (currRectData[1].left, mid_y), (currRectData[1].right, mid_y), 2)
            else:
                pg.draw.rect(newSurface, pg.Color(48, 53, 58), currRectData[1], 1)
    return newSurface

def screen_to_world(screen_x, screen_y):
    """Convert a screen coordinate into a world coordinate."""
    return (screen_x - lockX + playerPosX - vH.screenShakeX,
            screen_y - lockY + playerPosY - vH.screenShakeY)

def world_to_screen(world_x, world_y):
    """Convert a world coordinate into a screen coordinate."""
    return (world_x - playerPosX + lockX + vH.screenShakeX,
            world_y - playerPosY + lockY + vH.screenShakeY)

def rect_hits_wall(world_rect):
    """Return True if any tile overlapped by the world rect is a wall."""
    left = int(world_rect.left // vH.tileSizeGlobal)
    top = int(world_rect.top // vH.tileSizeGlobal)
    right = int((world_rect.right - 1) // vH.tileSizeGlobal)
    bottom = int((world_rect.bottom - 1) // vH.tileSizeGlobal)

    if left < 0 or top < 0 or bottom >= len(currRoomRects) or right >= len(currRoomRects[0]):
        return True

    for tileY in range(top, bottom + 1):
        for tileX in range(left, right + 1):
            if currRoomRects[tileY][tileX][0] in SOLID_TILES:
                return True
    return False


def count_overlapping_walls(world_rect):
    """Return the number of wall tiles overlapped by world_rect.

    Out-of-bounds areas count as a large number so callers treat them as impassable.
    """
    left = int(world_rect.left // vH.tileSizeGlobal)
    top = int(world_rect.top // vH.tileSizeGlobal)
    right = int((world_rect.right - 1) // vH.tileSizeGlobal)
    bottom = int((world_rect.bottom - 1) // vH.tileSizeGlobal)

    if left < 0 or top < 0 or bottom >= len(currRoomRects) or right >= len(currRoomRects[0]):
        return 10**6

    count = 0
    for tileY in range(top, bottom + 1):
        for tileX in range(left, right + 1):
            if currRoomRects[tileY][tileX][0] in SOLID_TILES:
                count += 1
    return count


def find_spawn_rect(size):
    """Return a world-space rect that fits completely inside a random open floor tile."""
    tile_size = vH.tileSizeGlobal
    room_height = len(currRoomRects)
    room_width = len(currRoomRects[0])
    min_distance_tiles = 4
    player_tile_x = int(playerPosX // tile_size)
    player_tile_y = int(playerPosY // tile_size)

    cache_key = (id(currRoomRects), room_width, room_height, tile_size)
    open_tiles = _open_tile_cache.get(cache_key)
    if open_tiles is None:
        open_tiles = tuple(
            (tile_x, tile_y)
            for tile_y in range(1, room_height - 1)
            for tile_x in range(1, room_width - 1)
            if currRoomRects[tile_y][tile_x][0] not in SOLID_TILES
        )
        _open_tile_cache.clear()
        _open_tile_cache[cache_key] = open_tiles

    if not open_tiles:
        return pg.Rect(tile_size, tile_size, size, size)

    # Random probing makes spawn work effectively constant-time even in large rooms.
    # The deterministic full scan is retained only as a rare fallback for crowded maps.
    for require_distance in (True, False):
        for _ in range(min(32, max(1, len(open_tiles)))):
            tile_x, tile_y = open_tiles[randint(0, len(open_tiles) - 1)]
            if require_distance and abs(tile_x - player_tile_x) < min_distance_tiles and abs(tile_y - player_tile_y) < min_distance_tiles:
                continue
            candidate = pg.Rect(
                tile_x * tile_size + (tile_size - size) / 2,
                tile_y * tile_size + (tile_size - size) / 2,
                size,
                size,
            )
            if not rect_hits_wall(candidate):
                return candidate

    for tile_x, tile_y in open_tiles:
        candidate = pg.Rect(
            tile_x * tile_size + (tile_size - size) / 2,
            tile_y * tile_size + (tile_size - size) / 2,
            size,
            size,
        )
        if not rect_hits_wall(candidate):
            return candidate

    return pg.Rect(tile_size, tile_size, size, size)


def find_nearest_open_rect(world_rect, size):
    """Return the nearest world-space rect that does not overlap wall tiles."""
    candidate = world_rect.copy()
    if not rect_hits_wall(candidate):
        return candidate

    tile_size = vH.tileSizeGlobal
    step = max(1, int(tile_size / 8))
    max_distance = max(step, tile_size)
    best_candidate = world_rect.copy()
    best_distance = 10**6

    for offset_x in range(-max_distance, max_distance + 1, step):
        for offset_y in range(-max_distance, max_distance + 1, step):
            if offset_x == 0 and offset_y == 0:
                continue

            candidate = world_rect.copy()
            candidate.x += offset_x
            candidate.y += offset_y
            if not rect_hits_wall(candidate):
                distance = abs(offset_x) + abs(offset_y)
                if distance < best_distance:
                    best_candidate = candidate
                    best_distance = distance

    return best_candidate


def find_path_around_walls(world_rect, desired_dx, desired_dy, size):
    """Try a tiny local search to find a nearby open rect around a blocking wall."""
    candidate_offsets = []
    if desired_dx != 0:
        candidate_offsets.append((int(abs(desired_dx) * 0.5) * (-1 if desired_dx < 0 else 1), 0))
    if desired_dy != 0:
        candidate_offsets.append((0, int(abs(desired_dy) * 0.5) * (-1 if desired_dy < 0 else 1)))
    candidate_offsets.extend([
        (-size, 0),
        (size, 0),
        (0, -size),
        (0, size),
        (-size, -size),
        (-size, size),
        (size, -size),
        (size, size),
    ])

    for offset_x, offset_y in candidate_offsets:
        candidate = world_rect.copy()
        candidate.x += offset_x
        candidate.y += offset_y
        if not rect_hits_wall(candidate):
            return candidate

    return find_nearest_open_rect(world_rect, size)


def moveAndDisplayBackground(surface):
    vH.screen.blit(surface, (-playerPosX + lockX + vH.screenShakeX,
                             -playerPosY + lockY + vH.screenShakeY))

# Example usage:

tileTypes = {
            0 : ["default", pg.Color(42,46,50)],
            1 : ["arena wall", pg.Color(61,65,72)],
            2 : ["road", pg.Color(67,61,52)],
            3 : ["building floor", pg.Color(34,40,49)],
            4 : ["building wall", pg.Color(52,61,75)],
            }

SOLID_TILES = {1, 4}


def _paint_road(grid, start, end, width=1):
    x1, y1 = start
    x2, y2 = end
    steps = max(abs(x2 - x1), abs(y2 - y1), 1)
    for step in range(steps + 1):
        x = round(x1 + (x2 - x1) * step / steps)
        y = round(y1 + (y2 - y1) * step / steps)
        for oy in range(-width, width + 1):
            for ox in range(-width, width + 1):
                if 0 <= y + oy < len(grid) and 0 <= x + ox < len(grid[0]) and grid[y + oy][x + ox] != 1:
                    grid[y + oy][x + ox] = 2


def _paint_building(grid, center_x, center_y, width=11, height=9, vertical_doors=False):
    left = center_x - width // 2
    top = center_y - height // 2
    right = left + width - 1
    bottom = top + height - 1
    for y in range(top, bottom + 1):
        for x in range(left, right + 1):
            grid[y][x] = 4 if x in (left, right) or y in (top, bottom) else 3

    # Every structure has two opposite, two-tile-wide passages.
    if vertical_doors:
        for x in (center_x - 1, center_x):
            grid[top][x] = 2
            grid[bottom][x] = 2
    else:
        for y in (center_y - 1, center_y):
            grid[y][left] = 2
            grid[y][right] = 2


def generate_battleground(size=97):
    """Create a circular arena with roads, a central plaza, and six buildings."""
    size = max(61, size | 1)
    center = size // 2
    radius = center - 2
    grid = [[0 for _ in range(size)] for _ in range(size)]

    for y in range(size):
        for x in range(size):
            distance = hypot(x - center, y - center)
            if distance >= radius - 1:
                grid[y][x] = 1

    layout_scale = size / 97
    buildings = tuple(
        (center + round(offset_x * layout_scale), center + round(offset_y * layout_scale), vertical)
        for offset_x, offset_y, vertical in (
            (-23, -22, False), (23, -22, False),
            (-28, 2, True), (28, 2, True),
            (-18, 26, False), (18, 26, False),
        )
    )
    for building_x, building_y, _ in buildings:
        _paint_road(grid, (center, center), (building_x, building_y), 1)

    for y in range(center - 7, center + 8):
        for x in range(center - 7, center + 8):
            if hypot(x - center, y - center) <= 7:
                grid[y][x] = 2

    for building_x, building_y, vertical_doors in buildings:
        _paint_building(grid, building_x, building_y, vertical_doors=vertical_doors)

    return [
        [[tile, pg.Rect(x * vH.tileSizeGlobal, y * vH.tileSizeGlobal, vH.tileSizeGlobal, vH.tileSizeGlobal)] for x, tile in enumerate(row)]
        for y, row in enumerate(grid)
    ]

basicRoomFile = 'data/backgrounds/basicRoom.csv'
basicRoomRects = generate_battleground()
currRoomRects = basicRoomRects

#playerPosX = ((len(currRoomRects[0]) * vH.tileSizeGlobal)/2)
#playerPosY = ((len(currRoomRects) * vH.tileSizeGlobal)/2)

lockX = ((vH.sW * 0.75) / 2)
lockY = (vH.sH / 2)

spawnX = (len(currRoomRects[0]) // 2) * vH.tileSizeGlobal - vH.tileSizeGlobal / 2
spawnY = (len(currRoomRects) // 2) * vH.tileSizeGlobal - vH.tileSizeGlobal / 2
playerPosX = spawnX
playerPosY = spawnY

currNumOfXTiles = len(currRoomRects[0])
currNumOfYTiles = len(currRoomRects)

repasteableRoomSurface = drawRepasteableBackground(currRoomRects)
