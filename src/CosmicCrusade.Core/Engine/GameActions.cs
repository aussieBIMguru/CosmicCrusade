using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>A decision a player can make. The engine only ever offers legal actions to controllers.</summary>
public abstract record GameAction(int Player)
{
    public abstract string Describe();
}

/// <summary>Pass the turn (Main Phase) or decline to respond ("OK, no response" in a priority window).</summary>
public sealed record PassAction(int Player) : GameAction(Player)
{
    public override string Describe() => "Pass";
}

/// <summary>Deploy a Unit from hand to one of the player's nodes.</summary>
public sealed record DeployFromHandAction(int Player, CardInstance Card, Node Target) : GameAction(Player)
{
    public override string Describe() => $"Deploy {Card.Name} to {Target.Label}";
}

/// <summary>Advance a Unit from Standby Si into the Empty Unit Node Ui (a Deploy, ruling #8).</summary>
public sealed record AdvanceFromStandbyAction(int Player, Node From, Node To) : GameAction(Player)
{
    public override string Describe() => $"Advance {From.Card?.Name} from {From.Label} to {To.Label}";
}

/// <summary>
/// Issue an Action or Ongoing Order from hand on your turn. <paramref name="OrderNode"/> is the Empty Order Node an
/// Ongoing will occupy (null for Actions).
/// </summary>
public sealed record IssueOrderAction(int Player, CardInstance Card, Node? OrderNode) : GameAction(Player)
{
    public override string Describe() =>
        OrderNode is null ? $"Issue {Card.Name}" : $"Issue {Card.Name} to {OrderNode.Label}";
}

/// <summary>Issue a React from hand in a priority window, in response to <paramref name="RespondingTo"/>.</summary>
public sealed record ReactAction(int Player, CardInstance Card, StackItem RespondingTo) : GameAction(Player)
{
    public override string Describe() => $"React with {Card.Name} to {RespondingTo.Description}";
}

/// <summary>
/// Deploy a Unit from a pile "as if it were in your hand" (Barkeep Bill, Galactomancer, Hills of Decay). The engine
/// then asks which Unit and which node.
/// </summary>
public sealed record DeployFromPileAction(int Player, PileKind Pile) : GameAction(Player)
{
    public override string Describe() => $"Deploy a Unit from the {(Pile == PileKind.RetiredUnits ? "Retired" : "Nullified")} pile";
}

/// <summary>
/// Shoot First, Think Second: Deploy this Unit from hand; the engine picks the node at random from the legal ones.
/// </summary>
public sealed record DeployAtRandomAction(int Player, CardInstance Card) : GameAction(Player)
{
    public override string Describe() => $"Deploy {Card.Name} (node chosen at random)";
}

/// <summary>
/// Psychic Coercion: "I'm deploying a Unit." The opponent then picks the node, and only then does this player pick
/// which Unit from hand goes there (spec §7.1).
/// </summary>
public sealed record CoercedDeployAction(int Player) : GameAction(Player)
{
    public override string Describe() => "Deploy a Unit (your opponent chooses the node first)";
}

/// <summary>
/// A turn action granted by a card in play (Abominations: Nullify a Necrolite card from hand → a non-Necrolite Unit
/// gets +2 Rank and becomes a Necrolite). The engine asks for the cost card, then runs the action.
/// </summary>
public sealed record GrantedAction(int Player, CardInstance Source, string Name) : GameAction(Player)
{
    public override string Describe() => $"{Name} ({Source.Name})";
}

/// <summary>Assemble a Unit from hand onto an allied Unit in play (ruling #18: uses your turn).</summary>
public sealed record AssembleAction(int Player, CardInstance Card, CardInstance Target) : GameAction(Player)
{
    public override string Describe() => $"Assemble {Card.Name} onto {Target.Name}";
}

/// <summary>What a controller sees when asked to decide.</summary>
/// <param name="State">The live state. Controllers must only read what their seat may see (own hand, public zones).</param>
/// <param name="OpponentJustPassed">True when the opponent's last Main Phase turn was a Pass (passing again ends the phase).</param>
/// <param name="Stack">The priority stack, top first (empty on a Main Phase turn).</param>
/// <param name="IsResponse">True for a priority window: Legal holds Reacts plus a Pass ("OK, no response").</param>
/// <param name="ExtraAction">Open Source: an additional action in the same turn. Pass here only ends the turn.</param>
public sealed record DecisionContext(
    GameState State,
    int Player,
    IReadOnlyList<GameAction> Legal,
    bool OpponentJustPassed,
    IReadOnlyList<StackItem>? Stack = null,
    bool IsResponse = false,
    bool ExtraAction = false)
{
    public IReadOnlyList<StackItem> StackTopFirst => Stack ?? Array.Empty<StackItem>();
}

