import pytest

from core.session_manager import (
    getSession,
    isDuplicateChat,
    updateChatSession,
    getCachedChatTranslation,
    isDuplicateCaption,
    updateCaptionSession,
    getCachedCaptionTranslation,
    isDuplicateInput,
    updateInputSession,
    getCachedInputTranslation,
    removeSession,
    _sessions,
)

SESSION_ID = "test-session-127.0.0.1"


@pytest.fixture(autouse=True)
def cleanupSession():
    _sessions.pop(SESSION_ID, None)
    yield
    _sessions.pop(SESSION_ID, None)


class TestGetSession:
    def test_createsNewSessionIfNotExists(self):
        session = getSession(SESSION_ID)
        assert session is not None
        assert session.lastChatText == ""
        assert session.lastChatTranslation == ""
        assert session.lastCaptionText == ""
        assert session.lastCaptionTranslation == ""

    def test_returnsSameSessionOnSubsequentCalls(self):
        session1 = getSession(SESSION_ID)
        session2 = getSession(SESSION_ID)
        assert session1 is session2


class TestChatCache:
    def test_returnsFalseForNewSession(self):
        assert isDuplicateChat(SESSION_ID, "some text") is False

    def test_returnsTrueForSameText(self):
        updateChatSession(SESSION_ID, "same text", "번역")
        assert isDuplicateChat(SESSION_ID, "same text") is True

    def test_returnsFalseForDifferentText(self):
        updateChatSession(SESSION_ID, "first text", "번역1")
        assert isDuplicateChat(SESSION_ID, "different text") is False

    def test_updatesFields(self):
        updateChatSession(SESSION_ID, "new text", "새 번역")
        assert getSession(SESSION_ID).lastChatText == "new text"
        assert getSession(SESSION_ID).lastChatTranslation == "새 번역"

    def test_returnsEmptyForNewSession(self):
        assert getCachedChatTranslation(SESSION_ID) == ""

    def test_returnsCachedTranslation(self):
        updateChatSession(SESSION_ID, "text", "캐시된 번역")
        assert getCachedChatTranslation(SESSION_ID) == "캐시된 번역"


class TestCaptionCache:
    def test_returnsFalseForNewSession(self):
        assert isDuplicateCaption(SESSION_ID, "some caption") is False

    def test_returnsTrueForSameText(self):
        updateCaptionSession(SESSION_ID, "same caption", "번역")
        assert isDuplicateCaption(SESSION_ID, "same caption") is True

    def test_returnsFalseForDifferentText(self):
        updateCaptionSession(SESSION_ID, "first caption", "번역1")
        assert isDuplicateCaption(SESSION_ID, "different caption") is False

    def test_updatesFields(self):
        updateCaptionSession(SESSION_ID, "caption text", "번역")
        assert getSession(SESSION_ID).lastCaptionText == "caption text"
        assert getSession(SESSION_ID).lastCaptionTranslation == "번역"

    def test_returnsEmptyForNewSession(self):
        assert getCachedCaptionTranslation(SESSION_ID) == ""

    def test_returnsCachedTranslation(self):
        updateCaptionSession(SESSION_ID, "caption", "캐시된 번역")
        assert getCachedCaptionTranslation(SESSION_ID) == "캐시된 번역"


class TestChatAndCaptionAreIsolated:
    def test_chatUpdateDoesNotAffectCaption(self):
        updateChatSession(SESSION_ID, "game text", "번역")
        assert isDuplicateCaption(SESSION_ID, "game text") is False

    def test_captionUpdateDoesNotAffectChat(self):
        updateCaptionSession(SESSION_ID, "caption text", "번역")
        assert isDuplicateChat(SESSION_ID, "caption text") is False


class TestInputCache:
    def test_isDuplicateInputReturnsFalseForNewSession(self):
        assert isDuplicateInput(SESSION_ID, "안녕하세요") is False

    def test_isDuplicateInputReturnsTrueForSameText(self):
        updateInputSession(SESSION_ID, "안녕하세요", "こんにちは")
        assert isDuplicateInput(SESSION_ID, "안녕하세요") is True

    def test_isDuplicateInputReturnsFalseForDifferentText(self):
        updateInputSession(SESSION_ID, "안녕하세요", "こんにちは")
        assert isDuplicateInput(SESSION_ID, "감사합니다") is False

    def test_getCachedInputTranslationReturnsEmptyForNewSession(self):
        assert getCachedInputTranslation(SESSION_ID) == ""

    def test_getCachedInputTranslationReturnsCachedResult(self):
        updateInputSession(SESSION_ID, "안녕하세요", "こんにちは")
        assert getCachedInputTranslation(SESSION_ID) == "こんにちは"


class TestRemoveSession:
    def test_removesExistingSession(self):
        getSession(SESSION_ID)
        removeSession(SESSION_ID)
        assert SESSION_ID not in _sessions

    def test_doesNotRaiseForMissingSession(self):
        removeSession("nonexistent-session")
