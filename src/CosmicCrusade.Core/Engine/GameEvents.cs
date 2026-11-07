using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// Something that happened. The UI awaits each event (to play animations) before the engine continues,
/// so events arrive strictly in order and the state is already updated when an event is published.
/// </summary>
public abstract record GameEvent;

public sealed record GameStartedEvent(int RoundsToWin) : GameEvent;
public sealed record RoundStartedEvent(int Round, int FirstPlayer) : GameEvent;
/// <param name="Source">The card whose effect made the player draw; null for Start-up draws.</param>
public sealed record CardDrawnEvent(int Player, CardInstance Card, PileKind FromDeck, CardInstance? Source = null) : GameEvent;
public sealed record DeckReshuffledEvent(PileKind Deck, int CardCount) : GameEvent;
public sealed record TurnStartedEvent(int Player) : GameEvent;
public sealed record PassedEvent(int Player) : GameEvent;

/// <summary>A unit has been announced (it is on the stack). <paramref name="FromNode"/> is set for Standby advances.</summary>
public sealed record UnitDeployAnnouncedEvent(int Player, CardInstance Card, Node Target, Node? FromNode, PileKind? FromPile = null) : GameEvent;

public sealed record UnitPlayedEvent(int Player, CardInstance Card, Node Node) : GameEvent;
public sealed record DeployCancelledEvent(int Player, CardInstance Card, PileKind To) : GameEvent;
// ------------------------------------------------------------------ the stack (M2)

/// <summary>An item was put on the priority stack. <paramref name="StackTopFirst"/> is the whole stack after the push.</summary>
public sealed record StackPushedEvent(StackItem Item, IReadOnlyList<StackItem> StackTopFirst) : GameEvent;

/// <summary>An item has left the stack (it resolved, or it was cancelled and has now been removed).</summary>
public sealed record StackPoppedEvent(StackItem Item, IReadOnlyList<StackItem> StackTopFirst) : GameEvent;

/// <summary>
/// A player was given priority with at least one React available (only then is anyone asked, spec §7.2 auto-allow).
/// </summary>
public sealed record PriorityEvent(int Player, IReadOnlyList<StackItem> StackTopFirst) : GameEvent;

/// <summary>An Order left the hand and is on the stack (Action/Ongoing on a turn, or a React).</summary>
public sealed record OrderIssueAnnouncedEvent(int Player, CardInstance Card, Node? OrderNode, StackItem Item) : GameEvent;

/// <summary>
/// An Order resolved. Ongoing: it now sits on <paramref name="OrderNode"/>. Action/React: it went to the Issued pile.
/// <paramref name="EffectApplied"/> is false while the card's effect is still waiting for the effect interpreter (M3).
/// </summary>
public sealed record OrderResolvedEvent(int Player, CardInstance Card, Node? OrderNode, bool EffectApplied, string Note) : GameEvent;

/// <summary>
/// A counter-React (Shadow Strike, Target Practice, Second Thought) Nullified a card while it was on the stack.
/// The card is now in <paramref name="To"/> (Units → Nullified pile, Orders → Issued pile) and is not Played.
/// </summary>
public sealed record CardNullifiedOnStackEvent(CardInstance Card, StackItem Item, PileKind To, CardInstance By) : GameEvent;

/// <summary>A Unit from hand is on the stack, about to be Assembled onto <paramref name="Target"/>.</summary>
public sealed record AssembleAnnouncedEvent(int Player, CardInstance Card, CardInstance Target, StackItem Item) : GameEvent;

/// <summary>
/// Assemble resolved (or Salvage for Parts assembled from <paramref name="FromPile"/>): <paramref name="Card"/> was Retired and <paramref name="Amount"/> Rank was added to
/// <paramref name="Target"/>. <paramref name="Applied"/> is false when the target had left play (the card is still Retired).
/// </summary>
public sealed record AssembledEvent(int Player, CardInstance Card, CardInstance Target, int Amount, bool Applied, PileKind? FromPile = null) : GameEvent;

// ------------------------------------------------------------------ abilities (M3)

/// <summary>A card's ability starts resolving (When you play, When this leaves play, an Order's effect...).</summary>
public sealed record AbilityTriggeredEvent(CardInstance Source, int Controller, string Trigger) : GameEvent;

