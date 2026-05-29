import uvicorn
from fastapi import FastAPI

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

from api.websocket_handler import router as websocketRouter

UVICORN_HOST = "0.0.0.0"
UVICORN_PORT = 8000
UVICORN_WORKERS = 3
# websockets v14+ enforces Origin validation and rejects non-browser clients.
# wsproto has no such restriction, making it compatible with native app clients.
UVICORN_WS = "wsproto"

app = FastAPI(title="ALCT Server")
app.include_router(websocketRouter)


@app.get("/health")
async def healthCheck():
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host=UVICORN_HOST,
        port=UVICORN_PORT,
        workers=UVICORN_WORKERS,
        ws=UVICORN_WS,
    )
