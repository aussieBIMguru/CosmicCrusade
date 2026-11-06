using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Ace, Council Tactician: "you may rearrange all Nodes (the new Nodes must be legal)". Errata and notes:
///   • it covers each player's Unit, Leader and Standby nodes (Standby ↔ Unit Node moves included);
///   • Units never cross the control line, so a rearrangement is a series of switches between two nodes on the
///     same side;
///   • Leaders can't move (and can never go into Standby, ruling #36);
///   • a Locked node keeps what it has (nothing can be played to it), and an Immune Unit can't be moved;
///   • every card must end up on a node its class allows (a Standby node takes any non-Leader Unit).
/// Order Nodes are not part of it: Ongoings have no position.
/// </summary>
public static class Rearrange
{
    /// <summary>The nodes of one side that take part (Unit nodes, Leader node, Standby nodes).</summary>
    public static IEnumerable<Node> NodesOf(GameState state, int side)
    {
        var ps = state.Players[side];
        foreach (var n in ps.UnitNodes) yield return n;
        if (ps.LeaderNode is not null) yield return ps.LeaderNode;
        foreach (var n in ps.StandbyNodes) yield return n;
    }

    /// <summary>Can neither this node nor its card move? (Locked, a Leader on it, an Immune Unit.)</summary>
    public static bool IsFixed(GameState state, Node node)
    {
        if (node.IsLocked) return true;
        if (node.Card is not { } c) return false;
        return c.Definition.IsLeader || c.PrintedDefinition.IsLeader || StaticLayer.IsImmune(state, c);
    }

    /// <summary>May <paramref name="card"/> (null = nothing) end up on <paramref name="to"/>?</summary>
    public static bool Fits(CardInstance? card, Node to) => card is null || to.Kind switch
    {
        NodeKind.Standby => Placement.CanEnterStandby(card),
        NodeKind.Unit or NodeKind.Leader => Placement.ClassAllows(card.Definition, to.Kind),
        _ => false,
    };

    /// <summary>Is switching the contents of <paramref name="a"/> and <paramref name="b"/> a legal step?</summary>
    public static bool CanSwap(GameState state, Node a, Node b) =>
        !ReferenceEquals(a, b) && a.Side == b.Side
        && a.Kind != NodeKind.Order && b.Kind != NodeKind.Order
        && (a.Card is not null || b.Card is not null)
        && !IsFixed(state, a) && !IsFixed(state, b)
        && Fits(a.Card, b) && Fits(b.Card, a);

    /// <summary>The nodes <paramref name="a"/> can switch with.</summary>
    public static List<Node> Partners(GameState state, Node a) =>
        NodesOf(state, a.Side).Where(b => CanSwap(state, a, b)).ToList();

    /// <summary>Every node (both sides, board order) that has at least one legal partner.</summary>
    public static List<Node> Movable(GameState state) =>
        state.Players.SelectMany(p => NodesOf(state, p.Index)).Where(n => Partners(state, n).Count > 0).ToList();

    /// <summary>
    /// What-if for the AI: switch the two nodes' cards without events or triggers, evaluate, switch back.
    /// (Rank changes a Unit would lose on going into Standby are not modelled; Standby doesn't score anyway.)
    /// </summary>
    internal static T WithSwap<T>(GameState state, Node a, Node b, Func<T> eval)
    {
        var ca = a.Card;
        var cb = b.Card;
        if (ca is not null) state.RemoveFromCurrentLocation(ca);
        if (cb is not null) state.RemoveFromCurrentLocation(cb);
        if (ca is not null) state.MoveToNode(ca, b);
        if (cb is not null) state.MoveToNode(cb, a);
        try
        {
            return eval();
        }
        finally
        {
            if (ca is not null) state.RemoveFromCurrentLocation(ca);
            if (cb is not null) state.RemoveFromCurrentLocation(cb);
            if (ca is not null) state.MoveToNode(ca, a);
            if (cb is not null) state.MoveToNode(cb, b);
        }
    }
}
