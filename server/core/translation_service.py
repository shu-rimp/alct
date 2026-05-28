import httpx

LIBRE_TRANSLATE_URL = "http://localhost:5000/translate"
SOURCE_LANG = "auto"
TARGET_LANG = "ko"
REQUEST_TIMEOUT_SECONDS = 10


async def translateText(text: str) -> str:
    payload = {
        "q": text,
        "source": SOURCE_LANG,
        "target": TARGET_LANG,
        "format": "text",
    }

    async with httpx.AsyncClient(timeout=REQUEST_TIMEOUT_SECONDS) as client:
        response = await client.post(LIBRE_TRANSLATE_URL, json=payload)
        response.raise_for_status()
        return response.json()["translatedText"]
