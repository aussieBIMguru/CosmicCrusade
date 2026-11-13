using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Net;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// Online play: protocol, state checksum, lobby, and whole lockstep games between two engines over an in-memory relay
/// (including dropped connections). The real relay's room rules are tested in server/cloudflare/test.
/// </summary>
public class NetTests
{
    private static readonly NetSettings Fast = new()
    {
        Heartbeat = TimeSpan.FromMilliseconds(50),
        SilentAfter = TimeSpan.FromSeconds(3),
        GiveUpAfter = TimeSpan.FromSeconds(20),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        MaxRetryDelay = TimeSpan.FromMilliseconds(200),
    };

    // ================================================================== protocol

    [Fact]
    public void Options_survive_the_wire()
    {
        var o = TestData.DefaultOptions(424242);
        o.Warzones = true;
        o.Archons = true;
        o.Spacecraft = true;
        o.Leaders = false;
        o.DeckMode = DeckMode.TrueSingleton;
        o.EmptyDeck = EmptyDeckRule.NoRefill;
        o.RoundsToWin = 5;
        o.CopyCounts["0U01"] = 7;

        var json = NetProtocol.Serialize(new NetMessage { T = "start", Options = NetOptions.From(o) });
        var back = NetProtocol.Parse(json)!.Options!.ToGameOptions();

        Assert.Equal(o.Seed, back.Seed);
        Assert.True(back.Warzones && back.Archons && back.Spacecraft && !back.Leaders);
        Assert.Equal(DeckMode.TrueSingleton, back.DeckMode);
        Assert.Equal(EmptyDeckRule.NoRefill, back.EmptyDeck);
        Assert.Equal(5, back.RoundsToWin);
        Assert.Equal(7, back.CopyCounts["0u01"]); // lookups stay case-insensitive
        Assert.Equal(o.CopyCounts.Count, back.CopyCounts.Count);
        Assert.Equal(o.LeaderRelatedCards.OrderBy(x => x), back.LeaderRelatedCards.OrderBy(x => x));

        // Same options → same shuffle.
        var a = GameEngine.CreateGame(TestData.Db, o);
        var b = GameEngine.CreateGame(TestData.Db, back);
        Assert.Equal(StateHash.Of(a), StateHash.Of(b));
    }

    [Fact]
    public void Messages_round_trip_and_junk_is_ignored()
    {
        var m = NetProtocol.Parse(NetProtocol.Serialize(new NetMessage { T = "d", N = 3, K = "c", V = new[] { 0, 2 }, H = 123u }))!;
        Assert.Equal(("d", 3, "c", 123u), (m.T, m.N!.Value, m.K!, m.H!.Value));
        Assert.Equal(new[] { 0, 2 }, m.V);
        Assert.Contains("\"t\":\"d\"", NetProtocol.Serialize(m)); // matches the relay's lower-case "t"

        var relay = NetProtocol.Parse("{\"t\":\"relay\",\"ev\":\"joined\",\"role\":\"guest\",\"peer\":true}")!;
        Assert.Equal(("relay", "joined", true), (relay.T, relay.Ev!, relay.Peer!.Value));

        Assert.Null(NetProtocol.Parse("pong"));
        Assert.Null(NetProtocol.Parse("{not json"));
        Assert.Null(NetProtocol.Parse("{\"x\":1}"));
    }

    [Theory]
    [InlineData("cosmic.example.workers.dev", "wss://cosmic.example.workers.dev/room/ABCDE")]
    [InlineData("https://cosmic.example.workers.dev/", "wss://cosmic.example.workers.dev/room/ABCDE")]
    [InlineData("http://localhost:5080", "ws://localhost:5080/room/ABCDE")]
    [InlineData("ws://192.168.1.20:5080", "ws://192.168.1.20:5080/room/ABCDE")]
    public void Server_box_accepts_what_people_paste(string typed, string expectedStart)
    {
        var uri = NetProtocol.RoomUri(typed, "ABCDE", "host", "secretkey123");
        Assert.StartsWith(expectedStart + "?role=host&key=secretkey123&proto=" + NetProtocol.RelayVersion, uri.ToString());
    }

    [Fact]
    public void Codes_keys_names_and_fingerprints()
    {
        var code = NetProtocol.NewRoomCode();
        Assert.Matches("^[A-Z2-9]{5}$", code);
        Assert.DoesNotMatch("[01OIL]", code);
        Assert.Matches("^[A-Za-z0-9_-]{8,64}$", NetProtocol.NewKey());
        Assert.Equal("ABCDE", NetProtocol.NormaliseCode(" ab-c de "));
        Assert.Equal("Player", NetLobby.Display("  \n "));
        Assert.Equal(20, NetLobby.Display(new string('x', 40)).Length);
        Assert.Equal(NetProtocol.DataFingerprint("a\r\nb"), NetProtocol.DataFingerprint("a\nb"));
        Assert.NotEqual(NetProtocol.DataFingerprint("a\nb"), NetProtocol.DataFingerprint("a\nc"));
    }

