import json
from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from core import session_manager
from api.websocket_handlers import (
    getClientIp, checkRateLimit,
    handleTextMessage, handleImageMessage, handleCaptionTextMessage, handleOcrMessage,
)

router = APIRouter()


@router.websocket("/ws")
async def websocketEndpoint(websocket: WebSocket):
    await websocket.accept()
    clientIp = getClientIp(websocket)

    try:
        while True:
            message = await websocket.receive()

            if message["type"] == "websocket.disconnect":
                break

            if await checkRateLimit(websocket, clientIp):
                continue

            if text := message.get("text"):
                await handleTextMessage(websocket, clientIp, json.loads(text))
            elif imageBytes := message.get("bytes"):
                await handleImageMessage(websocket, clientIp, imageBytes)

    except WebSocketDisconnect:
        pass
    finally:
        session_manager.removeSession(clientIp)


@router.websocket("/ws/caption")
async def captionEndpoint(websocket: WebSocket):
    """라이브 캡션 전용 — UIA로 추출한 텍스트를 직접 번역."""
    await websocket.accept()
    clientIp = getClientIp(websocket)

    try:
        while True:
            message = await websocket.receive()

            if message["type"] == "websocket.disconnect":
                break

            if await checkRateLimit(websocket, clientIp):
                continue

            if text := message.get("text"):
                data = json.loads(text)
                if data.get("type") == "translateCaption":
                    captionText = data.get("text", "").strip()
                    if captionText:
                        await handleCaptionTextMessage(websocket, clientIp, captionText)

    except WebSocketDisconnect:
        pass
    finally:
        session_manager.removeSession(clientIp)


@router.websocket("/ws/ocr")
async def ocrOnlyEndpoint(websocket: WebSocket):
    """OCR 결과만 반환 — 번역 없음. 실제 클라이언트 연동 테스트용."""
    await websocket.accept()
    clientIp = getClientIp(websocket)

    try:
        while True:
            imageBytes = await websocket.receive_bytes()
            if await checkRateLimit(websocket, clientIp):
                continue
            await handleOcrMessage(websocket, clientIp, imageBytes)

    except WebSocketDisconnect:
        pass
