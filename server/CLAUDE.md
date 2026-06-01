# ALCT Server

## Overview
Python OCR 서버. HTTP POST로 PNG 수신 → OCR → 정규화 → 결과 반환. 번역은 클라이언트 담당.

## Core Flow
```
POST /ocr (PNG bytes)
        → rate limit (30 req/min per IP)
        → ocr_service (cyan masking → CN + JP 엔진 병합)
        → text_normalizer (alias 치환 → <x>Korean</x> 래핑)
        → {"normalizedText": "..."} 반환
```

## Tech Stack
- Python 3.11+, FastAPI + uvicorn
- OCR: RapidOCR — CN (PP-OCRv3) + JP (PP-OCRv1)
- Deployment: Oracle Cloud Free Tier ARM, Docker

## Key Notes

### HTTP API
- `POST /ocr`: raw PNG bytes body → `{"normalizedText": "..."}` 반환
- Rate limit: IP당 30req/60s (커스텀 `_requestTimestamps` dict, x-forwarded-for 지원)
- 빈 body → 400, rate limit 초과 → 429

### OCR
- Dual engine: 가나 감지 시 JP 결과 우선, 아니면 confidence 높은 쪽 선택
- Cyan 픽셀 마스킹으로 유저명 제거 후 OCR

### Tag Handling
- normalizer가 `<x>Korean</x>` 태그로 감싼 별칭은 클라이언트 DeepL 호출 시 `ignore_tags=["x"]`로 보존됨

## Project Structure
```
alct-server/
├── main.py
├── core/
│   ├── ocr_service.py
│   ├── text_normalizer.py
│   └── normalizer_data.json
├── api/
│   ├── http_router.py
│   └── http_responses.py
├── requirements.txt
└── tests/
```

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer lambdas and list comprehensions
