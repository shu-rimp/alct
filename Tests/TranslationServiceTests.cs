using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AlctClient.Core;

namespace AlctClient.Tests;

public class TranslationServiceTests
{
    private static HttpClient MakeClient(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseJson, status);
        return new HttpClient(handler);
    }

    private static string DeepLResponse(string text) =>
        JsonSerializer.Serialize(new { translations = new[] { new { text } } });

    [Fact]
    public async Task TranslateToKoreanAsync_SendsTagHandlingPayload()
    {
        string? capturedBody = null;
        var handler = new CapturingHttpMessageHandler(DeepLResponse("번역결과"), async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
        });
        var svc = new DeepLTranslationService("test:fx", new HttpClient(handler));

        await svc.TranslateToKoreanAsync("hello", "JA");

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("xml", doc.RootElement.GetProperty("tag_handling").GetString());
        Assert.Equal("KO", doc.RootElement.GetProperty("target_lang").GetString());
        Assert.Equal("x", doc.RootElement.GetProperty("ignore_tags")[0].GetString());
    }

    [Fact]
    public async Task TranslateFromKoreanAsync_SendsSourceLangKO()
    {
        string? capturedBody = null;
        var handler = new CapturingHttpMessageHandler(DeepLResponse("translation"), async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
        });
        var svc = new DeepLTranslationService("test:fx", new HttpClient(handler));

        await svc.TranslateFromKoreanAsync("안녕", "JA");

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("KO", doc.RootElement.GetProperty("source_lang").GetString());
        Assert.Equal("JA", doc.RootElement.GetProperty("target_lang").GetString());
    }

    [Theory]
    [InlineData("abc123:fx", "api-free.deepl.com")]
    [InlineData("abc123", "api.deepl.com")]
    public async Task UsesCorrectDomain_BasedOnApiKeySuffix(string apiKey, string expectedDomain)
    {
        Uri? capturedUri = null;
        var handler = new CapturingHttpMessageHandler(DeepLResponse("result"), async req =>
        {
            capturedUri = req.RequestUri;
            await Task.CompletedTask;
        });
        var svc = new DeepLTranslationService(apiKey, new HttpClient(handler));

        await svc.TranslateToKoreanAsync("test", "EN");

        Assert.Contains(expectedDomain, capturedUri!.Host);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ReturnsTranslatedText()
    {
        var svc = new DeepLTranslationService("key:fx", MakeClient(DeepLResponse("안녕하세요")));
        var result = await svc.TranslateToKoreanAsync("Hello", "EN");
        Assert.Equal("안녕하세요", result);
    }

    [Fact]
    public async Task TranslateToKoreanAsync_ReturnsInputUnchanged_WhenEmpty()
    {
        var svc = new DeepLTranslationService("key:fx", MakeClient(DeepLResponse("")));
        var result = await svc.TranslateToKoreanAsync("   ", "JA");
        Assert.Equal("   ", result);
    }
}

