using System.Text.Json;
using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// Engine primitives used by abilities (M3). Each publishes its event(s). A Unit that leaves play (Unit/Leader node →
/// anywhere) fires its "When this Unit leaves play" ability and, if Nullified, its "If this Unit is Nullified" ability.
/// Leaving play resets Rank changes, race changes, memory and suppression.
/// </summary>
public sealed partial class GameEngine
{
    private EffectRunner? _runner;
    private EffectRunner Runner => _runner ??= new EffectRunner(this);

    // ================================================================== triggers

    /// <summary>
    /// Runs every <paramref name="trigger"/> effect of the card. Effects the interpreter can't run yet are logged.
    /// </summary>
    internal async Task FireTriggersAsync(CardInstance card, string trigger, int controller, Node? selfNode,
        Dictionary<string, object> memory, StackItem? eventItem, CancellationToken ct)
    {
        var effects = card.Definition.Effects;
        if (effects.ValueKind != JsonValueKind.Array) return;

        foreach (var effect in effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("trigger", out var t) || t.GetString() != trigger) continue;
            if (!EffectSupport.IsEffectLive(effect))
            {
                await EmitAsync(new LogEvent($"  {card.Name}: this ability isn't implemented yet."), ct);
                continue;
            }
            var ctx = new EffectContext(this, card, controller, selfNode, trigger) { Memory = memory, EventItem = eventItem };
            await Runner.RunAsync(effect, ctx, ct);
        }
    }

    private async Task LeftPlayAsync(CardInstance card, Node node, Dictionary<string, object> memory, bool suppressed,
        bool nullified, CancellationToken ct)
    {
        if (suppressed) return;
        await FireTriggersAsync(card, "OnLeavePlay", node.Side, node, memory, null, ct);
        if (nullified) await FireTriggersAsync(card, "OnSelfNullified", node.Side, node, memory, null, ct);
    }

    // ================================================================== choices

    internal async Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, CancellationToken ct)
    {
        int max = Math.Min(request.Max, request.Options.Count);
        int min = Math.Min(request.Min, max);
        var r = request with { Min = min, Max = max };
        if (r.Options.Count == 0 || max == 0) return Array.Empty<int>();

        IReadOnlyList<int> picks;
        if (r.Kind != ChoiceKind.YesNo && min == r.Options.Count)
            picks = Enumerable.Range(0, r.Options.Count).ToList(); // Forced: everything must be chosen.
        else
            picks = await _controllers[r.Player].ChooseAsync(r, State, ct);

        if (!r.IsValid(picks))
            throw new InvalidOperationException($"Player {r.Player} made an illegal choice for '{r.Prompt}'.");

        string summary = picks.Count == 0 ? "none" : string.Join(", ", picks.Select(i => r.Options[i].Label));
        await EmitAsync(new ChoiceMadeEvent(r, picks, summary), ct);
        return picks;
    }

    internal async Task<bool> AskYesNoAsync(int player, CardInstance? source, string prompt, string purpose, CancellationToken ct)
    {
        var picks = await ChooseAsync(ChoiceRequest.YesNo(player, source, prompt, purpose), ct);
        return picks.Count > 0 && picks[0] == 0;
    }

    // ================================================================== moves

    private StackItem? FindStackItem(CardInstance card) => _stack.FirstOrDefault(i => ReferenceEquals(i.Card, card));

    /// <summary>
    /// Nullify (spec §1): a Unit → Nullified pile, an Order → Issued pile. On the stack it is cancelled (not Played).
    /// A Unit in play first gets a chance to be protected (Immune, cannot be Nullified), then a React window opens
    /// (Respond in Kind, Already Dead), then replacements apply (Glorpaglorp, Fallout Shelter). Returns true when
    /// the card was Nullified.
    /// </summary>
    internal async Task<bool> NullifyAsync(CardInstance card, CardInstance? source, CancellationToken ct)
    {
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Stack:
            {
                var item = FindStackItem(card);
                if (item is null || item.Cancelled) return false;
                item.Cancelled = true;
                var to = item.Kind == StackItemKind.IssueOrder ? PileKind.IssuedOrders : PileKind.NullifiedUnits;
                State.MoveToPile(card, to);
                await EmitAsync(new CardNullifiedOnStackEvent(card, item, to, source ?? card), ct);
                if (card.Definition.IsUnit)
                    await RaiseAsync(new TriggerEvent("UnitNullified", loc.Player, card, source, ControllerOf(source), loc.Player), ct);
                return true;
            }

            case LocationKind.Node:
            {
                var node = loc.Node!;
                if (node.Kind == NodeKind.Order)
                {
                    State.MoveToPile(card, PileKind.IssuedOrders);
                    await EmitAsync(new CardNullifiedEvent(card, node, null, PileKind.IssuedOrders, source), ct);
                    return true;
                }

                if (node.IsScoring)
                {
                    if (StaticLayer.PreventsNullify(State, card, source, out string reason))
                    {
                        await EmitAsync(new NullifyPreventedEvent(card, source, reason), ct);
                        return false;
                    }

                    // "As a Unit is Nullified": priority passes, Reacts may answer (spec §7.2).
                    int actor = ControllerOf(source) is >= 0 and var a ? a : GameState.Other(node.Side);
                    var window = new StackItem(actor, StackItemKind.UnitNullifying, card, $"Nullify {card.Name}",
                        (self, token) => ResolveNullifyWindowAsync(self, token))
                    {
                        NullifySource = source,
                        TargetNode = node,
                    };
                    await RunPriorityAsync(window, actor, ct);
                    return card.Location is { Kind: LocationKind.Pile, Pile: PileKind.NullifiedUnits };
                }

                return await CompleteNullifyAsync(card, source, ct); // Standby
            }

            case LocationKind.Pile when loc.Pile == PileKind.RetiredUnits:
                State.MoveToPile(card, PileKind.NullifiedUnits);
                await EmitAsync(new CardNullifiedEvent(card, null, PileKind.RetiredUnits, PileKind.NullifiedUnits, source), ct);
                if (card.Definition.IsUnit)
                    await RaiseAsync(new TriggerEvent("UnitNullified", -1, card, source, ControllerOf(source), -1), ct);
                return true;
        }
        return false;
    }

    private async Task ResolveNullifyWindowAsync(StackItem item, CancellationToken ct)
    {
        var card = item.Card;
        if (item.Cancelled)
        {
            await EmitAsync(new NullifyPreventedEvent(card, item.NullifySource, "the Nullify was prevented"), ct);
            return;
        }
        // It may have left its node (or become protected) while the window was open.
        if (card.Location.Kind != LocationKind.Node || card.Location.Node != item.TargetNode) return;
        if (StaticLayer.PreventsNullify(State, card, item.NullifySource, out string reason))
        {
            await EmitAsync(new NullifyPreventedEvent(card, item.NullifySource, reason), ct);
            return;
        }
        await CompleteNullifyAsync(card, item.NullifySource, ct);
    }

    /// <summary>The Nullify itself, for a Unit on a node (in play or in Standby).</summary>
    private async Task<bool> CompleteNullifyAsync(CardInstance card, CardInstance? source, CancellationToken ct)
    {
        var node = card.Location.Node!;
        bool inPlay = node.IsScoring;
        int unitController = node.Side;

        if (inPlay && await TryNullifyReplacementAsync(card, node, ct)) return false;

        var memory = new Dictionary<string, object>(card.Memory);
        bool suppressed = StaticLayer.IsSuppressed(State, card);
        State.MoveToPile(card, PileKind.NullifiedUnits);
        await EmitAsync(new CardNullifiedEvent(card, node, null, PileKind.NullifiedUnits, source), ct);
        if (inPlay) await LeftPlayAsync(card, node, memory, suppressed, nullified: true, ct);
        await RaiseAsync(new TriggerEvent("UnitNullified", unitController, card, source, ControllerOf(source), unitController), ct);
        return true;
    }

    /// <summary>
    /// "If a Unit would be Nullified, ... may place it in the Standby Zone instead": Glorpaglorp (another Unit you
    /// control) and Fallout Shelter (any Unit, its controller decides). With no Standby room it is Nullified
    /// (errata). Leaders can't go to Standby (ruling #36); under Black Hole the move would Nullify it anyway.
    /// </summary>
    private async Task<bool> TryNullifyReplacementAsync(CardInstance card, Node node, CancellationToken ct)
    {
        if (!Placement.CanEnterStandby(card.Definition) || BlackHole() is not null) return false;
        if (State.Players[node.Side].StandbyNodes.All(n => !n.IsEmpty)) return false;

        foreach (var (src, controller) in ReplacementSources("UnitNullifying"))
        {
            bool isGlorp = src.Location.Kind == LocationKind.Node;
            if (isGlorp && (controller != node.Side || ReferenceEquals(src, card))) continue; // another Unit YOU control
            int decider = isGlorp ? controller : node.Side;
            bool yes = await AskYesNoAsync(decider, src, $"{src.Name}: put {card.Name} into Standby instead of Nullifying it?", "Optional", ct);
            if (!yes) continue;
            await EmitAsync(new LogEvent($"  {src.Name}: {card.Name} goes to Standby instead of being Nullified."), ct);
            if (await MoveToStandbyAsync(card, node.Side, retireIfNoRoom: false, ct)) return true;
        }
        return false;
    }

    /// <summary>Cards in play (Units on Unit/Leader nodes, the Active Warzone) with a live Replacement for this event.</summary>
    private IEnumerable<(CardInstance Card, int Controller)> ReplacementSources(string eventName)
    {
        var list = new List<(CardInstance, int)>();
        foreach (var p in State.Players)
            foreach (var n in p.ScoringNodes)
                if (n.Card is { } c && HasReplacement(c.Definition, eventName) && !StaticLayer.IsSuppressed(State, c))
                    list.Add((c, n.Side));
        foreach (var wz in State.ActiveWarzones)
            if (HasReplacement(wz.Definition, eventName) && !StaticLayer.IsSuppressed(State, wz))
                list.Add((wz, -1));
        return list;
    }

    internal static bool HasReplacement(CardDefinition def, string eventName)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return false;
        foreach (var e in def.Effects.EnumerateArray())
        {
            if (!e.TryGetProperty("trigger", out var t) || t.GetString() != "Replacement") continue;
            if (!e.TryGetProperty("replaces", out var r) || !r.TryGetProperty("event", out var ev)) continue;
            if (ev.ValueKind == JsonValueKind.String && ev.GetString() == eventName) return true;
            if (ev.ValueKind == JsonValueKind.Array && ev.EnumerateArray().Any(x => x.GetString() == eventName)) return true;
        }
        return false;
    }

    /// <summary>Black Hole: "If a Unit would be Retired or put into a Standby Zone, Nullify it instead."</summary>
    private CardInstance? BlackHole() =>
        ReplacementSources("UnitRetiring").Select(x => x.Card).FirstOrDefault(c => HasReplacement(c.Definition, "UnitMovingToStandby"));

    /// <summary>
    /// Applies Black Hole to a Unit about to be Retired / moved to Standby. True when that is replaced (the Unit was
    /// Nullified, or is already in the Nullified pile). If the Nullify is prevented, the original move goes ahead.
    /// </summary>
    private async Task<bool> BlackHoleReplacesAsync(CardInstance card, CancellationToken ct)
    {
        if (!card.Definition.IsUnit || BlackHole() is not { } hole) return false;
        if (card.Location is { Kind: LocationKind.Pile, Pile: PileKind.NullifiedUnits }) return true;
        var before = card.Location;
        await EmitAsync(new LogEvent($"  {hole.Name}: {card.Name} is Nullified instead."), ct);
        await NullifyAsync(card, hole, ct);
        return card.Location != before;
    }

    internal static int ControllerOf(CardInstance? card)
    {
        if (card is null) return -1;
        return card.Location.Kind switch
        {
            LocationKind.Node => card.Location.Node!.Side,
            LocationKind.Stack or LocationKind.Hand => card.Location.Player,
            _ => -1,
        };
    }

    /// <summary>Retire a card from a node (Clean-up, "Retire it if there is no room"), the stack or a pile.</summary>
    internal async Task RetireAsync(CardInstance card, CancellationToken ct)
    {
        bool unitMoving = card.Definition.IsUnit && card.Location.Kind is LocationKind.Node or LocationKind.Stack;
        if (unitMoving && await BlackHoleReplacesAsync(card, ct)) return;
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Node:
            {
                var node = loc.Node!;
                var pile = card.Definition.IsOrder ? PileKind.IssuedOrders : PileKind.RetiredUnits;
                bool inPlay = node.IsScoring;
                var memory = new Dictionary<string, object>(card.Memory);
                bool suppressed = StaticLayer.IsSuppressed(State, card);
                State.MoveToPile(card, pile);
                await EmitAsync(new CardMovedToPileEvent(card, node, pile), ct);
                if (inPlay) await LeftPlayAsync(card, node, memory, suppressed, nullified: false, ct);
                return;
            }

            case LocationKind.Stack:
            {
                var item = FindStackItem(card);
                if (item is not null) item.Cancelled = true;
                State.MoveToPile(card, PileKind.RetiredUnits);
                await EmitAsync(new DeployCancelledEvent(loc.Player, card, PileKind.RetiredUnits), ct);
                return;
            }

            case LocationKind.Pile when loc.Pile != PileKind.RetiredUnits:
                State.MoveToPile(card, PileKind.RetiredUnits);
                await EmitAsync(new CardMovedToPileEvent(card, null, PileKind.RetiredUnits), ct);
                return;
        }
    }

    /// <summary>
    /// "Place it in their Standby zone": the Standby node behind its Unit node if that is free, otherwise the only
    /// free one, otherwise the player chooses. With no room the Unit is Retired when <paramref name="retireIfNoRoom"/>.
    /// Returns true when the Unit moved (or was Retired for lack of room).
    /// </summary>
    internal async Task<bool> MoveToStandbyAsync(CardInstance card, int side, bool retireIfNoRoom, CancellationToken ct)
    {
        if (!card.Definition.IsUnit) return false;
        if (!Placement.CanEnterStandby(card.Definition))
        {
            await EmitAsync(new LogEvent($"  {card.Name} is a Leader: Leaders can never go into a Standby zone."), ct);
            return false;
        }
        if (await BlackHoleReplacesAsync(card, ct)) return true;
        var loc = card.Location;
        var ps = State.Players[side];

        // Behind the Unit node it is on (or, for a deploy being cancelled, the node it was going to).
        var behind = loc.Kind == LocationKind.Node ? loc.Node
            : loc.Kind == LocationKind.Stack ? FindStackItem(card)?.TargetNode
            : null;
        Node? dest = null;
        if (behind is { Kind: NodeKind.Unit } un && un.Side == side && ps.StandbyNodes[un.Index].IsEmpty)
            dest = ps.StandbyNodes[un.Index];
        if (dest is null)
        {
            var free = ps.StandbyNodes.Where(n => n.IsEmpty).ToList();
            if (free.Count == 1) dest = free[0];
            else if (free.Count > 1)
            {
                var options = free.Select(n => new ChoiceOption(n.Label, Node: n)).ToList();
                var pick = await ChooseAsync(new ChoiceRequest(side, card, $"Choose a Standby node for {card.Name}",
                    ChoiceKind.Nodes, options, 1, 1, "StandbyNode"), ct);
                dest = free[pick.Count > 0 ? pick[0] : 0];
            }
        }

        if (dest is null)
        {
            if (!retireIfNoRoom) return false;
            await RetireAsync(card, ct);
            return true;
        }

        switch (loc.Kind)
        {
            case LocationKind.Stack:
            {
                var item = FindStackItem(card);
                if (item is not null) item.Cancelled = true; // "it is not played"
                State.MoveToNode(card, dest);
                await EmitAsync(new CardMovedToStandbyEvent(card, null, null, true, dest), ct);
                return true;
            }

            case LocationKind.Node:
            {
                var from = loc.Node!;
                bool inPlay = from.IsScoring;
                var memory = new Dictionary<string, object>(card.Memory);
                bool suppressed = StaticLayer.IsSuppressed(State, card);
                State.MoveToNode(card, dest);
                if (inPlay) card.ResetInPlayState();
                await EmitAsync(new CardMovedToStandbyEvent(card, from, null, false, dest), ct);
                if (inPlay) await LeftPlayAsync(card, from, memory, suppressed, nullified: false, ct);
                return true;
            }

            case LocationKind.Pile:
            {
                var pile = loc.Pile;
                State.MoveToNode(card, dest);
                await EmitAsync(new CardMovedToStandbyEvent(card, null, pile, false, dest), ct);
                return true;
            }
        }
        return false;
    }

    internal async Task ReturnToHandAsync(CardInstance card, int player, CancellationToken ct)
    {
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Node:
            {
                var from = loc.Node!;
                bool inPlay = from.IsScoring;
                var memory = new Dictionary<string, object>(card.Memory);
                bool suppressed = StaticLayer.IsSuppressed(State, card);
                State.MoveToHand(card, player);
                card.ResetInPlayState();
                await EmitAsync(new CardReturnedToHandEvent(card, player, from, null), ct);
                if (inPlay) await LeftPlayAsync(card, from, memory, suppressed, nullified: false, ct);
                return;
            }
            case LocationKind.Pile:
            {
                var pile = loc.Pile;
                State.MoveToHand(card, player);
                await EmitAsync(new CardReturnedToHandEvent(card, player, null, pile), ct);
                return;
            }
        }
    }

    internal async Task RetireFromHandAsync(CardInstance card, CancellationToken ct)
    {
        if (card.Location.Kind != LocationKind.Hand) return;
        int player = card.Location.Player;
        State.MoveToPile(card, card.Definition.IsOrder ? PileKind.IssuedOrders : PileKind.RetiredUnits);
        await EmitAsync(new CardRetiredFromHandEvent(card, player), ct);
        await RaiseAsync(new TriggerEvent("CardRetiredFromHand", player, card), ct);
    }

    internal async Task SetLockAsync(Node node, bool locked, CardInstance? source, CancellationToken ct)
    {
        if (node.IsLocked == locked) return;
        node.IsLocked = locked;
        await EmitAsync(new NodeLockChangedEvent(node, locked, source), ct);
    }

    /// <summary>Switch the contents of two nodes. The Units stay in play; control follows the side (ruling #16).</summary>
    internal async Task SwapAsync(Node a, Node b, CancellationToken ct)
    {
        var ca = a.Card;
        var cb = b.Card;
        if (ca is not null) State.RemoveFromCurrentLocation(ca);
        if (cb is not null) State.RemoveFromCurrentLocation(cb);
        if (ca is not null) State.MoveToNode(ca, b);
        if (cb is not null) State.MoveToNode(cb, a);
        await EmitAsync(new NodesSwappedEvent(a, b, cb, ca), ct);
    }

    /// <summary>Assemble a card from a pile (Salvage for Parts): it is Retired and its Rank added to <paramref name="onto"/>.</summary>
    internal async Task AssembleFromAsync(CardInstance source, CardInstance onto, int player, CancellationToken ct)
    {
        var fromPile = source.Location.Kind == LocationKind.Pile ? source.Location.Pile : null;
        int amount = AssembleRules.AssembledRank(source);
        bool ok = onto.Location.Kind == LocationKind.Node && onto.Location.Node is { IsScoring: true } n && n.Side == player;
        if (ok) onto.PersistentModifiers.Add(new RankModifier($"Assemble: {source.Name}", amount));
        State.MoveToPile(source, PileKind.RetiredUnits);
        await EmitAsync(new AssembledEvent(player, source, onto, amount, ok, fromPile), ct);
        if (ok) await RaiseAsync(new TriggerEvent("Assembled", player, source, AssembledRank: amount, AssembleTarget: onto), ct);
    }
}
