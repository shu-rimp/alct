import os
import re
from html import unescape
import httpx

_KEEP_TAG_RE = re.compile(r"</?x>")
_TAG_CONTENT_RE = re.compile(r"<x>[^<]*</x>")

DEEPL_API_KEY = os.getenv("DEEPL_API_KEY", "")
DEEPL_URL = "https://api-free.deepl.com/v2/translate"
TARGET_LANG = "KO"
REQUEST_TIMEOUT_SECONDS = 10


async def _callDeepL(payload: dict) -> list[dict]:
    headers = {
        "Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}",
        "Content-Type": "application/json",
    }
    async with httpx.AsyncClient(timeout=REQUEST_TIMEOUT_SECONDS) as client:
        response = await client.post(DEEPL_URL, json=payload, headers=headers)
        response.raise_for_status()
        return response.json()["translations"]


async def translateInputText(text: str, targetLang: str = "JA") -> str:
    payload = {
        "text": [text],
        "source_lang": "KO",
        "target_lang": targetLang,
    }
    translations = await _callDeepL(payload)
    return translations[0]["text"]


async def translateText(text: str, sourceLang: str = "JA") -> str:
    lines = text.split("\n")

    translateIndices = [
        i for i, line in enumerate(lines)
        if _TAG_CONTENT_RE.sub("", line).strip()
    ]

    results = [_KEEP_TAG_RE.sub("", line) for line in lines]

    if not translateIndices:
        return "\n".join(results)

    payload = {
        "text": [lines[i] for i in translateIndices],
        "source_lang": sourceLang,
        "target_lang": TARGET_LANG,
        "tag_handling": "xml",
        "ignore_tags": ["x"],
    }
    translations = await _callDeepL(payload)
    for i, t in zip(translateIndices, translations):
        results[i] = unescape(_KEEP_TAG_RE.sub("", t["text"]))

    return "\n".join(results)
