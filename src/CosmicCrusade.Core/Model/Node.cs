namespace CosmicCrusade.Core.Model;

/// <summary>A board slot. Side = owning player index (0 or 1). Index is 0-based within its row.</summary>
public sealed class Node
{
    public Node(int side, NodeKind kind, int index)
    {
        Side = side;
        Kind = kind;
        Index = index;
    }

    public int Side { get; }
    public NodeKind Kind { get; }
    public int Index { get; }

    public CardInstance? Card { get; internal set; }
    public bool IsLocked { get; set; }

    /// <summary>Spec §1: a node with no card that is not Locked.</summary>
    public bool IsEmpty => Card is null && !IsLocked;

    /// <summary>True for Unit and Leader nodes: cards here are "in play" and score.</summary>
    public bool IsScoring => Kind is NodeKind.Unit or NodeKind.Leader;

    /// <summary>Short label, e.g. U1, L, S2, O1.</summary>
    public string Label => Kind switch
    {
        NodeKind.Unit => $"U{Index + 1}",
        NodeKind.Leader => "L",
        NodeKind.Standby => $"S{Index + 1}",
        NodeKind.Order => $"O{Index + 1}",
        _ => "?",
    };

    public string Id => $"P{Side}.{Label}";

    public override string ToString() => Id;
}
