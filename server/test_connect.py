"""
WebSocket 수동 연결 테스트.

사용법:
  python test_connect.py                       # 합성 이미지로 흐름 검증
  python test_connect.py --screenshot          # 현재 화면 캡처 1회 전송
  python test_connect.py --image path/to.png  # 지정 PNG 파일 전송
  python test_connect.py --loop               # 3초마다 화면 캡처 반복 전송

  위 모드에 --stub 추가 시 LibreTranslate 없이 stub 번역 사용
  예) python test_connect.py --screenshot --stub
"""

import argparse
import asyncio
import io
import socket
import sys
import threading
import time

# Windows cp949 콘솔에서 유니코드 출력 가능하도록 강제
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

import websockets
from PIL import Image, ImageDraw


# ── 이미지 소스 ────────────────────────────────────────────────

def makeSyntheticPng(text: str = "Hello OCR Test 123") -> bytes:
    img = Image.new("RGB", (500, 80), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    draw.rectangle([0, 0, 499, 79], fill=(240, 240, 240))
    draw.text((10, 25), text, fill=(0, 0, 0))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def takeScreenshot() -> bytes:
    from PIL import ImageGrab
    img = ImageGrab.grab()
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def loadImageFile(path: str) -> bytes:
    with open(path, "rb") as f:
        data = f.read()
    # PNG 이외 포맷은 PNG로 변환
    img = Image.open(io.BytesIO(data)).convert("RGB")
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


# ── stub LibreTranslate ────────────────────────────────────────

def startStubLibreTranslate():
    from fastapi import FastAPI
    import uvicorn

    stubApp = FastAPI()

    @stubApp.post("/translate")
    async def translate(body: dict):
        return {"translatedText": f"[STUB] {body.get('q', '')}"}

    config = uvicorn.Config(stubApp, host="127.0.0.1", port=5000, log_level="error")
    server = uvicorn.Server(config)
    threading.Thread(target=server.run, daemon=True).start()

    for _ in range(30):
        try:
            with socket.create_connection(("127.0.0.1", 5000), timeout=0.2):
                print("[stub] LibreTranslate stub 서버 시작 (localhost:5000)\n")
                return
        except OSError:
            time.sleep(0.1)

    print("[stub] 경고: stub 서버가 응답하지 않음", file=sys.stderr)


# ── WebSocket 테스트 ───────────────────────────────────────────

def printResponse(label: str, raw: str):
    import json
    try:
        data = json.loads(raw)
        cached = data.get("cached")
        text = data.get("translatedText", "")
        error = data.get("error")
        if error:
            print(f"  {label} → 오류: {error}")
        elif cached is True:
            print(f"  {label} → [캐시] {text}")
        elif cached is False:
            print(f"  {label} → [번역] {text}")
        else:
            print(f"  {label} → {text}")
    except Exception:
        print(f"  {label} → {raw}")


async def runSyntheticTest(ws):
    print("[ 합성 이미지 흐름 테스트 ]\n")

    await ws.send(makeSyntheticPng("Hello OCR Test 123"))
    printResponse("1차 (신규)", await ws.recv())

    await ws.send(makeSyntheticPng("Hello OCR Test 123"))
    printResponse("2차 (중복)", await ws.recv())

    await ws.send(makeSyntheticPng("Different Text 456"))
    printResponse("3차 (변경)", await ws.recv())


async def runScreenshotTest(ws):
    print("[ 화면 캡처 1회 전송 ]\n")
    imageBytes = takeScreenshot()
    print(f"  캡처 크기: {len(imageBytes):,} bytes")
    await ws.send(imageBytes)
    printResponse("응답", await ws.recv())


async def runImageFileTest(ws, path: str):
    print(f"[ 이미지 파일 전송: {path} ]\n")
    imageBytes = loadImageFile(path)
    print(f"  파일 크기: {len(imageBytes):,} bytes")
    await ws.send(imageBytes)
    printResponse("응답", await ws.recv())


async def runLoopTest(ws, intervalSeconds: float = 3.0):
    print(f"[ 화면 캡처 반복 전송 ({intervalSeconds}초 간격) — Ctrl+C로 종료 ]\n")
    count = 0
    while True:
        count += 1
        imageBytes = takeScreenshot()
        await ws.send(imageBytes)
        printResponse(f"{count:3d}회", await ws.recv())
        await asyncio.sleep(intervalSeconds)


async def main(args):
    target = "ws://localhost:8000/ws"
    print(f"연결: {target}\n")

    async with websockets.connect(target) as ws:
        if args.loop:
            await runLoopTest(ws)
        elif args.screenshot:
            await runScreenshotTest(ws)
        elif args.image:
            await runImageFileTest(ws, args.image)
        else:
            await runSyntheticTest(ws)


# ── 진입점 ────────────────────────────────────────────────────

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="ALCT Server WebSocket 테스트")
    parser.add_argument("--stub", action="store_true", help="LibreTranslate stub 서버 사용")
    parser.add_argument("--screenshot", action="store_true", help="현재 화면 캡처 1회 전송")
    parser.add_argument("--image", metavar="PATH", help="지정 이미지 파일 전송")
    parser.add_argument("--loop", action="store_true", help="3초마다 화면 캡처 반복 전송")
    args = parser.parse_args()

    if args.stub:
        print("[모드] stub 번역 사용\n")
        startStubLibreTranslate()
    else:
        print("[모드] 실제 LibreTranslate 사용 (없으면 --stub 추가)\n")

    try:
        asyncio.run(main(args))
    except KeyboardInterrupt:
        print("\n종료")
