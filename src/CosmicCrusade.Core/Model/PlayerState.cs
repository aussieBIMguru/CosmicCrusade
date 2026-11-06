namespace CosmicCrusade.Core.Model;

public sealed class PlayerState
{
    public const int UnitNodeCount = 3;
    public const int StandbyNodeCount = 3;
    public const int OrderNodeCount = 2;

    public PlayerState(int index, string name, bool hasLeaderNode)
    {
        Index = index;
        Name = name;
        UnitNodes = Enumerable.Range(0, UnitNodeCount).Select(i => new Node(index, NodeKind.Unit, i)).ToArray();
        StandbyNodes = Enumerable.Range(0, StandbyNodeCount).Select(i => new Node(index, NodeKind.Standby, i)).ToArray();
        OrderNodes = Enumerable.Range(0, OrderNodeCount).Select(i => new Node(index, NodeKind.Order, i)).ToArray();
        LeaderNode = hasLeaderNode ? new Node(index, NodeKind.Leader, 0) : null;
    }

    public int Index { get; }
    public string Name { get; set; }

    public List<CardInstance> Hand { get; } = new();

    public Node[] UnitNodes { get; }
    /// <summary>Null in No Leaders mode.</summary>
    public Node? LeaderNode { get; }
    public Node[] StandbyNodes { get; }
    public Node[] OrderNodes { get; }

    public int RoundsWon { get; set; }

    /// <summary>Deploy turns taken this round (Military Academy). Reset at Start-up.</summary>
    public int DeployTurnsThisRound { get; set; }

    /// <summary>Unit nodes then the Leader node (if any): the nodes that score.</summary>
    public IEnumerable<Node> ScoringNodes
    {
        get
        {
            foreach (var n in UnitNodes) yield return n;
            if (LeaderNode is not null) yield return LeaderNode;
        }
    }

    public IEnumerable<Node> AllNodes
    {
        get
        {
            foreach (var n in ScoringNodes) yield return n;
            foreach (var n in StandbyNodes) yield return n;
            foreach (var n in OrderNodes) yield return n;
        }
    }

    public IEnumerable<CardInstance> UnitsInPlay =>
        ScoringNodes.Where(n => n.Card is not null).Select(n => n.Card!);

    public int HandUnitCount => Hand.Count(c => c.Definition.IsUnit);
    public int HandOrderCount => Hand.Count(c => c.Definition.IsOrder);
}
