import io
import numpy as np
import pytest
from PIL import Image
from unittest.mock import patch, MagicMock

from core.ocr_service import (
    extractText,
    getEngineCN,
    getEngineJP,
    maskCyanText,
    _containsKana,
    _lineYKey,
    _mergeOcrResults,
)


# ── Engine singleton tests ─────────────────────────────────────

class TestGetEngineCN:
    def test_returnsSameInstanceOnMultipleCalls(self):
        assert getEngineCN() is getEngineCN()

    def test_engineIsNotNone(self):
        assert getEngineCN() is not None


# ── Cyan mask tests ────────────────────────────────────────────

class TestMaskCyanText:
    def _makeImageWithCyan(self):
        img = np.full((60, 200, 3), [20, 20, 20], dtype=np.uint8)
        # Place cyan pixels (simulating username) on left side
        img[10:30, 0:60, :] = [0, 210, 210]
        # Place white pixels (simulating message) on right side
        img[10:30, 80:160, :] = [255, 255, 255]
        return img

    def test_cyanPixelsAreMasked(self):
        img = self._makeImageWithCyan()
        result = maskCyanText(img)
        r = result[:, :, 0].astype(int)
        g = result[:, :, 1].astype(int)
        b = result[:, :, 2].astype(int)
        assert not np.any((g - r > 70) & (b - r > 70) & (g > 50)), \
            "Cyan pixels should be replaced"

    def test_whiteTextIsPreserved(self):
        # Cyan is at x=0..60, white message is at x=80..160 — gap avoids padding spill
        img = self._makeImageWithCyan()
        result = maskCyanText(img)
        # Cyan ends at x≈60, padding=5 → masked up to x≈65; white starts at x=80
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


# ── Japanese kana detection ────────────────────────────────────

class TestContainsKana:
    def test_detectsHiragana(self):
        assert _containsKana("こんにちは") is True

    def test_detectsKatakana(self):
        assert _containsKana("コンニチハ") is True

    def test_returnsFalseForChinese(self):
        assert _containsKana("你好") is False

    def test_returnsFalseForEnglish(self):
        assert _containsKana("Hello") is False

    def test_detectsMixedText(self):
        assert _containsKana("Hello こんにちは") is True


# ── Merge results tests ────────────────────────────────────────

def _makeLine(y: float, text: str, score: float):
    box = [[0, y], [100, y], [100, y + 20], [0, y + 20]]
    return [box, text, str(score)]


class TestMergeOcrResults:
    def test_returnsEmptyWhenBothNone(self):
        assert _mergeOcrResults(None, None) == []

    def test_returnsCnOnlyWhenJpIsNone(self):
        cn = [_makeLine(0, "Hello", 0.9)]
        assert _mergeOcrResults(cn, None) == ["Hello"]

    def test_returnsJpOnlyWhenCnIsNone(self):
        jp = [_makeLine(0, "こんにちは", 0.9)]
        assert _mergeOcrResults(None, jp) == ["こんにちは"]

    def test_prefersJpWhenKanaDetected(self):
        cn = [_makeLine(0, "garbage", 0.95)]
        jp = [_makeLine(0, "こんにちは", 0.7)]
        assert _mergeOcrResults(cn, jp) == ["こんにちは"]

    def test_prefersCnByScoreWhenNoKana(self):
        cn = [_makeLine(0, "Hello", 0.95)]
        jp = [_makeLine(0, "He11o", 0.6)]
        assert _mergeOcrResults(cn, jp) == ["Hello"]

    def test_prefersJpByScoreWhenNoCnMatch(self):
        cn = [_makeLine(0, "low", 0.4)]
        jp = [_makeLine(0, "high", 0.9)]
        assert _mergeOcrResults(cn, jp) == ["high"]

    def test_mergesMultipleLines(self):
        cn = [_makeLine(0, "Hello", 0.9), _makeLine(40, "你好", 0.9)]
        jp = [_makeLine(0, "Hello", 0.8), _makeLine(40, "garbage", 0.5)]
        result = _mergeOcrResults(cn, jp)
        assert "Hello" in result
        assert "你好" in result

    def test_includesLineOnlyInOneResult(self):
        cn = [_makeLine(0, "Chinese only", 0.9)]
        jp = [_makeLine(80, "こんにちは", 0.9)]
        result = _mergeOcrResults(cn, jp)
        assert len(result) == 2


# ── extractText integration tests ─────────────────────────────

class TestExtractText:
    def test_returnsEmptyWhenBothEnginesReturnNone(self, blankPngBytes):
        with (
            patch("core.ocr_service.getEngineCN") as mockCN,
            patch("core.ocr_service.getEngineJP") as mockJP,
        ):
            mockCN.return_value = MagicMock(return_value=(None, None))
            mockJP.return_value = MagicMock(return_value=(None, None))
            assert extractText(blankPngBytes) == ""

    def test_appliesCyanMaskBeforeOcr(self, blankPngBytes):
        with (
            patch("core.ocr_service.maskCyanText", wraps=maskCyanText) as spyMask,
            patch("core.ocr_service.getEngineCN") as mockCN,
            patch("core.ocr_service.getEngineJP") as mockJP,
        ):
            mockCN.return_value = MagicMock(return_value=(None, None))
            mockJP.return_value = MagicMock(return_value=(None, None))
            extractText(blankPngBytes)
            spyMask.assert_called_once()

    def test_mergesResultsFromBothEngines(self, blankPngBytes):
        cnResult = [_makeLine(0, "Hello", 0.9)]
        jpResult = [_makeLine(40, "こんにちは", 0.9)]
        with (
            patch("core.ocr_service.getEngineCN") as mockCN,
            patch("core.ocr_service.getEngineJP") as mockJP,
        ):
            mockCN.return_value = MagicMock(return_value=(cnResult, None))
            mockJP.return_value = MagicMock(return_value=(jpResult, None))
            result = extractText(blankPngBytes)
        assert "Hello" in result
        assert "こんにちは" in result
