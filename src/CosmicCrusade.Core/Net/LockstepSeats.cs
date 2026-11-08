using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Net;

/// <summary>
/// The local player's seat in an online game: asks the wrapped controller (the human UI), then sends the decision to
/// the opponent as an index into the same list their engine is looking at.
/// </summary>
public sealed class LocalSeat : IPlayerController
{
    private readonly IPlayerController _inner;
    private readonly NetConnection _net;

    public LocalSeat(IPlayerController inner, NetConnection net)
    {
        _inner = inner;
        _net = net;
    }

    public async Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct)
    {
        uint hash = StateHash.Of(context.State);
        var action = await _inner.ChooseActionAsync(context, ct);
        _net.SendDecision("a", new[] { IndexOf(context.Legal, action) }, hash);
        return action;
    }

    public async Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct)
    {
        uint hash = StateHash.Of(context.State);
        var action = await _inner.ChooseResponseAsync(context, ct);
        _net.SendDecision("r", new[] { action is null or PassAction ? -1 : IndexOf(context.Legal, action) }, hash);
        return action is PassAction ? null : action;
    }

    public async Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
    {
        uint hash = StateHash.Of(state);
        var picks = await _inner.ChooseAsync(request, state, ct);
        _net.SendDecision("c", picks.ToArray(), hash);
        return picks;
    }

    private static int IndexOf(IReadOnlyList<GameAction> legal, GameAction action)
    {
        for (int i = 0; i < legal.Count; i++)
            if (ReferenceEquals(legal[i], action)) return i;
        for (int i = 0; i < legal.Count; i++)
            if (legal[i].Equals(action)) return i;
        throw new InvalidOperationException($"Decision '{action.Describe()}' is not one of the legal options.");
    }
}

/// <summary>
/// The opponent's seat in an online game: waits for their decision from the network and turns the index back into
/// the matching option from our own engine's list. Each decision carries a checksum of the opponent's game state
/// just before they decided; if ours differs, the games have drifted apart (<see cref="DesyncException"/>).
/// </summary>
public sealed class RemoteSeat : IPlayerController
{
    private readonly NetConnection _net;

    public RemoteSeat(NetConnection net) => _net = net;

    /// <summary>True while we're waiting for the opponent (the UI shows "thinking").</summary>
    public event Action<bool>? WaitingChanged;

    public async Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct)
    {
        var v = await NextAsync("a", context.State, ct);
        return Pick(context.Legal, v[0]);
    }

    public async Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct)
    {
        var v = await NextAsync("r", context.State, ct);
        return v[0] < 0 ? null : Pick(context.Legal, v[0]);
    }

    public async Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
    {
        var v = await NextAsync("c", state, ct);
        if (!request.IsValid(v)) Desync($"an invalid answer to '{request.Prompt}'");
        return v;
    }

    private async Task<int[]> NextAsync(string kind, GameState state, CancellationToken ct)
    {
        uint mine = StateHash.Of(state);
        WaitingChanged?.Invoke(true);
        _net.WaitingForOpponent = true;
        NetMessage m;
        try
        {
            m = await _net.NextDecisionAsync(ct);
        }
        finally
        {
            _net.WaitingForOpponent = false;
            WaitingChanged?.Invoke(false);
        }
        if (m.K != kind) Desync($"a '{m.K}' decision where '{kind}' was expected");
        if (m.H != mine) Desync("a different game state");
        if (m.V is null || (kind != "c" && m.V.Length != 1)) Desync("a malformed decision");
        return m.V!;
    }

    private GameAction Pick(IReadOnlyList<GameAction> legal, int index)
    {
        if (index < 0 || index >= legal.Count) Desync("an option that doesn't exist here");
        return legal[index];
    }

    private void Desync(string what)
    {
        _net.ReportDesync();
        throw new DesyncException($"The two games no longer match (the opponent sent {what}). " +
                                  "Make sure you both run the same version of the app with the same card data.");
    }
}

/// <summary>
/// A checksum of everything both players' games must agree on: round, phase, whose turn, every card's place and its
/// per-instance state, deck order, locks and scores. FNV-1a over integers: stable across machines and runs (no
/// <c>string.GetHashCode</c>, which .NET randomises per process).
/// </summary>
public static class StateHash
{
    public static uint Of(GameState s)
    {
        var h = new Fnv();
        h.Add(s.Round);
        h.Add((int)s.Phase);
        h.Add(s.ActivePlayer);
        h.Add(s.FirstPlayer);
        h.Add(s.LastRoundWinner ?? -1);
        foreach (var p in s.Players)
        {
            h.Add(p.RoundsWon);
            h.Add(p.ArchonTaken ? 1 : 0);
            Card(ref h, p.ArchonSlot);
            h.Add(p.Hand.Count);
            foreach (var c in p.Hand) Card(ref h, c);
            foreach (var n in p.AllNodes)
            {
                h.Add(n.IsLocked ? 1 : 0);
                Card(ref h, n.Card);
                Card(ref h, n.Crew);
            }
        }
        foreach (var pile in new[] { s.UnitDeck, s.OrderDeck, s.RetiredUnits, s.NullifiedUnits, s.IssuedOrders, s.WarzoneDeck, s.ActiveWarzones, s.ArchonDeck })
        {
            h.Add(pile.Count);
            foreach (var c in pile) h.Add(c.InstanceId);
        }
        return h.Value;
    }

    private static void Card(ref Fnv h, CardInstance? c)
    {
        if (c is null)
        {
            h.Add(0);
            return;
        }
        h.Add(c.InstanceId);
        h.Add(c.Definition.Id);
        h.Add(c.BaseRankOverride ?? int.MinValue);
        h.Add(c.PersistentModifiers.Count);
        foreach (var m in c.PersistentModifiers) h.Add(m.Amount);
        int races = 0;
        foreach (var r in c.Races) races |= 1 << (int)r;
        h.Add(races);
        h.Add(c.AbilitiesSuppressed ? 1 : 0);
    }

    private struct Fnv
    {
        private uint _h;

        public Fnv() => _h = 2166136261;

        public readonly uint Value => _h;

        public void Add(int v)
        {
            for (int i = 0; i < 4; i++)
            {
                _h ^= (byte)(v >> (8 * i));
                _h *= 16777619;
            }
        }

        public void Add(string s)
        {
            foreach (char ch in s) Add(ch);
            Add(-1);
        }
    }
}
