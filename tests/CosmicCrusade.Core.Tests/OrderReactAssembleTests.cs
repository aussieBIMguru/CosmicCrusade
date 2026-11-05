using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>Milestone 2: issuing Orders, Reacts on the priority stack, Assemble.</summary>
public class OrderReactAssembleTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static CardInstance Give(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance Give(GameState s, int player, string name) => Give(s, player, Def(name));

    /// <summary>
    /// Plays until the scripts run out or the first round is scored (before Clean-up), so the board is left exactly
    /// as the plays made it. <paramref name="throughCleanUp"/> lets the game run on (tests about Clean-up).
    /// </summary>
    internal static async Task<StoppingSink> Run(GameState s, IPlayerController p0, IPlayerController p1, bool throughCleanUp = false)
    {
        var sink = new StoppingSink { StopAtEndOfMain = !throughCleanUp };
        try
        {
            await new GameEngine(s, p0, p1, sink).RunAsync();
        }
        catch (ScriptDone)
        {
            // Stopped on purpose: inspect the state as it is now.
        }
        return sink;
    }

    private static StackItem FakeItem(GameState s, int controller, StackItemKind kind, CardInstance card, Node? target = null)
    {
        s.MoveToStack(card, controller);
        return new StackItem(controller, kind, card, "test", (_, _) => Task.CompletedTask) { TargetNode = target };
    }

    private static CardDefinition PlainSynthenoid() => TestData.Db.All.First(c =>
        c.IsUnit && c.Races.Contains(Race.Synthenoid) && c.UnitClass is UnitClass.Minion or UnitClass.Underling
        && !c.Keywords.Contains("Assemble") && c.BaseRank is not null);

    // ------------------------------------------------------------------ data

    [Fact]
    public void All_ten_reacts_have_a_parsed_condition()
    {
        var reacts = TestData.Db.All.Where(c => c.OrderType == OrderType.React).ToList();
        Assert.Equal(10, reacts.Count);
        Assert.All(reacts, r => Assert.NotNull(ReactConditions.Of(r)));
    }

    [Theory]
    [InlineData("Shadow Strike", true)]
    [InlineData("Target Practice", true)]
    [InlineData("Second Thought", true)]
    [InlineData("Commandeer", false)]
    [InlineData("Already Dead", false)]
    public void Counter_reacts_are_recognised(string name, bool isCounter) =>
        Assert.Equal(isCounter, ReactCounters.IsCounter(Def(name)));

    // ------------------------------------------------------------------ orders

    [Fact]
    public async Task Action_order_resolves_to_the_issued_pile()
    {
        var s = TestData.EmptyGame();
        var action = Give(s, 0, TestData.Db.All.First(c => c.OrderType == OrderType.Action && c.Set == "Base"));
        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(a => a.Card == action));

        var sink = await Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(PileKind.IssuedOrders, action.Location.Pile);
        Assert.Contains(sink.Events, e => e is OrderResolvedEvent r && r.Card == action && r.OrderNode is null);
        Assert.Contains(sink.Events, e => e is StackPushedEvent p && p.Item.Card == action);
    }

    [Fact]
    public async Task Ongoing_order_sits_on_its_order_node_until_clean_up()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        s.Options.RoundsToWin = 1;
        var ongoing = Give(s, 0, TestData.Db.All.First(c => c.OrderType == OrderType.Ongoing && c.Set == "Base"));
        var o1 = s.Players[0].OrderNodes[0];

        var p0 = new ScriptedController { StopWhenDone = false }
            .Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(a => a.Card == ongoing && a.OrderNode == o1));
        var sink = await Run(s, p0, ScriptedController.PassForever(), throughCleanUp: true);

        int resolvedAt = sink.Events.FindIndex(e => e is OrderResolvedEvent r && r.OrderNode == o1);
        int cleanedAt = sink.Events.FindIndex(e => e is CardMovedToPileEvent m && m.Card == ongoing && m.Pile == PileKind.IssuedOrders);
        Assert.True(resolvedAt >= 0);
        Assert.True(cleanedAt > resolvedAt);
        Assert.Null(o1.Card);
    }

    [Fact]
    public void Ongoing_needs_an_empty_order_node_and_reacts_are_never_turn_actions()
    {
        var s = TestData.EmptyGame();
        var ongoing = Give(s, 0, TestData.Db.All.First(c => c.OrderType == OrderType.Ongoing && c.Set == "Base"));
        Give(s, 0, "Shadow Strike");
        s.Players[0].OrderNodes[0].IsLocked = true;

        var legal = LegalActions.ForTurn(s, 0);
        Assert.Single(legal.OfType<IssueOrderAction>(), a => a.Card == ongoing);
        Assert.DoesNotContain(legal.OfType<IssueOrderAction>(), a => a.Card.Definition.OrderType == OrderType.React);

        s.Players[0].OrderNodes[1].IsLocked = true;
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0).OfType<IssueOrderAction>(), a => a.Card == ongoing);
    }

    // ------------------------------------------------------------------ reacts

    [Fact]
    public async Task Shadow_Strike_nullifies_a_unit_as_it_is_deployed()
    {
        var s = TestData.EmptyGame();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));
        var strike = Give(s, 1, "Shadow Strike");
        var u1 = s.Players[0].UnitNodes[0];

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == brute && a.Target == u1));
        var p1 = ScriptedController.PassForever().React("Shadow Strike");
        var sink = await Run(s, p0, p1);

        Assert.Equal(PileKind.NullifiedUnits, brute.Location.Pile);
        Assert.Equal(PileKind.IssuedOrders, strike.Location.Pile);
        Assert.Null(u1.Card);
        Assert.DoesNotContain(sink.Events, e => e is UnitPlayedEvent p && p.Card == brute);
        Assert.Contains(sink.Events, e => e is CardNullifiedOnStackEvent n && n.Card == brute && n.By == strike);
    }

    [Fact]
    public async Task Second_Thought_counters_the_counter_and_the_unit_is_played()
    {
        var s = TestData.EmptyGame();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));
        var second = Give(s, 0, "Second Thought");
        var strike = Give(s, 1, "Shadow Strike");
        var u1 = s.Players[0].UnitNodes[0];

        var p0 = new ScriptedController()
            .Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == brute && a.Target == u1))
            .React("Second Thought");
        var p1 = ScriptedController.PassForever().React("Shadow Strike");
        var sink = await Run(s, p0, p1);

        Assert.Same(brute, u1.Card);
        Assert.Equal(PileKind.IssuedOrders, strike.Location.Pile);
        Assert.Equal(PileKind.IssuedOrders, second.Location.Pile);
        Assert.Contains(sink.Events, e => e is CardNullifiedOnStackEvent n && n.Card == strike && n.By == second);

        // LIFO: Second Thought resolves before Shadow Strike leaves, which happens before the deploy resolves.
        int popSecond = sink.Events.FindIndex(e => e is StackPoppedEvent p && p.Item.Card == second);
        int popStrike = sink.Events.FindIndex(e => e is StackPoppedEvent p && p.Item.Card == strike);
        int played = sink.Events.FindIndex(e => e is UnitPlayedEvent p && p.Card == brute);
        Assert.True(popSecond < popStrike && popStrike < played);
    }

    [Fact]
    public void Target_Practice_needs_the_target_node_to_be_opposed()
    {
        var s = TestData.EmptyGame();
        var practice = Give(s, 1, "Target Practice");
        var brute = s.CreateInstance(TestData.Brute(Race.Blaarg));
        var item = FakeItem(s, 0, StackItemKind.DeployUnit, brute, s.Players[0].UnitNodes[1]);

        Assert.Empty(LegalActions.ForResponse(s, 1, new[] { item }));

        TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[1].UnitNodes[1]);
        var legal = LegalActions.ForResponse(s, 1, new[] { item });
        Assert.Single(legal);
        Assert.Equal(practice, ((ReactAction)legal[0]).Card);
    }

    [Fact]
    public void Second_Thought_answers_actions_and_reacts_but_not_ongoings()
    {
        var s = TestData.EmptyGame();
        Give(s, 1, "Second Thought");
        var action = s.CreateInstance(TestData.Db.All.First(c => c.OrderType == OrderType.Action));
        var ongoing = s.CreateInstance(TestData.Db.All.First(c => c.OrderType == OrderType.Ongoing));

        Assert.Single(LegalActions.ForResponse(s, 1, new[] { FakeItem(s, 0, StackItemKind.IssueOrder, action) }));
        Assert.Empty(LegalActions.ForResponse(s, 1, new[] { FakeItem(s, 0, StackItemKind.IssueOrder, ongoing) }));
    }

    [Fact]
    public void Players_may_react_to_their_own_items()
    {
        var s = TestData.EmptyGame();
        Give(s, 0, "Shadow Strike");
        var brute = s.CreateInstance(TestData.Brute(Race.Blaarg));
        var item = FakeItem(s, 0, StackItemKind.DeployUnit, brute, s.Players[0].UnitNodes[0]);
        Assert.Single(LegalActions.ForResponse(s, 0, new[] { item }));
    }

    // ------------------------------------------------------------------ assemble

    [Fact]
    public async Task Assemble_adds_the_cards_rank_and_retires_it()
    {
        var s = TestData.EmptyGame();
        var host = TestData.Place(s, PlainSynthenoid(), s.Players[0].UnitNodes[0]);
        int before = RankCalculator.GetRank(s, host);
        var cog = Give(s, 0, "Cogwork Soldier");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<AssembleAction>().Single(a => a.Card == cog && a.Target == host));
        var sink = await Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(before + 2, RankCalculator.GetRank(s, host));
        Assert.Equal(PileKind.RetiredUnits, cog.Location.Pile);
        Assert.Contains(sink.Events, e => e is AssembledEvent { Applied: true, Amount: 2 });
    }

    [Fact]
    public void Assemble_needs_an_allied_synthenoid_unless_Cybernetic_Enhancements_is_in_play()
    {
        var s = TestData.EmptyGame();
        var brute = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        var enemySynth = TestData.Place(s, PlainSynthenoid(), s.Players[1].UnitNodes[0]);
        var spider = Give(s, 0, "Sprocket Spider");

        Assert.Empty(AssembleRules.Targets(s, 0, spider));
        Assert.False(AssembleRules.CanAssembleOnto(s, 0, spider, enemySynth));

        var cyber = s.CreateInstance(Def("Cybernetic Enhancements"));
        s.MoveToNode(cyber, s.Players[1].OrderNodes[0]); // Either player's Ongoing applies.
        Assert.Contains(brute, AssembleRules.Targets(s, 0, spider));
    }

    [Fact]
    public void Cybernetic_Enhancements_grants_Assemble_to_synthenoids_in_hand()
    {
        var s = TestData.EmptyGame();
        TestData.Place(s, PlainSynthenoid(), s.Players[0].UnitNodes[0]);
        var plain = Give(s, 0, PlainSynthenoid());
        Assert.False(AssembleRules.HasAssemble(s, plain));

        s.MoveToNode(s.CreateInstance(Def("Cybernetic Enhancements")), s.Players[0].OrderNodes[1]);
        Assert.True(AssembleRules.HasAssemble(s, plain));
        Assert.NotEmpty(LegalActions.ForTurn(s, 0).OfType<AssembleAction>());
    }

    [Fact]
    public async Task Heuristic_ai_counters_a_big_deploy()
    {
        var s = TestData.EmptyGame();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg)); // Rank 4
        Give(s, 1, "Shadow Strike");
        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(a => a.Card == brute && a.Target.Side == 0));

        await Run(s, p0, new Ai.HeuristicAi(1) { FillerOrderChance = 0 });

        Assert.Equal(PileKind.NullifiedUnits, brute.Location.Pile);
    }
}

