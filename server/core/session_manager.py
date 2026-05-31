from dataclasses import dataclass, field


@dataclass
class SessionState:
    sourceLang: str = "JA"
    lastChatText: str = ""
    lastChatTranslation: str = ""
    lastCaptionText: str = ""
    lastCaptionTranslation: str = ""
    lastInputText: str = ""
    lastInputTranslation: str = ""


_sessions: dict[str, SessionState] = {}


def getSession(sessionId: str) -> SessionState:
    if sessionId not in _sessions:
        _sessions[sessionId] = SessionState()
    return _sessions[sessionId]


def isDuplicateChat(sessionId: str, text: str) -> bool:
    return getSession(sessionId).lastChatText == text


def updateChatSession(sessionId: str, text: str, translation: str) -> None:
    session = getSession(sessionId)
    session.lastChatText = text
    session.lastChatTranslation = translation


def getCachedChatTranslation(sessionId: str) -> str:
    return getSession(sessionId).lastChatTranslation


def isDuplicateCaption(sessionId: str, text: str) -> bool:
    return getSession(sessionId).lastCaptionText == text


def updateCaptionSession(sessionId: str, text: str, translation: str) -> None:
    session = getSession(sessionId)
    session.lastCaptionText = text
    session.lastCaptionTranslation = translation


def getCachedCaptionTranslation(sessionId: str) -> str:
    return getSession(sessionId).lastCaptionTranslation


def isDuplicateInput(sessionId: str, inputText: str) -> bool:
    return getSession(sessionId).lastInputText == inputText


def updateInputSession(sessionId: str, inputText: str, translation: str) -> None:
    session = getSession(sessionId)
    session.lastInputText = inputText
    session.lastInputTranslation = translation


def getCachedInputTranslation(sessionId: str) -> str:
    return getSession(sessionId).lastInputTranslation


def updateSourceLang(sessionId: str, sourceLang: str) -> None:
    getSession(sessionId).sourceLang = sourceLang


def getSourceLang(sessionId: str) -> str:
    return getSession(sessionId).sourceLang


def removeSession(sessionId: str) -> None:
    _sessions.pop(sessionId, None)
