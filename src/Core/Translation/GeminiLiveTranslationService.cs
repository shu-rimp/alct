using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AlctClient.Utils;

namespace AlctClient.Core;

// Gemini Live API 전용 번역 엔진. 음성 자막 경로에서만 쓴다.
// 기존 GeminiTranslationService(REST, gemini-3.1-flash-lite)와는 별개 트랙 — 모델/전송 방식이 다름
// 통신: WebSocket(BidiGenerateContent)
// setup → setupComplete → clientContent(turn) → serverContent 순서로 한 줄씩 처리한다.
// 해당 모델은 음성을 직접 받아 음성으로 돌려주는 것에 최적화된 모델이나,
// 이미 LiveCaptions STT 파이프라인이 견고해 여기서 반환된 텍스트를 input으로 보냄.
// 네이티브 오디오 전용 모델이라 번역문은 텍스트 파트가 아니라 모델 음성의 전사
// (serverContent.outputTranscription.text)로 돌아온다. 오디오는 버리고 그 텍스트만 자막으로 쓴다.

// 현재 모든 Live API는 '프리뷰' 단계라 언제든 API 스펙이 변경되거나 지원 중단 될 수 있음을 염두에 둘 것.
// last modified: 2026-06
public sealed class GeminiLiveTranslationService : ITranslationService, IDisposable
{
    public const string Model = "gemini-3.1-flash-live-preview";
    // 서비스와 키 검증이 공유할 수 있게 단일 출처로 둔다(REST 엔드포인트와 동일한 의도).
    public const string BidiEndpoint =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    // 음성 자막용 시스템 프롬프트 — 한 줄 입력 → 한 줄 한국어. REST 쪽 fps 채팅 프롬프트와 달리 실시간 발화에 맞춤.
    private const string SystemPrompt =
        "You are a translation engine for live fps-game speech subtitles. The input is one line of transcribed speech " +
        "that may contain slang, abbreviations, unstable pronunciation or several speakers and languages mixed together. Translate it into natural, " +
        "concise Korean suitable for a subtitle. Output only the Korean translation, nothing else.";

    private const int ReceiveBufferSize = 32768;
    private const int TurnTimeoutMs = 8000;       // 한 턴 응답 대기 상한 — 막히면 예외로 끊어 자막 큐가 멈추지 않게
    private const long RecycleTokenThreshold = 24000;  // 32k 컨텍스트 창을 넘기 전에 세션 재생성(누적 히스토리 차단)

    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly SemaphoreSlim _gate = new(1, 1);  // 소켓 접근 직렬화(동시 호출/배치 대비 자체 방어)

    private ClientWebSocket? _ws;
    private long _sessionTokens;  // 현재 세션 누적 토큰(usageMetadata 기준) — 임계 초과 시 재생성
    private bool _recycleRequested;  // goAway 수신 — 이번 턴은 마저 읽고 다음 EnsureConnectedAsync에서 세션 재생성

    public GeminiLiveTranslationService(string apiKey) : this(apiKey, BidiEndpoint) { }

    // 테스트는 base URI를 로컬 mock(ws://localhost:...)으로 덮어쓴다 — REST 쪽 HttpClient 주입과 같은 패턴.
    internal GeminiLiveTranslationService(string apiKey, string endpoint)
    {
        _apiKey = apiKey;
        // Live(BidiGenerateContent)는 프로토콜상(wss) 키를 쿼리스트링으로만 받는다(헤더 인증 불가).
        // 따라서 _endpoint URL을 절대 Logger에 넘기거나 예외 메시지에 끼워 넣지 말 것.
        // (ConnectAsync 실패 예외는 host:port까지만 노출하고 쿼리는 빼므로 로그 유출 없음 검증 완료)
        _endpoint = new Uri($"{endpoint}?key={Uri.EscapeDataString(apiKey)}");
    }