internal sealed class ScriptDone : Exception { }

/// <summary>
/// Records events like <see cref="RecordingEventSink"/>. With <see cref="StopAtEndOfMain"/> it stops the game when
/// the first round is scored, before Clean-up retires anything.
///
/// Why: a scripted controller can't stop the game itself once its hand is empty, because a player whose only
/// legal action is Pass is auto-passed without being asked. The game would then run through Clean-up and later
/// rounds, and the board a test wants to inspect would be gone.
/// </summary>
internal sealed class StoppingSink : IGameEventSink
{
    public List<GameEvent> Events { get; } = new();
    public bool StopAtEndOfMain { get; init; } = true;

    public Task PublishAsync(GameEvent gameEvent, CancellationToken ct)
    {
        Events.Add(gameEvent);
        if (StopAtEndOfMain && gameEvent is RoundScoredEvent) throw new ScriptDone();
        return Task.CompletedTask;
    }
}

/// <summary>Plays a fixed list of turns, then passes (or stops the game). Reacts with named cards when it can.</summary>
internal sealed class ScriptedController : IPlayerController
{
    private readonly Queue<Func<DecisionContext, GameAction>> _turns = new();
    private readonly List<string> _reacts = new();

    /// <summary>When the turn script is used up: true = stop the game (throws), false = pass forever.</summary>
    public bool StopWhenDone { get; init; } = true;

