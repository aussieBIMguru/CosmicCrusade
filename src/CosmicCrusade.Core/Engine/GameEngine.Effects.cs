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
    /// </summary>
    internal async Task NullifyAsync(CardInstance card, CardInstance? source, CancellationToken ct)
    {
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Stack:
            {
                var item = FindStackItem(card);
                if (item is null || item.Cancelled) return;
                item.Cancelled = true;
                var to = item.Kind == StackItemKind.IssueOrder ? PileKind.IssuedOrders : PileKind.NullifiedUnits;
                State.MoveToPile(card, to);
                await EmitAsync(new CardNullifiedOnStackEvent(card, item, to, source ?? card), ct);
                return;
            }

            case LocationKind.Node:
            {
                var node = loc.Node!;
                if (node.Kind == NodeKind.Order)
                {
                    State.MoveToPile(card, PileKind.IssuedOrders);
                    await EmitAsync(new CardNullifiedEvent(card, node, null, PileKind.IssuedOrders, source), ct);
                    return;
                }

                bool inPlay = node.IsScoring;
                var memory = new Dictionary<string, object>(card.Memory);
                bool suppressed = card.AbilitiesSuppressed;
                State.MoveToPile(card, PileKind.NullifiedUnits);
                await EmitAsync(new CardNullifiedEvent(card, node, null, PileKind.NullifiedUnits, source), ct);
                if (inPlay) await LeftPlayAsync(card, node, memory, suppressed, nullified: true, ct);
                return;
            }

            case LocationKind.Pile when loc.Pile == PileKind.RetiredUnits:
                State.MoveToPile(card, PileKind.NullifiedUnits);
                await EmitAsync(new CardNullifiedEvent(card, null, PileKind.RetiredUnits, PileKind.NullifiedUnits, source), ct);
                return;
        }
    }

    /// <summary>Retire a card from a node (Clean-up, "Retire it if there is no room"), the stack or a pile.</summary>
    internal async Task RetireAsync(CardInstance card, CancellationToken ct)
    {
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Node:
            {
                var node = loc.Node!;
                var pile = card.Definition.IsOrder ? PileKind.IssuedOrders : PileKind.RetiredUnits;
                bool inPlay = node.IsScoring;
                var memory = new Dictionary<string, object>(card.Memory);
                bool suppressed = card.AbilitiesSuppressed;
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
                bool suppressed = card.AbilitiesSuppressed;
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
                bool suppressed = card.AbilitiesSuppressed;
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
        bool ok = onto.Location.Kind == LocationKind.Node && onto.Location.Node is { IsScoring: true };
        if (ok) onto.PersistentModifiers.Add(new RankModifier($"Assemble: {source.Name}", amount));
        State.MoveToPile(source, PileKind.RetiredUnits);
        await EmitAsync(new AssembledEvent(player, source, onto, amount, ok, fromPile), ct);
    }
}
