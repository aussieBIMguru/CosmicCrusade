namespace CosmicCrusade.Core.Model;

/// <summary>
/// The complete game state. Everything here is deterministic given <see cref="GameOptions.Seed"/> and the decisions made.
/// Decks and piles are lists whose LAST element is the top card.
/// </summary>
public sealed class GameState
{
    private int _nextInstanceId = 1;

    public GameState(CardDatabase database, GameOptions options, string player0Name = "Player", string player1Name = "AI")
    {
        Database = database;
        Options = options;
        Rng = new Random(options.Seed);
        Players = new[]
        {
            new PlayerState(0, player0Name, options.Leaders),
            new PlayerState(1, player1Name, options.Leaders),
        };
    }

    public CardDatabase Database { get; }
    public GameOptions Options { get; }
    public Random Rng { get; }

    public PlayerState[] Players { get; }

    public List<CardInstance> UnitDeck { get; } = new();
    public List<CardInstance> OrderDeck { get; } = new();
    public List<CardInstance> RetiredUnits { get; } = new();
    public List<CardInstance> NullifiedUnits { get; } = new();
    public List<CardInstance> IssuedOrders { get; } = new();

    /// <summary>Warzones expansion: the Warzone deck (last = top) and the Active Warzone(s).</summary>
    public List<CardInstance> WarzoneDeck { get; } = new();
    public List<CardInstance> ActiveWarzones { get; } = new();

    /// <summary>RandomDraw mode: enabled definitions per deck type (uniform per distinct card).</summary>
    public List<CardDefinition> RandomDrawUnits { get; } = new();
    public List<CardDefinition> RandomDrawOrders { get; } = new();

    public int Round { get; set; }
    public GamePhase Phase { get; set; } = GamePhase.NotStarted;
    public int FirstPlayer { get; set; }
    public int ActivePlayer { get; set; }
    public int? LastRoundWinner { get; set; }

    /// <summary>Set when a deck runs out under <see cref="EmptyDeckRule.EndAfterRound"/>.</summary>
    public bool EndAfterThisRound { get; set; }

    public static int Other(int player) => 1 - player;

    public PlayerState Player(int index) => Players[index];

    public IEnumerable<Node> AllNodes => Players.SelectMany(p => p.AllNodes);

    public IEnumerable<CardInstance> AllUnitsInPlay => Players.SelectMany(p => p.UnitsInPlay);

    public Node? FindNode(string id) => AllNodes.FirstOrDefault(n => n.Id == id);

    /// <summary>The node directly across the control line (Ui ↔ Ui, L ↔ L). Null for Standby/Order nodes.</summary>
    public Node? OpposingNode(Node node)
    {
        var other = Players[Other(node.Side)];
        return node.Kind switch
        {
            NodeKind.Unit => other.UnitNodes[node.Index],
            NodeKind.Leader => other.LeaderNode,
            _ => null,
        };
    }

    public CardInstance CreateInstance(CardDefinition def) => new(_nextInstanceId++, def);

    public List<CardInstance> GetPile(PileKind pile) => pile switch
    {
        PileKind.UnitDeck => UnitDeck,
        PileKind.OrderDeck => OrderDeck,
        PileKind.RetiredUnits => RetiredUnits,
        PileKind.NullifiedUnits => NullifiedUnits,
        PileKind.IssuedOrders => IssuedOrders,
        PileKind.WarzoneDeck => WarzoneDeck,
        PileKind.ActiveWarzone => ActiveWarzones,
        _ => throw new ArgumentOutOfRangeException(nameof(pile)),
    };

    // ---------------------------------------------------------------- movement primitives

    public void RemoveFromCurrentLocation(CardInstance card)
    {
        var loc = card.Location;
        switch (loc.Kind)
        {
            case LocationKind.Hand:
                Players[loc.Player].Hand.Remove(card);
                break;
            case LocationKind.Node:
                if (loc.Node is not null && ReferenceEquals(loc.Node.Card, card)) loc.Node.Card = null;
                break;
            case LocationKind.Pile:
                GetPile(loc.Pile!.Value).Remove(card);
                break;
        }
        card.Location = CardLocation.None;
    }

    public void MoveToHand(CardInstance card, int player)
    {
        RemoveFromCurrentLocation(card);
        Players[player].Hand.Add(card);
        card.Location = CardLocation.InHand(player);
    }

    public void MoveToNode(CardInstance card, Node node)
    {
        if (node.Card is not null) throw new InvalidOperationException($"{node} is occupied by {node.Card}.");
        RemoveFromCurrentLocation(card);
        node.Card = card;
        card.Location = CardLocation.OnNode(node);
    }

    /// <summary>Puts the card on top of a pile. Leaving play resets in-play state (rank modifiers, race changes).</summary>
    public void MoveToPile(CardInstance card, PileKind pile)
    {
        bool wasInPlay = card.Location.Kind == LocationKind.Node;
        RemoveFromCurrentLocation(card);
        if (wasInPlay) card.ResetInPlayState();
        GetPile(pile).Add(card);
        card.Location = CardLocation.InPile(pile);
    }

    /// <summary>Card leaves hand/standby and waits on the stack while priority passes.</summary>
    public void MoveToStack(CardInstance card, int controller)
    {
        RemoveFromCurrentLocation(card);
        card.Location = CardLocation.OnStack(controller);
    }

    /// <summary>Puts a card at the BOTTOM of a pile (index 0).</summary>
    public void MoveToPileBottom(CardInstance card, PileKind pile)
    {
        RemoveFromCurrentLocation(card);
        GetPile(pile).Insert(0, card);
        card.Location = CardLocation.InPile(pile);
    }

    /// <summary>
    /// What-if helper for the AI: puts <paramref name="card"/> on the empty <paramref name="node"/>, evaluates
    /// <paramref name="eval"/> (e.g. totals with static effects), then puts the card back exactly where it was
    /// (same hand / pile position or node). No events, no triggers.
    /// </summary>
    internal T WithTemporaryPlacement<T>(CardInstance card, Node node, Func<T> eval)
    {
        var loc = card.Location;
        int index = loc.Kind switch
        {
            LocationKind.Hand => Players[loc.Player].Hand.IndexOf(card),
            LocationKind.Pile => GetPile(loc.Pile!.Value).IndexOf(card),
            _ => -1,
        };
        if (node.Card is not null || loc.Kind is not (LocationKind.Hand or LocationKind.Node or LocationKind.Pile)) return eval();

        MoveToNode(card, node);
        try
        {
            return eval();
        }
        finally
        {
            RemoveFromCurrentLocation(card);
            if (loc.Kind == LocationKind.Hand)
            {
                Players[loc.Player].Hand.Insert(index, card);
                card.Location = loc;
            }
            else if (loc.Kind == LocationKind.Pile)
            {
                GetPile(loc.Pile!.Value).Insert(index, card);
                card.Location = loc;
            }
            else
            {
                MoveToNode(card, loc.Node!);
            }
        }
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
