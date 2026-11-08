using System.Threading.Channels;

namespace CosmicCrusade.Core.Net;

/// <summary>Timings for <see cref="NetConnection"/> (tests use shorter ones).</summary>
public sealed class NetSettings
{
    /// <summary>How often "ping" (to the relay) and a heartbeat (to the opponent) are sent.</summary>
    public TimeSpan Heartbeat { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Silence after which the opponent counts as "connection lost", and our own link as dead.</summary>
    public TimeSpan SilentAfter { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to keep trying to reconnect to the relay before giving up.</summary>
    public TimeSpan GiveUpAfter { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(8);
}

/// <summary>Our own link to the relay.</summary>
public enum NetLinkState
{
    Connecting,
    Connected,
    Reconnecting,
    Closed,
}

/// <summary>
/// One player's connection to an online game, through the relay. It keeps going through drops:
/// <list type="bullet">
/// <item>a heartbeat both ways (every 4 s) spots a silent opponent, or a dead link of our own, within ~15 s;</item>
/// <item>a dropped link reconnects on its own (same room code and secret key, so the relay gives back the seat);</item>
/// <item>every decision is kept in a log and numbered, so after a reconnect each side asks the other to re-send
///   whatever it missed ("resume"). Duplicates are ignored.</item>
/// </list>
/// Events are raised on the <see cref="SynchronizationContext"/> that created the connection (the UI thread in the app).
/// </summary>
public sealed class NetConnection : IAsyncDisposable
{
    private readonly Func<INetTransport> _newTransport;
    private readonly NetSettings _settings;
    private readonly SynchronizationContext? _events;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _life = new();

    private readonly List<string> _sentDecisions = new();   // index = n - 1
    private int _receivedDecisions;
    private readonly Channel<NetMessage> _decisions = Channel.CreateUnbounded<NetMessage>();
    private readonly Channel<NetMessage> _lobby = Channel.CreateUnbounded<NetMessage>();
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private INetTransport? _transport;
    private DateTime _lastHeardAnything;   // pongs included: is our own link alive?
    private DateTime _lastHeardPeer;
    private bool _peerHere;
    private bool _peerResponsive;
    private bool _peerQuit;
    private bool _closed;
    private int _stuckBeats;

    /// <param name="newTransport">Makes a fresh transport for each connection attempt.</param>
    public NetConnection(Func<INetTransport> newTransport, string role, string roomCode, NetSettings? settings = null)
    {
        _newTransport = newTransport;
        Role = role;
        RoomCode = roomCode;
        _settings = settings ?? new NetSettings();
        _events = SynchronizationContext.Current;
    }

    /// <summary>A connection to a real relay: <paramref name="server"/> is what the player typed in the Server box.</summary>
    public static NetConnection ForServer(string server, string role, string roomCode, NetSettings? settings = null)
    {
        string key = NetProtocol.NewKey();
        var uri = NetProtocol.RoomUri(server, roomCode, role, key);
        return new NetConnection(() => new WebSocketTransport(uri), role, roomCode, settings);
    }

    /// <summary>"host" or "guest". The host is player 0, the guest player 1.</summary>
    public string Role { get; }

    public string RoomCode { get; }

    public int MySeat => Role == "host" ? 0 : 1;

    public NetLinkState State { get; private set; } = NetLinkState.Connecting;

    /// <summary>True while the opponent is connected and answering heartbeats.</summary>
    public bool OpponentConnected => _peerHere && _peerResponsive;

    /// <summary>Our own link changed (connected / reconnecting / closed).</summary>
    public event Action<NetLinkState>? StateChanged;

    /// <summary>The opponent connected or answered again (true), or dropped / went silent (false).</summary>
    public event Action<bool>? OpponentPresenceChanged;

    /// <summary>A chat line from the opponent: (name, text).</summary>
    public event Action<string, string>? ChatReceived;

    /// <summary>The opponent left the game on purpose (closed the table / the app).</summary>
    public event Action<string>? OpponentQuit;

    /// <summary>The connection is gone for good (relay refused us, or reconnecting failed for too long).</summary>
    public event Action<string>? Failed;

    // ============================================================== start / stop

    /// <summary>
    /// Connects for the first time. Throws <see cref="NetRefusedException"/> with a readable reason if the relay says
    /// no (no such game, game full, wrong version), or <see cref="NetException"/> if the server can't be reached.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _life.Token);
        try
        {
            await ConnectOnceAsync(linked.Token);
        }
        catch (NetRefusedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NetException($"Couldn't reach the server. Check the Server address and your internet connection. ({Short(ex)})");
        }

        _ = Task.Run(() => SendLoopAsync(_life.Token));
        _ = Task.Run(() => ReceiveLoopAsync(_life.Token));
        _ = Task.Run(() => HeartbeatLoopAsync(_life.Token));
    }

    /// <summary>Tells the opponent we left (best effort), then closes everything.</summary>
    public async Task LeaveAsync(string reason = "left the game")
    {
        if (_closed) return;
        var t = _transport;
        if (t is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
                await t.SendAsync(NetProtocol.Serialize(new NetMessage { T = "bye", Reason = reason }), cts.Token);
            }
            catch
            {
                // Best effort: the relay will report the drop anyway.
            }
        }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true;
        _life.Cancel();
        _decisions.Writer.TryComplete(new OpponentLeftException("You left the game."));
        _lobby.Writer.TryComplete(new NetException("Connection closed."));
        var t = Interlocked.Exchange(ref _transport, null);
        if (t is not null) await t.DisposeAsync();
        SetState(NetLinkState.Closed);
    }

    // ============================================================== sending

    /// <summary>Sends one of our decisions (numbered and kept for re-sending after a drop).</summary>
    public void SendDecision(string kind, int[] values, uint stateHash)
    {
        string json;
        lock (_gate)
        {
            json = NetProtocol.Serialize(new NetMessage
            {
                T = "d", N = _sentDecisions.Count + 1, K = kind, V = values, H = stateHash,
            });
            _sentDecisions.Add(json);
        }
        _outbox.Writer.TryWrite(json);
    }

    /// <summary>
    /// Host: the "start" message to send back whenever the guest says hello again (its first answer may have been
    /// lost in a drop). Set by <see cref="NetLobby.HostAsync"/>.
    /// </summary>
    public NetMessage? HelloReply { get; set; }

    /// <summary>Lobby messages (hello / start / reject). Not logged: the lobby repeats them until answered.</summary>
    public void SendLobby(NetMessage m) => _outbox.Writer.TryWrite(NetProtocol.Serialize(m));

    public void SendChat(string name, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text.Length > 300) text = text[..300];
        _outbox.Writer.TryWrite(NetProtocol.Serialize(new NetMessage { T = "chat", Name = name, Text = text }));
    }

