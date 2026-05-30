import os
import re
from html import unescape
import httpx

_KEEP_TAG_RE = re.compile(r"</?x>")

DEEPL_API_KEY = os.getenv("DEEPL_API_KEY", "")
DEEPL_URL = "https://api-free.deepl.com/v2/translate"
TARGET_LANG = "KO"
REQUEST_TIMEOUT_SECONDS = 10


async def translateText(text: str, sourceLang: str = "JA") -> str:
    lines = text.split("\n")
    payload = {
        "text": lines,
        "source_lang": sourceLang,
        "target_lang": TARGET_LANG,
        "tag_handling": "xml",
        "ignore_tags": ["x"],
    }
    headers = {
        "Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}",
        "Content-Type": "application/json",
    }

    async with httpx.AsyncClient(timeout=REQUEST_TIMEOUT_SECONDS) as client:
        response = await client.post(DEEPL_URL, json=payload, headers=headers)
        response.raise_for_status()
        translations = response.json()["translations"]
        return "\n".join(unescape(_KEEP_TAG_RE.sub("", t["text"])) for t in translations)
