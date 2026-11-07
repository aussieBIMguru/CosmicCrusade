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

    /// <summary>Copy constructor for <see cref="Clone"/>: same database and options, fresh players and RNG.</summary>
    private GameState(GameState source, int seed)
    {
        Database = source.Database;
        Options = source.Options;
        Rng = new Random(seed);
        Players = new[]
        {
            new PlayerState(0, source.Players[0].Name, source.Players[0].LeaderNode is not null),
            new PlayerState(1, source.Players[1].Name, source.Players[1].LeaderNode is not null),
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

    /// <summary>Archons module: the shared Archon deck (last = top).</summary>
    public List<CardInstance> ArchonDeck { get; } = new();

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
        PileKind.ArchonDeck => ArchonDeck,
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
                if (loc.Node is { } node)
                {
                    if (ReferenceEquals(node.Crew, card))
                    {
                        node.Crew = null; // the spacecraft stays, uncrewed (ruling S12)
                    }
                    else if (ReferenceEquals(node.Card, card))
                    {
                        // A spacecraft leaving drops its crew back to the ground on the same node (brief).
                        node.Card = node.Crew;
                        node.Crew = null;
                    }
                }
                break;
            case LocationKind.Pile:
                GetPile(loc.Pile!.Value).Remove(card);
                break;
            case LocationKind.ArchonSlot:
                if (ReferenceEquals(Players[loc.Player].ArchonSlot, card)) Players[loc.Player].ArchonSlot = null;
                break;
        }
        card.Location = CardLocation.None;
    }

    public void MoveToHand(CardInstance card, int player)
    {
        if (VanishIfToken(card)) return;
        // Archons never join a hand (e.g. "return it to its owner's hand"): back to the Archon deck.
        if (card.PrintedDefinition.IsArchon)
        {
            ReturnArchonToDeck(card);
            return;
        }
        RemoveFromCurrentLocation(card);
        Players[player].Hand.Add(card);
        card.Location = CardLocation.InHand(player);
    }

    /// <summary>
    /// Puts the card on the node. On an occupied node a Unit and a spacecraft join up (Spacecraft module): a Unit
    /// boards the uncrewed spacecraft there, or a spacecraft is laid under the Unit there, which becomes its crew.
    /// Legality (class, crewable, Viscous Fog...) is checked by the callers (Rules.Spacecraft).
    /// </summary>
    public void MoveToNode(CardInstance card, Node node)
    {
        if (node.Card is { } occupant)
        {
            if (node.Crew is not null || ReferenceEquals(occupant, card))
                throw new InvalidOperationException($"{node} is occupied by {occupant}.");
            bool cardIsShip = card.Definition.IsSpacecraft, occupantIsShip = occupant.Definition.IsSpacecraft;
            if (cardIsShip == occupantIsShip || !card.Definition.IsUnit || !occupant.Definition.IsUnit)
                throw new InvalidOperationException($"{node} is occupied by {occupant}.");
            RemoveFromCurrentLocation(card);
            if (cardIsShip)
            {
                node.Card = card;      // the spacecraft is laid under the Unit
                node.Crew = occupant;  // which stays on this node, now aboard
            }
            else
            {
                node.Crew = card;      // the Unit boards the spacecraft
            }
            card.Location = CardLocation.OnNode(node);
            return;
        }
        RemoveFromCurrentLocation(card);
        node.Card = card;
        card.Location = CardLocation.OnNode(node);
    }

    /// <summary>Switches everything on two nodes (spacecraft travel with their crews). No events, no triggers.</summary>
    public void SwapContents(Node a, Node b)
    {
        if (ReferenceEquals(a, b)) return;
        (a.Card, b.Card) = (b.Card, a.Card);
        (a.Crew, b.Crew) = (b.Crew, a.Crew);
        foreach (var c in a.Occupants) c.Location = CardLocation.OnNode(a);
        foreach (var c in b.Occupants) c.Location = CardLocation.OnNode(b);
    }

    /// <summary>
    /// Tokens (the Mothership's Fighters) never go to a hand or pile: leaving play, they simply vanish (ruling S13).
    /// Returns true when the card was a token and is gone.
    /// </summary>
    private bool VanishIfToken(CardInstance card)
    {
        if (!card.PrintedDefinition.IsToken) return false;
        RemoveFromCurrentLocation(card);
        card.ResetInPlayState();
        return true;
    }

    /// <summary>Puts the card on top of a pile. Leaving play resets in-play state (rank modifiers, race changes).</summary>
    public void MoveToPile(CardInstance card, PileKind pile)
    {
        if (VanishIfToken(card)) return;
        // Archons rule P2: an Archon leaving play (Nullified, Retired, Issued...) goes back into the Archon deck.
        if (card.PrintedDefinition.IsArchon && pile != PileKind.ArchonDeck)
        {
            ReturnArchonToDeck(card);
            return;
        }
        bool wasInPlay = card.Location.Kind == LocationKind.Node;
        RemoveFromCurrentLocation(card);
        if (wasInPlay) card.ResetInPlayState();
        GetPile(pile).Add(card);
        card.Location = CardLocation.InPile(pile);
    }

    /// <summary>Archons: shuffles the card into the Archon deck at a random position (spec: Archons P2, A8).</summary>
    public void ReturnArchonToDeck(CardInstance card)
    {
        bool wasInPlay = card.Location.Kind == LocationKind.Node;
        RemoveFromCurrentLocation(card);
        if (wasInPlay) card.ResetInPlayState();
        ArchonDeck.Insert(Rng.Next(ArchonDeck.Count + 1), card);
        card.Location = CardLocation.InPile(PileKind.ArchonDeck);
    }

    /// <summary>Archons: the picked Archon goes to the player's (revealed) Archon slot.</summary>
    public void MoveToArchonSlot(CardInstance card, int player)
    {
        if (Players[player].ArchonSlot is { } old && !ReferenceEquals(old, card)) ReturnArchonToDeck(old);
        RemoveFromCurrentLocation(card);
        Players[player].ArchonSlot = card;
        card.Location = CardLocation.InArchonSlot(player);
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
        if (VanishIfToken(card)) return;
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
        bool joins = node.Card is { } occ && node.Crew is null && !ReferenceEquals(occ, card)
                     && occ.Definition.IsUnit && card.Definition.IsUnit && occ.Definition.IsSpacecraft != card.Definition.IsSpacecraft;
        if ((node.Card is not null && !joins)
            || loc.Kind is not (LocationKind.Hand or LocationKind.Node or LocationKind.Pile or LocationKind.ArchonSlot))
            return eval();
        // A card already on a node can only be tried elsewhere when it's alone there (no crew to drop).
        if (loc.Kind == LocationKind.Node && loc.Node is { Crew: not null })
            return eval();

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
            else if (loc.Kind == LocationKind.ArchonSlot)
            {
                Players[loc.Player].ArchonSlot = card;
                card.Location = loc;
            }
            else
            {
                MoveToNode(card, loc.Node!);
            }
        }
    }

    // ---------------------------------------------------------------- cloning (M4: AI search)

    /// <summary>
    /// A deep copy for AI simulations. Every card instance, node, hand and pile is copied (instance ids are kept, so
    /// actions can be matched between the copies); card definitions and options are shared (they never change during
    /// a game). The copy gets its own RNG seeded with <paramref name="seed"/>.
    /// Only cards in hands, piles and on nodes are copied: call it when the stack is empty (a Main Phase turn).
    /// </summary>
    public GameState Clone(int seed)
    {
        var c = new GameState(this, seed)
        {
            _nextInstanceId = _nextInstanceId,
            Round = Round,
            Phase = Phase,
            FirstPlayer = FirstPlayer,
            ActivePlayer = ActivePlayer,
            LastRoundWinner = LastRoundWinner,
            EndAfterThisRound = EndAfterThisRound,
        };
        c.RandomDrawUnits.AddRange(RandomDrawUnits);
        c.RandomDrawOrders.AddRange(RandomDrawOrders);

        var map = new Dictionary<CardInstance, CardInstance>(ReferenceEqualityComparer.Instance);
        CardInstance Map(CardInstance x)
        {
            if (!map.TryGetValue(x, out var y)) map[x] = y = x.CloneCore();
            return y;
        }

        foreach (PileKind kind in Enum.GetValues<PileKind>())
            foreach (var card in GetPile(kind))
            {
                var copy = Map(card);
                c.GetPile(kind).Add(copy);
                copy.Location = CardLocation.InPile(kind);
            }

        for (int p = 0; p < Players.Length; p++)
        {
            var src = Players[p];
            var dst = c.Players[p];
            dst.RoundsWon = src.RoundsWon;
            dst.DeployTurnsThisRound = src.DeployTurnsThisRound;
            dst.ArchonTaken = src.ArchonTaken;
            if (src.ArchonSlot is { } archon)
            {
                var copy = Map(archon);
                dst.ArchonSlot = copy;
                copy.Location = CardLocation.InArchonSlot(p);
            }
            foreach (var card in src.Hand)
            {
                var copy = Map(card);
                dst.Hand.Add(copy);
                copy.Location = CardLocation.InHand(p);
            }
        }

        foreach (var node in AllNodes)
        {
            var n = c.CorrespondingNode(node);
            n.IsLocked = node.IsLocked;
            if (node.Card is { } card)
            {
                var copy = Map(card);
                n.Card = copy;
                copy.Location = CardLocation.OnNode(n);
            }
            if (node.Crew is { } crew)
            {
                var copy = Map(crew);
                n.Crew = copy;
                copy.Location = CardLocation.OnNode(n);
            }
        }

        // Remembered values (Roundup Gang's node, Extreme Sanctions' race, once-per-round latches...).
        object MapValue(object v) => v switch
        {
            Node n => c.CorrespondingNode(n),
            CardInstance ci => Map(ci),
            List<object> list => list.Select(MapValue).ToList(),
            _ => v, // ints, strings, Races: immutable
        };
        foreach (var (orig, copy) in map.ToList())
            foreach (var (key, value) in orig.Memory)
                copy.Memory[key] = MapValue(value);

        return c;
    }

    /// <summary>This state's node with the same side, kind and index as <paramref name="node"/> (from any state).</summary>
    public Node CorrespondingNode(Node node)
    {
        var ps = Players[node.Side];
        return node.Kind switch
        {
            NodeKind.Unit => ps.UnitNodes[node.Index],
            NodeKind.Leader => ps.LeaderNode ?? throw new InvalidOperationException("No Leader Node in this game."),
            NodeKind.Standby => ps.StandbyNodes[node.Index],
            NodeKind.Order => ps.OrderNodes[node.Index],
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };
    }

    /// <summary>Every card instance in a hand, pile or on a node.</summary>
    public IEnumerable<CardInstance> AllCards =>
        Enum.GetValues<PileKind>().SelectMany(k => GetPile(k))
            .Concat(Players.SelectMany(p => p.Hand))
            .Concat(Players.Where(p => p.ArchonSlot is not null).Select(p => p.ArchonSlot!))
            .Concat(AllNodes.SelectMany(n => n.Occupants));

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
