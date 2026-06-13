import asyncio
import os
import time
from collections import defaultdict
from pathlib import Path

from fastapi import APIRouter, Depends, Header, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse

from api.http_responses import ErrorResponse, NormalizedTextResponse
from core import ocr_service, text_normalizer

_SERVER_TOKEN = os.getenv("ALCT_SERVER_TOKEN")


def _verifyToken(x_alct_token: str | None = Header(default=None)) -> None:
    if _SERVER_TOKEN and x_alct_token != _SERVER_TOKEN:
        raise HTTPException(status_code=403)

RATE_LIMIT_MAX_REQUESTS = 30
RATE_LIMIT_WINDOW_SECONDS = 60
MAX_CONCURRENT_OCR = int(os.getenv("MAX_CONCURRENT_OCR", "2"))

_requestTimestamps: dict[str, list[float]] = defaultdict(list)
_activeOcrCount = 0

router = APIRouter()


def _getClientIp(request: Request) -> str:
    cf_ip = request.headers.get("cf-connecting-ip")
    if cf_ip:
        return cf_ip
    forwarded = request.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    return request.client.host if request.client else "unknown"


def _isRateLimited(ip: str) -> bool:
    now = time.monotonic()
    windowStart = now - RATE_LIMIT_WINDOW_SECONDS
    _requestTimestamps[ip] = [t for t in _requestTimestamps[ip] if t > windowStart]
    if len(_requestTimestamps[ip]) >= RATE_LIMIT_MAX_REQUESTS:
        return True
    _requestTimestamps[ip].append(now)
    return False


GLOSSARY_PATH = Path(__file__).resolve().parent.parent / "core" / "glossary_data.json"


@router.get("/health")
async def healthCheck():
    return {"status": "ok"}


# 게임 용어집 — 파일만 교체하면 클라이언트 재배포 없이 용어집 갱신 가능
@router.get("/glossary", dependencies=[Depends(_verifyToken)])
async def glossaryEndpoint():
    return FileResponse(GLOSSARY_PATH, media_type="application/json")


@router.post("/ocr", dependencies=[Depends(_verifyToken)])
async def ocrEndpoint(request: Request):
    global _activeOcrCount
    clientIp = _getClientIp(request)
    if _isRateLimited(clientIp):
        return JSONResponse(ErrorResponse(error="rate limit exceeded").model_dump(), status_code=429)
    if _activeOcrCount >= MAX_CONCURRENT_OCR:
        return JSONResponse(ErrorResponse(error="server busy").model_dump(), status_code=503)
    imageBytes = await request.body()
    if not imageBytes:
        return JSONResponse(ErrorResponse(error="no image data").model_dump(), status_code=400)
    _activeOcrCount += 1
    try:
        extractedText = await asyncio.to_thread(ocr_service.extractText, imageBytes)
        normalizedText = text_normalizer.normalizeText(extractedText)
        return NormalizedTextResponse(normalizedText=normalizedText, rawText=extractedText)
    finally:
        _activeOcrCount -= 1
