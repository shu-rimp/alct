# ALCT Server

Python OCR server: PNG via HTTP POST → RapidOCR (PP-OCRv5 MOBILE, ONNX CPU) → normalization → JSON. Translation happens client-side.
FastAPI + Uvicorn multi-worker. `src/api/` routing, `src/core/` OCR + text data, `test/` pytest.

## Endpoints & data
- `POST /ocr` — token + per-IP rate limit (`CF-Connecting-IP` first) + `MAX_CONCURRENT_OCR` gate
- `GET /glossary` — serves `src/core/glossary_data.json`, read per request: **file swap updates all clients without restart** (clients fetch at startup)
- `GET /health` — no auth
- Auth: `ALCT_SERVER_TOKEN` env ↔ `X-ALCT-Token` header; unset token = auth disabled (self-hosting)
- Data role split: `normalizer_data.json` = chat slang/romaji (OCR path only, applied server-side); `glossary_data.json` = game terms (applied client-side, also embedded in client as offline fallback)

## Deployment
- Oracle Cloud Free Tier ARM (4 vCore / 24 GB) — this is the account-wide free maximum, no second free instance
- Docker + nginx + Cloudflare in front; `docker-compose.dev.yml` for local

## Code Style
camelCase variables (intentional, not PEP8 — author's Java/Kotlin background; do not "fix" to snake_case), UPPER_SNAKE_CASE constants, one function = one responsibility, prefer comprehensions.
