# ALCT Server

## Overview
Python translation server deployed on Oracle Cloud Free Tier (ARM, 4core/24GB).
Receives PNG image from client via WebSocket → OCR → text normalization → translate → return result.

## Core Flow
```
[Text message: {"type":"settings","sourceLang":"JA"|"EN"}]
        ↓ session_manager.updateSourceLang → wait for next message

[Binary message: PNG bytes]
        ↓
[rate limit check: 30 req/min per IP]
        ↓
[ocr_service] cyan masking → CN engine + JP engine → merge results
        ↓
[text_normalizer] alias substitution → wrap with <x>Korean</x>
        ↓ empty → {"translatedText": ""} return
        ↓
[session_manager] isDuplicate check
        ↓ same → return cached {"translatedText": ..., "cached": true}
        ↓ different
[translation_service] DeepL POST /v2/translate (source_lang from session)
        ↓
[session_manager] updateSession
        ↓
{"translatedText": "...", "cached": false} return
```

## Tech Stack
- Language: Python 3.11+
- Framework: FastAPI + uvicorn (ws="wsproto")
- OCR: RapidOCR (ONNX Runtime) — CN engine (PP-OCRv3) + JP engine (PP-OCRv1)
- Normalization: text_normalizer.py — alias substitution from normalizer_data.json
- Translation: DeepL Free API (월 50만 자)
- Cache: in-memory dict per session
- Deployment: Oracle Cloud Free Tier ARM VM, Docker

## Key Implementation Notes

### WebSocket Handler
- Handles two message types: text (settings) and binary (PNG image)
- Settings message updates sourceLang in session (no response sent)
- Image message triggers full OCR → normalize → translate pipeline
- On disconnect: clean up session state

### OCR (RapidOCR)
- Dual engine: CN (PP-OCRv3, built-in) + JP (PP-OCRv1, auto-downloaded)
- Cyan pixel masking removes usernames before OCR (row-level column block erase)
- Merge strategy: JP result wins if kana detected, otherwise higher confidence wins
- JP model: models/japan_rec_crnn.onnx (3.6MB, downloaded on first use)

### Text Normalizer
- Single-pass regex substitution from normalizer_data.json
- ASCII aliases: word boundary (\b) + case-insensitive
- Matches wrapped in <x>Korean</x> — DeepL ignore_tags=["x"] skips them
- Covers: gaming slang (gg/ez/ff), Apex characters, weapons, romaji Japanese

### Translation (DeepL)
- source_lang: per-session setting ("JA" or "EN", default "JA")
- target_lang: "KO"
- tag_handling="xml", ignore_tags=["x"] preserves normalizer output
- Multiline text split into array → per-line independent translation

### Translation Dedup
- Per-session: if extractedText == lastExtractedText → return cached translation
- No external cache needed at this scale

### Rate Limiting
- Sliding window: 30 requests/minute per IP
- Implemented manually (slowapi doesn't support WebSocket message-level limiting)

### Concurrency
- FastAPI async WebSocket handlers
- uvicorn multi-worker: `--workers 3` (utilizes ARM 4-core)
- wsproto backend: avoids websockets v14+ Origin validation that rejects native clients

## Project Structure
```
alct-server/
├── main.py
├── core/
│   ├── ocr_service.py           # RapidOCR wrapper, cyan masking, dual engine merge
│   ├── text_normalizer.py       # alias substitution, <x> tag wrapping
│   ├── normalizer_data.json     # Korean → [EN/JP aliases] mapping
│   ├── translation_service.py  # DeepL Free API client
│   └── session_manager.py      # per-session state (dedup, sourceLang)
├── api/
│   └── websocket_handler.py    # WebSocket endpoints, rate limiter
├── requirements.txt
└── tests/
    ├── conftest.py
    ├── test_ocr_service.py
    ├── test_session_manager.py
    ├── test_translation_service.py
    └── test_websocket_handler.py
```

## Infrastructure
- Platform: Oracle Cloud Free Tier ARM VM
- Docker Compose: alct-server only (no LibreTranslate)
- Prod port: 8000 (docker-compose.yml)
- Dev port: 8001 (docker-compose.dev.yml, project name: alct-dev)
- DEEPL_API_KEY: injected via .env → docker-compose environment

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- No raw coding — abstract magic values and repeated logic
- Function and variable names must clearly describe purpose
- One function = one responsibility
- Avoid raw loops; prefer lambdas and list comprehensions

## Testing
- Framework: pytest + pytest-asyncio + pytest-mock
- DeepL calls: mocked (no real API needed)
- RapidOCR calls: mocked (no model download needed)

### Test counts
| File | Count |
|---|---|
| test_ocr_service.py | 22 |
| test_session_manager.py | 9 |
| test_translation_service.py | 6 |
| test_websocket_handler.py | 9 |
