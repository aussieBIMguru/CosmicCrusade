using System.Net.WebSockets;
using System.Text;

namespace CosmicCrusade.Core.Net;

/// <summary>One connection attempt to the relay. A new transport is made for every (re)connect.</summary>
public interface INetTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);

    Task SendAsync(string text, CancellationToken ct);

    /// <summary>The next text message, or null once the connection has closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);
}

/// <summary>A WebSocket to the relay (wss:// online, ws://localhost:5080 for the local relay).</summary>
public sealed class WebSocketTransport : INetTransport
{
    private readonly Uri _uri;
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _buffer = new byte[16 * 1024];

    public WebSocketTransport(Uri uri)
    {
        _uri = uri;
        // The app sends its own "ping" every few seconds; this is just a backstop for proxies.
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    }

    public Task ConnectAsync(CancellationToken ct) => _ws.ConnectAsync(_uri, ct);

    public async Task SendAsync(string text, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await _ws.ReceiveAsync(_buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(_buffer, 0, r.Count);
            if (r.EndOfMessage) break;
            if (ms.Length > 256 * 1024) throw new WebSocketException("Message too large.");
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);
            }
        }
        catch
        {
            // Closing is best effort.
        }
        _ws.Dispose();
    }
}
