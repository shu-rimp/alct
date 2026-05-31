import pytest
from fastapi.testclient import TestClient
from unittest.mock import patch, MagicMock, AsyncMock

from main import app
from core.session_manager import _sessions
from api.websocket_handlers import _requestTimestamps

CLIENT_IP = "testclient"


@pytest.fixture(autouse=True)
def cleanupState():
    _sessions.clear()
    _requestTimestamps.clear()
    yield
    _sessions.clear()
    _requestTimestamps.clear()


@pytest.fixture
def client():
    return TestClient(app)


class TestWebSocketFlow:
    def test_receivesTranslatedTextForNewImage(self, client, samplePngBytes):
        with (
            patch("core.ocr_service.extractText", return_value="Hello") as _,
            patch(
                "core.translation_service.translateText",
                new=AsyncMock(return_value="안녕하세요"),
            ),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                response = ws.receive_json()

        assert response["translatedText"] == "안녕하세요"
        assert response["cached"] is False

    def test_returnsCachedResultForDuplicateImage(self, client, samplePngBytes):
        translateMock = AsyncMock(return_value="안녕하세요")
        with (
            patch("core.ocr_service.extractText", return_value="Hello"),
            patch("core.translation_service.translateText", new=translateMock),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                ws.receive_json()

                ws.send_bytes(samplePngBytes)
                response = ws.receive_json()

        assert response["translatedText"] == "안녕하세요"
        assert response["cached"] is True
        assert translateMock.call_count == 1

    def test_returnsEmptyStringWhenOcrExtractsNothing(self, client, blankPngBytes):
        with patch("core.ocr_service.extractText", return_value=""):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(blankPngBytes)
                response = ws.receive_json()

        assert response["translatedText"] == ""

    def test_returnsErrorWhenTranslationFails(self, client, samplePngBytes):
        with (
            patch("core.ocr_service.extractText", return_value="Hello"),
            patch(
                "core.translation_service.translateText",
                new=AsyncMock(side_effect=Exception("LibreTranslate down")),
            ),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                response = ws.receive_json()

        assert response.get("error") == "translation failed"

    def test_returnsErrorWhenRateLimitExceeded(self, client, samplePngBytes):
        translateMock = AsyncMock(return_value="번역")
        with (
            patch("core.ocr_service.extractText", return_value="text"),
            patch("core.translation_service.translateText", new=translateMock),
            patch("api.websocket_handlers._isRateLimited", return_value=True),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                response = ws.receive_json()

        assert "error" in response
        assert response["error"] == "rate limit exceeded"


class TestRateLimiting:
    def test_allowsRequestsBelowLimit(self, client, samplePngBytes):
        translateMock = AsyncMock(return_value="번역")
        with (
            patch("core.ocr_service.extractText", return_value="text"),
            patch("core.translation_service.translateText", new=translateMock),
        ):
            with client.websocket_connect("/ws") as ws:
                for _ in range(5):
                    ws.send_bytes(samplePngBytes)
                    response = ws.receive_json()
                    assert "error" not in response

    def test_blocksRequestsAboveLimit(self, client, samplePngBytes):
        translateMock = AsyncMock(return_value="번역")
        with (
            patch("core.ocr_service.extractText", return_value="text"),
            patch("core.translation_service.translateText", new=translateMock),
        ):
            # Pre-fill timestamps to simulate hitting the limit
            import time
            from api.websocket_handlers import RATE_LIMIT_MAX_REQUESTS, _requestTimestamps
            now = time.monotonic()
            _requestTimestamps[CLIENT_IP] = [now] * RATE_LIMIT_MAX_REQUESTS

            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                response = ws.receive_json()

        assert response.get("error") == "rate limit exceeded"


class TestSessionCleanup:
    def test_settingsMessageUpdatesSourceLang(self, client, samplePngBytes):
        import json as _json
        from core.session_manager import getSourceLang
        with (
            patch("core.ocr_service.extractText", return_value="Hello"),
            patch("core.translation_service.translateText", new=AsyncMock(return_value="안녕")),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_text(_json.dumps({"type": "settings", "sourceLang": "EN"}))
                ws.send_bytes(samplePngBytes)
                ws.receive_json()
                lang = getSourceLang(CLIENT_IP)

        assert lang == "EN"

    def test_sessionRemovedOnDisconnect(self, client, samplePngBytes):
        with (
            patch("core.ocr_service.extractText", return_value="Hello"),
            patch("core.translation_service.translateText", new=AsyncMock(return_value="안녕")),
        ):
            with client.websocket_connect("/ws") as ws:
                ws.send_bytes(samplePngBytes)
                ws.receive_json()

        assert CLIENT_IP not in _sessions


class TestHealthCheck:
    def test_healthEndpointReturnsOk(self, client):
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok"}
