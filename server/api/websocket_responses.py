from dataclasses import dataclass


@dataclass
class ErrorResponse:
    error: str

@dataclass
class TranslatedTextResponse:
    translatedText: str
    cached: bool

@dataclass
class TranslatedInputResponse:
    translatedInputText: str
    cached: bool

@dataclass
class OcrTextResponse:
    extractedText: str
