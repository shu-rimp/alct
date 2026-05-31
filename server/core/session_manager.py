from dataclasses import dataclass, field


@dataclass
class SessionState:
    lastExtractedText: str = ""
    lastTranslatedText: str = ""
    lastInputText: str = ""
    lastTranslatedInputText: str = ""
    sourceLang: str = "JA"


_sessions: dict[str, SessionState] = {}


def getSession(sessionId: str) -> SessionState:
    if sessionId not in _sessions:
        _sessions[sessionId] = SessionState()
    return _sessions[sessionId]


def isDuplicate(sessionId: str, extractedText: str) -> bool:
    return getSession(sessionId).lastExtractedText == extractedText


def updateSession(sessionId: str, extractedText: str, translatedText: str) -> None:
    session = getSession(sessionId)
    session.lastExtractedText = extractedText
    session.lastTranslatedText = translatedText


def getCachedTranslation(sessionId: str) -> str:
    return getSession(sessionId).lastTranslatedText


def isDuplicateInput(sessionId: str, inputText: str) -> bool:
    return getSession(sessionId).lastInputText == inputText


def updateInputSession(sessionId: str, inputText: str, translatedText: str) -> None:
    session = getSession(sessionId)
    session.lastInputText = inputText
    session.lastTranslatedInputText = translatedText


def getCachedInputTranslation(sessionId: str) -> str:
    return getSession(sessionId).lastTranslatedInputText


def updateSourceLang(sessionId: str, sourceLang: str) -> None:
    getSession(sessionId).sourceLang = sourceLang


def getSourceLang(sessionId: str) -> str:
    return getSession(sessionId).sourceLang


def removeSession(sessionId: str) -> None:
    _sessions.pop(sessionId, None)
