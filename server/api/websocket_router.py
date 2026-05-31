import json
from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from core import session_manager
from api.websocket_handlers import (
    getClientIp, checkRateLimit, handleTextMessage, handleImageMessage, handleOcrMessage
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


    """OCR 결과만 반환 — 번역 없음. 실제 클라이언트 연동 테스트용."""
@router.websocket("/ws/ocr")
async def ocrOnlyEndpoint(websocket: WebSocket):
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
