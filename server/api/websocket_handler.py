import time
from collections import defaultdict
from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from core import ocr_service, translation_service, session_manager

RATE_LIMIT_MAX_REQUESTS = 30
RATE_LIMIT_WINDOW_SECONDS = 60

router = APIRouter()

# {ip: [timestamp, ...]}
_requestTimestamps: dict[str, list[float]] = defaultdict(list)


def _getClientIp(websocket: WebSocket) -> str:
    forwarded = websocket.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    return websocket.client.host if websocket.client else "unknown"


def _isRateLimited(ip: str) -> bool:
    now = time.monotonic()
    windowStart = now - RATE_LIMIT_WINDOW_SECONDS
    timestamps = _requestTimestamps[ip]

    # Drop timestamps outside the window
    _requestTimestamps[ip] = [t for t in timestamps if t > windowStart]

    if len(_requestTimestamps[ip]) >= RATE_LIMIT_MAX_REQUESTS:
        return True

    _requestTimestamps[ip].append(now)
    return False


@router.websocket("/ws")
async def websocketEndpoint(websocket: WebSocket):
    await websocket.accept()

    clientIp = _getClientIp(websocket)

    try:
        while True:
            imageBytes = await websocket.receive_bytes()

            if _isRateLimited(clientIp):
                await websocket.send_json({"error": "rate limit exceeded"})
                continue

            extractedText = ocr_service.extractText(imageBytes)

            if not extractedText:
                await websocket.send_json({"translatedText": ""})
                continue

            if session_manager.isDuplicate(clientIp, extractedText):
                cachedTranslation = session_manager.getCachedTranslation(clientIp)
                await websocket.send_json({"translatedText": cachedTranslation, "cached": True})
                continue

            try:
                translatedText = await translation_service.translateText(extractedText)
            except Exception:
                await websocket.send_json({"error": "translation failed"})
                continue

            session_manager.updateSession(clientIp, extractedText, translatedText)

            await websocket.send_json({"translatedText": translatedText, "cached": False})

    except WebSocketDisconnect:
        session_manager.removeSession(clientIp)


@router.websocket("/ws/ocr")
async def ocrOnlyEndpoint(websocket: WebSocket):
    """OCR 결과만 반환 — 번역 없음. 실제 클라이언트 연동 테스트용."""
    await websocket.accept()

    clientIp = _getClientIp(websocket)

    try:
        while True:
            imageBytes = await websocket.receive_bytes()

            if _isRateLimited(clientIp):
                await websocket.send_json({"error": "rate limit exceeded"})
                continue

            extractedText = ocr_service.extractText(imageBytes)
            await websocket.send_json({"extractedText": extractedText})

    except WebSocketDisconnect:
        pass
