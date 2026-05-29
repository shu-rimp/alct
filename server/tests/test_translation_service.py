import pytest
import httpx
from unittest.mock import AsyncMock, patch, MagicMock

from core.translation_service import translateText, DEEPL_URL, TARGET_LANG


class TestTranslateText:
    @pytest.mark.asyncio
    async def test_returnsTranslatedText(self, mocker):
        mockResponse = MagicMock()
        mockResponse.json.return_value = {"translations": [{"text": "안녕하세요"}]}
        mockResponse.raise_for_status = MagicMock()

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        result = await translateText("Hello")

        assert result == "안녕하세요"

    @pytest.mark.asyncio
    async def test_returnsMultiLineTranslation(self, mocker):
        mockResponse = MagicMock()
        mockResponse.json.return_value = {
            "translations": [{"text": "안녕하세요"}, {"text": "잘 지내?"}]
        }
        mockResponse.raise_for_status = MagicMock()

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        result = await translateText("Hello\nHow are you")

        assert result == "안녕하세요\n잘 지내?"

    @pytest.mark.asyncio
    async def test_sendsCorrectPayload(self, mocker):
        mockResponse = MagicMock()
        mockResponse.json.return_value = {"translations": [{"text": "테스트"}]}
        mockResponse.raise_for_status = MagicMock()

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        await translateText("test")

        _, callKwargs = mockPost.call_args
        payload = callKwargs.get("json") or mockPost.call_args[0][1]
        assert payload["text"] == ["test"]
        assert payload["target_lang"] == TARGET_LANG
        assert payload["tag_handling"] == "xml"
        assert payload["ignore_tags"] == ["x"]

    @pytest.mark.asyncio
    async def test_stripsXmlTagsFromResult(self, mocker):
        mockResponse = MagicMock()
        mockResponse.json.return_value = {"translations": [{"text": "<x>레이스</x> 차이"}]}
        mockResponse.raise_for_status = MagicMock()

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        result = await translateText("wraith diff")

        assert result == "레이스 차이"

    @pytest.mark.asyncio
    async def test_raisesOnHttpError(self, mocker):
        mockResponse = MagicMock()
        mockResponse.raise_for_status.side_effect = httpx.HTTPStatusError(
            "500", request=MagicMock(), response=MagicMock()
        )

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        with pytest.raises(httpx.HTTPStatusError):
            await translateText("error test")

    @pytest.mark.asyncio
    async def test_postsToCorrectUrl(self, mocker):
        mockResponse = MagicMock()
        mockResponse.json.return_value = {"translations": [{"text": "결과"}]}
        mockResponse.raise_for_status = MagicMock()

        mockPost = AsyncMock(return_value=mockResponse)
        mocker.patch("httpx.AsyncClient.post", mockPost)

        await translateText("test")

        calledUrl = mockPost.call_args[0][0]
        assert calledUrl == DEEPL_URL
