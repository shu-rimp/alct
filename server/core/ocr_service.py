import io
from pathlib import Path
from urllib.request import urlretrieve

import numpy as np
from PIL import Image
from rapidocr_onnxruntime import RapidOCR

# ── Model paths ────────────────────────────────────────────────
_MODELS_DIR = Path(__file__).parent.parent / "models"
_JAPAN_MODEL_PATH = _MODELS_DIR / "japan_rec_crnn.onnx"
# PP-OCRv1 Japanese ONNX from RapidOCR model hub
_JAPAN_MODEL_URL = (
    "https://huggingface.co/SWHL/RapidOCR/resolve/main/PP-OCRv1/japan_rec_crnn.onnx"
)
# PP-OCRv1 rec model uses 32px height, not 48px like v3
_JAPAN_REC_IMG_SHAPE = [3, 32, 320]

_engineCN: RapidOCR | None = None
_engineJP: RapidOCR | None = None

# ── Cyan username mask ─────────────────────────────────────────
# In-game chat usernames are always rendered in cyan; messages are white.
# Strategy: detect cyan pixels, then mask the ENTIRE left portion of each
# affected row (x=0 to last_cyan_x + padding). This removes the username
# block completely — pixel-only replacement leaves anti-aliased ghost edges
# that OCR still picks up.
#
# Real pixel samples from game capture (incl. anti-aliased edges):
#   core:  RGB( 55, 233, 255)  → G-R=178, B-R=200
#   mid:   RGB( 44, 194, 213)  → G-R=150, B-R=169
#   edge:  RGB( 21, 111, 123)  → G-R= 90, B-R=102
#   dark:  RGB( 17,  95, 105)  → G-R= 78, B-R= 88
# White text: G-R=0, B-R=0  → not masked
# Background: G-R=0, B-R=0  → not masked
_CYAN_GB_MINUS_R_THRESHOLD = 70   # both (G-R) and (B-R) must exceed this
_CYAN_G_MIN = 50                  # minimum brightness to exclude pure-dark noise
_CYAN_MASK_RIGHT_PADDING = 5     # extra pixels after last cyan col to erase
_BACKGROUND_COLOR = [20, 20, 20]

# ── Japanese Unicode ranges ────────────────────────────────────
_KANA_START = "぀"  # Hiragana start
_KANA_END = "ヿ"    # Katakana end


def getEngineCN() -> RapidOCR:
    global _engineCN
    if _engineCN is None:
        _engineCN = RapidOCR()
    return _engineCN


def getEngineJP() -> RapidOCR:
    global _engineJP
    if _engineJP is None:
        if not _JAPAN_MODEL_PATH.exists():
            _MODELS_DIR.mkdir(parents=True, exist_ok=True)
            urlretrieve(_JAPAN_MODEL_URL, _JAPAN_MODEL_PATH)
        _engineJP = RapidOCR(
            rec_model_path=str(_JAPAN_MODEL_PATH),
            rec_img_shape=_JAPAN_REC_IMG_SHAPE,
        )
    return _engineJP


def maskCyanText(imageArray: np.ndarray) -> np.ndarray:
    """Remove cyan username text by zeroing the entire left column segment of each affected row."""
    masked = imageArray.copy()
    r = masked[:, :, 0].astype(int)
    g = masked[:, :, 1].astype(int)
    b = masked[:, :, 2].astype(int)
    isCyan = (
        (g - r > _CYAN_GB_MINUS_R_THRESHOLD)
        & (b - r > _CYAN_GB_MINUS_R_THRESHOLD)
        & (g > _CYAN_G_MIN)
    )
    # For each row that contains cyan pixels, erase from x=0 to
    # (last cyan column + padding). This eliminates anti-aliased ghost edges
    # that pixel-level replacement leaves behind.
    for rowIdx in np.where(np.any(isCyan, axis=1))[0]:
        lastCyanX = int(np.where(isCyan[rowIdx])[0][-1]) + _CYAN_MASK_RIGHT_PADDING
        masked[rowIdx, :lastCyanX, :] = _BACKGROUND_COLOR
    return masked


def _containsKana(text: str) -> bool:
    return any(_KANA_START <= c <= _KANA_END for c in text)


def _lineYKey(line) -> int:
    """Quantized y-center of a text box for matching lines between engines."""
    yCoords = [pt[1] for pt in line[0]]
    return round(sum(yCoords) / len(yCoords) / 8) * 8


def _mergeOcrResults(resultCN, resultJP) -> list[str]:
    """Merge CN and JP engine results, selecting the best text per line."""
    if not resultCN and not resultJP:
        return []
    if not resultCN:
        return [line[1] for line in resultJP]
    if not resultJP:
        return [line[1] for line in resultCN]

    def buildIndex(result):
        index = {}
        for line in result:
            key = _lineYKey(line)
            # Keep higher-confidence result when two lines share the same bucket
            if key not in index or float(line[2]) > float(index[key][2]):
                index[key] = line
        return index

    cnByY = buildIndex(resultCN)
    jpByY = buildIndex(resultJP)

    lines = []
    for y in sorted(set(cnByY) | set(jpByY)):
        cn = cnByY.get(y)
        jp = jpByY.get(y)

        if cn and jp:
            cnText, cnScore = cn[1], float(cn[2])
            jpText, jpScore = jp[1], float(jp[2])
            # Japanese kana detected → Japanese model wins regardless of score
            lines.append(jpText if _containsKana(jpText) else
                         (cnText if cnScore >= jpScore else jpText))
        else:
            lines.append((cn or jp)[1])

    return lines


def extractText(imageBytes: bytes) -> str:
    image = Image.open(io.BytesIO(imageBytes)).convert("RGB")
    imageArray = np.array(image)
    maskedArray = maskCyanText(imageArray)

    resultCN, _ = getEngineCN()(maskedArray)
    resultJP, _ = getEngineJP()(maskedArray)

    return "\n".join(_mergeOcrResults(resultCN, resultJP))
