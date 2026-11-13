using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Cosmic Crusade LAN server (relay): the same protocol as server/cloudflare/src/index.js, for testing on one PC or
// playing on a home network without any hosting. Run it (F5 in Visual Studio, or "Cosmic Crusade LAN Server" from the
// Start menu once installed), then put  ws://localhost:5080  (or ws://<this-PC's-IP>:5080 on other PCs) in the app's
// Server box. It prints the addresses to use.
//
//   Port:  5080 by default;  CosmicCrusade-LanServer.exe --port 6000  (or the RELAY_URLS environment variable)
//
//   GET /                                              -> "Cosmic Crusade relay OK"
//   GET /room/<CODE>?role=host|guest&key=<secret>&proto=1 (WebSocket)

int port = 5080;
int portArg = Array.FindIndex(args, a => a is "--port" or "-p");
if (portArg >= 0 && portArg + 1 < args.Length && int.TryParse(args[portArg + 1], out int chosen) && chosen is > 0 and < 65536)
    port = chosen;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning); // our own one-line messages are enough
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("RELAY_URLS") ?? $"http://0.0.0.0:{port}");
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

var rooms = new ConcurrentDictionary<string, Room>(StringComparer.OrdinalIgnoreCase);
var codeRe = new Regex("^[A-Za-z0-9]{4,8}$");
var keyRe = new Regex("^[A-Za-z0-9_-]{8,64}$");

app.MapGet("/", () => $"Cosmic Crusade relay OK (protocol {Room.Proto})\n");

app.Map("/room/{code}", async (HttpContext http, string code) =>
{
    if (!http.WebSockets.IsWebSocketRequest || !codeRe.IsMatch(code))
    {
        http.Response.StatusCode = 426;
        return;
    }

    string role = http.Request.Query["role"].ToString();
    string key = http.Request.Query["key"].ToString();
    int.TryParse(http.Request.Query["proto"].ToString(), out int proto);

    using var ws = await http.WebSockets.AcceptWebSocketAsync();
    var conn = new Conn(ws, role);

    string? error =
        proto != Room.Proto ? $"This app version doesn't match the relay (protocol {Room.Proto}). Update the app." :
        role is not ("host" or "guest") ? "Unknown role." :
        !keyRe.IsMatch(key) ? "Bad key." :
        null;

    var room = rooms.GetOrAdd(code.ToUpperInvariant(), _ => new Room());
    error ??= room.TryJoin(conn, key);
    if (error is not null)
    {
        await conn.SendAsync(Room.Relay("error", ("msg", error)));
        await ws.CloseAsync((WebSocketCloseStatus)4001, error.Length > 100 ? error[..100] : error, CancellationToken.None);
        return;
    }

    Console.WriteLine($"[{DateTime.Now:T}] {code.ToUpperInvariant()}: {role} connected");
    var buffer = new byte[64 * 1024];
    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, http.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buffer, 0, r.Count);
                if (ms.Length > 64 * 1024) break;
            } while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) break;
            if (r.MessageType != WebSocketMessageType.Text || ms.Length > 64 * 1024) continue;

            string text = Encoding.UTF8.GetString(ms.ToArray());
            if (text == "ping") { await conn.SendAsync("pong"); continue; }
            await room.ForwardAsync(conn, text);
        }
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
    }
    finally
    {
        Console.WriteLine($"[{DateTime.Now:T}] {code.ToUpperInvariant()}: {role} left");
        await room.LeftAsync(conn);
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
    }
});

Console.Title = "Cosmic Crusade LAN Server";
Console.WriteLine("==============================================================");
Console.WriteLine(" Cosmic Crusade LAN Server");
Console.WriteLine("==============================================================");
Console.WriteLine($" On THIS PC, put this in the game's Server box:   ws://localhost:{port}");
var lanAddresses = LanAddresses().ToList();
if (lanAddresses.Count > 0)
{
    Console.WriteLine(" On OTHER PCs on your network, use one of these:");
    foreach (var ip in lanAddresses) Console.WriteLine($"     ws://{ip}:{port}");
}
Console.WriteLine();
Console.WriteLine(" Leave this window open while you play. Close it to stop the server.");
Console.WriteLine(" If Windows Firewall asks, allow it on Private networks.");
Console.WriteLine("==============================================================");
app.Run();

