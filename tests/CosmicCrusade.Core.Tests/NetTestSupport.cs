using System.Collections.Concurrent;
using System.Threading.Channels;
using CosmicCrusade.Core.Net;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// The relay's rules (server/cloudflare/src/index.js) in memory: rooms by code, host/guest keys, peer-joined /
/// peer-left, "ping" → "pong", reconnect takeover. Can drop connections on purpose to test recovery.
/// </summary>
internal sealed class InMemoryRelay
{
    private sealed class Room
    {
        public bool Open;
        public string? HostKey, GuestKey;
        public RelayPipe? Host, Guest;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new();
    private int _forwarded;

    /// <summary>Drop the guest's connection after every N messages the relay forwards (0 = never).</summary>
    public int DropGuestEvery { get; set; }

    /// <summary>How many times a connection was dropped on purpose.</summary>
    public int Drops { get; private set; }

    public Func<INetTransport> Factory(string code, string role, string? key = null)
    {
        string k = key ?? NetProtocol.NewKey();
        return () => new RelayPipe(this, code, role, k);
    }

    public NetConnection Connection(string code, string role, NetSettings settings, string? key = null) =>
        new(Factory(code, role, key), role, code, settings);

    internal void Join(RelayPipe p)
    {
        RelayPipe? replaced, peer;
        lock (_gate)
        {
            if (!_rooms.TryGetValue(p.Code, out var room)) _rooms[p.Code] = room = new Room();
            string? error = null;
            if (p.Role == "host")
            {
                if (room.Open && room.HostKey != p.Key) error = "That room code is already in use.";
                else { room.Open = true; room.HostKey = p.Key; }
            }
            else
            {
                if (!room.Open) error = "No game with that code.";
                else if (room.GuestKey is not null && room.GuestKey != p.Key) error = "That game already has two players.";
                else room.GuestKey = p.Key;
            }
            if (error is not null)
            {
                p.Deliver(NetProtocol.Serialize(new NetMessage { T = "relay", Ev = "error", Msg = error }));
                p.CloseFromRelay();
                return;
            }
            replaced = p.Role == "host" ? room.Host : room.Guest;
            if (p.Role == "host") room.Host = p; else room.Guest = p;
            peer = p.Role == "host" ? room.Guest : room.Host;
            if (replaced is not null) replaced.Replaced = true;
        }
        replaced?.CloseFromRelay();
        p.Deliver(NetProtocol.Serialize(new NetMessage { T = "relay", Ev = "joined", Role = p.Role, Peer = peer is not null }));
        peer?.Deliver(NetProtocol.Serialize(new NetMessage { T = "relay", Ev = "peer-joined" }));
    }

    internal void Forward(RelayPipe from, string text)
    {
        RelayPipe? to;
        bool drop = false;
        lock (_gate)
        {
            if (from.Replaced || !_rooms.TryGetValue(from.Code, out var room)) return;
            to = from.Role == "host" ? room.Guest : room.Host;
            _forwarded++;
            if (DropGuestEvery > 0 && _forwarded % DropGuestEvery == 0) drop = true;
        }
        to?.Deliver(text);
        if (drop) DropGuest(from.Code);
    }

    internal void Left(RelayPipe p)
    {
        RelayPipe? peer;
        lock (_gate)
        {
            if (p.Replaced || !_rooms.TryGetValue(p.Code, out var room)) return;
            p.Replaced = true;
            if (p.Role == "host") { if (room.Host == p) room.Host = null; peer = room.Guest; }
            else { if (room.Guest == p) room.Guest = null; peer = room.Host; }
        }
        peer?.Deliver(NetProtocol.Serialize(new NetMessage { T = "relay", Ev = "peer-left" }));
    }

    public void DropGuest(string code)
    {
        RelayPipe? g;
        lock (_gate) g = _rooms.TryGetValue(code, out var room) ? room.Guest : null;
        if (g is null) return;
        Drops++;
        g.CloseFromRelay();
        Left(g);
    }
}

internal sealed class RelayPipe : INetTransport
{
    private readonly InMemoryRelay _relay;
    private readonly Channel<string> _inbox = Channel.CreateUnbounded<string>();
    private volatile bool _closed;

    public RelayPipe(InMemoryRelay relay, string code, string role, string key)
    {
        _relay = relay;
        Code = code;
        Role = role;
        Key = key;
    }

    public string Code { get; }
    public string Role { get; }
    public string Key { get; }
    public bool Replaced { get; set; }

    public Task ConnectAsync(CancellationToken ct)
    {
        _relay.Join(this);
        return Task.CompletedTask;
    }

    public Task SendAsync(string text, CancellationToken ct)
    {
        if (_closed) throw new IOException("closed");
        if (text == "ping") Deliver("pong");
        else _relay.Forward(this, text);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        try
        {
            return await _inbox.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    internal void Deliver(string text)
    {
        if (!_closed) _inbox.Writer.TryWrite(text);
    }

    internal void CloseFromRelay()
    {
        _closed = true;
        _inbox.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        if (!_closed)
        {
            CloseFromRelay();
            _relay.Left(this);
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Runs async code on one thread, the way the WPF dispatcher does: both engines in a test then never run at the same
/// time (the engine's static caches aren't thread-safe), while the network loops run on the thread pool.
/// </summary>
internal sealed class SingleThreadPump : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        try { _queue.Add((d, state)); }
        catch (InvalidOperationException) { /* finished: late callbacks are dropped */ }
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public static T Run<T>(Func<CancellationToken, Task<T>> work, int timeoutSeconds = 120)
    {
        var previous = Current;
        var pump = new SingleThreadPump();
        SetSynchronizationContext(pump);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var task = work(cts.Token);
            task.ContinueWith(_ => pump._queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (w, s) in pump._queue.GetConsumingEnumerable()) w(s);
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
