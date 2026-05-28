# ALCT Server

## Overview
Python translation server deployed on Oracle Cloud Free Tier (ARM, 4core/24GB).
Receives PNG image from client via WebSocket → OCR → translate → return result.

## Core Flow
```
[Receive PNG image via WebSocket]
        ↓
[RapidOCR → extract text]
        ↓
[Compare with session's last extracted text]
        ↓ same → skip translation, return cached result
        ↓ different → LibreTranslate → cache result
        ↓
[Return translated text via WebSocket]
```

## Tech Stack
- Language: Python 3.11+
- Framework: FastAPI + uvicorn
- OCR: RapidOCR (ONNX Runtime backend)
- Translation: LibreTranslate (self-hosted, unmodified, REST API call)
- Cache: in-memory dict per session (no Redis needed at this scale)
- Deployment: Oracle Cloud Free Tier ARM VM

## Key Implementation Notes

### WebSocket Handler
- One persistent WebSocket connection per client
- Each session maintains `lastExtractedText` for dedup
- On disconnect: clean up session state

### OCR (RapidOCR)
- Load model once at startup, reuse across requests
- Input: PNG bytes from client
- Output: extracted text string

### Translation Dedup
- Per-session comparison: if `extractedText == session.lastExtractedText` → skip
- Return cached translation immediately without calling LibreTranslate

### LibreTranslate
- Self-hosted on same Oracle VM, default port 5000
- Used as-is (no modification) → AGPL-3.0 obligation does not apply to server code
- Source lang: auto-detect
- Target lang: Korean (ko), configurable

### Rate Limiting
- Per-IP: 30 requests/minute (slowapi)
- Prevents server abuse

### Concurrency
- FastAPI async WebSocket handlers
- uvicorn multi-worker: `--workers 3` (utilizes ARM 4-core)

## Project Structure
```
alct-server/
├── main.py
├── core/
│   ├── ocr_service.py       # RapidOCR wrapper
│   ├── translation_service.py # LibreTranslate client
│   └── session_manager.py   # per-session state
├── api/
│   └── websocket_handler.py
├── requirements.txt
└── tests/
    ├── test_ocr_service.py
    ├── test_translation_service.py
    └── test_websocket_handler.py
```

## Infrastructure
- Platform: Oracle Cloud Free Tier ARM VM
- LibreTranslate: self-hosted, same VM, port 5000
- Check RAM before deploy: `free -h` (need ~4GB free)

## Development Environment
- IDE: VS Code (Python extension + Pylance)
- Package manager: pip + requirements.txt

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- No raw coding — abstract magic values and repeated logic
- Function and variable names must clearly describe purpose
- One function = one responsibility
- Avoid raw loops; prefer lambdas and list comprehensions

## Testing
- Framework: pytest
- Tools: httpx (TestClient), pytest-mock

### Strategy
| Target | Tool | Method |
|---|---|---|
| RapidOCR text extraction | pytest | Sample PNG fixtures |
| Translation dedup logic | pytest | Unit test with mock session |
| LibreTranslate integration | pytest + pytest-mock | Mock HTTP calls |
| WebSocket flow | pytest + httpx | Connect → send image → assert response |
| Rate limiting | pytest | Simulate burst requests |
