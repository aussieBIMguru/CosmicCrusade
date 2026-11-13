using System.Text.Json;
using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// Something that happened, for "Whenever ..." abilities (<c>OnEvent</c>) and Senate Chambers.
/// <paramref name="Player"/> is whoever did it (played, drew, assembled) or, for UnitNullified, the Unit's controller.
/// </summary>
internal sealed record TriggerEvent(
    string Name,
    int Player,
    CardInstance? Card,
    CardInstance? Source = null,
    int SourceController = -1,
    int UnitController = -1,
    int AssembledRank = 0,
    CardInstance? AssembleTarget = null);

/// <summary>
/// M3.2b: phase triggers (End of Main, Clean-up), "Whenever ..." triggers and state checks.
///   • Ordering (rulings #12, High Noon errata): <c>"priority": "First"</c> (Y'nosh) first, then the first player's
///     triggers before the second player's, and <c>"ordering": "AfterRankModifiers"</c> (High Noon) last.
///     A player's own simultaneous triggers resolve in board order (Unit nodes, Leader, Order nodes, Warzones).
///   • "Whenever" triggers ignore the card's own event unless <c>includeSelf</c> (Fast Eddie).
///   • <c>oncePerRound</c> (Xeno Laser) is remembered on the card for the round.
/// </summary>
public sealed partial class GameEngine
{
    private int? _forcedRoundWinner;
    private string? _forcedRoundReason;
    private bool _roundEndsNow;
    /// <summary>Ceasefire: the Main Phase ends at once; the round is still scored normally (End of Main triggers fire).</summary>
    private bool _mainEndsNow;
    private readonly HashSet<CardInstance> _remainInPlay = new();

    private void ResetRoundFlags()
    {
        _forcedRoundWinner = null;
        _forcedRoundReason = null;
        _roundEndsNow = false;
        _mainEndsNow = false;
        _remainInPlay.Clear();
    }

    /// <summary>WinRound (The Empress, Senate Chambers). The first effect to decide the round wins it.</summary>
    internal async Task WinRoundAsync(int player, CardInstance source, CancellationToken ct)
    {
        if (player < 0 || _forcedRoundWinner is not null) return;
        _forcedRoundWinner = player;
        _forcedRoundReason = source.Name;
        await EmitAsync(new LogEvent($"  {source.Name}: {State.Players[player].Name} wins the round!"), ct);
    }

    /// <summary>Dimensional Rift: "Add the top N Warzones to the current Warzone" – they all enter play at once.</summary>
    internal async Task RevealWarzonesAsync(int count, CardInstance source, CancellationToken ct)
    {
        var added = new List<CardInstance>();
        for (int i = 0; i < count && State.WarzoneDeck.Count > 0; i++)
        {
            var wz = State.WarzoneDeck[^1];
            State.MoveToPile(wz, PileKind.ActiveWarzone);
            added.Add(wz);
        }
        if (added.Count > 0) await EmitAsync(new WarzonesAddedEvent(added, source), ct);
        else await EmitAsync(new LogEvent($"  {source.Name}: the Warzone deck is empty."), ct);
    }

    // ================================================================== scripted handlers (M3.3)

    /// <summary>Card abilities written in C# rather than as data (<c>"script"</c> in cards.json).</summary>
    internal static readonly HashSet<string> Scripts = new()
    {
        "Wormhole", "RearrangeAllNodes", "Terraform", "EnterAsCopy", "RetriggerAbilities",
        // Archons
        "Ceasefire", "Insurrection", "QuantumLeap",
        // Spacecraft
        "Mothership", "CrewFromPile", "SporePod", "Abduct", "SwapCrews", "BeamUp", "Hijack", "GelatinousHull",
        "Engulf", "ExoFrame", "Refit", "SelfDestruct",
    };

    internal async Task RunScriptAsync(string name, JsonElement effect, EffectContext ctx, CancellationToken ct)
    {
        switch (name)
        {
            case "Wormhole":
                await WormholeAsync(ctx.Controller, ctx.Source, ct);
                break;
            case "RearrangeAllNodes":
                await RearrangeAllNodesAsync(ctx, ct);
                break;
            case "Terraform":
                await TerraformAsync(ctx, ct);
                break;
            case "EnterAsCopy":
                await EnterAsCopyAsync(effect, ctx, ct);
                break;
            case "RetriggerAbilities":
                await RetriggerAbilitiesAsync(ctx, ct);
                break;
            case "Ceasefire":
                await CeasefireAsync(ctx, ct);
                break;
            case "Insurrection":
                await InsurrectionAsync(ctx, ct);
                break;
            case "QuantumLeap":
                await QuantumLeapAsync(ctx, ct);
                break;
            case "Mothership":
            case "CrewFromPile":
            case "SporePod":
            case "Abduct":
            case "SwapCrews":
            case "BeamUp":
            case "Hijack":
            case "GelatinousHull":
            case "Engulf":
            case "ExoFrame":
            case "Refit":
            case "SelfDestruct":
                await RunSpacecraftScriptAsync(name, effect, ctx, ct);
                break;
            default:
                await EmitAsync(new LogEvent($"  {ctx.Source.Name}: this ability isn't implemented yet."), ct);
                break;
        }
    }