/// <summary>A player answered a <see cref="ChoiceRequest"/>. <paramref name="Summary"/> is a readable answer for the log.</summary>
public sealed record ChoiceMadeEvent(ChoiceRequest Request, IReadOnlyList<int> Picks, string Summary) : GameEvent;

/// <summary>
/// A card was Nullified outside the stack: a Unit in play or in Standby (→ Nullified pile), a Retired Unit
/// (→ Nullified pile), or an Ongoing Order on its node (→ Issued pile).
/// </summary>
public sealed record CardNullifiedEvent(CardInstance Card, Node? FromNode, PileKind? FromPile, PileKind To, CardInstance? Source) : GameEvent;

/// <summary>A node was Locked or Unlocked by an ability.</summary>
public sealed record NodeLockChangedEvent(Node Node, bool Locked, CardInstance? Source) : GameEvent;

/// <summary>A Unit's Rank changed (ModifyRank, SetBaseRank, Manipulate). <paramref name="Description"/> e.g. "+2", "Base Rank 0".</summary>
public sealed record RankChangedEvent(CardInstance Card, string Description, CardInstance? Source,
    RankChangeKind Kind = RankChangeKind.Modify) : GameEvent;

/// <summary>What kind of Rank change a <see cref="RankChangedEvent"/> is (the UI picks its effect from it).</summary>
public enum RankChangeKind { Modify, BaseRank, Manipulate, Assembled }

/// <summary>A Unit in play has new races ("That Unit becomes a Necrolite"; Voidslime-style "has no Race").</summary>
public sealed record RacesChangedEvent(CardInstance Card, IReadOnlyList<Race> Races, CardInstance? Source) : GameEvent;

/// <summary>A Unit went to a Standby node: from a node (leaving play), a pile, or the stack (its deploy cancelled).</summary>
public sealed record CardMovedToStandbyEvent(CardInstance Card, Node? FromNode, PileKind? FromPile, bool FromStack, Node To) : GameEvent;

/// <summary>A card went back to a player's hand from a node or a pile.</summary>
public sealed record CardReturnedToHandEvent(CardInstance Card, int Player, Node? FromNode, PileKind? FromPile) : GameEvent;

/// <summary>A card went from a hand straight to the Retired pile (10K-LAN, New Recruits...).</summary>
public sealed record CardRetiredFromHandEvent(CardInstance Card, int Player) : GameEvent;

/// <summary>A card was Nullified straight from a hand (Abominations' cost).</summary>
public sealed record CardNullifiedFromHandEvent(CardInstance Card, int Player, PileKind To, CardInstance? Source) : GameEvent;

/// <summary>Two Units Duelled. <paramref name="Losers"/>: the Units Nullified by it (empty, one, or both on a matched Rank).</summary>
public sealed record DuelEvent(CardInstance A, CardInstance B, IReadOnlyList<CardInstance> Losers, bool Protected) : GameEvent
{
    /// <summary>The single loser (null when nothing happened, or both lost on a matched Rank).</summary>
    public CardInstance? Loser => Losers.Count == 1 ? Losers[0] : null;
}

/// <summary>
/// The contents of two nodes were switched (Arganium; Ace, Council Tactician also between a player's Unit, Leader and
/// Standby nodes). Control follows the side.
/// </summary>
public sealed record NodesSwappedEvent(Node A, Node B, CardInstance? CardNowOnA, CardInstance? CardNowOnB) : GameEvent;

/// <summary>
/// Warzones put into play at once by an effect (Dimensional Rift: the top 2; Wormhole: the chosen one).
/// They are already in the Active Warzone slot when this is published.
/// </summary>
public sealed record WarzonesAddedEvent(IReadOnlyList<CardInstance> Warzones, CardInstance? Source) : GameEvent;

/// <summary>A Unit in play would have been Nullified but wasn't (Immune, Diplomatic Immunity, Force Field...).</summary>
public sealed record NullifyPreventedEvent(CardInstance Card, CardInstance? Source, string Reason) : GameEvent;

/// <summary>A card's abilities were Suppressed (Gloop!).</summary>
public sealed record AbilitiesSuppressedEvent(CardInstance Card, CardInstance? Source) : GameEvent;

// ------------------------------------------------------------------ M3.3

