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

    private static string MyMemoryResponse(string translatedText, int status = 200) =>
        JsonSerializer.Serialize(new { responseData = new { translatedText }, responseStatus = status });

    [Fact]
    public async Task MyMemory_TreatsInlineWarningAsQuota()
    {
        // 한도 초과 시 HTTP 200 + quotaFinished 없이 translatedText에 경고문을 담아 보냄 — 번역문으로 노출되면 안 됨
        var warning = "MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY. NEXT AVAILABLE IN 07 HOURS 12 MINUTES";
        var svc = new MyMemoryTranslationService(MakeClient(MyMemoryResponse(warning)));
        await Assert.ThrowsAsync<TranslationRateLimitException>(() => svc.TranslateToKoreanAsync("hello", "EN"));
    }

    [Fact]
    public async Task MyMemory_ReturnsTranslatedText_WhenNormal()
    {
        var svc = new MyMemoryTranslationService(MakeClient(MyMemoryResponse("안녕")));
        var result = await svc.TranslateToKoreanAsync("hello", "EN");
        Assert.Equal("안녕", result);
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

// 고정 응답을 돌려주며 호출 횟수를 센다 — 마스킹이 줄당 HTTP 1회로 줄었는지 검증용
internal sealed class CallCountingHandler(string responseJson) : HttpMessageHandler
{
    public int CallCount { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
    }
}
