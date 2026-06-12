import io
import pytest
from PIL import Image, ImageDraw, ImageFont


def _makePngBytes(text: str = "Hello World") -> bytes:
    img = Image.new("RGB", (300, 60), color=(255, 255, 255))
    draw = ImageDraw.Draw(img)
    draw.text((10, 15), text, fill=(0, 0, 0))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def _makeBlankPngBytes() -> bytes:
    img = Image.new("RGB", (100, 30), color=(255, 255, 255))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


@pytest.fixture
def samplePngBytes():
    return _makePngBytes("Hello World")


@pytest.fixture
def blankPngBytes():
    return _makeBlankPngBytes()


@pytest.fixture
def pngBytesFactory():
    return _makePngBytes
