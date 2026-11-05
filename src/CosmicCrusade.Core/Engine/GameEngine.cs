using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Engine;

/// <summary>
/// Runs a game: Start-up → Main → End of Main → Clean-up, round after round (spec §5).
/// Single-threaded and deterministic: all randomness comes from <see cref="GameState.Rng"/>.
/// When awaited on the WPF dispatcher, every continuation stays on the UI thread.
/// </summary>
public sealed partial class GameEngine
{
    private readonly IPlayerController[] _controllers;
    private readonly IGameEventSink _sink;
    private readonly Stack<StackItem> _stack = new();
    private bool _lastActionWasPass;
    private int _actionsThisRound;

    public GameEngine(GameState state, IPlayerController player0, IPlayerController player1, IGameEventSink? sink = null)
    {
        State = state;
        _controllers = new[] { player0, player1 };
        _sink = sink ?? NullEventSink.Instance;
    }

    public GameState State { get; }

    /// <summary>Safety valve against runaway loops while effects are being added.</summary>
    public int MaxTurnsPerRound { get; set; } = 500;
    public int MaxRounds { get; set; } = 200;

    public IReadOnlyCollection<StackItem> PendingStack => _stack;

    /// <summary>Creates a state with decks built and shuffled.</summary>
    public static GameState CreateGame(CardDatabase db, GameOptions options, string player0Name = "Player", string player1Name = "AI")
    {
        var state = new GameState(db, options, player0Name, player1Name);
        DeckBuilder.Build(state);
        return state;
    }

