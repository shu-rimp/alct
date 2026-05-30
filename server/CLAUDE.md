# ALCT Server

## Overview
Python 번역 서버. WebSocket으로 PNG 수신 → OCR → 정규화 → 번역 → 결과 반환.

## Core Flow
```
[Text: {"type":"settings","sourceLang":"JA"|"ZH"|"EN"}]
        → session_manager.updateSourceLang

[Binary: PNG bytes]
        → rate limit (30 req/min per IP)
        → ocr_service (cyan masking → CN + JP 엔진 병합)
        → text_normalizer (alias 치환 → <x>Korean</x> 래핑)
        → session_manager.isDuplicate → 캐시 반환 or
        → translation_service (DeepL) → session 업데이트 → 반환
```

## Tech Stack
- Python 3.11+, FastAPI + uvicorn (`ws="wsproto"`)
- OCR: RapidOCR — CN (PP-OCRv3) + JP (PP-OCRv1)
- Translation: DeepL Free API (월 100만 자)
- Deployment: Oracle Cloud Free Tier ARM, Docker

## Key Notes

### WebSocket
- `websocket.receive()`는 disconnect 시 `{"type": "websocket.disconnect"}` dict 반환 → `message["type"] == "websocket.disconnect"` 체크 후 break (`WebSocketDisconnect` 예외는 항상 발생하지 않음)
- 세션 정리는 `finally` 블록에서 처리

### OCR
- Dual engine: 가나 감지 시 JP 결과 우선, 아니면 confidence 높은 쪽 선택
- Cyan 픽셀 마스킹으로 유저명 제거 후 OCR

### Translation
- `tag_handling="xml", ignore_tags=["x"]` → normalizer의 `<x>Korean</x>` 보존
- source_lang: 세션별 설정, target_lang: 항상 "KO"

### Concurrency
- `wsproto` 백엔드: websockets v14+ Origin 검증 우회 (네이티브 클라이언트 접속 가능)

## Project Structure
```
alct-server/
├── main.py
├── core/
│   ├── ocr_service.py
│   ├── text_normalizer.py
│   ├── normalizer_data.json
│   ├── translation_service.py
│   └── session_manager.py
├── api/
│   └── websocket_handler.py
├── requirements.txt
└── tests/
```

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer lambdas and list comprehensions
