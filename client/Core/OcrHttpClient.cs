using System.Net.Http;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class OcrHttpClient
{
    private static readonly HttpClient _defaultHttp = new();
    private readonly HttpClient _http;
    private readonly string _ocrUrl;

    public event Action<string>? OcrTextReceived;

    public OcrHttpClient(string serverUrl) : this(serverUrl, _defaultHttp) { }

    internal OcrHttpClient(string serverUrl, HttpClient http)
    {
        _ocrUrl = serverUrl.TrimEnd('/') + "/ocr";
        _http = http;
    }

    public async Task SendImageAsync(byte[] imageBytes)
    {
        using var content = new ByteArrayContent(imageBytes);
        var response = await _http.PostAsync(_ocrUrl, content);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var text = doc.RootElement.TryGetProperty("normalizedText", out var val)
            ? val.GetString() : null;
        if (!string.IsNullOrEmpty(text))
            OcrTextReceived?.Invoke(text);
    }
}
