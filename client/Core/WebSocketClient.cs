using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AlctClient.Core;

public sealed class WebSocketClient : IDisposable
{
    private const int RECEIVE_BUFFER_SIZE = 4096;
    private const int RECONNECT_DELAY_MS = 3000;

    private ClientWebSocket _socket = new();
    private readonly Uri _serverUri;
    private bool _disposed;

    public event Action<string>? MessageReceived;
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _socket.State == WebSocketState.Open;

    public WebSocketClient(string serverUrl)
    {
        _serverUri = new Uri(serverUrl);
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _socket.Dispose();
                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(_serverUri, ct);
                ConnectionChanged?.Invoke(true);
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                ConnectionChanged?.Invoke(false);
                await Task.Delay(RECONNECT_DELAY_MS, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task SendImageAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (!IsConnected) return;
        await _socket.SendAsync(imageBytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async Task SendSettingsAsync(string sourceLang, CancellationToken ct = default)
    {
        if (!IsConnected) return;
        var json = JsonSerializer.Serialize(new { type = "settings", sourceLang });
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[RECEIVE_BUFFER_SIZE];
        var messageBuilder = new StringBuilder();

        while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                var text = ParseTranslatedText(messageBuilder.ToString());
                if (text is not null)
                    MessageReceived?.Invoke(text);
                messageBuilder.Clear();
            }
        }
    }

    private static string? ParseTranslatedText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("translatedText").GetString();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _socket.Dispose();
        _disposed = true;
    }
}
