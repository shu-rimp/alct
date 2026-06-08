import os
import time
from collections import defaultdict

from fastapi import APIRouter, Depends, Header, HTTPException, Request
from fastapi.responses import JSONResponse

from api.http_responses import ErrorResponse, NormalizedTextResponse
from core import ocr_service, text_normalizer

_SERVER_TOKEN = os.getenv("ALCT_SERVER_TOKEN")


def _verifyToken(x_alct_token: str | None = Header(default=None)) -> None:
    if _SERVER_TOKEN and x_alct_token != _SERVER_TOKEN:
        raise HTTPException(status_code=403)

RATE_LIMIT_MAX_REQUESTS = 30
RATE_LIMIT_WINDOW_SECONDS = 60

_requestTimestamps: dict[str, list[float]] = defaultdict(list)

router = APIRouter()


def _getClientIp(request: Request) -> str:
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


@router.post("/ocr", dependencies=[Depends(_verifyToken)])
async def ocrEndpoint(request: Request):
    clientIp = _getClientIp(request)
    if _isRateLimited(clientIp):
        return JSONResponse(ErrorResponse(error="rate limit exceeded").model_dump(), status_code=429)
    imageBytes = await request.body()
    if not imageBytes:
        return JSONResponse(ErrorResponse(error="no image data").model_dump(), status_code=400)
    extractedText = ocr_service.extractText(imageBytes)
    normalizedText = text_normalizer.normalizeText(extractedText)
    return NormalizedTextResponse(normalizedText=normalizedText, rawText=extractedText)
