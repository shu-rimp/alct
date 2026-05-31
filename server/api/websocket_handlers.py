import time
from collections import defaultdict
from dataclasses import asdict
from fastapi import WebSocket

from core import ocr_service, translation_service, session_manager, text_normalizer
from api.websocket_responses import (
    ErrorResponse, TranslatedTextResponse, TranslatedInputResponse, OcrTextResponse
)

RATE_LIMIT_MAX_REQUESTS = 30
RATE_LIMIT_WINDOW_SECONDS = 60

_requestTimestamps: dict[str, list[float]] = defaultdict(list)


async def send(websocket: WebSocket, response) -> None:
    await websocket.send_json(asdict(response))


def getClientIp(websocket: WebSocket) -> str:
    forwarded = websocket.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    return websocket.client.host if websocket.client else "unknown"


def _isRateLimited(ip: str) -> bool:
    now = time.monotonic()
    windowStart = now - RATE_LIMIT_WINDOW_SECONDS
    _requestTimestamps[ip] = [t for t in _requestTimestamps[ip] if t > windowStart]
    if len(_requestTimestamps[ip]) >= RATE_LIMIT_MAX_REQUESTS:
        return True
    _requestTimestamps[ip].append(now)
    return False


async def checkRateLimit(websocket: WebSocket, clientIp: str) -> bool:
    if _isRateLimited(clientIp):
        await send(websocket, ErrorResponse("rate limit exceeded"))
        return True
    return False


async def handleTextMessage(websocket: WebSocket, clientIp: str, data: dict):
    match data.get("type"):
        case "settings":
            session_manager.updateSourceLang(clientIp, data.get("sourceLang", "JA"))
        case "translateInput":
            inputText = data.get("text", "").strip()
            if not inputText:
                return
            if await checkRateLimit(websocket, clientIp):
                return
            targetLang = session_manager.getSourceLang(clientIp)
            try:
                result = await translation_service.translateInputText(inputText, targetLang)
                await send(websocket, TranslatedInputResponse(result))
            except Exception:
                await send(websocket, ErrorResponse("translation failed"))


async def handleImageMessage(websocket: WebSocket, clientIp: str, imageBytes: bytes):
    extractedText = ocr_service.extractText(imageBytes)
    sourceLang = session_manager.getSourceLang(clientIp)
    extractedText = text_normalizer.normalizeText(extractedText)

    if not extractedText:
        await send(websocket, TranslatedTextResponse(translatedText="", cached=False))
        return

    if session_manager.isDuplicate(clientIp, extractedText):
        cachedTranslation = session_manager.getCachedTranslation(clientIp)
        await send(websocket, TranslatedTextResponse(translatedText=cachedTranslation, cached=True))
        return

    try:
        translatedText = await translation_service.translateText(extractedText, sourceLang)
    except Exception:
        await send(websocket, ErrorResponse("translation failed"))
        return

    session_manager.updateSession(clientIp, extractedText, translatedText)
    await send(websocket, TranslatedTextResponse(translatedText=translatedText, cached=False))


async def handleOcrMessage(websocket: WebSocket, clientIp: str, imageBytes: bytes):
    extractedText = ocr_service.extractText(imageBytes)
    await send(websocket, OcrTextResponse(extractedText))
