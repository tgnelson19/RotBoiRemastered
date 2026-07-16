"""Small uniform spatial index used by combat collision queries."""

from collections import defaultdict
from math import floor


class SpatialHash:
    def __init__(self, cell_size=128):
        self.cell_size = max(1, cell_size)
        self._cells = defaultdict(list)

    def _cell_range(self, rect):
        left = floor(rect.left / self.cell_size)
        right = floor((rect.right - 1) / self.cell_size)
        top = floor(rect.top / self.cell_size)
        bottom = floor((rect.bottom - 1) / self.cell_size)
        for cell_y in range(top, bottom + 1):
            for cell_x in range(left, right + 1):
                yield cell_x, cell_y

    def insert(self, item, rect):
        for cell in self._cell_range(rect):
            self._cells[cell].append(item)

    def query(self, rect):
        seen = set()
        for cell in self._cell_range(rect):
            for item in self._cells.get(cell, ()):
                identity = id(item)
                if identity not in seen:
                    seen.add(identity)
                    yield item
