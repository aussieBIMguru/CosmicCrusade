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
        foreach (var ps in State.Players) ps.DeployTurnsThisRound = 0;
        ResetRoundFlags();

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
        await RaiseAsync(new TriggerEvent("CardDrawn", player, card), ct);
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
                await CheckStateAsync(ct);
                if (_roundEndsNow) break; // Senate Chambers: the round ends at once.
            }

            current = GameState.Other(current);
        }
    }

    private async Task ExecuteAsync(GameAction action, CancellationToken ct)
    {
        switch (action)
        {
            case DeployFromHandAction d:
                State.Players[d.Player].DeployTurnsThisRound++;
                await DeployAsync(d.Player, d.Card, d.Target, fromNode: null, ct);
                break;

            case AdvanceFromStandbyAction a:
                State.Players[a.Player].DeployTurnsThisRound++;
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

            case DeployFromPileAction p:
                await DeployFromPileAsync(p, ct);
                break;

            case DeployAtRandomAction r:
            {
                var nodes = LegalActions.Deploys(State, r.Player).Hand.Where(d => d.Card == r.Card).Select(d => d.Target).ToList();
                var node = await RandomDeployNodeAsync(r.Player, r.Card, nodes, ct);
                State.Players[r.Player].DeployTurnsThisRound++;
                await DeployAsync(r.Player, r.Card, node, fromNode: null, ct);
                break;
            }

            case CoercedDeployAction c:
            {
                var options = LegalActions.Deploys(State, c.Player).Hand.Select(d => (d.Card, d.Target)).ToList();
                var (card, node) = await CoercedDeployPickAsync(c.Player, options, ct);
                State.Players[c.Player].DeployTurnsThisRound++;
                await DeployAsync(c.Player, card, node, fromNode: null, ct);
                break;
            }

            case GrantedAction g:
                await RunGrantedActionAsync(g, ct);
                break;

            default:
                throw new NotSupportedException(action.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ Deploy (§7.1)

    /// <summary>Deploy pipeline (§7.1): announce → priority/stack → Played (or cancelled).</summary>
    private async Task DeployAsync(int player, CardInstance card, Node target, Node? fromNode, CancellationToken ct,
        PileKind? fromPile = null)
    {
        await PayCostAsync(player, card, ct);
        State.MoveToStack(card, player);
        await EmitAsync(new UnitDeployAnnouncedEvent(player, card, target, fromNode, fromPile), ct);

        var item = new StackItem(player, StackItemKind.DeployUnit, card, $"Deploy {card.Name} to {target.Label}",
            (self, token) => ResolveDeployAsync(self, token))
        {
            TargetNode = target,
            FromNode = fromNode,
            FromPile = fromPile,
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
        if (StaticLayer.IsTriggerSuppressed(State, card, "OnPlay"))
        {
            if (EffectSupport.IsLive(card.Definition, "OnPlay") || card.AbilitiesSuppressed)
                await EmitAsync(new LogEvent($"  {card.Name}'s abilities are Suppressed."), ct);
        }
        else
            await FireTriggersAsync(card, "OnPlay", target.Side, target, card.Memory, null, ct);

        // "Whenever a Unit / Card is played" (Xeno Laser hits after on-play abilities: ruling #21).
        await RaisePlayedAsync(item.Controller, card, fromHand: item.FromNode is null && item.FromPile is null, ct);
    }

    /// <summary>Barkeep Bill & co.: pick the Unit from the pile, then its node, then Deploy it as usual.</summary>
    private async Task DeployFromPileAsync(DeployFromPileAction a, CancellationToken ct)
    {
        var cards = StaticLayer.PlayableFromPile(State, a.Player, a.Pile)
            .Where(c => LegalActions.DeployNodes(State, a.Player, c).Count > 0).ToList();
        if (cards.Count == 0) return;

        CardInstance card;
        Node node;
        var deployOverride = StaticLayer.DeployNodeOverride(State, a.Player);
        if (deployOverride is { Random: false })
        {
            // Psychic Coercion: the node first, then a Unit from the pile that may go there.
            var options = cards.SelectMany(c => LegalActions.DeployNodes(State, a.Player, c).Select(n => (c, n))).ToList();
            (card, node) = await CoercedDeployPickAsync(a.Player, options, ct);
        }
        else
        {
            string pileName = a.Pile == PileKind.RetiredUnits ? "Retired" : "Nullified";
            var cardPick = await ChooseAsync(new ChoiceRequest(a.Player, null, $"Choose a Unit to Deploy from the {pileName} pile",
                ChoiceKind.Cards, cards.Select(c => new ChoiceOption($"{c.Name} (Rank {RankCalculator.BaseRank(c)})", c)).ToList(),
                1, 1, "PlayFromPile"), ct);
            card = cards[cardPick.Count > 0 ? cardPick[0] : 0];

            var nodes = LegalActions.DeployNodes(State, a.Player, card);
            if (deployOverride is { Random: true })
                node = await RandomDeployNodeAsync(a.Player, card, nodes, ct);
            else
            {
                var nodePick = await ChooseAsync(new ChoiceRequest(a.Player, card, $"Deploy {card.Name} to which node?",
                    ChoiceKind.Nodes, nodes.Select(n => new ChoiceOption($"{(n.Side == a.Player ? "Your" : "Opponent's")} {n.Label}", Node: n)).ToList(),
                    1, 1, "DeployNode"), ct);
                node = nodes[nodePick.Count > 0 ? nodePick[0] : 0];
            }
        }

        State.Players[a.Player].DeployTurnsThisRound++; // Military Academy counts deploys from any zone (ruling #23)
        await DeployAsync(a.Player, card, node, fromNode: null, ct, fromPile: a.Pile);
    }

    // ------------------------------------------------------------------ M3.2c: who picks the node

    /// <summary>Shoot First, Think Second: one of the legal nodes, at random (seeded).</summary>
    private async Task<Node> RandomDeployNodeAsync(int player, CardInstance card, IReadOnlyList<Node> nodes, CancellationToken ct)
    {
        var node = nodes[State.Rng.Next(nodes.Count)];
        string source = StaticLayer.DeployNodeOverride(State, player)?.Source.Name ?? "Random";
        await EmitAsync(new LogEvent($"{source}: {card.Name} goes to {State.Players[node.Side].Name}'s {node.Label} at random."), ct);
        return node;
    }

    /// <summary>
    /// Psychic Coercion (spec §7.1): the deploying player has declared "I'm deploying a Unit". The coercer picks the
    /// node first – only nodes at least one of the Units could legally go to, so a Deploy is always possible – then the
    /// deploying player picks a Unit for that node.
    /// </summary>
    private async Task<(CardInstance Card, Node Node)> CoercedDeployPickAsync(int player, List<(CardInstance Card, Node Node)> options,
        CancellationToken ct)
    {
        var coercion = StaticLayer.DeployNodeOverride(State, player)
            ?? throw new InvalidOperationException("No deploy override applies.");
        int chooser = coercion.Chooser;
        var nodes = options.Select(o => o.Node).Distinct().ToList();
        var nodePick = await ChooseAsync(new ChoiceRequest(chooser, coercion.Source,
            $"{coercion.Source.Name}: choose the node {State.Players[player].Name} must Deploy a Unit to",
            ChoiceKind.Nodes, nodes.Select(n => new ChoiceOption($"{(n.Side == chooser ? "Your" : "Opponent's")} {n.Label}", Node: n)).ToList(),
            1, 1, "CoerceNode"), ct);
        var node = nodes[nodePick.Count > 0 ? nodePick[0] : 0];

        var cards = options.Where(o => o.Node == node).Select(o => o.Card).Distinct().ToList();
        var cardPick = await ChooseAsync(new ChoiceRequest(player, coercion.Source,
            $"{coercion.Source.Name}: choose a Unit to Deploy to {(node.Side == player ? "your" : "the opponent's")} {node.Label}",
            ChoiceKind.Cards, cards.Select(c => new ChoiceOption($"{c.Name} (Rank {RankCalculator.BaseRank(c)})", c, node)).ToList(),
            1, 1, "CoercedUnit"), ct);
        return (cards[cardPick.Count > 0 ? cardPick[0] : 0], node);
    }

    // ------------------------------------------------------------------ M3.2c: granted actions

    /// <summary>
    /// Abominations: pay the cost (Nullify a Necrolite card from hand), then run the granted ops as the acting
    /// player, with the granting card as the source. It resolves at once: no React answers a granted action.
    /// </summary>
    private async Task RunGrantedActionAsync(GrantedAction a, CancellationToken ct)
    {
        var info = StaticLayer.GrantedActions(State, a.Player).FirstOrDefault(g => g.Source == a.Source);
        if (info is null) return;
        await EmitAsync(new LogEvent($"{State.Players[a.Player].Name}: {a.Name} ({a.Source.Name})."), ct);

        var candidates = LegalActions.GrantedCostCandidates(State, a.Player, info);
        if (candidates.Count == 0) return;
        var pick = await ChooseAsync(new ChoiceRequest(a.Player, a.Source, $"{a.Name}: choose a card to Nullify from your hand",
            ChoiceKind.Cards, candidates.Select(c => new ChoiceOption(c.Name, c)).ToList(), 1, 1, "PayCost"), ct);
        await NullifyFromHandAsync(candidates[pick.Count > 0 ? pick[0] : 0], a.Source, ct);

        var ctx = new EffectContext(this, a.Source, a.Player, a.Source.Location.Node, "GrantAction");
        await Runner.RunAsync(info.Op, ctx, ct);
    }

    /// <summary>PlayCost (Mechatron, Scrapped Metal): Retire the required cards from hand as the card is played.</summary>
    private async Task PayCostAsync(int player, CardInstance card, CancellationToken ct)
    {
        if (PlayRules.CostOf(State, card) is not { } cost) return;
        var candidates = PlayRules.CostCandidates(State, card, player);
        var picks = await ChooseAsync(new ChoiceRequest(player, card,
            $"{card.Name}: choose {cost.Count} card{(cost.Count == 1 ? "" : "s")} to Retire from your hand",
            ChoiceKind.Cards, candidates.Select(c => new ChoiceOption(c.Name, c)).ToList(), cost.Count, cost.Count, "PayCost"), ct);
        foreach (var i in picks) await RetireFromHandAsync(candidates[i], ct);
    }

    // ------------------------------------------------------------------ Orders (§3, §7.1)

    /// <summary>Card leaves hand, goes on the stack. The caller pushes it (turn action) or it is a React response.</summary>
    private async Task<StackItem> AnnounceOrderAsync(int player, CardInstance card, Node? orderNode, StackItem? respondingTo, CancellationToken ct)
    {
        await PayCostAsync(player, card, ct);
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
                    bool suppressed = StaticLayer.IsSuppressed(State, card);
                    // "As you Issue this Order, choose a Race" (Extreme Sanctions, Containment Network).
                    if (!suppressed && EffectSupport.IsLive(card.Definition, "OnIssue"))
                        await FireTriggersAsync(card, "OnIssue", player, node, card.Memory, null, ct);
                    bool live = StaticLayer.HasLiveStatics(card.Definition);
                    string detail = suppressed ? "on its Order Node, but Suppressed"
                        : live ? "in effect"
                        : "on its Order Node (its effect isn't implemented yet)";
                    await EmitAsync(new OrderResolvedEvent(player, card, node, live && !suppressed, detail), ct);
                    await RaisePlayedAsync(player, card, fromHand: true, ct);
                }
                else
                {
                    State.MoveToPile(card, PileKind.IssuedOrders);
                    await EmitAsync(new OrderResolvedEvent(player, card, null, false, "fizzled: its Order Node is no longer Empty"), ct);
                }
                break;
            }

            case OrderType.React when StaticLayer.IsSuppressed(State, card):
            case OrderType.Action when StaticLayer.IsSuppressed(State, card):
            {
                // Suppressing Goop: "Suppress all Card Abilities and Effects (even when not in play)".
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new OrderResolvedEvent(player, card, null, false, "Suppressed: it has no effect"), ct);
                await RaisePlayedAsync(player, card, fromHand: true, ct);
                break;
            }

            case OrderType.React:
            {
                bool live = EffectSupport.IsLive(card.Definition, "React");
                if (live) await FireTriggersAsync(card, "React", player, null, new(), item.RespondingTo, ct);
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new OrderResolvedEvent(player, card, null, live,
                    live ? "resolved" : "its effect isn't implemented yet"), ct);
                await RaisePlayedAsync(player, card, fromHand: true, ct);
                break;
            }

            default: // Action
            {
                bool live = EffectSupport.IsLive(card.Definition, "OnResolve");
                if (live) await FireTriggersAsync(card, "OnResolve", player, null, new(), null, ct);
                State.MoveToPile(card, PileKind.IssuedOrders);
                await EmitAsync(new OrderResolvedEvent(player, card, null, live,
                    live ? "resolved" : "its effect isn't implemented yet"), ct);
                await RaisePlayedAsync(player, card, fromHand: true, ct);
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
        await RaiseAsync(new TriggerEvent("CardRetiredFromHand", item.Controller, card), ct);
        if (ok) await RaiseAsync(new TriggerEvent("Assembled", item.Controller, card, AssembledRank: amount, AssembleTarget: target), ct);
    }

    // ------------------------------------------------------------------ Priority (§7.2)

    private IReadOnlyList<StackItem> StackSnapshot() => _stack.ToList(); // Stack<T> enumerates top first.

    private async Task PushAsync(StackItem item, CancellationToken ct)
    {
        // A Nullify window only appears on the stack once someone responds to it (so it doesn't reveal hands).
        if (item.RespondingTo is { Kind: StackItemKind.UnitNullifying, Shown: false } window)
        {
            window.Shown = true;
            await EmitAsync(new StackPushedEvent(window, StackSnapshot()), ct);
        }
        _stack.Push(item);
        if (item.Kind != StackItemKind.UnitNullifying) await EmitAsync(new StackPushedEvent(item, StackSnapshot()), ct);
    }

    /// <summary>
    /// MTG Arena-style priority (§7.2): after an action the other player gets priority. Responses stack LIFO;
    /// the top resolves once both players pass in a row, then priority passes again. A player is only asked when
    /// they hold a React whose condition is met (auto-allow); the human seat can also auto-pass everything.
    /// A cancelled item on top leaves the stack at once (there is nothing left to respond to).
    /// </summary>
    private async Task RunPriorityAsync(StackItem first, int actor, CancellationToken ct)
    {
        // Windows can open while another item resolves (a Nullify during an ability): resolve only down to here.
        int baseDepth = _stack.Count;
        await PushAsync(first, ct);
        int priority = GameState.Other(actor);
        int passesInRow = 0;
        int guard = 0;

        while (_stack.Count > baseDepth)
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
        if (top.Kind != StackItemKind.UnitNullifying || top.Shown)
            await EmitAsync(new StackPoppedEvent(top, StackSnapshot()), ct);
    }

    // ================================================================== End of Main (§5.3)

    private async Task EndOfMainAsync(CancellationToken ct)
    {
        State.Phase = GamePhase.EndOfMain;
        // End of Main triggers (The Empress, High Noon, Weed out the Weak), skipped when Senate Chambers already
        // ended the round.
        if (!_roundEndsNow) await FirePhaseTriggersAsync("EndOfMain", ct);
        var result = Scoring.Score(State);
        if (_forcedRoundWinner is int forced)
            result = result with { Winner = forced, DecidedByTiebreak = false, WonBy = _forcedRoundReason };
        if (result.Winner is int w) State.Players[w].RoundsWon++;
        State.LastRoundWinner = result.Winner;
        await EmitAsync(new RoundScoredEvent(result), ct);
    }

    // ================================================================== Clean-up (§5.4)

    private async Task CleanUpAsync(CancellationToken ct)
    {
        State.Phase = GamePhase.CleanUp;
        // 1. Clean-up triggers: Y'nosh first, then Preferred Candidate, Lone Wolf & co.
        await FirePhaseTriggersAsync("CleanUp", ct);

        // 2. Units on nodes are Retired, except those that remain in play (Preferred Candidate).
        foreach (int p in TurnOrder())
        {
            foreach (var node in State.Players[p].ScoringNodes.ToList())
            {
                if (node.Card is null || _remainInPlay.Contains(node.Card)) continue;
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
