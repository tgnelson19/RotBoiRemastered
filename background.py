import pygame as pg
import variableHolster as vH
import csv
from math import ceil, cos, floor, hypot, radians, sin
from random import randint
import uiTheme as ui

_open_tile_cache = {}


def _gameplay_clip_width():
    """Width of the playable viewport, matching the HUD sidebar's actual size.

    Falls back to a 75% guess before characterStats has built the sidebar
    (e.g. during early import), since the real width depends on hud_mode.
    """
    import characterStats as cS
    sheet = getattr(cS, "informationSheet", None)
    if sheet is not None:
        return int(sheet.arena_width)
    return int(vH.sW * .75)


def gameplay_viewport_rect():
    """Public rect version of the playable viewport, for clipping world-space draws."""
    return pg.Rect(0, 0, _gameplay_clip_width(), int(vH.sH))

# The arena stays deliberately restrained: each ward is mostly charcoal and slate,
# with one low-saturation identity color.  These are renderer palettes rather than
# gameplay tile types, so biome flavor never changes collision rules.
SOUND_PALETTES = (
    {
        "ground": pg.Color(35, 38, 48), "ground_alt": pg.Color(39, 42, 53),
        "road": pg.Color(48, 48, 57), "interior": pg.Color(31, 34, 45),
        "wall_top": pg.Color(67, 67, 82), "wall_face": pg.Color(43, 43, 56),
        "accent": pg.Color(91, 78, 119), "detail": pg.Color(120, 111, 137),
    },
    {
        "ground": pg.Color(43, 39, 42), "ground_alt": pg.Color(48, 42, 45),
        "road": pg.Color(54, 47, 48), "interior": pg.Color(38, 32, 37),
        "wall_top": pg.Color(77, 65, 68), "wall_face": pg.Color(49, 39, 43),
        "accent": pg.Color(124, 62, 67), "detail": pg.Color(139, 91, 91),
    },
    {
        "ground": pg.Color(34, 41, 48), "ground_alt": pg.Color(37, 45, 54),
        "road": pg.Color(43, 50, 58), "interior": pg.Color(29, 37, 47),
        "wall_top": pg.Color(58, 70, 84), "wall_face": pg.Color(36, 47, 59),
        "accent": pg.Color(62, 78, 108), "detail": pg.Color(91, 99, 126),
    },
)

TOUCH_PALETTES = (
    {
        "ground": pg.Color(20, 31, 25), "ground_alt": pg.Color(24, 37, 28),
        "road": pg.Color(37, 48, 34), "interior": pg.Color(17, 27, 21),
        "wall_top": pg.Color(48, 67, 48), "wall_face": pg.Color(28, 43, 31),
        "accent": pg.Color(83, 104, 54), "detail": pg.Color(119, 104, 61),
    },
    {
        "ground": pg.Color(31, 32, 22), "ground_alt": pg.Color(37, 38, 24),
        "road": pg.Color(49, 48, 29), "interior": pg.Color(27, 27, 18),
        "wall_top": pg.Color(68, 65, 39), "wall_face": pg.Color(45, 43, 25),
        "accent": pg.Color(104, 91, 48), "detail": pg.Color(130, 113, 63),
    },
    {
        "ground": pg.Color(18, 29, 27), "ground_alt": pg.Color(21, 35, 31),
        "road": pg.Color(31, 45, 38), "interior": pg.Color(15, 25, 23),
        "wall_top": pg.Color(42, 64, 55), "wall_face": pg.Color(25, 41, 35),
        "accent": pg.Color(55, 100, 69), "detail": pg.Color(87, 117, 76),
    },
)

SIGHT_PALETTES = (
    {
        "ground": pg.Color(49, 75, 91), "ground_alt": pg.Color(56, 84, 101),
        "road": pg.Color(75, 109, 125), "interior": pg.Color(43, 67, 82),
        "wall_top": pg.Color(104, 174, 204), "wall_face": pg.Color(60, 111, 137),
        "accent": pg.Color(234, 145, 61), "detail": pg.Color(247, 188, 96),
    },
    {
        "ground": pg.Color(58, 80, 94), "ground_alt": pg.Color(64, 89, 104),
        "road": pg.Color(84, 116, 132), "interior": pg.Color(48, 70, 85),
        "wall_top": pg.Color(126, 193, 218), "wall_face": pg.Color(69, 123, 147),
        "accent": pg.Color(211, 111, 48), "detail": pg.Color(241, 164, 78),
    },
    {
        "ground": pg.Color(43, 70, 87), "ground_alt": pg.Color(48, 78, 96),
        "road": pg.Color(69, 103, 121), "interior": pg.Color(37, 61, 76),
        "wall_top": pg.Color(94, 164, 196), "wall_face": pg.Color(53, 105, 132),
        "accent": pg.Color(242, 157, 67), "detail": pg.Color(252, 203, 116),
    },
)