    public static ScriptedController PassForever() => new() { StopWhenDone = false };

    public ScriptedController Turn(Func<DecisionContext, GameAction> pick)
    {
        _turns.Enqueue(pick);
        return this;
    }

    public ScriptedController React(string cardName)
    {
        _reacts.Add(cardName);
        return this;
    }

    /// <summary>Answers ability choices; return null to fall back to the default (first options / "Yes").</summary>
    public Func<ChoiceRequest, IReadOnlyList<int>?>? Chooser { get; set; }

    public List<ChoiceRequest> ChoicesAsked { get; } = new();

    public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
    {
        ChoicesAsked.Add(request);
        return Task.FromResult(Chooser?.Invoke(request) ?? request.DefaultPick());
    }

    public Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct)
    {
        if (_turns.Count > 0) return Task.FromResult(_turns.Dequeue()(context));
        if (StopWhenDone) throw new ScriptDone();
        return Task.FromResult<GameAction>(context.Legal.OfType<PassAction>().First());
    }

    public Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct)
    {
        foreach (var name in _reacts)
        {
            var r = context.Legal.OfType<ReactAction>().FirstOrDefault(a => a.Card.Name == name);
            if (r is null) continue;
            _reacts.Remove(name);
            return Task.FromResult<GameAction?>(r);
        }
        return Task.FromResult<GameAction?>(null);
    }
}
