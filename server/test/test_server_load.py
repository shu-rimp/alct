"""
ALCT OCR server load test script

how to:
  1. pip install locust pillow

  2. locust -f test/test_server_load.py --host=https://api.example.com \
    --headless -u 20 -r 5 --run-time 3m --csv=test/results/scenario_A
    
    # results are saved in .csv
    # --host: remote server URL
    # -u: user count / -r: ramp-up rate

  3. repeat per scenarios

environment:
  ALCT_SERVER_TOKEN  server_token (omitted if not configured)

scenarios example:
  (Apply to remote server's environment)
  A: UVICORN_WORKERS=3, ONNX_NUM_THREADS=0 (default)
  B: UVICORN_WORKERS=3, ONNX_NUM_THREADS=1
  C: UVICORN_WORKERS=4, ONNX_NUM_THREADS=1 
  D: UVICORN_WORKERS=2, ONNX_NUM_THREADS=2
"""
import io
import os
import random

import numpy as np
from locust import HttpUser, between, task
from PIL import Image, ImageDraw, ImageFont

_TOKEN = os.getenv("ALCT_SERVER_TOKEN", "")

_IMG_W, _IMG_H = 800, 80
_BG_COLOR = (20, 20, 20)
_TEXT_COLOR = (255, 255, 255)
_SAMPLE_LINES = [
    "Hello, I'm from Korea. Nice to meet you",
    "I think it would be better to move to another place.",
    "It's dangerous here. Run away.",
    "I'm out of ammo. Can you give me some?",
]


def _makePng(text: str) -> bytes:
    img = Image.new("RGB", (_IMG_W, _IMG_H), color=_BG_COLOR)
    draw = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("arial.ttf", 16)
    except OSError:
        font = ImageFont.load_default()
    draw.text((10, 30), text, fill=_TEXT_COLOR, font=font)
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


_PNG_POOL = [_makePng(line) for line in _SAMPLE_LINES]


def _randomIp() -> str:
    return f"10.{random.randint(0,255)}.{random.randint(0,255)}.{random.randint(1,254)}"


class OcrUser(HttpUser):
    wait_time = between(0.3, 1.5)

    def on_start(self):
        self._ip = _randomIp()
        self._headers = {"Content-Type": "image/png", "X-Forwarded-For": self._ip}
        if _TOKEN:
            self._headers["X-ALCT-Token"] = _TOKEN

    @task(9)
    def ocr(self):
        png = random.choice(_PNG_POOL)
        self.client.post("/ocr", data=png, headers=self._headers, name="/ocr")

    @task(1)
    def health(self):
        self.client.get("/health", name="/health")
