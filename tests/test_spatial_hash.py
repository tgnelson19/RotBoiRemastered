import unittest

from spatialHash import SpatialHash


class RectStub:
    def __init__(self, left, top, right, bottom):
        self.left = left
        self.top = top
        self.right = right
        self.bottom = bottom


class SpatialHashTests(unittest.TestCase):
    def test_query_only_returns_nearby_objects_without_duplicates(self):
        grid = SpatialHash(cell_size=10)
        near = object()
        far = object()
        grid.insert(near, RectStub(5, 5, 16, 16))
        grid.insert(far, RectStub(40, 40, 45, 45))
        result = list(grid.query(RectStub(8, 8, 18, 18)))
        self.assertEqual(result, [near])


if __name__ == "__main__":
    unittest.main()
