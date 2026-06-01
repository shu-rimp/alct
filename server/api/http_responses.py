from pydantic import BaseModel


class NormalizedTextResponse(BaseModel):
    normalizedText: str


class ErrorResponse(BaseModel):
    error: str
