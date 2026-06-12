# ALCT Server

## Overview
Python OCR server. Receives PNG via HTTP POST → OCR → normalization → returns result. Translation is handled by the client.

## Project Structure
```
server/
├── main.py
├── api/
│   ├── http_router.py
│   └── http_responses.py
├── core/
│   ├── ocr_service.py
│   ├── text_normalizer.py
│   └── normalizer_data.json
├── tests/
│   ├── conftest.py
│   ├── test_ocr_service.py
│   ├── test_ocr_service.py
│   └── test_server_load.py
├── Dockerfile
├── docker-compose.dev.yml
├── docker-compose.yml
├── requirements-dev.txt
└── requirements.txt
```

## Tech Stack
- Python 3.11+, FastAPI, Uvicorn (multi-worker)
- OCR: RapidOCR 3.8.3 — PP-OCRv5 Det + Rec (MOBILE), ONNX Runtime (CPU)
- Deployment: Oracle Cloud Free Tier ARM (4 vCore / 24 GB), Docker, Cloudflare, nginx

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer list comprehensions and lambdas
