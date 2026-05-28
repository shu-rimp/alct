namespace AlctClient.Core;

public record AppState(
    bool IsConnected,
    bool IsCapturing,
    string TranslationResult,
    string StatusMessage
)
{
    public static AppState Initial => new(
        IsConnected: false,
        IsCapturing: false,
        TranslationResult: string.Empty,
        StatusMessage: "대기 중"
    );
}