    /// <summary>
    /// Wormhole: "Look at the top 3 Warzones. Retire the current Warzone(s) and put 1 of the other Warzones in play.
    /// Retire the rest." A Retired Warzone goes to the bottom of the Warzone deck (spec §8). Only the issuer sees
    /// the three.
    /// </summary>
    private async Task WormholeAsync(int player, CardInstance source, CancellationToken ct)
    {
        var deck = State.WarzoneDeck;
        var top = deck.Skip(Math.Max(0, deck.Count - 3)).Reverse().ToList(); // top card first
        if (top.Count == 0)
        {
            await EmitAsync(new LogEvent($"  {source.Name}: the Warzone deck is empty."), ct);
            return;
        }

        var pick = await ChooseAsync(new ChoiceRequest(player, source,
            $"{source.Name}: choose the Warzone to put in play (the current one and the others are Retired)",
            ChoiceKind.Cards, top.Select(w => new ChoiceOption(w.Name, w)).ToList(), 1, 1, "Wormhole"), ct);
        var chosen = top[pick.Count > 0 ? pick[0] : 0];

        foreach (var wz in State.ActiveWarzones.ToList())
        {
            State.MoveToPileBottom(wz, PileKind.WarzoneDeck);
            await EmitAsync(new WarzoneReturnedEvent(wz), ct);
        }
        foreach (var wz in top.Where(w => !ReferenceEquals(w, chosen)))
            State.MoveToPileBottom(wz, PileKind.WarzoneDeck);
        State.MoveToPile(chosen, PileKind.ActiveWarzone);
        await EmitAsync(new WarzonesAddedEvent(new[] { chosen }, source), ct);
    }

    /// <summary>Preferred Candidate: "this Unit may remain in play" during this Clean-up.</summary>
    internal void RemainInPlay(CardInstance card) => _remainInPlay.Add(card);

    // ================================================================== sources

    private sealed record TriggerSource(CardInstance Card, int Controller, Node? Node, int Order);

    /// <summary>Cards in play that can have triggered abilities, in board order (Warzones: controller -1).</summary>
    private List<TriggerSource> TriggerSources()
    {
        var list = new List<TriggerSource>();
        int order = 0;
        foreach (var p in State.Players)
        {
            foreach (var n in p.ScoringNodes)
                foreach (var c in n.Occupants) // a spacecraft, then its crew
                    list.Add(new TriggerSource(c, n.Side, n, order++));
            foreach (var n in p.OrderNodes)
                if (n.Card is { } c) list.Add(new TriggerSource(c, n.Side, n, order++));
        }
        foreach (var wz in State.ActiveWarzones) list.Add(new TriggerSource(wz, -1, null, order++));
        return list;
    }

    private static IEnumerable<JsonElement> EffectsWith(CardDefinition def, string trigger) =>
        def.Effects.ValueKind != JsonValueKind.Array
            ? Enumerable.Empty<JsonElement>()
            : def.Effects.EnumerateArray().Where(e => e.TryGetProperty("trigger", out var t) && t.GetString() == trigger);

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ================================================================== phase triggers

    /// <summary>Runs every End of Main / Clean-up ability of the cards in play, in rules order.</summary>
    private async Task FirePhaseTriggersAsync(string trigger, CancellationToken ct)
    {
        var pending = new List<(TriggerSource Src, JsonElement Effect, int Sort)>();
        foreach (var src in TriggerSources())
        {
            if (StaticLayer.IsTriggerSuppressed(State, src.Card, trigger)) continue;
            foreach (var e in EffectsWith(src.Card.Definition, trigger))
            {
                int sort = Str(e, "priority") == "First" ? 0 : Str(e, "ordering") == "AfterRankModifiers" ? 3 : 1;
                if (sort == 1 && src.Controller >= 0 && src.Controller != State.FirstPlayer) sort = 2;
                pending.Add((src, e, sort));
            }
        }

        foreach (var (src, effect, _) in pending.OrderBy(x => x.Sort).ThenBy(x => x.Src.Order))
        {
            ct.ThrowIfCancellationRequested();
            // It may have left play (or been Suppressed) because of an earlier trigger.
            if (!StillThere(src) || StaticLayer.IsTriggerSuppressed(State, src.Card, trigger)) continue;
            if (!EffectSupport.IsEffectLive(effect))
            {
                await EmitAsync(new LogEvent($"  {src.Card.Name}: this ability isn't implemented yet."), ct);
                continue;
            }
            int controller = src.Controller >= 0 ? src.Controller : State.FirstPlayer;
            var ctx = new EffectContext(this, src.Card, controller, src.Node, trigger) { Memory = src.Card.Memory };
            await Runner.RunAsync(effect, ctx, ct);
        }
    }