CHEMESTHESIS_PALETTES = (
    {
        "ground": pg.Color(54, 31, 24), "ground_alt": pg.Color(64, 36, 26),
        "road": pg.Color(85, 48, 29), "interior": pg.Color(45, 25, 21),
        "wall_top": pg.Color(126, 54, 33), "wall_face": pg.Color(77, 35, 27),
        "accent": pg.Color(202, 77, 35), "detail": pg.Color(116, 132, 50),
    },
    {
        "ground": pg.Color(50, 38, 22), "ground_alt": pg.Color(61, 45, 24),
        "road": pg.Color(82, 57, 29), "interior": pg.Color(42, 31, 18),
        "wall_top": pg.Color(126, 79, 34), "wall_face": pg.Color(76, 49, 24),
        "accent": pg.Color(160, 48, 34), "detail": pg.Color(104, 132, 52),
    },
    {
        "ground": pg.Color(34, 44, 25), "ground_alt": pg.Color(40, 52, 28),
        "road": pg.Color(59, 67, 31), "interior": pg.Color(28, 37, 21),
        "wall_top": pg.Color(79, 100, 43), "wall_face": pg.Color(49, 62, 29),
        "accent": pg.Color(194, 66, 37), "detail": pg.Color(137, 142, 55),
    },
)

PHANTASIA_PALETTES = (
    {
        "ground": pg.Color(43, 24, 52), "ground_alt": pg.Color(51, 28, 62),
        "road": pg.Color(68, 37, 78), "interior": pg.Color(36, 19, 45),
        "wall_top": pg.Color(113, 55, 126), "wall_face": pg.Color(68, 34, 80),
        "accent": pg.Color(198, 77, 170), "detail": pg.Color(226, 125, 199),
    },
    {
        "ground": pg.Color(51, 23, 48), "ground_alt": pg.Color(61, 27, 57),
        "road": pg.Color(79, 35, 72), "interior": pg.Color(43, 18, 40),
        "wall_top": pg.Color(129, 51, 112), "wall_face": pg.Color(79, 31, 70),
        "accent": pg.Color(218, 82, 158), "detail": pg.Color(239, 137, 195),
    },
    {
        "ground": pg.Color(36, 25, 55), "ground_alt": pg.Color(43, 29, 66),
        "road": pg.Color(58, 39, 83), "interior": pg.Color(30, 20, 47),
        "wall_top": pg.Color(94, 61, 133), "wall_face": pg.Color(57, 38, 84),
        "accent": pg.Color(177, 75, 184), "detail": pg.Color(214, 122, 218),
    },
)

BIOME_PALETTES = SOUND_PALETTES

WALL_HEIGHT = 14
CAMERA_BACKGROUND_TARGET_WIDTH = 640
cameraAngleDegrees = 0.0
_camera_background_cache = {}
_camera_render_surfaces = {}
_raised_scenery_cache = {}

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

def _biome_for_tile(tile_x, tile_y, width, height):
    """Split the circular map into three broad, intentionally soft-edged wards."""
    dx = tile_x - width / 2
    dy = tile_y - height / 2
    if dy < -abs(dx) * .28:
        return 0  # the violet archive
    if dx < 0:
        return 1  # the ember ward
    return 2      # the drowned circuit