    // ================================================================== state checksum

    [Fact]
    public void State_hash_matches_for_same_seed_and_changes_with_the_game()
    {
        var a = TestData.NewGame(9);
        var b = TestData.NewGame(9);
        var c = TestData.NewGame(10);
        Assert.Equal(StateHash.Of(a), StateHash.Of(b));
        Assert.NotEqual(StateHash.Of(a), StateHash.Of(c));
        Assert.Equal(StateHash.Of(a), StateHash.Of(a.Clone(1234))); // the RNG's own state isn't part of it

        uint before = StateHash.Of(a);
        a.MoveToHand(a.UnitDeck[^1], 0);
        Assert.NotEqual(before, StateHash.Of(a));
    }

    // ================================================================== whole games

    private sealed record LockstepResult(GameOutcome Host, GameOutcome Guest, uint HostHash, uint GuestHash, int HostSent, int GuestSent, int Drops);

    /// <summary>
    /// Host (player 0) and guest (player 1) each run their own engine, connected through the in-memory relay, each
    /// with a heuristic AI in its own seat, exactly as two apps would with two humans.
    /// </summary>
    private static LockstepResult PlayOnline(int seed, Action<GameOptions>? configure = null, int dropGuestEvery = 0,
        Action<GameOptions>? tamperGuest = null)
    {
        return SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            string code = NetProtocol.NewRoomCode();
            var host = relay.Connection(code, "host", Fast);
            var guest = relay.Connection(code, "guest", Fast);
            await host.StartAsync(ct);
            await guest.StartAsync(ct);

            var options = TestData.DefaultOptions(seed);
            configure?.Invoke(options);
            var hostSetupTask = NetLobby.HostAsync(host, options, "Gavin", "data", ct);
            var guestSetup = await NetLobby.JoinAsync(guest, "Sam", "data", ct);
            var hostSetup = await hostSetupTask;
            Assert.Equal((0, 1), (hostSetup.MySeat, guestSetup.MySeat));
            Assert.Equal(("Gavin", "Sam"), (guestSetup.HostName, guestSetup.GuestName));

            relay.DropGuestEvery = dropGuestEvery; // only once the game is running
            tamperGuest?.Invoke(guestSetup.Options);

            var hostState = GameEngine.CreateGame(TestData.Db, hostSetup.Options, hostSetup.HostName, hostSetup.GuestName);
            var guestState = GameEngine.CreateGame(TestData.Db, guestSetup.Options, guestSetup.HostName, guestSetup.GuestName);
            var hostEngine = new GameEngine(hostState, new LocalSeat(new HeuristicAi(seed), host), new RemoteSeat(host));
            var guestEngine = new GameEngine(guestState, new RemoteSeat(guest), new LocalSeat(new HeuristicAi(seed + 1), guest));

            try
            {
                var hostRun = hostEngine.RunAsync(ct);
                var guestRun = guestEngine.RunAsync(ct);
                var results = await Task.WhenAll(hostRun, guestRun);
                return new LockstepResult(results[0], results[1], StateHash.Of(hostState), StateHash.Of(guestState),
                    host.DecisionsSent, guest.DecisionsSent, relay.Drops);
            }
            finally
            {
                await host.DisposeAsync();
                await guest.DisposeAsync();
            }
        });
    }

    private static void AssertSameGame(LockstepResult r)
    {
        Assert.Equal(r.Host, r.Guest);
        Assert.Equal(r.HostHash, r.GuestHash);
        Assert.True(r.HostSent > 0 && r.GuestSent > 0, "both players made decisions");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Lockstep_base_game_ends_the_same_on_both_sides(int seed) => AssertSameGame(PlayOnline(seed));

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void Lockstep_with_every_module_on(int seed) =>
        AssertSameGame(PlayOnline(seed, o => { o.Warzones = true; o.Archons = true; o.Spacecraft = true; }));

    [Fact]
    public void Lockstep_random_draw_and_no_leaders() =>
        AssertSameGame(PlayOnline(21, o => { o.DeckMode = DeckMode.RandomDraw; o.Leaders = false; }));

    [Fact]
    public void Dropped_connections_reconnect_and_resend_missing_moves()
    {
        // A heuristic game is only a few dozen decisions, so drop often (every 7th forwarded message, heartbeats
        // included) and play a longer game, so the guest drops several times mid-game.
        var r = PlayOnline(5, o => { o.Warzones = true; o.RoundsToWin = 5; }, dropGuestEvery: 7);
        Assert.True(r.Drops >= 3, $"expected several drops, got {r.Drops} ({r.HostSent} host and {r.GuestSent} guest decisions)");
        AssertSameGame(r);
    }

    [Fact]
    public void Different_rules_on_each_side_are_caught_as_a_desync()
    {
        var ex = Assert.ThrowsAny<Exception>(() => PlayOnline(6, tamperGuest: o => o.Seed += 1));
        Assert.IsType<DesyncException>(ex);
    }

    // ================================================================== lobby, chat, leaving

    [Fact]
    public void Lobby_refuses_a_guest_with_different_card_data()
    {
        SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            var host = relay.Connection("ROOM1", "host", Fast);
            var guest = relay.Connection("ROOM1", "guest", Fast);
            await host.StartAsync(ct);
            await guest.StartAsync(ct);
            var hosting = NetLobby.HostAsync(host, TestData.DefaultOptions(1), "Gavin", "data-A", ct);
            var joining = NetLobby.JoinAsync(guest, "Sam", "data-B", ct);
            var h = await Assert.ThrowsAsync<NetException>(() => hosting);
            var g = await Assert.ThrowsAsync<NetException>(() => joining);
            Assert.Contains("card data", h.Message);
            Assert.Contains("card data", g.Message);
            await host.DisposeAsync();
            await guest.DisposeAsync();
            return 0;
        });
    }

    [Fact]
    public void Joining_a_code_nobody_hosts_says_so()
    {
        SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            var guest = relay.Connection("NOPE2", "guest", Fast);
            var ex = await Assert.ThrowsAsync<NetRefusedException>(() => guest.StartAsync(ct));
            Assert.Contains("No game", ex.Message);
            return 0;
        });
    }

    [Fact]
    public void Same_names_are_told_apart()
    {
        SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            var host = relay.Connection("ROOM2", "host", Fast);
            var guest = relay.Connection("ROOM2", "guest", Fast);
            await host.StartAsync(ct);
            await guest.StartAsync(ct);
            var hosting = NetLobby.HostAsync(host, TestData.DefaultOptions(1), "Gavin", "d", ct);
            var g = await NetLobby.JoinAsync(guest, "gavin", "d", ct);
            await hosting;
            Assert.Equal("gavin 2", g.GuestName);
            await host.DisposeAsync();
            await guest.DisposeAsync();
            return 0;
        });
    }

    [Fact]
    public void Chat_arrives_and_leaving_ends_the_opponents_wait()
    {
        SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            var host = relay.Connection("ROOM3", "host", Fast);
            var guest = relay.Connection("ROOM3", "guest", Fast);
            var chat = new TaskCompletionSource<(string, string)>();
            var quit = new TaskCompletionSource<string>();
            host.ChatReceived += (n, t) => chat.TrySetResult((n, t));
            host.OpponentQuit += r => quit.TrySetResult(r);
            await host.StartAsync(ct);
            await guest.StartAsync(ct);

            guest.SendChat("Sam", "  good luck!  ");
            Assert.Equal(("Sam", "good luck!"), await chat.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            var waiting = host.NextDecisionAsync(ct);
            await guest.LeaveAsync("closed the game");
            Assert.Equal("closed the game", await quit.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<OpponentLeftException>(() => waiting);
            await host.DisposeAsync();
            return 0;
        });
    }

    [Fact]
    public void A_dropped_opponent_is_reported_and_their_return_too()
    {
        SingleThreadPump.Run(async ct =>
        {
            var relay = new InMemoryRelay();
            var host = relay.Connection("ROOM4", "host", Fast);
            var guest = relay.Connection("ROOM4", "guest", Fast);
            var seen = new List<bool>();
            var back = new TaskCompletionSource();
            host.OpponentPresenceChanged += here =>
            {
                seen.Add(here);
                if (here && seen.Contains(false)) back.TrySetResult();
            };
            await host.StartAsync(ct);
            await guest.StartAsync(ct);
            await Task.Delay(200, ct);

            relay.DropGuest("ROOM4"); // the guest's app reconnects by itself
            await back.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { true, false, true }, seen.Take(3));
            Assert.True(host.OpponentConnected);
            await host.DisposeAsync();
            await guest.DisposeAsync();
            return 0;
        });
    }
}