    private bool StillThere(TriggerSource src) => src.Node is not null
        ? src.Node.Holds(src.Card)
        : State.ActiveWarzones.Contains(src.Card);

    // ================================================================== "Whenever ..." (OnEvent)

    private int _raiseDepth;

    /// <summary>Fires every OnEvent ability listening for <paramref name="ev"/>.</summary>
    internal async Task RaiseAsync(TriggerEvent ev, CancellationToken ct)
    {
        if (++_raiseDepth > 30) { _raiseDepth--; return; } // runaway chains (shouldn't happen)
        try
        {
            foreach (var src in TriggerSources())
            {
                foreach (var e in EffectsWith(src.Card.Definition, "OnEvent"))
                {
                    if (!Listens(e, ev.Name)) continue;
                    if (!StillThere(src) || StaticLayer.IsTriggerSuppressed(State, src.Card, "OnEvent:" + ev.Name)) continue;
                    if (ReferenceEquals(ev.Card, src.Card) && !(e.TryGetProperty("includeSelf", out var inc) && inc.ValueKind == JsonValueKind.True))
                        continue;
                    if (!EffectSupport.IsEffectLive(e))
                    {
                        await EmitAsync(new LogEvent($"  {src.Card.Name}: this ability isn't implemented yet."), ct);
                        continue;
                    }

                    bool once = e.TryGetProperty("oncePerRound", out var op) && op.ValueKind == JsonValueKind.True;
                    if (once && src.Card.Memory.TryGetValue("$firedRound", out var r) && r is int round && round == State.Round) continue;

                    int controller = EventController(src, e, ev);
                    if (controller < 0) continue;
                    var ctx = new EffectContext(this, src.Card, controller, src.Node, "OnEvent") { Memory = src.Card.Memory, Event = ev };
                    if (e.TryGetProperty("filter", out var f) && !Runner.EventFilterMatches(f, ctx)) continue;

                    if (once) src.Card.Memory["$firedRound"] = State.Round;
                    await Runner.RunAsync(e, ctx, ct);
                }
            }
        }
        finally
        {
            _raiseDepth--;
        }
    }

    private static bool Listens(JsonElement e, string name)
    {
        if (!e.TryGetProperty("event", out var ev)) return false;
        if (ev.ValueKind == JsonValueKind.String) return ev.GetString() == name;
        return ev.ValueKind == JsonValueKind.Array && ev.EnumerateArray().Any(x => x.GetString() == name);
    }

    /// <summary>
    /// "You" for the ability: the source's controller. A Warzone has none, so its ops name the player
    /// (Mercenary Camp: the Nullifying card's controller; Primordial Marsh: the played card's controller).
    /// </summary>
    private static int EventController(TriggerSource src, JsonElement effect, TriggerEvent ev)
    {
        if (src.Controller >= 0) return src.Controller;
        if (effect.TryGetProperty("do", out var ops) && ops.ValueKind == JsonValueKind.Array)
            foreach (var op in ops.EnumerateArray())
                switch (Str(op, "player"))
                {
                    case "Event.Source.Controller": return ev.SourceController;
                    case "Event.Card.Controller": return ev.Player;
                }
        return ev.Player;
    }

    // ================================================================== Played / state checks

    /// <summary>A Unit was Played: OnEvent UnitPlayed / CardPlayed (/ CardPlayedFromHand), then state checks.</summary>
    private async Task RaisePlayedAsync(int player, CardInstance card, bool fromHand, CancellationToken ct)
    {
        string specific = card.Definition.IsUnit ? "UnitPlayed" : "OrderPlayed";
        await RaiseAsync(new TriggerEvent(specific, player, card), ct);
        await RaiseAsync(new TriggerEvent("CardPlayed", player, card), ct);
        if (fromHand) await RaiseAsync(new TriggerEvent("CardPlayedFromHand", player, card), ct);
    }

    /// <summary>
    /// Senate Chambers: "If a Player fills all 4 Nodes they control, they Win the Round" – checked immediately and
    /// the round ends at once (ruling #22). In No Leaders mode: all 3 Unit nodes.
    /// </summary>
    private async Task CheckStateAsync(CancellationToken ct)
    {
        if (_roundEndsNow) return;
        foreach (var wz in State.ActiveWarzones.ToList())
        {
            if (StaticLayer.IsSuppressed(State, wz)) continue;
            foreach (var e in EffectsWith(wz.Definition, "StateCheck"))
            {
                if (!e.TryGetProperty("condition", out var c) || !c.TryGetProperty("playerFillsAll", out _)) continue;
                foreach (int p in TurnOrder())
                {
                    if (!State.Players[p].ScoringNodes.All(n => n.Card is not null)) continue;
                    await WinRoundAsync(p, wz, ct);
                    _roundEndsNow = true;
                    return;
                }
            }
        }
    }
}