public static class LegalActions
{
    public static IReadOnlyList<GameAction> ForTurn(GameState state, int player)
    {
        var list = new List<GameAction>();
        var ps = state.Players[player];

        var (handDeploys, advances) = Deploys(state, player);

        // Shoot First / Psychic Coercion: the player doesn't pick the node for a Deploy from hand.
        switch (StaticLayer.DeployNodeOverride(state, player))
        {
            case null:
                list.AddRange(handDeploys);
                break;
            case { Random: true }:
                foreach (var card in handDeploys.Select(d => d.Card).Distinct())
                    list.Add(new DeployAtRandomAction(player, card));
                break;
            default:
                if (handDeploys.Count > 0) list.Add(new CoercedDeployAction(player));
                break;
        }
        list.AddRange(advances);

        // Orders on your own turn: Actions resolve, Ongoings need an Empty Order Node of your own.
        // Deploys from piles (Barkeep Bill, Galactomancer, Hills of Decay): one action per pile with a deployable Unit.
        foreach (var pile in new[] { PileKind.RetiredUnits, PileKind.NullifiedUnits })
            if (StaticLayer.PlayableFromPile(state, player, pile).Any(c => DeployNodes(state, player, c).Count > 0))
                list.Add(new DeployFromPileAction(player, pile));

        // Temporal Stasis / Extreme Sanctions: some Orders can't be Issued.
        foreach (var card in ps.PlayableCards.Where(c => c.Definition.IsOrder && CanIssue(state, player, c)))
        {
            switch (card.Definition.OrderType)
            {
                case OrderType.Action:
                    list.Add(new IssueOrderAction(player, card, null));
                    break;
                case OrderType.Ongoing:
                    foreach (var node in ps.OrderNodes.Where(n => n.IsEmpty))
                        list.Add(new IssueOrderAction(player, card, node));
                    break;
                // Reacts are only issued in priority windows (ForResponse).
            }
        }

        foreach (var card in ps.Hand.Where(c => c.Definition.IsUnit))
            foreach (var target in AssembleRules.Targets(state, player, card))
                list.Add(new AssembleAction(player, card, target));

        // Card-granted actions (Abominations): the cost must be payable and there must be something to affect.
        foreach (var g in StaticLayer.GrantedActions(state, player))
            if (GrantedCostCandidates(state, player, g).Count > 0 && GrantedHasTarget(state, player, g)
                && !list.OfType<GrantedAction>().Any(x => x.Source == g.Source))
                list.Add(new GrantedAction(player, g.Source, g.Name));

        list.Add(new PassAction(player));
        return list;
    }

    /// <summary>
    /// Deploys from hand (one per legal card/node) and Standby advances, after Mind Games: "Your Opponents must play
    /// Units on Opposed Nodes if able" (otherwise they may use their own). Shoot First / Psychic Coercion then decide
    /// the node among these.
    /// </summary>
    public static (List<DeployFromHandAction> Hand, List<AdvanceFromStandbyAction> Advances) Deploys(GameState state, int player)
    {
        var ps = state.Players[player];
        var hand = new List<DeployFromHandAction>();
        foreach (var card in ps.PlayableCards.Where(c => c.Definition.IsUnit))
            foreach (var node in Placement.LegalDeployNodes(state, player, card))
                hand.Add(new DeployFromHandAction(player, card, node));

        var advances = new List<AdvanceFromStandbyAction>();
        for (int i = 0; i < PlayerState.StandbyNodeCount; i++)
        {
            var from = ps.StandbyNodes[i];
            var to = ps.UnitNodes[i];
            if (from.Card is null || to.IsLocked) continue;
            // Spacecraft: a lone Unit may advance onto its own uncrewed spacecraft (or a spacecraft under its Unit);
            // a crewed spacecraft needs an Empty node (its crew comes along).
            bool fits = to.IsEmpty
                ? Placement.ClassAllows(from.Card.Definition, NodeKind.Unit)
                : from.Crew is null && Spacecraft.CanJoin(state, from.Card, to);
            if (fits && StaticLayer.DeployAllowed(state, player, from.Card, to))
                advances.Add(new AdvanceFromStandbyAction(player, from, to));
        }

        if (StaticLayer.MustDeployToOpposedNodes(state, player))
        {
            bool Forced(Node n) => n.Side == player && state.OpposingNode(n)?.Card is not null;
            if (hand.Any(a => Forced(a.Target)) || advances.Any(a => Forced(a.To)))
            {
                hand.RemoveAll(a => !Forced(a.Target));
                advances.RemoveAll(a => !Forced(a.To));
            }
        }
        return (hand, advances);
    }

