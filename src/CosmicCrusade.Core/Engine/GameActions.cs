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
public sealed record DecisionContext(
    GameState State,
    int Player,
    IReadOnlyList<GameAction> Legal,
    bool OpponentJustPassed,
    IReadOnlyList<StackItem>? Stack = null,
    bool IsResponse = false)
{
    public IReadOnlyList<StackItem> StackTopFirst => Stack ?? Array.Empty<StackItem>();
}

public static class LegalActions
{
    public static IReadOnlyList<GameAction> ForTurn(GameState state, int player)
    {
        var list = new List<GameAction>();
        var ps = state.Players[player];

        foreach (var card in ps.Hand.Where(c => c.Definition.IsUnit))
            foreach (var node in Placement.LegalDeployNodes(state, player, card))
                list.Add(new DeployFromHandAction(player, card, node));

        for (int i = 0; i < PlayerState.StandbyNodeCount; i++)
        {
            var from = ps.StandbyNodes[i];
            var to = ps.UnitNodes[i];
            if (from.Card is not null && to.IsEmpty && Placement.ClassAllows(from.Card.Definition, NodeKind.Unit))
                list.Add(new AdvanceFromStandbyAction(player, from, to));
        }

        // Orders on your own turn: Actions resolve, Ongoings need an Empty Order Node of your own.
        foreach (var card in ps.Hand.Where(c => c.Definition.IsOrder))
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

        // TODO(M3): PlayCost checks (Mechatron, Scrapped Metal), card-granted actions (Barkeep Bill, Abominations...).
        list.Add(new PassAction(player));
        return list;
    }

    /// <summary>
    /// Reacts in <paramref name="player"/>'s hand whose condition is met by an item on the stack.
    /// One action per (React, item) pair, topmost item first. Does not include the Pass.
    /// </summary>
    public static IReadOnlyList<GameAction> ForResponse(GameState state, int player, IEnumerable<StackItem> stackTopFirst)
    {
        var items = stackTopFirst.ToList();
        var list = new List<GameAction>();
        foreach (var card in state.Players[player].Hand.Where(c => c.Definition.OrderType == OrderType.React))
            foreach (var item in items)
                if (ReactConditions.IsMet(state, card, item))
                    list.Add(new ReactAction(player, card, item));
        return list;
    }
}
