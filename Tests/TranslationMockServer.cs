using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AlctClient.Tests;

public sealed class TranslationMockServer : IDisposable
{
    private const string MOCK_RESPONSE = "안녕하세요.";
    private const string LISTEN_PREFIX = "http://localhost:8765/";
    private const int RECEIVE_BUFFER_SIZE = 65536;

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public TranslationMockServer()
    {
        _listener.Prefixes.Add(LISTEN_PREFIX);
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                    _ = Task.Run(() => HandleConnectionAsync(context, ct), ct);
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var ws = wsContext.WebSocket;
        var buffer = new byte[RECEIVE_BUFFER_SIZE];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, ct);
                    break;
                }

                var responseBytes = Encoding.UTF8.GetBytes(MOCK_RESPONSE);
                await ws.SendAsync(responseBytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
        }
        catch { }
        finally
        {
            ws.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
