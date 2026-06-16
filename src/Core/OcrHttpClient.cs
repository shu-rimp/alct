using System.Net.Http;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class OcrHttpClient
{
    private const int OCR_MAX_RETRIES = 2;                          // 503(동시성 풀) 한정 재시도 횟수
    private static readonly TimeSpan DEFAULT_RETRY_DELAY = TimeSpan.FromSeconds(1);  // Retry-After 헤더 없을 때 폴백

    private static readonly HttpClient _defaultHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),  // 다른 번역 서비스와 동일 — DNS/커넥션 갱신
    });
    private readonly HttpClient _http;
    private readonly string _ocrUrl;

    public event Action<string, string>? OcrTextReceived;  // (normalizedText, rawText)

    public OcrHttpClient(string serverUrl) : this(serverUrl, _defaultHttp) { }

    internal OcrHttpClient(string serverUrl, HttpClient http)
    {
        _ocrUrl = serverUrl.TrimEnd('/') + "/ocr";
        _http = http;
    }

    public async Task SendImageAsync(byte[] imageBytes)
    {
        using var response = await PostWithRetryAsync(imageBytes);
        if (!response.IsSuccessStatusCode)
            throw ToOcrException(response);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var normalized = doc.RootElement.TryGetProperty("normalizedText", out var nVal)
            ? nVal.GetString() : null;
        var raw = doc.RootElement.TryGetProperty("rawText", out var rVal)
            ? rVal.GetString() ?? string.Empty : string.Empty;
        if (!string.IsNullOrEmpty(normalized))
            OcrTextReceived?.Invoke(normalized, raw);
    }

    // 503(동시성 풀)만 Retry-After만큼 대기 후 같은 이미지로 재요청. 그 외 코드(성공 포함)는 즉시 반환.
    // 재시도 대기 동안 호출부의 _ocrLock이 잡혀 있어 새 핫키 요청은 드롭됨 → 요청 누적/중복 캡처 없음
    private async Task<HttpResponseMessage> PostWithRetryAsync(byte[] imageBytes)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var content = new ByteArrayContent(imageBytes);
            if (!BuildConstants.SERVER_TOKEN.StartsWith("#{"))
                content.Headers.Add("X-ALCT-Token", BuildConstants.SERVER_TOKEN);

            var response = await _http.PostAsync(_ocrUrl, content);
            if ((int)response.StatusCode != 503 || attempt >= OCR_MAX_RETRIES)
                return response;

            var delay = response.Headers.RetryAfter?.Delta ?? DEFAULT_RETRY_DELAY;
            response.Dispose();
            await Task.Delay(delay);
        }
    }

    // 서버 /ocr 응답 코드를 사용자 안내로 변환. 4xx/504는 재시도 무의미, 429만 Retry-After까지 차단 힌트 전달.
    // 503은 PostWithRetryAsync에서 이미 재시도를 소진한 경우라 여기선 최종 안내만
    private static OcrRequestException ToOcrException(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        OcrRequestException ex = status switch
        {
            400 => new("이미지를 인식하지 못했어요. 다시 시도해주세요."),
            403 => new("서버 인증에 실패했어요. 앱이 최신 버전인지 확인해주세요."),
            413 => new("캡처 영역이 너무 커요. 영역을 줄여서 다시 시도해주세요."),
            429 => RateLimited(response),
            503 => new("서버가 바빠요. 잠시 후 다시 시도해주세요."),
            504 => new("이미지 처리 시간이 초과됐어요. 다시 시도해주세요."),
            _   => new("서버 오류가 발생했어요. 잠시 후 다시 시도해주세요."), // status code in log
        };
        ex.StatusCode = status;
        return ex;
    }

    // 429 — Retry-After(남은 윈도, 초) 동안 OCR 요청을 억제하도록 재개 시각을 함께 실어 보냄
    private static OcrRequestException RateLimited(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return new($"요청이 많아 잠시 제한됐어요. {(int)delta.TotalSeconds}초 후 다시 시도해주세요.", DateTime.UtcNow + delta);
        return new("요청이 많아 잠시 제한됐어요. 잠시 후 다시 시도해주세요.");
    }
}

// /ocr 비-2xx 응답을 사용자 안내 메시지로 표현. RetryAtUtc(429)는 이 시각까지 요청을 억제하라는 힌트(없으면 단발 안내)
public sealed class OcrRequestException : Exception
{
    public DateTime? RetryAtUtc { get; }
    public int StatusCode { get; set; }  // 로그용 — 사용자 메시지엔 노출 안 함

    public OcrRequestException(string message, DateTime? retryAtUtc = null) : base(message)
        => RetryAtUtc = retryAtUtc;
}