    /// <summary>Hand cards that can pay a granted action's cost (Abominations: a Necrolite card).</summary>
    public static List<CardInstance> GrantedCostCandidates(GameState state, int player, StaticLayer.GrantedActionInfo g) =>
        state.Players[player].Hand
            .Where(c => g.CostKind is null || c.Definition.Kind == g.CostKind)
            .Where(c => g.CostRace is null
                        || StaticLayer.HasRace(state, c, StaticLayer.TextRace(state, CardDatabase.ParseEnum<Race>(g.CostRace), g.Source)))
            .ToList();

    /// <summary>
    /// Is there anything for the granted action's first choice to pick? (Abominations: a non-Necrolite Unit in play
    /// that isn't Immune.) Only the filter keys the granted actions use are read; anything else counts as "yes".
    /// </summary>
    public static bool GrantedHasTarget(GameState state, int player, StaticLayer.GrantedActionInfo g)
    {
        if (!g.Op.TryGetProperty("do", out var ops) || ops.GetArrayLength() == 0) return true;
        if (!ops[0].TryGetProperty("targets", out var t) || t.ValueKind != System.Text.Json.JsonValueKind.Object
            || !t.TryGetProperty("filter", out var f) || f.ValueKind != System.Text.Json.JsonValueKind.Object)
            return true;
        Race Word(string r) => StaticLayer.TextRace(state, CardDatabase.ParseEnum<Race>(r), g.Source);
        return state.Players.SelectMany(p => p.UnitsInPlay).Any(u =>
        {
            if (StaticLayer.IsImmune(state, u)) return false;
            foreach (var p in f.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "race" when !StaticLayer.HasRace(state, u, Word(p.Value.GetString()!)): return false;
                    case "notRace" when StaticLayer.HasRace(state, u, Word(p.Value.GetString()!)): return false;
                    case "controller" when (p.Value.GetString() == "You") != (u.Location.Node!.Side == player): return false;
                }
            }
            return true;
        });
    }

    /// <summary>Restrictions (Temporal Stasis, Extreme Sanctions), extra costs (Scrapped Metal), play conditions.</summary>
    private static bool CanIssue(GameState state, int player, CardInstance card) =>
        !StaticLayer.IsPlayRestricted(state, card, "Issue", out _)
        && PlayRules.CanPay(state, player, card)
        && PlayRules.ConditionMet(state, player, card.Definition);

    /// <summary>
    /// Nodes <paramref name="player"/> may Deploy <paramref name="card"/> to, Mind Games included (used for Units
    /// deployed from piles, whose node is chosen after the action).
    /// </summary>
    public static List<Node> DeployNodes(GameState state, int player, CardInstance card)
    {
        var nodes = Placement.LegalDeployNodes(state, player, card).ToList();
        if (StaticLayer.MustDeployToOpposedNodes(state, player))
        {
            var forced = nodes.Where(n => n.Side == player && state.OpposingNode(n)?.Card is not null).ToList();
            if (forced.Count > 0) return forced;
        }
        return nodes;
    }

    /// <summary>
    /// Reacts in <paramref name="player"/>'s hand whose condition is met by an item on the stack.
    /// One action per (React, item) pair, topmost item first. Does not include the Pass.
    /// </summary>
    public static IReadOnlyList<GameAction> ForResponse(GameState state, int player, IEnumerable<StackItem> stackTopFirst)
    {
        var items = stackTopFirst.ToList();
        var list = new List<GameAction>();
        foreach (var card in state.Players[player].Hand.Where(c => c.Definition.OrderType == OrderType.React && CanIssue(state, player, c)))
            foreach (var item in items)
                if (ReactConditions.IsMet(state, card, item))
                    list.Add(new ReactAction(player, card, item));
        return list;
    }
}
