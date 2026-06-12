import io
import os

import numpy as np
from PIL import Image
from rapidocr import EngineType, ModelType, OCRVersion, RapidOCR

_engine: RapidOCR | None = None

# ONNX_NUM_THREADS=1 을 환경변수로 설정하면 worker별 스레드 경합을 줄일 수 있음
# 0 이면 ONNX Runtime 기본값(가용 코어 전체) 사용
_ONNX_NUM_THREADS = int(os.getenv("ONNX_NUM_THREADS", "0"))

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
_CYAN_MASK_RIGHT_PADDING = 5      # extra pixels after last cyan col to erase
_BACKGROUND_COLOR = [20, 20, 20]


def _getEngine() -> RapidOCR:
    global _engine
    if _engine is None:
        params: dict = {
            "Det.ocr_version": OCRVersion.PPOCRV5,
            "Det.engine_type": EngineType.ONNXRUNTIME,
            "Det.model_type": ModelType.MOBILE, 
            "Rec.ocr_version": OCRVersion.PPOCRV5,
            "Rec.engine_type": EngineType.ONNXRUNTIME,
            "Rec.model_type": ModelType.MOBILE,  
        }
        if _ONNX_NUM_THREADS > 0:
            for prefix in ("Det", "Rec", "Cls"):
                params[f"{prefix}.intra_op_num_threads"] = _ONNX_NUM_THREADS
                params[f"{prefix}.inter_op_num_threads"] = _ONNX_NUM_THREADS
        _engine = RapidOCR(params=params)
    return _engine


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
    for rowIdx in np.where(np.any(isCyan, axis=1))[0]:
        lastCyanX = int(np.where(isCyan[rowIdx])[0][-1]) + _CYAN_MASK_RIGHT_PADDING
        masked[rowIdx, :lastCyanX, :] = _BACKGROUND_COLOR
    return masked


def extractText(imageBytes: bytes) -> str:
    image = Image.open(io.BytesIO(imageBytes)).convert("RGB")
    imageArray = np.array(image)
    maskedArray = maskCyanText(imageArray)

    output = _getEngine()(maskedArray)
    return "\n".join(output.txts) if output.txts else ""
