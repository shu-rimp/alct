using System.Runtime.InteropServices;

namespace AlctClient.Utils;

// 클립보드는 한 번에 한 프로세스만 열 수 있어, 다른 앱이 잠깐 쥐고 있으면 OpenClipboard이 실패한다
// (COMException / CLIPBRD_E_CANT_OPEN 0x800401D0). 특히 WM_CLIPBOARDUPDATE 직후엔 방금 복사한
// 앱이 아직 클립보드를 점유 중이라 흔하다 — 짧게 재시도하고, 끝내 실패하면 조용히 포기한다(크래시 금지).
// 반드시 STA(UI) 스레드에서 호출.
public static class ClipboardHelper
{
    private const int RETRY_COUNT = 5;
    private const int RETRY_DELAY_MS = 12;

    public static string? TryGetText()
    {
        for (int attempt = 0; attempt < RETRY_COUNT; attempt++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (ExternalException) { Thread.Sleep(RETRY_DELAY_MS); }  // COMException 포함 — 클립보드 점유 충돌
        }
        return null;
    }

    public static bool TrySetText(string text)
    {
        for (int attempt = 0; attempt < RETRY_COUNT; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (ExternalException) { Thread.Sleep(RETRY_DELAY_MS); }
        }
        return false;
    }
}
