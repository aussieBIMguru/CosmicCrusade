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

    /// <summary>
    /// The card on the node: a ground Unit, a spacecraft (Spacecraft module), or an Order. When a spacecraft is here,
    /// the Unit aboard it is <see cref="Crew"/>.
    /// </summary>
    public CardInstance? Card { get; internal set; }

    /// <summary>
    /// Spacecraft module: the Unit crewing the spacecraft in <see cref="Card"/> (null when there is none). It is in
    /// play on this node too (same location), elevated above the ground (spacecraft rulings S1–S12).
    /// </summary>
    public CardInstance? Crew { get; internal set; }

    public bool IsLocked { get; set; }

    /// <summary>Is <paramref name="card"/> on this node (as its card or as the crew)?</summary>
    public bool Holds(CardInstance card) => ReferenceEquals(Card, card) || ReferenceEquals(Crew, card);

    /// <summary>The card, then the crew (if any).</summary>
    public IEnumerable<CardInstance> Occupants
    {
        get
        {
            if (Card is not null) yield return Card;
            if (Crew is not null) yield return Crew;
        }
    }

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
