import json
import re
from pathlib import Path

_DATA = json.loads((Path(__file__).parent / "normalizer_data.json").read_text(encoding="utf-8"))

# ASCII keys lowercased so case-insensitive regex matches look up correctly
_ALIAS_MAP: dict[str, str] = {
    (alias.lower() if alias.isascii() else alias): korean
    for korean, aliases in _DATA["aliases"].items()
    for alias in aliases
}


def _buildAliasEntry(alias: str) -> str:
    if alias.isascii():
        return r"\b" + re.escape(alias) + r"\b"
    return re.escape(alias)


_ALIAS_SORTED = sorted(_ALIAS_MAP, key=len, reverse=True)
_ALIAS_PATTERN = re.compile(
    "|".join(_buildAliasEntry(k) for k in _ALIAS_SORTED),
    re.IGNORECASE,
)


def _escapeXml(text: str) -> str:
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def normalizeText(text: str) -> str:
    text = _escapeXml(text)
    text = _ALIAS_PATTERN.sub(
        lambda m: f"<x>{_ALIAS_MAP[m.group(0).lower() if m.group(0).isascii() else m.group(0)]}</x>",
        text,
    )
    return text
