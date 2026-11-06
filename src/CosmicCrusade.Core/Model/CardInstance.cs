namespace CosmicCrusade.Core.Model;

/// <summary>A persistent rank change on a unit (Manipulate, Assemble, ...). Cleared when the unit leaves play.</summary>
public sealed record RankModifier(string Source, int Amount);

/// <summary>One physical copy of a card during a game. Races and rank changes are mutable per spec §2 / §7.4.</summary>
public sealed class CardInstance
{
    public CardInstance(int instanceId, CardDefinition definition)
    {
        InstanceId = instanceId;
        PrintedDefinition = definition;
        Definition = definition;
        Races = new HashSet<Race>(definition.Races);
    }

    public int InstanceId { get; }

    /// <summary>
    /// What the card is right now. Normally the printed card; while a Unit is in play as a copy of another Unit
    /// (Mr Morph, Forced Mitosis) it is the copied card's definition, so its Rank, class and abilities are the copy's.
    /// Leaving play turns it back into <see cref="PrintedDefinition"/>.
    /// </summary>
    public CardDefinition Definition { get; private set; }

    /// <summary>The card as printed (never changes).</summary>
    public CardDefinition PrintedDefinition { get; }

    /// <summary>True while the Unit is in play as a copy of another Unit.</summary>
    public bool IsCopy => !ReferenceEquals(Definition, PrintedDefinition);

    /// <summary>
    /// "Enters play as a copy of another Unit, except it is a Blaarg": takes the copied card's printed values (Rank,
    /// class, abilities) and the given races. Rank changes, memory and suppression from before are cleared.
    /// </summary>
    public void BecomeCopy(CardDefinition copied, IEnumerable<Race> races)
    {
        Definition = copied;
        Races.Clear();
        foreach (var r in races) Races.Add(r);
        BaseRankOverride = null;
        PersistentModifiers.Clear();
        Memory.Clear();
    }

    /// <summary>Current races (mutable: Stagnation, The Voidslime, "becomes a Necrolite"...).</summary>
    public HashSet<Race> Races { get; }

    /// <summary>"Rank becomes N" style overrides (layer 2 of §7.4).</summary>
    public int? BaseRankOverride { get; set; }

    /// <summary>Persistent modifiers (layer 4 of §7.4: Manipulate counters, Assemble additions).</summary>
    public List<RankModifier> PersistentModifiers { get; } = new();

    /// <summary>Values an ability remembers while the card stays in play (e.g. Roundup Gang's "lockedNode").</summary>
    public Dictionary<string, object> Memory { get; } = new();

    /// <summary>All abilities ignored while in play (Gloop!: "Suppress all Abilities for that Unit as it is played").</summary>
    public bool AbilitiesSuppressed { get; set; }

    /// <summary>Where the card is right now. Maintained by <see cref="GameState"/> move helpers.</summary>
    public CardLocation Location { get; internal set; } = CardLocation.None;

    /// <summary>The display name. A copy reads "Mr Morph (as Fast Eddie)".</summary>
    public string Name => IsCopy ? $"{PrintedDefinition.Name} (as {Definition.Name})" : Definition.Name;

    /// <summary>Resets everything that only lasts while a unit stays in play (a copy turns back into itself).</summary>
    public void ResetInPlayState()
    {
        Definition = PrintedDefinition;
        Races.Clear();
        foreach (var r in Definition.Races) Races.Add(r);
        BaseRankOverride = null;
        PersistentModifiers.Clear();
        Memory.Clear();
        AbilitiesSuppressed = false;
    }

    public override string ToString() => $"{Definition.Name}#{InstanceId}";
}

/// <summary>Where a card instance currently is.</summary>
public readonly record struct CardLocation(LocationKind Kind, int Player, Node? Node, PileKind? Pile)
{
    public static readonly CardLocation None = new(LocationKind.None, -1, null, null);
    public static CardLocation InHand(int player) => new(LocationKind.Hand, player, null, null);
    public static CardLocation OnNode(Node node) => new(LocationKind.Node, node.Side, node, null);
    public static CardLocation InPile(PileKind pile) => new(LocationKind.Pile, -1, null, pile);
    public static CardLocation OnStack(int player) => new(LocationKind.Stack, player, null, null);
}

public enum LocationKind
{
    None,
    Hand,
    Node,
    Pile,
    Stack,
}
