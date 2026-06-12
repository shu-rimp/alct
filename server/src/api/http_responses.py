from pydantic import BaseModel


class NormalizedTextResponse(BaseModel):
    normalizedText: str
    rawText: str


class ErrorResponse(BaseModel):
    error: str
