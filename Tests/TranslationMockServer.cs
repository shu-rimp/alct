using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AlctClient.Tests;

// Gemini Live(BidiGenerateContent) WebSocket 프로토콜을 흉내내는 로컬 mock.
// 핸드셰이크: 첫 메시지(setup)를 받으면 setupComplete를 돌려주고,
// 이후 들어오는 각 메시지(clientContent turn)마다 고정 번역문을 담은 serverContent + turnComplete를 보낸다.
// 드롭/한도초과/토큰누적 시나리오를 옵션으로 흉내낼 수 있다.
public sealed class TranslationMockServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private int _connectionCount;

    public int Port { get; }
    public string WsEndpoint => $"ws://localhost:{Port}/ws";
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    public string Response { get; set; } = "안녕하세요.";
    public long EmitTokenCount { get; set; }       // >0이면 각 턴에 usageMetadata(totalTokenCount) 프레임 동봉
    public bool FailFirstConnection { get; set; }   // 첫 연결의 첫 턴에서 소켓 강제 중단(드롭 → 재연결 검증)
    public bool RateLimitClose { get; set; }        // 첫 턴에서 RESOURCE_EXHAUSTED 사유로 종료

    public TranslationMockServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
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
                else { context.Response.StatusCode = 400; context.Response.Close(); }
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
    {
        var conn = Interlocked.Increment(ref _connectionCount);
        var wsContext = await context.AcceptWebSocketAsync(null);
        var ws = wsContext.WebSocket;

        try
        {
            await ReceiveAsync(ws, ct);                                // setup
            await SendAsync(ws, "{\"setupComplete\":{}}", ct);

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var msg = await ReceiveAsync(ws, ct);                  // clientContent turn
                if (msg is null) break;                                // close frame

                if (FailFirstConnection && conn == 1) { ws.Abort(); break; }
                if (RateLimitClose)
                {
                    await ws.CloseAsync((WebSocketCloseStatus)1008, "RESOURCE_EXHAUSTED: quota", ct);
                    break;
                }

                if (EmitTokenCount > 0)
                    await SendAsync(ws, $"{{\"usageMetadata\":{{\"totalTokenCount\":{EmitTokenCount}}}}}", ct);

                // 실제 모델처럼 오디오 출력의 전사를 outputTranscription으로 전달(+빈 modelTurn 오디오 자리).
                var payload = $"{{\"serverContent\":{{\"outputTranscription\":{{\"text\":{Quote(Response)}}},\"turnComplete\":true}}}}";
                await SendAsync(ws, payload, ct);
            }
        }
        catch { }
        finally { ws.Dispose(); }
    }

    // 한 메시지를 끝까지 모아 반환. Close 프레임이면 null.
    private static async Task<string?> ReceiveAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static Task SendAsync(WebSocket ws, string json, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, ct);

    private static string Quote(string s) => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