def _draw_floor_detail(surface, rect, tile, tile_x, tile_y, palette, biome):
    noise = (tile_x * 37 + tile_y * 71 + tile_x * tile_y * 3) % 113
    if tile == 2:
        # Broken road edging and occasional colored routing packets make the paths
        # read like ancient circuitry without becoming neon.
        pg.draw.line(surface, pg.Color(28, 30, 37), rect.topleft, rect.topright, 2)
        pg.draw.line(surface, pg.Color(67, 65, 72), rect.bottomleft, rect.bottomright, 1)
        if noise % 4 == 0:
            dash = pg.Rect(rect.centerx - 6, rect.centery - 2, 12, 4)
            pg.draw.rect(surface, palette["accent"], dash)
    elif tile == 3:
        inset = rect.inflate(-12, -12)
        pg.draw.rect(surface, pg.Color(24, 27, 35), inset, 2)
        if noise % 3 == 0:
            pg.draw.rect(surface, palette["detail"],
                         (rect.x + 7, rect.y + 7, 5, 5))
    elif tile == 0:
        if noise < 9:
            pg.draw.line(surface, pg.Color(26, 29, 36),
                         (rect.x + 9, rect.y + 14),
                         (rect.x + 20 + noise, rect.y + 18 + noise // 2), 2)
            pg.draw.line(surface, pg.Color(26, 29, 36),
                         (rect.x + 20 + noise, rect.y + 18 + noise // 2),
                         (rect.x + 24 + noise, rect.y + 29), 1)
        elif noise in (31, 63, 91):
            pg.draw.rect(surface, palette["accent"],
                         (rect.centerx - 2, rect.centery - 2, 4, 4))


def _draw_raised_decoration(surface, rect, palette, biome):
    """Draw one tiny 2.5D landmark with a top, face, and grounded shadow."""
    cx, floor_y = rect.centerx, rect.bottom - 8
    pg.draw.ellipse(surface, pg.Color(20, 22, 29),
                    (cx - 13, floor_y - 3, 30, 13))
    if biome == 1:
        # Ember ward brazier.
        pg.draw.rect(surface, ui.INK, (cx - 8, floor_y - 16, 16, 18))
        pg.draw.rect(surface, palette["wall_face"],
                     (cx - 6, floor_y - 15, 12, 15))
        pg.draw.polygon(surface, palette["wall_top"],
                        ((cx - 7, floor_y - 16), (cx, floor_y - 21),
                         (cx + 7, floor_y - 16), (cx, floor_y - 12)))
        pg.draw.rect(surface, palette["accent"],
                     (cx - 3, floor_y - 26, 6, 9))
    else:
        # Archive plinth / drowned circuit relay.
        height = 24 if biome == 0 else 20
        pg.draw.polygon(surface, ui.INK,
                        ((cx - 9, floor_y - height), (cx + 4, floor_y - height - 5),
                         (cx + 10, floor_y - height + 1), (cx + 10, floor_y),
                         (cx - 9, floor_y)))
        pg.draw.polygon(surface, palette["wall_face"],
                        ((cx - 6, floor_y - height + 1), (cx + 6, floor_y - height + 1),
                         (cx + 6, floor_y - 3), (cx - 6, floor_y - 1)))
        pg.draw.polygon(surface, palette["wall_top"],
                        ((cx - 7, floor_y - height), (cx, floor_y - height - 5),
                         (cx + 7, floor_y - height), (cx, floor_y - height + 4)))
        pg.draw.rect(surface, palette["accent"],
                     (cx - 2, floor_y - height + 7, 4, 7))


def _is_raised(room_rects, tile_x, tile_y):
    return (0 <= tile_y < len(room_rects)
            and 0 <= tile_x < len(room_rects[0])
            and room_rects[tile_y][tile_x][0] in RAISED_TILES)


def drawRepasteableBackground(roomRects):
    """Bake only the rotating ground plane; raised scenery is drawn afterward."""
    width, height = len(roomRects[0]), len(roomRects)
    tile_size = vH.tileSizeGlobal
    newSurface = pg.surface.Surface((width * tile_size, height * tile_size))

    # Ground pass. Drawing every footprint first lets raised walls overlap the tile
    # north of them without being painted over later.
    for rowIndex, row in enumerate(roomRects):
        for colIndex, currRectData in enumerate(row):
            tile, rect = currRectData
            rect.update(colIndex * tile_size, rowIndex * tile_size, tile_size, tile_size)
            biome = _biome_for_tile(colIndex, rowIndex, width, height)
            palette = BIOME_PALETTES[biome]
            if tile == 5:
                color = pg.Color(15, 18, 25)
            elif tile in SOLID_TILES:
                color = palette["ground"]
            elif tile == 2:
                color = palette["road"]
            elif tile == 3:
                color = palette["interior"]
            else:
                color = palette["ground_alt"] if (colIndex + rowIndex) % 7 == 0 else palette["ground"]
            pg.draw.rect(newSurface, color, rect)
            if tile not in SOLID_TILES:
                pg.draw.rect(newSurface, pg.Color(48, 51, 60), rect, 1)
                _draw_floor_detail(newSurface, rect, tile, colIndex, rowIndex,
                                   palette, biome)

    return newSurface

def screen_to_world(screen_x, screen_y):
    """Convert a screen coordinate into a world coordinate."""
    screen_dx = screen_x - lockX - vH.screenShakeX
    screen_dy = screen_y - lockY - vH.screenShakeY
    world_dx, world_dy = screen_vector_to_world(screen_dx, screen_dy)
    return playerPosX + world_dx, playerPosY + world_dy

def world_to_screen(world_x, world_y):
    """Convert a world coordinate into a screen coordinate."""
    screen_dx, screen_dy = world_vector_to_screen(
        world_x - playerPosX, world_y - playerPosY,
    )
    return (screen_dx + lockX + vH.screenShakeX,
            screen_dy + lockY + vH.screenShakeY)


def _camera_components():
    angle = radians(cameraAngleDegrees)
    return cos(angle), sin(angle)


def world_vector_to_screen(delta_x, delta_y):
    """Rotate a world-space vector into the current camera orientation."""
    cosine, sine = _camera_components()
    return (delta_x * cosine + delta_y * sine,
            -delta_x * sine + delta_y * cosine)


def screen_vector_to_world(delta_x, delta_y):
    """Rotate a screen-space vector back onto the world's ground plane."""
    cosine, sine = _camera_components()
    return (delta_x * cosine - delta_y * sine,
            delta_x * sine + delta_y * cosine)


def set_camera_angle(degrees):
    """Set continuous camera yaw in degrees, normalized to one revolution."""
    global cameraAngleDegrees
    cameraAngleDegrees = float(degrees) % 360.0


def set_camera_quarter_turns(turns):
    """Compatibility helper for callers that want an exact cardinal view."""
    set_camera_angle(turns * 90.0)


def rotate_camera(degrees):
    """Rotate the world counter-clockwise for positive degree values."""
    set_camera_angle(cameraAngleDegrees + degrees)

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


def find_spawn_rect(size, min_distance_tiles=4):
    """Return a world-space rect that fits completely inside a random open floor tile."""
    tile_size = vH.tileSizeGlobal
    room_height = len(currRoomRects)
    room_width = len(currRoomRects[0])
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
    """Rotate only a low-resolution camera window, never the full arena texture."""
    global _camera_background_cache, _camera_render_surfaces
    gameplay_clip = pg.Rect(0, 0, _gameplay_clip_width(), int(vH.sH))
    old_clip = vH.screen.get_clip()
    vH.screen.set_clip(gameplay_clip)

    if abs(cameraAngleDegrees) < .0001:
        origin_x, origin_y = world_to_screen(0, 0)
        vH.screen.blit(surface, (origin_x, origin_y))
    else:
        # Scaling the static arena once bounds continuous rotation work regardless
        # of desktop resolution. At 1080p this uses a 640px-wide camera buffer.
        render_scale = min(1.0, CAMERA_BACKGROUND_TARGET_WIDTH
                           / max(1, gameplay_clip.width))
        scaled_size = (max(1, round(surface.get_width() * render_scale)),
                       max(1, round(surface.get_height() * render_scale)))
        cache_key = (id(surface), scaled_size)
        scaled_background = _camera_background_cache.get(cache_key)
        if scaled_background is None:
            scaled_background = pg.transform.scale(surface, scaled_size)
            _camera_background_cache.clear()
            _camera_background_cache[cache_key] = scaled_background

        view_width = max(1, round(gameplay_clip.width * render_scale))
        view_height = max(1, round(gameplay_clip.height * render_scale))
        angle = radians(cameraAngleDegrees)
        source_width = ceil(abs(view_width * cos(angle))
                            + abs(view_height * sin(angle))) + 4
        source_height = ceil(abs(view_width * sin(angle))
                             + abs(view_height * cos(angle))) + 4
        source_rect = pg.Rect(
            floor(playerPosX * render_scale - source_width / 2),
            floor(playerPosY * render_scale - source_height / 2),
            source_width, source_height,
        )
        staging = pg.Surface(source_rect.size)
        staging.fill(pg.Color(15, 18, 25))
        clipped_source = source_rect.clip(scaled_background.get_rect())
        if clipped_source.width and clipped_source.height:
            staging.blit(
                scaled_background,
                (clipped_source.x - source_rect.x,
                 clipped_source.y - source_rect.y),
                clipped_source,
            )

        rotated = pg.transform.rotate(staging, cameraAngleDegrees)
        view_key = ("view", view_width, view_height)
        low_res_view = _camera_render_surfaces.get(view_key)
        if low_res_view is None:
            low_res_view = pg.Surface((view_width, view_height))
            _camera_render_surfaces[view_key] = low_res_view
        low_res_view.fill(pg.Color(15, 18, 25))
        low_res_view.blit(
            rotated,
            (view_width / 2 - rotated.get_width() / 2,
             view_height / 2 - rotated.get_height() / 2),
        )
        output_size = (gameplay_clip.width, gameplay_clip.height)
        output_key = ("output", *output_size)
        camera_surface = _camera_render_surfaces.get(output_key)
        if camera_surface is None:
            camera_surface = pg.Surface(output_size)
            _camera_render_surfaces[output_key] = camera_surface
        pg.transform.scale(low_res_view, output_size, camera_surface)
        vH.screen.blit(camera_surface,
                       (round(vH.screenShakeX), round(vH.screenShakeY)))
    vH.screen.set_clip(old_clip)


def _raised_scenery(room_rects):
    """Cache the small subset of tiles that need full-resolution raised drawing."""
    cache_key = id(room_rects)
    cached = _raised_scenery_cache.get(cache_key)
    if cached is not None:
        return cached

    width, height = len(room_rects[0]), len(room_rects)
    center_x, center_y = width // 2, height // 2
    walls = []
    decorations = []
    for tile_y, row in enumerate(room_rects):
        for tile_x, (tile, _) in enumerate(row):
            biome = _biome_for_tile(tile_x, tile_y, width, height)
            if tile in RAISED_TILES:
                walls.append((tile_x, tile_y, tile, biome))
            elif tile == 0:
                marker = (tile_x * 43 + tile_y * 89 + tile_x * tile_y) % 211
                far_from_spawn = hypot(tile_x - center_x, tile_y - center_y) > 11
                if marker in (7, 8) and far_from_spawn:
                    decorations.append((tile_x, tile_y, biome))
    cached = walls, decorations
    _raised_scenery_cache.clear()
    _raised_scenery_cache[cache_key] = cached
    return cached


def _wall_screen_geometry(tile_x, tile_y, height):
    """Return ground and cap polygons; cap height is always vertical on screen."""
    size = vH.tileSizeGlobal
    ground = tuple(world_to_screen(world_x, world_y) for world_x, world_y in (
        (tile_x * size, tile_y * size),
        ((tile_x + 1) * size, tile_y * size),
        ((tile_x + 1) * size, (tile_y + 1) * size),
        (tile_x * size, (tile_y + 1) * size),
    ))
    cap = tuple((x, y - height) for x, y in ground)
    return ground, cap


def _decoration_screen_rect(tile_x, tile_y):
    """Return an axis-aligned billboard rect anchored to a rotating ground tile."""
    size = vH.tileSizeGlobal
    center = world_to_screen((tile_x + .5) * size, (tile_y + .5) * size)
    rect = pg.Rect(0, 0, size, size)
    rect.center = center
    return rect


def _draw_camera_facing_wall(surface, room_rects, tile_x, tile_y, tile, palette):
    height = WALL_HEIGHT + (2 if tile == 1 else 0)
    ground, cap = _wall_screen_geometry(tile_x, tile_y, height)

    shadow = tuple((x + 6, y + 7) for x, y in ground)
    pg.draw.polygon(surface, pg.Color(18, 20, 27), shadow)

    # Each exposed ground edge owns an outward world normal. Only normals that
    # project toward screen-bottom reveal a vertical face to the camera.
    edges = (
        ((0, 1), (0, -1), (tile_x, tile_y - 1)),
        ((1, 2), (1, 0), (tile_x + 1, tile_y)),
        ((2, 3), (0, 1), (tile_x, tile_y + 1)),
        ((3, 0), (-1, 0), (tile_x - 1, tile_y)),
    )
    visible_faces = []
    for (start, end), normal, neighbor in edges:
        if _is_raised(room_rects, *neighbor):
            continue
        _, normal_y = world_vector_to_screen(*normal)
        if normal_y <= .001:
            continue
        face = (cap[start], cap[end], ground[end], ground[start])
        visible_faces.append((normal_y, face))

    for _, face in sorted(visible_faces):
        pg.draw.polygon(surface, palette["wall_face"], face)
        pg.draw.polygon(surface, ui.INK, face, 2)
        lower_left, lower_right = face[3], face[2]
        accent_left = (lower_left[0] * .82 + lower_right[0] * .18,
                       lower_left[1] * .82 + lower_right[1] * .18 - 5)
        accent_right = (lower_left[0] * .18 + lower_right[0] * .82,
                        lower_left[1] * .18 + lower_right[1] * .82 - 5)
        pg.draw.line(surface, palette["accent"], accent_left, accent_right, 2)

    pg.draw.polygon(surface, palette["wall_top"], cap)
    pg.draw.polygon(surface, ui.INK, cap, 2)
    top_edge = min(((cap[index], cap[(index + 1) % 4]) for index in range(4)),
                   key=lambda edge: (edge[0][1] + edge[1][1]) / 2)
    pg.draw.line(surface, palette["detail"], *top_edge, 2)

    center_x = sum(point[0] for point in cap) / 4
    center_y = sum(point[1] for point in cap) / 4
    if tile == 1:
        pg.draw.rect(surface, palette["accent"],
                     (center_x - 3, center_y - 3, 6, 6))
    elif (tile_x + tile_y) % 2 == 0:
        pg.draw.line(surface, palette["accent"],
                     (center_x - 9, center_y), (center_x + 9, center_y), 2)


def drawRaisedScenery(room_rects):
    """Draw walls and props after yaw so all vertical height points screen-up."""
    gameplay_clip = pg.Rect(0, 0, _gameplay_clip_width(), int(vH.sH))
    visibility = gameplay_clip.inflate(vH.tileSizeGlobal * 3,
                                       vH.tileSizeGlobal * 3)
    walls, decorations = _raised_scenery(room_rects)
    visible_items = []
    size = vH.tileSizeGlobal

    for tile_x, tile_y, tile, biome in walls:
        center = world_to_screen((tile_x + .5) * size, (tile_y + .5) * size)
        if visibility.collidepoint(center):
            visible_items.append((center[1], 0, tile_x, tile_y, tile, biome))
    for tile_x, tile_y, biome in decorations:
        center = world_to_screen((tile_x + .5) * size, (tile_y + .5) * size)
        if visibility.collidepoint(center):
            visible_items.append((center[1], 1, tile_x, tile_y, 0, biome))

    old_clip = vH.screen.get_clip()
    vH.screen.set_clip(gameplay_clip)
    for _, kind, tile_x, tile_y, tile, biome in sorted(visible_items):
        palette = BIOME_PALETTES[biome]
        if kind == 0:
            _draw_camera_facing_wall(vH.screen, room_rects, tile_x, tile_y,
                                     tile, palette)
        else:
            rect = _decoration_screen_rect(tile_x, tile_y)
            _draw_raised_decoration(vH.screen, rect, palette, biome)
    vH.screen.set_clip(old_clip)

# Example usage:

tileTypes = {
            0 : ["default", pg.Color(42,46,50)],
            1 : ["arena wall", pg.Color(61,65,72)],
            2 : ["road", pg.Color(67,61,52)],
            3 : ["building floor", pg.Color(34,40,49)],
            4 : ["building wall", pg.Color(52,61,75)],
            5 : ["outer void", pg.Color(15,18,25)],
            }

RAISED_TILES = {1, 4}
SOLID_TILES = RAISED_TILES | {5}


def _paint_road(grid, start, end, width=1):
    x1, y1 = start
    x2, y2 = end
    steps = max(abs(x2 - x1), abs(y2 - y1), 1)
    for step in range(steps + 1):
        x = round(x1 + (x2 - x1) * step / steps)
        y = round(y1 + (y2 - y1) * step / steps)
        for oy in range(-width, width + 1):
            for ox in range(-width, width + 1):
                if (0 <= y + oy < len(grid) and 0 <= x + ox < len(grid[0])
                        and grid[y + oy][x + ox] not in (1, 5)):
                    grid[y + oy][x + ox] = 2


def _paint_building(grid, center_x, center_y, width=11, height=9,
                    vertical_doors=False, style="plain"):
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

    # Small silhouette changes make each ruin recognizable at a glance while every
    # room keeps its two safe, opposite exits.
    if style == "bastion":
        for x, y in ((left + 2, top + 2), (right - 2, top + 2),
                     (left + 2, bottom - 2), (right - 2, bottom - 2)):
            grid[y][x] = 4
    elif style == "archive":
        for y in range(top + 2, bottom - 1, 3):
            grid[y][left + 2] = 4
            grid[y][right - 2] = 4
    elif style == "forge":
        for x, y in ((left + 2, top + 2), (right - 2, top + 2)):
            grid[y][x] = 4
        grid[bottom - 2][center_x] = 4
    elif style == "shrine":
        for x, y in ((center_x, center_y - 1), (center_x - 1, center_y),
                     (center_x + 1, center_y), (center_x, center_y + 1)):
            grid[y][x] = 4
    elif style == "vault":
        for x in range(left + 2, right - 1):
            if x not in (center_x - 1, center_x):
                grid[top + 2][x] = 4


def generate_battleground(size=97):
    """Create a circular arena with roads, a central plaza, and six buildings."""
    size = max(61, size | 1)
    center = size // 2
    radius = center - 2
    grid = [[0 for _ in range(size)] for _ in range(size)]

    for y in range(size):
        for x in range(size):
            distance = hypot(x - center, y - center)
            if distance >= radius:
                grid[y][x] = 5
            elif distance >= radius - 1:
                grid[y][x] = 1

    layout_scale = size / 97
    buildings = tuple(
        (center + round(offset_x * layout_scale),
         center + round(offset_y * layout_scale), vertical,
         width, height, style)
        for offset_x, offset_y, vertical, width, height, style in (
            (-23, -22, False, 13, 9, "bastion"),
            (23, -22, False, 9, 13, "archive"),
            (-28, 2, True, 11, 11, "forge"),
            (28, 2, True, 15, 9, "plain"),
            (-18, 26, False, 9, 11, "shrine"),
            (18, 26, False, 13, 11, "vault"),
        )
    )
    for building_x, building_y, *_ in buildings:
        _paint_road(grid, (center, center), (building_x, building_y), 1)

    for y in range(center - 7, center + 8):
        for x in range(center - 7, center + 8):
            if hypot(x - center, y - center) <= 7:
                grid[y][x] = 2

    for building_x, building_y, vertical_doors, width, height, style in buildings:
        _paint_building(grid, building_x, building_y, width, height,
                        vertical_doors, style)

    return [
        [[tile, pg.Rect(x * vH.tileSizeGlobal, y * vH.tileSizeGlobal, vH.tileSizeGlobal, vH.tileSizeGlobal)] for x, tile in enumerate(row)]
        for y, row in enumerate(grid)
    ]


def generate_touch_battleground(size=87):
    """Create the cramped sewer-prison used by the Path of Touch."""
    size = max(65, size | 1)
    center = size // 2
    grid = [[0 for _ in range(size)] for _ in range(size)]

    # A square containment shell makes this dungeon feel built to hold something.
    for y in range(size):
        for x in range(size):
            edge = min(x, y, size - 1 - x, size - 1 - y)
            if edge < 2:
                grid[y][x] = 5
            elif edge < 4:
                grid[y][x] = 1

    # Dense cell blocks leave narrow north/south and east/west drainage lanes.
    blocks = (
        (-27, -25, 13, 13, False, "vault"), (-9, -25, 11, 13, False, "archive"),
        (10, -25, 13, 13, False, "bastion"), (28, -25, 11, 13, False, "vault"),
        (-27, -7, 13, 11, True, "archive"), (27, -7, 13, 11, True, "forge"),
        (-27, 11, 13, 11, True, "bastion"), (27, 11, 13, 11, True, "vault"),
        (-27, 28, 13, 11, False, "forge"), (-9, 28, 11, 11, False, "vault"),
        (10, 28, 13, 11, False, "archive"), (28, 28, 11, 11, False, "bastion"),
    )
    for ox, oy, width, height, vertical, style in blocks:
        _paint_building(grid, center + ox, center + oy, width, height, vertical, style)

    # Main sewer channels and a small safe cistern at the spawn.
    _paint_road(grid, (center, 4), (center, size - 5), 1)
    _paint_road(grid, (4, center), (size - 5, center), 1)
    for y in range(center - 5, center + 6):
        for x in range(center - 5, center + 6):
            if hypot(x - center, y - center) <= 5:
                grid[y][x] = 2

    return [
        [[tile, pg.Rect(x * vH.tileSizeGlobal, y * vH.tileSizeGlobal,
                        vH.tileSizeGlobal, vH.tileSizeGlobal)]
         for x, tile in enumerate(row)]
        for y, row in enumerate(grid)
    ]


def _rect_grid(grid):
    return [
        [[tile, pg.Rect(x * vH.tileSizeGlobal, y * vH.tileSizeGlobal,
                        vH.tileSizeGlobal, vH.tileSizeGlobal)]
         for x, tile in enumerate(row)]
        for y, row in enumerate(grid)
    ]


def _circular_shell(grid, thickness=2):
    size = len(grid)
    center = size // 2
    radius = center - 2
    for y in range(size):
        for x in range(size):
            distance = hypot(x - center, y - center)
            if distance >= radius:
                grid[y][x] = 5
            elif distance >= radius - thickness:
                grid[y][x] = 1


def generate_sight_battleground(size=91):
    """An exposed, building-free field with clear long sight lines."""
    size = max(65, size | 1)
    center = size // 2
    grid = [[0 for _ in range(size)] for _ in range(size)]
    _circular_shell(grid, 1)
    for angle_index in range(8):
        angle = angle_index * 6.283185307 / 8
        end = (center + round(cos(angle) * (center - 6)),
               center + round(sin(angle) * (center - 6)))
        _paint_road(grid, (center, center), end, 1)
    for y in range(center - 7, center + 8):
        for x in range(center - 7, center + 8):
            if hypot(x - center, y - center) <= 7:
                grid[y][x] = 2
    return _rect_grid(grid)


def generate_chemesthesis_battleground(size=93):
    """Open contaminated ground scattered with deterministic ruin fragments."""
    size = max(67, size | 1)
    center = size // 2
    grid = [[0 for _ in range(size)] for _ in range(size)]
    _circular_shell(grid, 1)
    # Broken corners and wall runs imply structures without creating real rooms.
    for y in range(7, size - 7):
        for x in range(7, size - 7):
            far_from_spawn = hypot(x - center, y - center) > 9
            marker = (x * 47 + y * 83 + x * y * 7) % 317
            if far_from_spawn and marker in (2, 3, 5, 71, 72):
                grid[y][x] = 4
                if marker in (2, 71) and x + 1 < size - 5:
                    grid[y][x + 1] = 4
                if marker in (3, 72) and y + 1 < size - 5:
                    grid[y + 1][x] = 4
    _paint_road(grid, (5, center), (size - 6, center), 1)
    _paint_road(grid, (center, 5), (center, size - 6), 1)
    return _rect_grid(grid)


def generate_phantasia_battleground(size=101):
    """Large dream courts with only three elaborate architectural anchors."""
    size = max(75, size | 1)
    center = size // 2
    grid = [[0 for _ in range(size)] for _ in range(size)]
    _circular_shell(grid, 2)
    buildings = (
        (center - 28, center - 21, 17, 15, False, "archive"),
        (center + 29, center - 8, 15, 19, True, "shrine"),
        (center - 6, center + 30, 21, 13, False, "bastion"),
    )
    for x, y, width, height, vertical, style in buildings:
        _paint_road(grid, (center, center), (x, y), 2)
        _paint_building(grid, x, y, width, height, vertical, style)
        # Ornamental crowns, paired pillars, and approach gates add feature
        # density without filling the otherwise broad arena.
        for ox, oy in ((-width // 2 - 2, -height // 2),
                       (width // 2 + 2, -height // 2),
                       (-width // 2 - 2, height // 2),
                       (width // 2 + 2, height // 2)):
            grid[y + oy][x + ox] = 4
        for offset in (-3, 0, 3):
            if vertical:
                grid[y + offset][x - width // 2 - 3] = 4
                grid[y + offset][x + width // 2 + 3] = 4
            else:
                grid[y - height // 2 - 3][x + offset] = 4
                grid[y + height // 2 + 3][x + offset] = 4
    for y in range(center - 9, center + 10):
        for x in range(center - 9, center + 10):
            if hypot(x - center, y - center) <= 9:
                grid[y][x] = 2
    return _rect_grid(grid)

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


def configure_battleground(path_key):
    """Swap path-owned map/render data while preserving camera and game systems."""
    global BIOME_PALETTES, WALL_HEIGHT, currRoomRects, repasteableRoomSurface
    global spawnX, spawnY, playerPosX, playerPosY, currNumOfXTiles, currNumOfYTiles
    palettes = {
        "sound": SOUND_PALETTES, "touch": TOUCH_PALETTES,
        "sight": SIGHT_PALETTES, "chemesthesis": CHEMESTHESIS_PALETTES,
        "phantasia": PHANTASIA_PALETTES,
    }
    generators = {
        "sound": lambda: basicRoomRects,
        "touch": generate_touch_battleground,
        "sight": generate_sight_battleground,
        "chemesthesis": generate_chemesthesis_battleground,
        "phantasia": generate_phantasia_battleground,
    }
    if path_key not in generators:
        raise KeyError(f"Unknown battleground path: {path_key}")
    BIOME_PALETTES = palettes[path_key]
    WALL_HEIGHT = {"touch": 22, "phantasia": 20}.get(path_key, 14)
    currRoomRects = generators[path_key]()
    currNumOfXTiles = len(currRoomRects[0])
    currNumOfYTiles = len(currRoomRects)
    spawnX = currNumOfXTiles // 2 * vH.tileSizeGlobal - vH.tileSizeGlobal / 2
    spawnY = currNumOfYTiles // 2 * vH.tileSizeGlobal - vH.tileSizeGlobal / 2
    playerPosX, playerPosY = spawnX, spawnY
    _open_tile_cache.clear()
    _camera_background_cache.clear()
    _raised_scenery_cache.clear()
    repasteableRoomSurface = drawRepasteableBackground(currRoomRects)