    /// <summary>Our game no longer matches: tell the opponent so both games stop.</summary>
    public void ReportDesync() => _outbox.Writer.TryWrite(NetProtocol.Serialize(new NetMessage { T = "desync" }));

    /// <summary>
    /// Set by <see cref="RemoteSeat"/> while our engine waits for the opponent's decision. In lockstep only one side
    /// waits at a time; if both wait with nothing in flight, the games have drifted apart (reported as a desync).
    /// </summary>
    public bool WaitingForOpponent { get; set; }

    /// <summary>Number of decisions sent so far (for tests and diagnostics).</summary>
    public int DecisionsSent { get { lock (_gate) return _sentDecisions.Count; } }

    // ============================================================== receiving

    /// <summary>
    /// The opponent's next decision, in order. Throws <see cref="OpponentLeftException"/> if they quit or the link is
    /// gone for good, or <see cref="DesyncException"/> if they reported a desync.
    /// </summary>
    public async Task<NetMessage> NextDecisionAsync(CancellationToken ct)
    {
        try
        {
            return await _decisions.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    public async Task<NetMessage> NextLobbyAsync(CancellationToken ct)
    {
        try
        {
            return await _lobby.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    // ============================================================== loops

    private async Task ConnectOnceAsync(CancellationToken ct)
    {
        var t = _newTransport();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_settings.ConnectTimeout);
            await t.ConnectAsync(timeout.Token);

            // The relay's first message says whether we're in, and whether the opponent is already there.
            NetMessage? first = null;
            while (first is null)
            {
                string? text = await t.ReceiveAsync(timeout.Token);
                if (text is null) throw new NetException("The server closed the connection.");
                first = NetProtocol.Parse(text);
                if (first is not null && first.T != "relay") first = null; // stray message: skip
            }
            if (first.Ev == "error") throw new NetRefusedException(first.Msg ?? "The server refused the connection.");
            if (first.Ev != "joined") throw new NetException("Unexpected answer from the server.");

            var old = Interlocked.Exchange(ref _transport, t);
            if (old is not null) _ = old.DisposeAsync().AsTask();
            _lastHeardAnything = DateTime.UtcNow;
            SetState(NetLinkState.Connected);
            if (first.Peer == true) PeerArrived();
            else SetPeer(here: false);
        }
        catch
        {
            await t.DisposeAsync();
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t = _transport;
            try
            {
                if (t is null) throw new NetException("No connection.");
                while (true)
                {
                    string? text = await t.ReceiveAsync(ct);
                    if (text is null) break;
                    _lastHeardAnything = DateTime.UtcNow;
                    if (text == "pong") continue;
                    if (NetProtocol.Parse(text) is { } m) Handle(m);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Dropped: fall through to reconnect.
            }

            if (ct.IsCancellationRequested || _closed) return;
            if (!await ReconnectAsync(ct)) return;
        }
    }

    /// <summary>Retries with back-off until connected, refused, or <see cref="NetSettings.GiveUpAfter"/> passes.</summary>
    private async Task<bool> ReconnectAsync(CancellationToken ct)
    {
        SetPeer(here: false);
        SetState(NetLinkState.Reconnecting);
        var started = DateTime.UtcNow;
        var delay = TimeSpan.FromMilliseconds(500);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(ct);
                return true;
            }
            catch (NetRefusedException ex)
            {
                Fail(ex.Message);
                return false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                if (DateTime.UtcNow - started > _settings.GiveUpAfter)
                {
                    Fail("Lost the connection to the server and couldn't get it back.");
                    return false;
                }
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { return false; }
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _settings.MaxRetryDelay.Ticks));
            }
        }
        return false;
    }

    /// <summary>One sender, so messages leave in order. A failed send is dropped: "resume" re-sends decisions.</summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var text in _outbox.Reader.ReadAllAsync(ct))
            {
                var t = _transport;
                if (t is null) continue;
                try
                {
                    await t.SendAsync(text, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // The receive loop notices the drop and reconnects.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_settings.Heartbeat, ct); } catch (OperationCanceledException) { return; }
            if (State != NetLinkState.Connected) continue;

            _outbox.Writer.TryWrite("ping");   // the relay answers "pong": proves our own link is alive
            if (_peerHere)
            {
                int sent, have;
                lock (_gate) { sent = _sentDecisions.Count; have = _receivedDecisions; }
                _outbox.Writer.TryWrite(NetProtocol.Serialize(new NetMessage { T = "hb", W = WaitingForOpponent, N = sent, Have = have }));
            }

            var now = DateTime.UtcNow;
            if (now - _lastHeardAnything > _settings.SilentAfter)
            {
                // Our link is dead even though nothing said so: drop it, the receive loop reconnects.
                var t = _transport;
                if (t is not null) _ = t.DisposeAsync().AsTask();
                continue;
            }
            if (_peerHere && _peerResponsive && now - _lastHeardPeer > _settings.SilentAfter)
            {
                _peerResponsive = false;
                Raise(() => OpponentPresenceChanged?.Invoke(false));
            }
        }
    }

    // ============================================================== message handling

    private void Handle(NetMessage m)
    {
        if (m.T == "relay")
        {
            switch (m.Ev)
            {
                case "peer-joined":
                    PeerArrived();
                    break;
                case "peer-left":
                    SetPeer(here: false);
                    break;
                case "error":
                    Fail(m.Msg ?? "The server closed the room.");
                    break;
            }
            return;
        }

        // Anything from the opponent proves they're alive.
        _lastHeardPeer = DateTime.UtcNow;
        if (!_peerHere || !_peerResponsive) SetPeer(here: true);

        switch (m.T)
        {
            case "hb":
                CheckStuck(m);
                break;

            case "resume":
                ResendFrom(m.Have ?? 0);
                break;

            case "d":
            {
                int n = m.N ?? 0;
                bool accept = false, gap = false;
                int have;
                lock (_gate)
                {
                    if (n == _receivedDecisions + 1) { _receivedDecisions++; accept = true; }
                    else if (n > _receivedDecisions + 1) gap = true;
                    have = _receivedDecisions;
                }
                if (accept) _decisions.Writer.TryWrite(m);
                else if (gap) SendResume(have); // something went missing: ask again
                break;
            }

            case "chat":
                if (!string.IsNullOrWhiteSpace(m.Text))
                {
                    string name = string.IsNullOrWhiteSpace(m.Name) ? "Opponent" : m.Name!;
                    Raise(() => ChatReceived?.Invoke(name, m.Text!));
                }
                break;

            case "bye":
                _peerQuit = true;
                _decisions.Writer.TryComplete(new OpponentLeftException("Your opponent left the game."));
                _lobby.Writer.TryComplete(new NetException("The other player left."));
                Raise(() => OpponentQuit?.Invoke(m.Reason ?? "left the game"));
                break;

            case "desync":
                _decisions.Writer.TryComplete(new DesyncException("Your opponent's game no longer matches yours."));
                break;

            case "hello" when HelloReply is not null:
                SendLobby(HelloReply);
                break;

            case "hello":
            case "start":
            case "reject":
                _lobby.Writer.TryWrite(m);
                break;
        }
    }

    /// <summary>
    /// Both games waiting for the other, with every decision delivered both ways, on two heartbeats in a row: they
    /// can't both be right, so the games no longer match.
    /// </summary>
    private void CheckStuck(NetMessage hb)
    {
        bool stuck;
        lock (_gate)
            stuck = hb.W == true && WaitingForOpponent
                    && hb.N == _receivedDecisions && hb.Have == _sentDecisions.Count;
        _stuckBeats = stuck ? _stuckBeats + 1 : 0;
        if (_stuckBeats < 2) return;
        ReportDesync();
        _decisions.Writer.TryComplete(new DesyncException(
            "The two games no longer match (each is waiting for the other). Make sure you both run the same version of the app with the same card data."));
    }

    /// <summary>The opponent is (back) on the relay: ask for anything we missed.</summary>
    private void PeerArrived()
    {
        _lastHeardPeer = DateTime.UtcNow;
        SetPeer(here: true);
        int have;
        lock (_gate) have = _receivedDecisions;
        SendResume(have);
    }

    private void SendResume(int have) =>
        _outbox.Writer.TryWrite(NetProtocol.Serialize(new NetMessage { T = "resume", Have = have }));

    private void ResendFrom(int have)
    {
        List<string> again;
        lock (_gate) again = _sentDecisions.Skip(Math.Max(0, have)).ToList();
        foreach (var json in again) _outbox.Writer.TryWrite(json);
    }

    private void SetPeer(bool here)
    {
        bool before = OpponentConnected;
        _peerHere = here;
        _peerResponsive = here;
        if (here) _lastHeardPeer = DateTime.UtcNow;
        bool after = OpponentConnected;
        if (before != after && !_peerQuit) Raise(() => OpponentPresenceChanged?.Invoke(after));
    }

    private void SetState(NetLinkState s)
    {
        if (State == s) return;
        State = s;
        Raise(() => StateChanged?.Invoke(s));
    }

    private void Fail(string reason)
    {
        if (_closed) return;
        _decisions.Writer.TryComplete(new OpponentLeftException(reason));
        _lobby.Writer.TryComplete(new NetException(reason));
        Raise(() => Failed?.Invoke(reason));
        _ = DisposeAsync().AsTask();
    }

    private void Raise(Action a)
    {
        if (_events is null) a();
        else _events.Post(_ => a(), null);
    }

    private static string Short(Exception ex) => (ex.InnerException ?? ex).Message;
}
