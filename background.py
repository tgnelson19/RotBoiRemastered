import pygame as pg
import variableHolster as vH
import csv
from random import randint

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
    return newSurface

def screen_to_world(screen_x, screen_y):
    """Convert a screen coordinate into a world coordinate."""
    return screen_x - lockX + playerPosX, screen_y - lockY + playerPosY

def world_to_screen(world_x, world_y):
    """Convert a world coordinate into a screen coordinate."""
    return world_x - playerPosX + lockX, world_y - playerPosY + lockY

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
            if currRoomRects[tileY][tileX][0] == 1:
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
            if currRoomRects[tileY][tileX][0] == 1:
                count += 1
    return count


def find_spawn_rect(size):
    """Return a world-space rect that fits completely inside a random open floor tile."""
    tile_size = vH.tileSizeGlobal
    room_height = len(currRoomRects)
    room_width = len(currRoomRects[0])
    open_positions = []
    distant_positions = []

    min_distance_tiles = 4
    player_tile_x = int(playerPosX // tile_size)
    player_tile_y = int(playerPosY // tile_size)

    for tile_y in range(1, room_height - 1):
        for tile_x in range(1, room_width - 1):
            if currRoomRects[tile_y][tile_x][0] != 1:
                candidate = pg.Rect(
                    tile_x * tile_size + (tile_size - size) / 2,
                    tile_y * tile_size + (tile_size - size) / 2,
                    size,
                    size,
                )
                if not rect_hits_wall(candidate):
                    open_positions.append(candidate)
                    if abs(tile_x - player_tile_x) >= min_distance_tiles or abs(tile_y - player_tile_y) >= min_distance_tiles:
                        distant_positions.append(candidate)

    if distant_positions:
        return distant_positions[randint(0, len(distant_positions) - 1)]

    if open_positions:
        return open_positions[randint(0, len(open_positions) - 1)]

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
    vH.screen.blit(surface, (-playerPosX + lockX, -playerPosY + lockY))

# Example usage:

tileTypes = {
            0 : ["default", pg.Color(170,170,170)],
            1 : ["wall", pg.Color(90,90,90)]
            }

basicRoomFile = 'data/backgrounds/basicRoom.csv'
basicRoomRects = loadBackgroundRects(basicRoomFile)
currRoomRects = basicRoomRects

#playerPosX = ((len(currRoomRects[0]) * vH.tileSizeGlobal)/2)
#playerPosY = ((len(currRoomRects) * vH.tileSizeGlobal)/2)

lockX = ((vH.sW * 0.75) / 2)
lockY = (vH.sH / 2)

playerPosX = 200
playerPosY = 200

currNumOfXTiles = len(currRoomRects[0])
currNumOfYTiles = len(currRoomRects)

repasteableRoomSurface = drawRepasteableBackground(currRoomRects)