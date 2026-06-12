import pytest
from fastapi.testclient import TestClient

from main import app

client = TestClient(app)


class TestOcrEndpoint:
    def test_returnsNormalizedText(self, samplePngBytes, mocker):
        mocker.patch("core.ocr_service.extractText", return_value="gg wp")
        mocker.patch("core.text_normalizer.normalizeText", return_value="<x>굿겜</x> wp")
        r = client.post("/ocr", content=samplePngBytes)
        assert r.status_code == 200
        assert r.json() == {"normalizedText": "<x>굿겜</x> wp", "rawText": "gg wp"}

    def test_returnsEmptyNormalizedText_whenOcrEmpty(self, samplePngBytes, mocker):
        mocker.patch("core.ocr_service.extractText", return_value="")
        mocker.patch("core.text_normalizer.normalizeText", return_value="")
        r = client.post("/ocr", content=samplePngBytes)
        assert r.status_code == 200
        assert r.json()["normalizedText"] == ""

    def test_returns429_whenRateLimitExceeded(self, samplePngBytes, mocker):
        mocker.patch("api.http_router._isRateLimited", return_value=True)
        r = client.post("/ocr", content=samplePngBytes)
        assert r.status_code == 429

    def test_returns400_whenEmptyBody(self):
        r = client.post("/ocr", content=b"")
        assert r.status_code == 400


class TestTokenAuth:
    def test_returns403_whenTokenMissing(self, samplePngBytes, mocker):
        mocker.patch("api.http_router._SERVER_TOKEN", "secret")
        r = client.post("/ocr", content=samplePngBytes)
        assert r.status_code == 403

    def test_returns403_whenTokenInvalid(self, samplePngBytes, mocker):
        mocker.patch("api.http_router._SERVER_TOKEN", "secret")
        r = client.post("/ocr", content=samplePngBytes, headers={"X-ALCT-Token": "wrong"})
        assert r.status_code == 403

    def test_returns200_whenTokenValid(self, samplePngBytes, mocker):
        mocker.patch("api.http_router._SERVER_TOKEN", "secret")
        mocker.patch("core.ocr_service.extractText", return_value="gg")
        mocker.patch("core.text_normalizer.normalizeText", return_value="gg")
        r = client.post("/ocr", content=samplePngBytes, headers={"X-ALCT-Token": "secret"})
        assert r.status_code == 200

    def test_returns200_whenNoServerToken(self, samplePngBytes, mocker):
        mocker.patch("api.http_router._SERVER_TOKEN", None)
        mocker.patch("core.ocr_service.extractText", return_value="gg")
        mocker.patch("core.text_normalizer.normalizeText", return_value="gg")
        r = client.post("/ocr", content=samplePngBytes)
        assert r.status_code == 200

    def test_healthReturns200_withoutToken(self):
        r = client.get("/health")
        assert r.status_code == 200


class TestHealthCheck:
    def test_returns200(self):
        r = client.get("/health")
        assert r.status_code == 200
        assert r.json() == {"status": "ok"}
