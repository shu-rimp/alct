import pytest

from core.session_manager import (
    getSession,
    isDuplicate,
    updateSession,
    getCachedTranslation,
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
        assert session.lastExtractedText == ""
        assert session.lastTranslatedText == ""

    def test_returnsSameSessionOnSubsequentCalls(self):
        session1 = getSession(SESSION_ID)
        session2 = getSession(SESSION_ID)
        assert session1 is session2


class TestIsDuplicate:
    def test_returnsFalseForNewSession(self):
        assert isDuplicate(SESSION_ID, "some text") is False

    def test_returnsTrueForSameText(self):
        updateSession(SESSION_ID, "same text", "번역")
        assert isDuplicate(SESSION_ID, "same text") is True

    def test_returnsFalseForDifferentText(self):
        updateSession(SESSION_ID, "first text", "번역1")
        assert isDuplicate(SESSION_ID, "different text") is False


class TestUpdateSession:
    def test_updatesLastExtractedText(self):
        updateSession(SESSION_ID, "new text", "새 번역")
        assert getSession(SESSION_ID).lastExtractedText == "new text"

    def test_updatesLastTranslatedText(self):
        updateSession(SESSION_ID, "any text", "번역 결과")
        assert getSession(SESSION_ID).lastTranslatedText == "번역 결과"


class TestGetCachedTranslation:
    def test_returnsEmptyStringForNewSession(self):
        assert getCachedTranslation(SESSION_ID) == ""

    def test_returnsCachedTranslation(self):
        updateSession(SESSION_ID, "text", "캐시된 번역")
        assert getCachedTranslation(SESSION_ID) == "캐시된 번역"


class TestRemoveSession:
    def test_removesExistingSession(self):
        getSession(SESSION_ID)
        removeSession(SESSION_ID)
        assert SESSION_ID not in _sessions

    def test_doesNotRaiseForMissingSession(self):
        removeSession("nonexistent-session")
