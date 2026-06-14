import io
import numpy as np
import pytest
from PIL import Image
from unittest.mock import patch, MagicMock

from core.ocr_service import extractText, maskCyanText


# ── Cyan mask tests ────────────────────────────────────────────

class TestMaskCyanText:
    def _makeImageWithCyan(self):
        img = np.full((60, 200, 3), [20, 20, 20], dtype=np.uint8)
        img[10:30, 0:60, :] = [0, 210, 210]
        img[10:30, 80:160, :] = [255, 255, 255]
        return img

    def test_cyanPixelsAreMasked(self):
        img = self._makeImageWithCyan()
        result = maskCyanText(img)
        r = result[:, :, 0].astype(int)
        g = result[:, :, 1].astype(int)
        b = result[:, :, 2].astype(int)
        assert not np.any((g - r > 70) & (b - r > 70) & (g > 50))

    def test_whiteTextIsPreserved(self):
        img = self._makeImageWithCyan()
        result = maskCyanText(img)
        assert np.all(result[10:30, 90:160, :] == [255, 255, 255])

    def test_doesNotModifyOriginalArray(self):
        img = self._makeImageWithCyan()
        original = img.copy()
        maskCyanText(img)
        assert np.array_equal(img, original)

    def test_darkBackgroundUnchanged(self):
        img = np.full((50, 50, 3), [20, 20, 20], dtype=np.uint8)
        result = maskCyanText(img)
        assert np.array_equal(result, img)


# ── extractText tests ──────────────────────────────────────────

def _box(left: float, top: float, width: float = 100, height: float = 20):
    return [[left, top], [left + width, top], [left + width, top + height], [left, top + height]]


class TestExtractText:
    def _mockEngine(self, boxes, txts):
        output = MagicMock()
        output.boxes = boxes
        output.txts = txts
        return MagicMock(return_value=MagicMock(return_value=output))

    def test_returnsEmptyWhenNoText(self, blankPngBytes):
        with patch("core.ocr_service._getEngine", self._mockEngine([], [])):
            assert extractText(blankPngBytes) == ""

    def test_appliesCyanMaskBeforeOcr(self, blankPngBytes):
        with (
            patch("core.ocr_service.maskCyanText", wraps=maskCyanText) as spyMask,
            patch("core.ocr_service._getEngine", self._mockEngine([], [])),
        ):
            extractText(blankPngBytes)
            spyMask.assert_called_once()

    def test_separateRowsAreJoinedWithNewline(self, blankPngBytes):
        boxes = [_box(0, 0), _box(0, 40)]
        with patch("core.ocr_service._getEngine", self._mockEngine(boxes, ["Hello", "こんにちは"])):
            assert extractText(blankPngBytes) == "Hello\nこんにちは"

    def test_sameRowBoxesBecomeOneLineOrderedByX(self, blankPngBytes):
        # Two boxes vertically overlapping (same row) → one line, left-to-right
        boxes = [_box(120, 0), _box(0, 2)]
        with patch("core.ocr_service._getEngine", self._mockEngine(boxes, ["world", "Hello"])):
            assert extractText(blankPngBytes) == "Hello world"

    def test_outOfOrderRowsAreSortedTopToBottom(self, blankPngBytes):
        boxes = [_box(0, 80), _box(0, 0), _box(0, 40)]
        with patch("core.ocr_service._getEngine", self._mockEngine(boxes, ["third", "first", "second"])):
            assert extractText(blankPngBytes) == "first\nsecond\nthird"