    public async Task<GameOutcome> RunAsync(CancellationToken ct = default)
    {
        await EmitAsync(new GameStartedEvent(State.Options.RoundsToWin), ct);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await StartUpAsync(ct);
            await MainPhaseAsync(ct);
            await EndOfMainAsync(ct);
            await CleanUpAsync(ct);

            var outcome = CheckGameEnd();
            if (outcome is not null)
            {
                State.Phase = GamePhase.GameOver;
                await EmitAsync(new GameEndedEvent(outcome), ct);
                return outcome;
            }
        }
    }

    // ================================================================== Start-up (§5.1)

    private async Task StartUpAsync(CancellationToken ct)
    {
        State.Round++;
        State.Phase = GamePhase.StartUp;
        _actionsThisRound = 0;

        if (State.Round == 1) State.FirstPlayer = State.Rng.Next(2);
        else if (State.LastRoundWinner is int w) State.FirstPlayer = w;
        // No winner last round: the previous first player goes first again (ruling #31).
        State.ActivePlayer = State.FirstPlayer;

        await EmitAsync(new RoundStartedEvent(State.Round, State.FirstPlayer), ct);

        // 1. Mandatory draw up to 5 Units and 3 Orders (ruling #10), in turn order.
        foreach (int p in TurnOrder())
            await DrawUpAsync(p, ct);

        // 2. TODO(M3): carried-over effects, StartUp triggers.

        // 3. Warzones: reveal the top Warzone; it is Active immediately.
        if (State.Options.Warzones && State.WarzoneDeck.Count > 0)
        {
            var wz = State.WarzoneDeck[^1];
            State.MoveToPile(wz, PileKind.ActiveWarzone);
            await EmitAsync(new WarzoneRevealedEvent(wz), ct);
            // TODO(M3): apply the Warzone's effects (only placement overrides are live so far).
        }
    }

    private IEnumerable<int> TurnOrder()
    {
        yield return State.FirstPlayer;
        yield return GameState.Other(State.FirstPlayer);
    }

    private async Task DrawUpAsync(int player, CancellationToken ct)
    {
        var ps = State.Players[player];
        while (ps.HandUnitCount < State.Options.HandUnitTarget)
            if (await DrawAsync(player, PileKind.UnitDeck, ct) is null) break;
        while (ps.HandOrderCount < State.Options.HandOrderTarget)
            if (await DrawAsync(player, PileKind.OrderDeck, ct) is null) break;
    }

    /// <summary>Draws one card from a deck, applying the empty-deck rule. Null when nothing can be drawn.</summary>
    public async Task<CardInstance?> DrawAsync(int player, PileKind deckKind, CancellationToken ct)
    {
        CardInstance card;
        if (State.Options.DeckMode == DeckMode.RandomDraw)
        {
            var pool = deckKind == PileKind.UnitDeck ? State.RandomDrawUnits : State.RandomDrawOrders;
            if (pool.Count == 0) return null;
            card = State.CreateInstance(pool[State.Rng.Next(pool.Count)]);
            State.MoveToHand(card, player);
        }
        else
        {
            var deck = State.GetPile(deckKind);
            if (deck.Count == 0)
            {
                switch (State.Options.EmptyDeck)
                {
                    case EmptyDeckRule.Reshuffle:
                        var source = deckKind == PileKind.UnitDeck ? PileKind.RetiredUnits : PileKind.IssuedOrders;
                        var pile = State.GetPile(source);
                        if (pile.Count == 0) return null;
                        foreach (var c in pile.ToList()) State.MoveToPile(c, deckKind);
                        State.Shuffle(deck);
                        await EmitAsync(new DeckReshuffledEvent(deckKind, deck.Count), ct);
                        break;
                    case EmptyDeckRule.EndAfterRound:
                        State.EndAfterThisRound = true;
                        return null;
                    default:
                        return null;
                }
            }
            card = deck[^1];
            State.MoveToHand(card, player);
        }

        await EmitAsync(new CardDrawnEvent(player, card, deckKind), ct);
        return card;
    }

    // ================================================================== Main Phase (§5.2)

    private async Task MainPhaseAsync(CancellationToken ct)
    {
        State.Phase = GamePhase.Main;
        int current = State.FirstPlayer;
        int passesInRow = 0;
        int turns = 0;
        _lastActionWasPass = false;

        while (passesInRow < 2 && turns++ < MaxTurnsPerRound)
        {
            ct.ThrowIfCancellationRequested();
            State.ActivePlayer = current;
            var legal = LegalActions.ForTurn(State, current);

            GameAction action;
            if (legal.Count == 1)
            {
                action = legal[0]; // Only Pass is possible: pass automatically.
            }
            else
            {
                await EmitAsync(new TurnStartedEvent(current), ct);
                var context = new DecisionContext(State, current, legal, _lastActionWasPass);
                action = await _controllers[current].ChooseActionAsync(context, ct);
                if (!legal.Contains(action))
                    throw new InvalidOperationException($"Controller for player {current} returned an illegal action: {action.Describe()}");
            }

            if (action is PassAction)
            {
                passesInRow++;
                _lastActionWasPass = true;
                await EmitAsync(new PassedEvent(current), ct);
            }
            else
            {
                passesInRow = 0;
                _lastActionWasPass = false;
                _actionsThisRound++;
                await ExecuteAsync(action, ct);
            }

            current = GameState.Other(current);
        }
    }

    private async Task ExecuteAsync(GameAction action, CancellationToken ct)
    {
        switch (action)
        {
            case DeployFromHandAction d:
                await DeployAsync(d.Player, d.Card, d.Target, fromNode: null, ct);
                break;

            case AdvanceFromStandbyAction a:
                await DeployAsync(a.Player, a.From.Card!, a.To, fromNode: a.From, ct);
                break;

            case IssueOrderAction o:
            {
                var item = await AnnounceOrderAsync(o.Player, o.Card, o.OrderNode, respondingTo: null, ct);
                await RunPriorityAsync(item, o.Player, ct);
                break;
            }

            case AssembleAction a:
                await AssembleAsync(a, ct);
                break;

            default:
                throw new NotSupportedException(action.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ Deploy (§7.1)

    /// <summary>Deploy pipeline (§7.1): announce → priority/stack → Played (or cancelled).</summary>
    private async Task DeployAsync(int player, CardInstance card, Node target, Node? fromNode, CancellationToken ct)
    {
        State.MoveToStack(card, player);
        await EmitAsync(new UnitDeployAnnouncedEvent(player, card, target, fromNode), ct);

        var item = new StackItem(player, StackItemKind.DeployUnit, card, $"Deploy {card.Name} to {target.Label}",
            (self, token) => ResolveDeployAsync(self, token))
        {
            TargetNode = target,
            FromNode = fromNode,
        };
        await RunPriorityAsync(item, player, ct);
    }

    private async Task ResolveDeployAsync(StackItem item, CancellationToken ct)
    {
        var card = item.Card;
        var target = item.TargetNode!;
        if (item.Cancelled) return; // The counter-effect already moved the card; it is not Played.

        if (!target.IsEmpty)
        {
            // The node was filled or Locked while the deploy waited (possible once effects exist). Not Played.
            State.MoveToPile(card, PileKind.RetiredUnits);
            await EmitAsync(new DeployCancelledEvent(item.Controller, card, PileKind.RetiredUnits), ct);
            return;
        }

        State.MoveToNode(card, target);
        await EmitAsync(new UnitPlayedEvent(item.Controller, card, target), ct);

        // "When you play this Unit": the controller is the side it landed on (ruling #15).
        if (card.AbilitiesSuppressed)
            await EmitAsync(new LogEvent($"  {card.Name}'s abilities are Suppressed."), ct);
        else
            await FireTriggersAsync(card, "OnPlay", target.Side, target, card.Memory, null, ct);
        // TODO: OnCardPlayed triggers (Military Academy...), state-based checks (Senate Chambers).
    }

    // ------------------------------------------------------------------ Orders (§3, §7.1)

    /// <summary>Card leaves hand, goes on the stack. The caller pushes it (turn action) or it is a React response.</summary>
    private async Task<StackItem> AnnounceOrderAsync(int player, CardInstance card, Node? orderNode, StackItem? respondingTo, CancellationToken ct)
    {
        State.MoveToStack(card, player);
        string desc = respondingTo is not null
            ? $"{card.Name} → {respondingTo.Card.Name}"
            : orderNode is not null ? $"Issue {card.Name} to {orderNode.Label}" : $"Issue {card.Name}";
        var item = new StackItem(player, StackItemKind.IssueOrder, card, desc, (self, token) => ResolveOrderAsync(self, token))
        {
            OrderNode = orderNode,
            RespondingTo = respondingTo,
        };
        await EmitAsync(new OrderIssueAnnouncedEvent(player, card, orderNode, item), ct);
        return item;
    }

    private async Task ResolveOrderAsync(StackItem item, CancellationToken ct)
    {
        if (item.Cancelled) return; // Nullified on the stack: already in the Issued pile, not played.

        var card = item.Card;
        int player = item.Controller;

        switch (card.Definition.OrderType)
        {
            case OrderType.Ongoing:
            {
                if (item.OrderNode is { IsEmpty: true } node)
                {
                    State.MoveToNode(card, node);
                    bool live = AssembleRules.GrantsAssemble(card.Definition) || Placement.GlobalUnitOverrides(card.Definition).Count > 0;
                    await EmitAsync(new OrderResolvedEvent(player, card, node, live,
                        live ? "in effect" : "on its Order Node (its static effect isn't implemented yet)"), ct);
                }
                else
                {
                    State.MoveToPile(card, PileKind.IssuedOrders);
                    await EmitAsync(new OrderResolvedEvent(player, card, null, false, "fizzled: its Order Node is no longer Empty"), ct);
                }
                break;
            }

            case OrderType.React:
            {
                bool live = EffectSupport.IsLive(card.Definition, "React");
                if (live) await FireTriggersAsync(card, "React", player, null, new(), item.RespondingTo, ct);
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new OrderResolvedEvent(player, card, null, live,
                    live ? "resolved" : "its effect isn't implemented yet"), ct);
                break;
            }

            default: // Action
            {
                bool live = EffectSupport.IsLive(card.Definition, "OnResolve");
                if (live) await FireTriggersAsync(card, "OnResolve", player, null, new(), null, ct);
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new OrderResolvedEvent(player, card, null, live,
                    live ? "resolved" : "its effect isn't implemented yet"), ct);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ Assemble (ruling #18)

    private async Task AssembleAsync(AssembleAction a, CancellationToken ct)
    {
        State.MoveToStack(a.Card, a.Player);
        var item = new StackItem(a.Player, StackItemKind.Assemble, a.Card, $"Assemble {a.Card.Name} onto {a.Target.Name}",
            (self, token) => ResolveAssembleAsync(self, token))
        {
            AssembleTarget = a.Target,
        };
        await EmitAsync(new AssembleAnnouncedEvent(a.Player, a.Card, a.Target, item), ct);
        await RunPriorityAsync(item, a.Player, ct);
    }

    private async Task ResolveAssembleAsync(StackItem item, CancellationToken ct)
    {
        if (item.Cancelled) return;
        var card = item.Card;
        var target = item.AssembleTarget!;
        int amount = AssembleRules.AssembledRank(card);

        // The target must still be an allied Unit in play.
        var node = target.Location.Node;
        bool ok = target.Location.Kind == LocationKind.Node && node is not null && node.IsScoring && node.Side == item.Controller;
        if (ok) target.PersistentModifiers.Add(new RankModifier($"Assemble: {card.Name}", amount));

        State.MoveToPile(card, PileKind.RetiredUnits);
        await EmitAsync(new AssembledEvent(item.Controller, card, target, amount, ok), ct);
        // TODO(M3): "Assembled" OnEvent triggers (Cntrl-C Cntrl-V).
    }

    // ------------------------------------------------------------------ Priority (§7.2)

    private IReadOnlyList<StackItem> StackSnapshot() => _stack.ToList(); // Stack<T> enumerates top first.

    private async Task PushAsync(StackItem item, CancellationToken ct)
    {
        _stack.Push(item);
        await EmitAsync(new StackPushedEvent(item, StackSnapshot()), ct);
    }

    /// <summary>
    /// MTG Arena-style priority (§7.2): after an action the other player gets priority. Responses stack LIFO;
    /// the top resolves once both players pass in a row, then priority passes again. A player is only asked when
    /// they hold a React whose condition is met (auto-allow); the human seat can also auto-pass everything.
    /// A cancelled item on top leaves the stack at once (there is nothing left to respond to).
    /// </summary>
    private async Task RunPriorityAsync(StackItem first, int actor, CancellationToken ct)
    {
        await PushAsync(first, ct);
        int priority = GameState.Other(actor);
        int passesInRow = 0;
        int guard = 0;

        while (_stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (++guard > 1000) throw new InvalidOperationException("Priority loop did not terminate.");

            var top = _stack.Peek();
            if (top.Cancelled)
            {
                await PopAndResolveAsync(ct);
                passesInRow = 0;
                priority = GameState.Other(top.Controller);
                continue;
            }

            var snapshot = StackSnapshot();
            var reacts = LegalActions.ForResponse(State, priority, snapshot);
            GameAction? response = null;
            if (reacts.Count > 0)
            {
                var legal = reacts.Append(new PassAction(priority)).ToList();
                await EmitAsync(new PriorityEvent(priority, snapshot), ct);
                response = await _controllers[priority].ChooseResponseAsync(
                    new DecisionContext(State, priority, legal, false, snapshot, IsResponse: true), ct);
                if (response is not null && !legal.Contains(response))
                    throw new InvalidOperationException($"Controller for player {priority} returned an illegal response: {response.Describe()}");
            }

            if (response is ReactAction r)
            {
                var item = await AnnounceOrderAsync(r.Player, r.Card, null, r.RespondingTo, ct);
                await PushAsync(item, ct);
                passesInRow = 0;
                priority = GameState.Other(r.Player);
                continue;
            }

            passesInRow++;
            if (passesInRow >= 2)
            {
                await PopAndResolveAsync(ct);
                passesInRow = 0;
                priority = GameState.Other(top.Controller);
            }
            else
            {
                priority = GameState.Other(priority);
            }
        }
    }

    private async Task PopAndResolveAsync(CancellationToken ct)
    {
        var top = _stack.Pop();
        await top.Resolve(top, ct); // Each item handles its own Cancelled state.
        await EmitAsync(new StackPoppedEvent(top, StackSnapshot()), ct);
    }

    // ================================================================== End of Main (§5.3)

    private async Task EndOfMainAsync(CancellationToken ct)
    {
        State.Phase = GamePhase.EndOfMain;
        // TODO(M3): End of Main triggers (first player's first; rank modifiers before duels).
        var result = Scoring.Score(State);
        if (result.Winner is int w) State.Players[w].RoundsWon++;
        State.LastRoundWinner = result.Winner;
        await EmitAsync(new RoundScoredEvent(result), ct);
    }

    // ================================================================== Clean-up (§5.4)

    private async Task CleanUpAsync(CancellationToken ct)
    {
        State.Phase = GamePhase.CleanUp;
        // TODO(M3): Clean-up triggers (Y'nosh first).

        foreach (int p in TurnOrder())
        {
            foreach (var node in State.Players[p].ScoringNodes.ToList())
            {
                if (node.Card is null) continue;
                await RetireAsync(node.Card, ct); // fires "When this Unit leaves play"
            }
        }

        foreach (int p in TurnOrder())
        {
            foreach (var node in State.Players[p].OrderNodes)
            {
                if (node.Card is null) continue;
                var card = node.Card;
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new CardMovedToPileEvent(card, node, PileKind.IssuedOrders), ct);
            }
        }

        foreach (var node in State.AllNodes) node.IsLocked = false;

        // 5. Warzones: Active Warzones go to the bottom of the Warzone deck in random order.
        if (State.ActiveWarzones.Count > 0)
        {
            var active = State.ActiveWarzones.ToList();
            State.Shuffle(active);
            foreach (var wz in active)
            {
                State.MoveToPileBottom(wz, PileKind.WarzoneDeck);
                await EmitAsync(new WarzoneReturnedEvent(wz), ct);
            }
        }

        await EmitAsync(new RoundEndedEvent(State.Round, State.Players[0].RoundsWon, State.Players[1].RoundsWon), ct);
    }

    // ================================================================== Game end (§5.5)

    private GameOutcome? CheckGameEnd()
    {
        int r0 = State.Players[0].RoundsWon;
        int r1 = State.Players[1].RoundsWon;
        int target = State.Options.RoundsToWin;

        if (r0 >= target || r1 >= target)
            return new GameOutcome(r0 >= target ? 0 : 1, r0, r1, State.Round, $"First to {target} rounds");

        if (State.EndAfterThisRound)
            return new GameOutcome(r0 == r1 ? null : (r0 > r1 ? 0 : 1), r0, r1, State.Round, "A deck ran out");

        if (_actionsThisRound == 0 && !CanStillDrawUnits())
            return new GameOutcome(r0 == r1 ? null : (r0 > r1 ? 0 : 1), r0, r1, State.Round, "No playable cards left");

        if (State.Round >= MaxRounds)
            return new GameOutcome(r0 == r1 ? null : (r0 > r1 ? 0 : 1), r0, r1, State.Round, "Round limit reached");

        return null;
    }

    private bool CanStillDrawUnits() =>
        State.Options.DeckMode == DeckMode.RandomDraw
        || State.UnitDeck.Count > 0
        || (State.Options.EmptyDeck == EmptyDeckRule.Reshuffle && State.RetiredUnits.Count > 0);

    internal Task EmitAsync(GameEvent e, CancellationToken ct) => _sink.PublishAsync(e, ct);
}
