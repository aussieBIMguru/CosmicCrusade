using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>Archons module (design/archons.md): the deck, the Start-up draw, the slot, and all 12 cards.</summary>
public class ArchonTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static GameState Board()
    {
        var s = TestData.EmptyGame();
        s.Options.Archons = true;
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        s.Options.HandUnitTarget = 0; // no Start-up draws: tests put cards where they want them
        s.Options.HandOrderTarget = 0;
        s.Round = 1;              // Start-up makes this round 2 ...
        s.LastRoundWinner = 0;    // ... and player 0 goes first
        return s;
    }

    private static CardInstance Slot(GameState s, int player, string name)
    {
        var c = s.CreateInstance(Def(name));
        s.MoveToArchonSlot(c, player);
        return c;
    }

    private static CardInstance Hand(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance Place(GameState s, string name, Node node, int? rank = null) => TestData.Place(s, Def(name), node, rank);

    private sealed class Stop : Exception { }

    /// <summary>Answers choices with the defaults (or <see cref="Chooser"/>) and stops the game at the first turn.</summary>
    private sealed class StopAtFirstTurn : IPlayerController
    {
        public Func<ChoiceRequest, IReadOnlyList<int>?>? Chooser { get; init; }
        public List<ChoiceRequest> Asked { get; } = new();

        public Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct) => throw new Stop();
        public Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct) => Task.FromResult<GameAction?>(null);

        public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct)
        {
            Asked.Add(request);
            return Task.FromResult(Chooser?.Invoke(request) ?? request.DefaultPick());
        }
    }

    // ------------------------------------------------------------------ module, deck, Start-up

    [Fact]
    public void All_twelve_archons_are_fully_implemented()
    {
        var archons = TestData.Db.All.Where(c => c.IsArchon).ToList();
        Assert.Equal(12, archons.Count);
        Assert.All(archons, a => Assert.Equal(Implementation.Full, CardStatus.Of(a)));
        Assert.All(archons, a => Assert.Equal(new[] { Race.Archon }, a.Races));
        Assert.Equal(10, Def("The Unnameable").BaseRank);
        Assert.Equal(4, Def("Androsiar").BaseRank);
        Assert.Equal(OrderType.Ongoing, Def("Unstoppable").OrderType);
    }

    [Fact]
    public void Archons_are_off_by_default_and_never_join_the_normal_decks()
    {
        var off = TestData.NewGame(3);
        Assert.False(off.Options.Archons);
        Assert.Empty(off.ArchonDeck);
        Assert.Equal(150, off.UnitDeck.Count);
        Assert.Equal(80, off.OrderDeck.Count);

        var on = TestData.NewGame(3, o => o.Archons = true);
        Assert.Equal(12, on.ArchonDeck.Count);
        Assert.All(on.ArchonDeck, c => Assert.True(c.Definition.IsArchon));
        Assert.Equal(150, on.UnitDeck.Count);
        Assert.Equal(80, on.OrderDeck.Count);
        Assert.DoesNotContain(on.UnitDeck.Concat(on.OrderDeck), c => c.Definition.IsArchon);
    }

    [Fact]
    public async Task Taking_the_archon_draw_fills_the_slot_and_costs_one_card()
    {
        var s = TestData.NewGame(5, o => o.Archons = true);
        var p0 = new StopAtFirstTurn();                                                   // Yes, one fewer Unit, first Archon
        var p1 = new StopAtFirstTurn { Chooser = r => r.Purpose == "ArchonSkip" ? new[] { 1 } : null }; // one fewer Order
        var sink = new RecordingEventSink();
        await Assert.ThrowsAsync<Stop>(() => new GameEngine(s, p0, p1, sink).RunAsync());

        Assert.All(s.Players, p => Assert.True(p.ArchonTaken));
        Assert.All(s.Players, p => Assert.True(p.ArchonSlot is { } a && a.Definition.IsArchon && a.Location.Kind == LocationKind.ArchonSlot));
        Assert.Equal((4, 3), (s.Players[0].HandUnitCount, s.Players[0].HandOrderCount));
        Assert.Equal((5, 2), (s.Players[1].HandUnitCount, s.Players[1].HandOrderCount));
        Assert.Equal(10, s.ArchonDeck.Count);
        Assert.Equal(2, sink.Events.OfType<ArchonTakenEvent>().Count());
        Assert.Equal(3, p0.Asked.Single(r => r.Purpose == "ArchonPick").Options.Count);
    }

    [Fact]
    public async Task Declining_the_archon_draw_keeps_the_full_draw()
    {
        var s = TestData.NewGame(6, o => o.Archons = true);
        var no = new StopAtFirstTurn { Chooser = r => r.Purpose == "ArchonDraw" ? new[] { 1 } : null };
        await Assert.ThrowsAsync<Stop>(() => new GameEngine(s, no, no).RunAsync());

        Assert.All(s.Players, p => Assert.False(p.ArchonTaken));
        Assert.All(s.Players, p => Assert.Null(p.ArchonSlot));
        Assert.All(s.Players, p => Assert.Equal((5, 3), (p.HandUnitCount, p.HandOrderCount)));
        Assert.Equal(12, s.ArchonDeck.Count);
    }

    [Fact]
    public async Task An_unplayed_archon_is_lost_at_clean_up()
    {
        var s = Board();
        s.Options.RoundsToWin = 1;
        var archon = Slot(s, 0, "Planetscape");
        s.Players[0].ArchonTaken = true;

        Assert.Contains(LegalActions.ForTurn(s, 0).OfType<IssueOrderAction>(), a => a.Card == archon);

        var sink = await OrderReactAssembleTests.Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);

        Assert.Null(s.Players[0].ArchonSlot);
        Assert.Equal(PileKind.ArchonDeck, archon.Location.Pile);
        Assert.Contains(sink.Events, e => e is ArchonLostEvent l && l.Archon == archon);
    }

    [Fact]
    public void An_archon_leaving_play_goes_back_to_the_archon_deck()
    {
        var s = Board();
        var unnameable = Place(s, "The Unnameable", s.Players[0].LeaderNode!);
        s.MoveToPile(unnameable, PileKind.NullifiedUnits);
        Assert.Equal(PileKind.ArchonDeck, unnameable.Location.Pile);
        Assert.Empty(s.NullifiedUnits);

        var arsenal = Place(s, "Archon Arsenal", s.Players[0].OrderNodes[0]);
        s.MoveToPile(arsenal, PileKind.IssuedOrders);
        Assert.Equal(PileKind.ArchonDeck, arsenal.Location.Pile);
        Assert.Equal(2, s.ArchonDeck.Count);
    }

    [Fact]
    public void Clone_keeps_the_archon_state()
    {
        var s = Board();
        var a = Slot(s, 1, "Deify");
        s.Players[1].ArchonTaken = true;
        s.ReturnArchonToDeck(s.CreateInstance(Def("Ceasefire")));

        var c = s.Clone(1);
        Assert.True(c.Players[1].ArchonTaken);
        Assert.Equal(a.InstanceId, c.Players[1].ArchonSlot!.InstanceId);
        Assert.NotSame(a, c.Players[1].ArchonSlot);
        Assert.Equal(LocationKind.ArchonSlot, c.Players[1].ArchonSlot!.Location.Kind);
        Assert.Single(c.ArchonDeck);
    }

    // ------------------------------------------------------------------ static Archons

    [Fact]
    public void Archon_Arsenal_and_Unstoppable_help_only_their_side()
    {
        var s = Board();
        var mine = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        var theirs = TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[1].UnitNodes[0]);
        Place(s, "Archon Arsenal", s.Players[0].OrderNodes[0]);
        Place(s, "Unstoppable", s.Players[0].OrderNodes[1]);

        Assert.Equal(6, RankCalculator.GetRank(s, mine));
        Assert.Equal(4, RankCalculator.GetRank(s, theirs));
        Assert.True(StaticLayer.PreventsNullify(s, mine, null, out _));
        Assert.False(StaticLayer.PreventsNullify(s, theirs, null, out _));
    }

    [Fact]
    public void The_Unnameable_counts_your_other_units_as_zero()
    {
        var s = Board();
        Place(s, "The Unnameable", s.Players[0].LeaderNode!);
        var brute = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]);

        Assert.Equal(10, RankCalculator.TotalRank(s, 0));
        Assert.Equal(4, RankCalculator.GetRank(s, brute)); // real Rank for pairs and Duels (P11)
        Assert.Equal(4, RankCalculator.TotalRank(s, 1));
    }

    [Fact]
    public void Deify_gives_your_leader_plus_3_and_immunity_to_everything_else()
    {
        var s = Board();
        var leader = Place(s, "Lance Corporal", s.Players[0].LeaderNode!);
        Place(s, "Deify", s.Players[0].OrderNodes[0]);
        Place(s, "Archon Arsenal", s.Players[0].OrderNodes[1]); // +2: blocked by the Immunity
        Place(s, "Acid Rain", s.Players[1].OrderNodes[0]);      // −2: blocked too

        Assert.True(StaticLayer.IsImmune(s, leader));
        Assert.Equal(1 + 3, RankCalculator.GetRank(s, leader));
        Assert.True(StaticLayer.PreventsNullify(s, leader, null, out _));
    }

    [Fact]
    public void Galactic_Siege_stops_their_orders_and_suppresses_their_ongoings()
    {
        var s = Board();
        Place(s, "Galactic Siege", s.Players[0].OrderNodes[0]);
        Place(s, "Acid Rain", s.Players[1].OrderNodes[0]);
        var brute = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        Hand(s, 1, Def("Depose"));
        Hand(s, 0, Def("Depose"));

        Assert.Equal(4, RankCalculator.GetRank(s, brute)); // Acid Rain is Suppressed
        Assert.Empty(LegalActions.ForTurn(s, 1).OfType<IssueOrderAction>());
        Assert.NotEmpty(LegalActions.ForTurn(s, 0).OfType<IssueOrderAction>());
    }

    [Fact]
    public async Task Open_Source_gives_an_additional_action_in_the_same_turn()
    {
        var s = Board();
        Place(s, "Open Source", s.Players[0].OrderNodes[0]);
        var a = Hand(s, 0, TestData.Brute(Race.Blaarg));
        var b = Hand(s, 0, TestData.Brute(Race.Blaarg));
        var u1 = s.Players[0].UnitNodes[0];
        var u2 = s.Players[0].UnitNodes[1];
        Hand(s, 1, TestData.Brute(Race.Overmind));

        var p0 = new ScriptedController()
            .Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(x => x.Card == a && x.Target == u1))
            .Turn(ctx =>
            {
                Assert.True(ctx.ExtraAction);
                return ctx.Legal.OfType<DeployFromHandAction>().Single(x => x.Card == b && x.Target == u2);
            });
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(a, u1.Card);
        Assert.Same(b, u2.Card);
        int second = sink.Events.FindIndex(e => e is UnitPlayedEvent p && p.Card == b);
        int opponentTurn = sink.Events.FindIndex(e => e is PassedEvent { Player: 1 } or TurnStartedEvent { Player: 1 });
        Assert.True(opponentTurn < 0 || second < opponentTurn);
    }

    // ------------------------------------------------------------------ Action Archons

    [Fact]
    public async Task Planetscape_nullifies_every_unit_in_play()
    {
        var s = Board();
        var archon = Slot(s, 0, "Planetscape");
        TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        Place(s, "Lance Corporal", s.Players[0].LeaderNode!);
        TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[1].UnitNodes[2]);
        var standby = TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[1].StandbyNodes[0]);

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(x => x.Card == archon));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Empty(s.AllUnitsInPlay);
        Assert.Same(standby, s.Players[1].StandbyNodes[0].Card); // not in play (P13)
        Assert.Equal(3, s.NullifiedUnits.Count);
        Assert.Equal(PileKind.ArchonDeck, archon.Location.Pile);
    }

    [Fact]
    public async Task Nebula_Cannon_nullifies_the_unit_and_locks_its_node()
    {
        var s = Board();
        var archon = Slot(s, 0, "Nebula Cannon");
        var target = s.Players[1].UnitNodes[1];
        var victim = TestData.Place(s, TestData.Brute(Race.Overmind), target);

        var p0 = new ScriptedController
        {
            Chooser = r => r.Kind == ChoiceKind.Nodes ? new[] { r.Options.ToList().FindIndex(o => o.Node == target) } : null,
        }.Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(x => x.Card == archon));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(PileKind.NullifiedUnits, victim.Location.Pile);
        Assert.True(target.IsLocked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ceasefire_ends_the_main_phase_only_when_both_sides_have_a_leader(bool bothLeaders)
    {
        var s = Board();
        var archon = Slot(s, 0, "Ceasefire");
        Place(s, "Lance Corporal", s.Players[0].LeaderNode!);
        if (bothLeaders) Place(s, "Preferred Candidate", s.Players[1].LeaderNode!);
        var brute = Hand(s, 1, TestData.Brute(Race.Overmind));

        var p0 = new ScriptedController { StopWhenDone = false }.Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(x => x.Card == archon));
        var p1 = new ScriptedController { StopWhenDone = false }.Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(x => x.Card == brute));
        var sink = await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Contains(sink.Events, e => e is RoundScoredEvent);
        Assert.Equal(bothLeaders, brute.Location.Kind == LocationKind.Hand);
    }

    [Fact]
    public async Task Insurrection_wins_the_round_with_three_units_that_share_a_race()
    {
        var s = Board();
        var archon = Slot(s, 0, "Insurrection");
        for (int i = 0; i < 3; i++) TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[i], rank: 1);
        for (int i = 0; i < 3; i++) TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[1].UnitNodes[i], rank: 9);

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(x => x.Card == archon));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        var scored = sink.Events.OfType<RoundScoredEvent>().Single();
        Assert.Equal(0, scored.Result.Winner);
        Assert.Equal("Insurrection", scored.Result.WonBy);
    }

    [Fact]
    public async Task Quantum_Leap_draws_orders_issues_some_and_retires_the_rest()
    {
        var s = Board();
        var archon = Slot(s, 0, "Quantum Leap");
        foreach (var name in new[] { "Depose", "Blazing Saddles", "Acid Rain" })
        {
            var c = s.CreateInstance(Def(name));
            s.MoveToPile(c, PileKind.OrderDeck);
        }
        var drawn = s.OrderDeck.ToList();
        int issues = 0;

        var p0 = new ScriptedController
        {
            Chooser = r => r.Purpose switch
            {
                "QuantumLeapDraw" => new[] { r.Options.Count - 1 },                 // 3 Orders
                "QuantumLeapIssue" => issues++ == 0 ? new[] { 0 } : Array.Empty<int>(), // issue one, then stop
                _ => null,
            },
        }.Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(x => x.Card == archon));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Empty(s.OrderDeck);
        Assert.DoesNotContain(s.Players[0].Hand, c => drawn.Contains(c));
        Assert.Equal(1, sink.Events.OfType<OrderResolvedEvent>().Count(e => drawn.Contains(e.Card)));
        Assert.Equal(2, sink.Events.OfType<CardRetiredFromHandEvent>().Count(e => drawn.Contains(e.Card)));
    }

    // ------------------------------------------------------------------ Unit Archons

    [Fact]
    public async Task Androsiar_can_make_a_unit_enter_play_as_its_copy()
    {
        var s = Board();
        Place(s, "Androsiar", s.Players[0].LeaderNode!);
        var small = Hand(s, 0, Def("Chrome Domer"));
        var u1 = s.Players[0].UnitNodes[0];

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(x => x.Card == small && x.Target == u1));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever()); // default answer: Yes

        Assert.True(small.IsCopy);
        Assert.Equal("Androsiar", small.Definition.Name);
        Assert.Equal(4, RankCalculator.GetRank(s, small));
        Assert.Contains(sink.Events, e => e is CardCopiedEvent c && c.Card == small);
    }

    [Fact]
    public void An_archon_unit_in_the_slot_can_be_deployed_to_the_leader_node()
    {
        var s = Board();
        var archon = Slot(s, 0, "The Unnameable");
        var deploys = LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>().Where(d => d.Card == archon).ToList();
        Assert.Single(deploys);
        Assert.Equal(NodeKind.Leader, deploys[0].Target.Kind);
    }

    // ------------------------------------------------------------------ whole games

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public async Task Heuristic_games_with_archons_finish_and_every_archon_comes_home(int seed)
    {
        var s = TestData.NewGame(seed, o => o.Archons = true);
        var outcome = await new GameEngine(s, new HeuristicAi(seed), new HeuristicAi(seed + 1)).RunAsync();
        Assert.True(outcome.RoundsPlayed > 0);
        Assert.Equal(12, s.ArchonDeck.Count);
        Assert.All(s.Players, p => Assert.Null(p.ArchonSlot));
        Assert.Equal(150, s.AllCards.Count(c => c.PrintedDefinition.IsUnit && !c.PrintedDefinition.IsArchon));
        Assert.Equal(80, s.AllCards.Count(c => c.PrintedDefinition.IsOrder && !c.PrintedDefinition.IsArchon));
    }

    [Fact]
    public async Task Monte_carlo_plays_with_archons()
    {
        var s = TestData.NewGame(21, o => o.Archons = true);
        var mc = new MonteCarloAi(3, new MonteCarloSettings { TimeBudgetMs = 0, MaxRollouts = 30, MinRolloutsPerAction = 1, Background = false });
        var outcome = await new GameEngine(s, mc, new HeuristicAi(4)).RunAsync();
        Assert.True(outcome.RoundsPlayed > 0);
        Assert.Equal(12, s.ArchonDeck.Count);
    }
}