/// <summary>
/// A Unit entered play as a copy of another card (Mr Morph, Forced Mitosis). <see cref="CardInstance.Definition"/>
/// is already the copied card when this is published.
/// </summary>
public sealed record CardCopiedEvent(CardInstance Card, CardDefinition CopiedFrom, CardInstance? Source) : GameEvent;

/// <summary>Recolonize: a Unit being Deployed now goes to <paramref name="To"/> instead of <paramref name="From"/>.</summary>
public sealed record DeployRedirectedEvent(StackItem Item, Node From, Node To, CardInstance? Source) : GameEvent;

/// <summary>Commandeer: a player took control of an Order on the stack (and maybe pointed a React at a new target).</summary>
public sealed record StackItemControlChangedEvent(StackItem Item, int OldController, int NewController, CardInstance? Source) : GameEvent;

/// <summary>
/// One of Us: a Unit in play moved across the control line to a new node and changed controller. It stays in play
/// (it is not Played again).
/// </summary>
public sealed record UnitControlGainedEvent(CardInstance Card, Node From, Node To, int NewController, CardInstance? Source) : GameEvent;

/// <summary>Shoot First, Think Second: the engine picked <paramref name="Chosen"/> at random from <paramref name="Candidates"/>.</summary>
public sealed record RandomDeployNodeChosenEvent(int Player, CardInstance Card, IReadOnlyList<Node> Candidates, Node Chosen,
    CardInstance? Source) : GameEvent;

// ------------------------------------------------------------------ Spacecraft module

/// <summary>
/// A Unit came aboard a spacecraft without being Deployed there (Beam Up, Chrome Steed, Abductor, Mindscrambler, a
/// crewed spacecraft advancing from Standby...). <paramref name="FromNode"/> / <paramref name="FromPile"/> say where
/// the crew came from (both null when it was already on this node, e.g. a spacecraft laid under it by Mothership).
/// </summary>
public sealed record CrewBoardedEvent(CardInstance Crew, CardInstance Ship, Node Node, Node? FromNode, PileKind? FromPile,
    CardInstance? Source) : GameEvent;

/// <summary>A token entered play on a node (the Mothership's Fighters, laid under the Unit there).</summary>
public sealed record TokenCreatedEvent(CardInstance Token, Node Node, CardInstance? Source) : GameEvent;

public sealed record RoundScoredEvent(RoundResult Result) : GameEvent;
public sealed record CardMovedToPileEvent(CardInstance Card, Node? From, PileKind Pile) : GameEvent;
/// <summary>Warzones: the top Warzone was revealed and is now Active (Start-up step 3).</summary>
public sealed record WarzoneRevealedEvent(CardInstance Warzone) : GameEvent;

/// <summary>Warzones: an Active Warzone went to the bottom of the Warzone deck (Clean-up step 5).</summary>
public sealed record WarzoneReturnedEvent(CardInstance Warzone) : GameEvent;

/// <summary>Archons: a player took their Archon draw and picked <paramref name="Archon"/> (revealed), giving up one card from <paramref name="Skipped"/>.</summary>
public sealed record ArchonTakenEvent(int Player, CardInstance Archon, PileKind Skipped, int Offered) : GameEvent;

/// <summary>Archons: an unplayed Archon is lost at Clean-up and shuffled back into the Archon deck.</summary>
public sealed record ArchonLostEvent(int Player, CardInstance Archon) : GameEvent;

public sealed record RoundEndedEvent(int Round, int RoundsWon0, int RoundsWon1) : GameEvent;
public sealed record GameEndedEvent(GameOutcome Outcome) : GameEvent;
public sealed record LogEvent(string Message) : GameEvent;

public sealed record GameOutcome(int? Winner, int RoundsWon0, int RoundsWon1, int RoundsPlayed, string Reason);

/// <summary>Receives events. Return a task that completes when the event has been presented (animation done).</summary>
public interface IGameEventSink
{
    Task PublishAsync(GameEvent gameEvent, CancellationToken ct);
}

public sealed class NullEventSink : IGameEventSink
{
    public static readonly NullEventSink Instance = new();
    public Task PublishAsync(GameEvent gameEvent, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Collects events in memory (tests, replays).</summary>
public sealed class RecordingEventSink : IGameEventSink
{
    public List<GameEvent> Events { get; } = new();

    public Task PublishAsync(GameEvent gameEvent, CancellationToken ct)
    {
        Events.Add(gameEvent);
        return Task.CompletedTask;
    }
}