// IPv4 addresses of this PC's active network adapters (not loopback / virtual-only), most likely first.
static IEnumerable<string> LanAddresses()
{
    var found = new List<(int Rank, string Ip)>();
    try
    {
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
            bool virtualish = nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                              || nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                              || nic.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase);
            foreach (var a in nic.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                string ip = a.Address.ToString();
                if (ip.StartsWith("169.254.")) continue; // no DHCP answer: not a usable network
                bool privateRange = ip.StartsWith("192.168.") || ip.StartsWith("10.")
                                    || System.Text.RegularExpressions.Regex.IsMatch(ip, @"^172\.(1[6-9]|2\d|3[01])\.");
                found.Add(((virtualish ? 2 : 0) + (privateRange ? 0 : 1), ip));
            }
        }
    }
    catch
    {
        // Listing adapters is only a convenience.
    }
    return found.OrderBy(f => f.Rank).Select(f => f.Ip).Distinct();
}

sealed class Conn(WebSocket ws, string role)
{
    private readonly SemaphoreSlim _send = new(1, 1);
    public string Role { get; } = role;
    public bool Replaced { get; set; }
    public WebSocket Socket { get; } = ws;

    public async Task SendAsync(string text)
    {
        await _send.WaitAsync();
        try
        {
            if (Socket.State == WebSocketState.Open)
                await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (WebSocketException) { }
        finally { _send.Release(); }
    }
}

sealed class Room
{
    public const int Proto = 1;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private readonly object _gate = new();
    private DateTime _openedAt;
    private string? _hostKey, _guestKey;
    private Conn? _host, _guest;

    public static string Relay(string ev, params (string Key, object Value)[] extra)
    {
        var d = new Dictionary<string, object> { ["t"] = "relay", ["ev"] = ev };
        foreach (var (k, v) in extra) d[k] = v;
        return JsonSerializer.Serialize(d);
    }

    /// <summary>Same rules as the Cloudflare relay. Returns an error message, or null when joined.</summary>
    public string? TryJoin(Conn conn, string key)
    {
        Conn? replaced, peer;
        lock (_gate)
        {
            if (_openedAt != default && DateTime.UtcNow - _openedAt > Ttl)
            {
                _openedAt = default;
                _hostKey = _guestKey = null;
            }

            if (conn.Role == "host")
            {
                if (_openedAt != default && _hostKey != key) return "That room code is already in use. Try hosting again.";
                if (_openedAt == default) { _openedAt = DateTime.UtcNow; _hostKey = key; }
                replaced = _host;
                _host = conn;
                peer = _guest;
            }
            else
            {
                if (_openedAt == default) return "No game with that code. Check the code, and that the host is still waiting.";
                if (_guestKey is not null && _guestKey != key) return "That game already has two players.";
                _guestKey = key;
                replaced = _guest;
                _guest = conn;
                peer = _host;
            }
            if (replaced is not null) replaced.Replaced = true;
        }

        if (replaced is not null)
            _ = replaced.Socket.CloseAsync((WebSocketCloseStatus)4000, "Replaced by a new connection", CancellationToken.None)
                .ContinueWith(_ => { });
        _ = conn.SendAsync(Relay("joined", ("role", conn.Role), ("peer", peer is not null)));
        if (peer is not null) _ = peer.SendAsync(Relay("peer-joined"));
        return null;
    }

    public Task ForwardAsync(Conn from, string text)
    {
        if (from.Replaced) return Task.CompletedTask;
        Conn? to;
        lock (_gate) to = from.Role == "host" ? _guest : _host;
        return to is null ? Task.CompletedTask : to.SendAsync(text);
    }

    public Task LeftAsync(Conn conn)
    {
        Conn? peer;
        lock (_gate)
        {
            if (conn.Replaced) return Task.CompletedTask;
            conn.Replaced = true;
            if (conn.Role == "host") { if (_host == conn) _host = null; peer = _guest; }
            else { if (_guest == conn) _guest = null; peer = _host; }
        }
        return peer is null ? Task.CompletedTask : peer.SendAsync(Relay("peer-left"));
    }
}
