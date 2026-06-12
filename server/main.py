import os
import uvicorn
from contextlib import asynccontextmanager
from fastapi import FastAPI

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

from api.http_router import router as httpRouter
from core import ocr_service

UVICORN_HOST = "0.0.0.0"
UVICORN_PORT = 8000
UVICORN_WORKERS = int(os.getenv("UVICORN_WORKERS", "3"))


@asynccontextmanager
async def lifespan(app: FastAPI):
    ocr_service._getEngine()  # 워커 시작 시 모델 로드 — 첫 요청 지연 방지
    yield


app = FastAPI(title="ALCT Server", lifespan=lifespan)
app.include_router(httpRouter)


@app.get("/health")
async def healthCheck():
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host=UVICORN_HOST,
        port=UVICORN_PORT,
        workers=UVICORN_WORKERS,
    )