    // 프롬프트의 타깃 언어명 — REST 엔진과 동일하게 영어명을 써 입력에 섞인 한국어와의 충돌을 피한다.
    public string MapLanguageCode(string bcp47) => bcp47 switch
    {
        "ja-JP" => "Japanese",
        "zh-CN" => "Simplified Chinese",
        "en-US" => "English",
        _       => bcp47,
    };

    public async Task<string> TranslateToKoreanAsync(string text, string sourceLang, string? context = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;

        var contextBlock = string.IsNullOrWhiteSpace(context)
            ? ""
            : $"Recent utterances for context (reference only, do NOT translate):\n{context}\n\n";
        var userContent = $"{contextBlock}Translate this line to Korean:\n\n{ITranslationService.StripXmlTags(text)}";

        return await SendTurnWithRetryAsync(userContent, ct);
    }

    public async Task<string> TranslateFromKoreanAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(_apiKey)) return text;
        var userContent = $"Translate the following text into {MapLanguageCode(targetLang)}, preserving every sentence. Output only the translation:\n\n{text}";
        return await SendTurnWithRetryAsync(userContent, CancellationToken.None);
    }

    // 음성 전용 엔진이라 배치 경로는 실사용되지 않지만 인터페이스 충족을 위해 줄별로 처리(1:1 보존).
    public async Task<IReadOnlyList<string>> TranslateBatchToKoreanAsync(IReadOnlyList<string> texts, string sourceLang, CancellationToken ct = default)
    {
        if (texts.Count == 0 || string.IsNullOrEmpty(_apiKey)) return texts;
        var results = new List<string>(texts.Count);
        foreach (var t in texts)
            results.Add(string.IsNullOrWhiteSpace(t) ? t : await TranslateToKoreanAsync(t, sourceLang, null, ct));
        return results;
    }

    // 소켓이 끊겨 있으면 재연결 후 한 번 재시도. 재시도도 실패하면 호출부(자막 핸들러)가 원문을 표시하도록 예외 전파.
    private async Task<string> SendTurnWithRetryAsync(string userContent, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try
            {
                return await SendTurnAsync(userContent, ct);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                Logger.Warn("GeminiLive", ex);  // 첫 시도 실패 — 세션을 버리고 한 번 더
                ResetSocket();
                return await SendTurnAsync(userContent, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> SendTurnAsync(string userContent, CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);

        var clientContent = new
        {
            clientContent = new
            {
                turns = new[] { new { role = "user", parts = new[] { new { text = userContent } } } },
                turnComplete = true,
            },
        };
        await SendJsonAsync(clientContent, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TurnTimeoutMs);

        var sb = new StringBuilder();
        while (true)
        {
            using var doc = await ReceiveJsonAsync(timeout.Token);
            var root = doc.RootElement;

            if (root.TryGetProperty("serverContent", out var server))
            {
                // 텍스트는 모델 음성의 전사(outputTranscription)로 들어온다. modelTurn.parts는 오디오(inlineData)라 버린다.
                if (server.TryGetProperty("outputTranscription", out var transcript)
                    && transcript.TryGetProperty("text", out var tt) && tt.ValueKind == JsonValueKind.String)
                    sb.Append(tt.GetString());

                // turnComplete만이 진짜 종료 신호. generationComplete는 그 직전에 별도 프레임으로 오므로
                // 여기서 끊으면 turnComplete 프레임이 버퍼에 남아 다음 턴이 그걸 먼저 읽는다(한 턴씩 밀림).
                if (IsTrue(server, "turnComplete"))
                    break;
            }
            else if (root.TryGetProperty("usageMetadata", out var usage)
                     && usage.TryGetProperty("totalTokenCount", out var total)
                     && total.TryGetInt64(out var count))
            {
                _sessionTokens = count;  // 다음 턴 전에 임계 넘으면 재생성
            }
            else if (root.TryGetProperty("goAway", out _))
            {
                // goAway는 '곧 끊는다'는 예고일 뿐 — 소켓은 아직 살아있다. 여기서 ResetSocket()하면
                // _ws가 null이 되어 이 루프의 다음 ReceiveJsonAsync가 NRE로 터진다.
                // 이번 턴(turnComplete)까지는 마저 읽고, 재생성은 다음 턴 EnsureConnectedAsync에 맡긴다.
                _recycleRequested = true;
            }
        }

        return sb.ToString().Trim();
    }

    // 지연 연결 + 세션 재사용. 토큰 임계 초과 또는 goAway 재생성 요청이면 먼저 끊어 새 세션으로 다시 연다.
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open }
            && _sessionTokens < RecycleTokenThreshold
            && !_recycleRequested)
            return;

        ResetSocket();  // 끊김 / 토큰 임계 초과 / goAway 재생성 요청 → 새 세션
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(_endpoint, ct);

        // 이 모델은 네이티브 오디오 전용 — 응답 모달리티가 AUDIO만 허용된다(TEXT 불가).
        // 텍스트 자막이 필요하므로 outputAudioTranscription을 켜서, 모델이 말한 한국어를 전사한 텍스트
        // (serverContent.outputTranscription.text)를 읽고 오디오 자체는 버린다.
        var setup = new
        {
            setup = new
            {
                model = $"models/{Model}",
                // thinkingLevel= minimal | low | medium | high(default: minimal)
                // 사고 단계를 꺼 응답 지연을 줄인다(자막은 정확도보다 즉시성이 우선, 기본값이 minimal이지만 명시).
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    temperature = 0.1,
                    thinkingConfig = new { thinkingLevel = "minimal" },
                },
                systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
                outputAudioTranscription = new { },
            },
        };
        _ws = ws;
        _sessionTokens = 0;
        try
        {
            await SendJsonAsync(setup, ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TurnTimeoutMs);
            using var ack = await ReceiveJsonAsync(timeout.Token);  // setupComplete 대기 후에야 turn 전송 가능
            if (!ack.RootElement.TryGetProperty("setupComplete", out _))
                throw new InvalidOperationException("Gemini Live did not return setupComplete.");
        }
        catch
        {
            ResetSocket();  // 핸드셰이크 실패 시 반쯤 열린 소켓을 남기지 않음(다음 호출이 새로 연결)
            throw;
        }

        Logger.Info("GeminiLive", $"WebSocket session ready (model={Model}).");
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        var ws = _ws ?? throw new WebSocketException("Gemini Live socket is not connected.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    // 한 메시지(여러 프레임일 수 있음)를 끝까지 모아 JSON으로 파싱. 텍스트/바이너리 프레임 모두 UTF-8 JSON.
    private async Task<JsonDocument> ReceiveJsonAsync(CancellationToken ct)
    {
        var ws = _ws ?? throw new WebSocketException("Gemini Live socket is not connected.");
        var buffer = new byte[ReceiveBufferSize];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw CloseToException(result);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms, cancellationToken: ct);
    }

    // 종료 코드/사유에 사용량 초과 신호가 있으면 한도 예외로 매핑 — 음성 할당량 차단 로직과 연동.
    // 프리뷰 모델은 현재 별도 한도가 없다고 알려져 있어 그 외 종료는 재연결 대상.
    private static Exception CloseToException(WebSocketReceiveResult result)
    {
        var reason = result.CloseStatusDescription ?? "";
        if (reason.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("exhausted", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
            return new TranslationRateLimitException("[Gemini Live] 번역 한도를 소진했어요.", DateTime.UtcNow.AddSeconds(60));
        return new WebSocketException($"Gemini Live closed: {result.CloseStatus} {reason}");
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    private void ResetSocket()
    {
        try { _ws?.Abort(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        _sessionTokens = 0;
        _recycleRequested = false;
    }

    public void Dispose()
    {
        ResetSocket();
        _gate.Dispose();
    }
}