public class OcrHttpClientTests
{
    private static HttpClient MakeClient(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(responseJson, status);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task SendImageAsync_PostsToOcrEndpoint()
    {
        Uri? capturedUri = null;
        HttpMethod? capturedMethod = null;
        var handler = new CapturingHttpMessageHandler("""{"normalizedText":"test"}""", async req =>
        {
            capturedUri = req.RequestUri;
            capturedMethod = req.Method;
            await Task.CompletedTask;
        });
        var client = new OcrHttpClient("http://localhost:8000", new HttpClient(handler));

        await client.SendImageAsync(new byte[] { 1, 2, 3 });

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("http://localhost:8000/ocr", capturedUri!.ToString());
    }

    [Fact]
    public async Task SendImageAsync_FiresOcrTextReceived_WithNormalizedText()
    {
        string? received = null;
        var client = new OcrHttpClient("http://localhost:8000",
            MakeClient("""{"normalizedText":"정규화된 텍스트","rawText":"원본"}"""));
        client.OcrTextReceived += (normalized, _) => received = normalized;

        await client.SendImageAsync(new byte[] { 1, 2, 3 });

        Assert.Equal("정규화된 텍스트", received);
    }

    [Fact]
    public async Task SendImageAsync_Throws_OcrRequestException_OnHttpError()
    {
        var client = new OcrHttpClient("http://localhost:8000",
            MakeClient("{}", HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<OcrRequestException>(
            () => client.SendImageAsync(new byte[] { 1 }));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]                        // 400 인식 실패
    [InlineData(HttpStatusCode.Forbidden)]                        // 403 인증
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]            // 413 용량/픽셀 초과
    [InlineData(HttpStatusCode.GatewayTimeout)]                   // 504 OCR 타임아웃 (503은 재시도 테스트에서 별도 검증)
    public async Task SendImageAsync_MapsServerCodes_ToOcrRequestException(HttpStatusCode status)
    {
        var client = new OcrHttpClient("http://localhost:8000", MakeClient("{}", status));

        var ex = await Assert.ThrowsAsync<OcrRequestException>(
            () => client.SendImageAsync(new byte[] { 1 }));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.Null(ex.RetryAtUtc);  // 429 외에는 차단 힌트 없음
    }

    [Fact]
    public async Task SendImageAsync_429_CarriesRetryAtUtc_FromRetryAfter()
    {
        var handler = new FakeHttpMessageHandler("{}", HttpStatusCode.TooManyRequests,
            retryAfter: TimeSpan.FromSeconds(5));
        var client = new OcrHttpClient("http://localhost:8000", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OcrRequestException>(
            () => client.SendImageAsync(new byte[] { 1 }));
        Assert.NotNull(ex.RetryAtUtc);
        Assert.True(ex.RetryAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task SendImageAsync_Retries_On503_ThenSucceeds()
    {
        var fast = TimeSpan.FromMilliseconds(1);
        var handler = new SequencedHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "{}", fast),
            (HttpStatusCode.OK, """{"normalizedText":"성공"}""", null));
        var client = new OcrHttpClient("http://localhost:8000", new HttpClient(handler));
        string? received = null;
        client.OcrTextReceived += (normalized, _) => received = normalized;

        await client.SendImageAsync(new byte[] { 1 });

        Assert.Equal("성공", received);
        Assert.Equal(2, handler.CallCount);  // 503 1회 + 성공 1회
    }

    [Fact]
    public async Task SendImageAsync_ExhaustsRetries_On503_ThenThrows()
    {
        var fast = TimeSpan.FromMilliseconds(1);
        var handler = new SequencedHttpMessageHandler((HttpStatusCode.ServiceUnavailable, "{}", fast));
        var client = new OcrHttpClient("http://localhost:8000", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OcrRequestException>(
            () => client.SendImageAsync(new byte[] { 1 }));
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(3, handler.CallCount);  // 최초 1회 + 재시도 2회 (OCR_MAX_RETRIES)
    }

    [Fact]
    public async Task SendImageAsync_DoesNotFireEvent_WhenNormalizedTextEmpty()
    {
        bool fired = false;
        var client = new OcrHttpClient("http://localhost:8000",
            MakeClient("""{"normalizedText":""}"""));
        client.OcrTextReceived += (_, _) => fired = true;

        await client.SendImageAsync(new byte[] { 1 });

        Assert.False(fired);
    }
}

// Test helpers

internal sealed class FakeHttpMessageHandler(string responseJson, HttpStatusCode status, TimeSpan? retryAfter = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is { } delta)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
        return Task.FromResult(response);
    }
}

// 호출마다 큐에서 다음 응답 스펙을 꺼내 새 응답을 생성(재시도 경로 검증용). 큐가 비면 마지막 스펙을 반복
internal sealed class SequencedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode status, string json, TimeSpan? retryAfter)> _queue;
    private (HttpStatusCode status, string json, TimeSpan? retryAfter) _last;
    public int CallCount { get; private set; }

    public SequencedHttpMessageHandler(params (HttpStatusCode, string, TimeSpan?)[] responses)
        => _queue = new Queue<(HttpStatusCode, string, TimeSpan?)>(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        if (_queue.Count > 0) _last = _queue.Dequeue();
        var (status, json, retryAfter) = _last;
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is { } delta)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
        return Task.FromResult(response);
    }
}

internal sealed class CapturingHttpMessageHandler(string responseJson, Func<HttpRequestMessage, Task> capture) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await capture(request);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
    }
}
