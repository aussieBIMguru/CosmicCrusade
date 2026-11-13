using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using Xunit;

namespace CosmicCrusade.Core.Tests;

public class EngineTests
{
    private static async Task<(GameOutcome Outcome, RecordingEventSink Sink, GameState State)> Play(
        int seed, IPlayerController p0, IPlayerController p1, Action<GameOptions>? configure = null)
    {
        var state = TestData.NewGame(seed, configure);
        var sink = new RecordingEventSink();
        var engine = new GameEngine(state, p0, p1, sink);
        var outcome = await engine.RunAsync();
        return (outcome, sink, state);
    }

    [Fact]
    public async Task Start_up_fills_hands_to_5_units_and_3_orders()
    {
        var state = TestData.NewGame(7);
        var probe = new ProbeController();
        var engine = new GameEngine(state, probe, probe);
        await Assert.ThrowsAsync<StopGame>(() => engine.RunAsync());

        Assert.All(state.Players, p =>
        {
            Assert.Equal(5, p.HandUnitCount);
            Assert.Equal(3, p.HandOrderCount);
        });
        Assert.Equal(150 - 10, state.UnitDeck.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Heuristic_vs_random_finishes_with_a_winner(int seed)
    {
        var (outcome, _, state) = await Play(seed, new HeuristicAi(seed), new RandomAi(seed + 100));
        Assert.Contains(outcome.Winner, new int?[] { 0, 1, null });
        Assert.True(outcome.RoundsWon0 == 3 || outcome.RoundsWon1 == 3 || outcome.Reason != "First to 3 rounds");
        AssertCardConservation(state);
    }

    [Fact]
    public async Task Soak_random_vs_random_200_games()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var (outcome, _, state) = await Play(seed, new RandomAi(seed), new RandomAi(seed + 1000));
            Assert.True(outcome.RoundsPlayed > 0);
            AssertCardConservation(state);
        }
    }

    [Theory]
    [InlineData(EmptyDeckRule.Reshuffle)]
    [InlineData(EmptyDeckRule.EndAfterRound)]
    [InlineData(EmptyDeckRule.NoRefill)]
    public async Task Empty_deck_rules_terminate(EmptyDeckRule rule)
    {
        // A tiny pool forces the deck to run out quickly.
        var (outcome, _, _) = await Play(9, new RandomAi(1), new RandomAi(2), o =>
        {
            o.EmptyDeck = rule;
            o.RoundsToWin = 9;
            foreach (var k in o.CopyCounts.Keys.ToList()) o.CopyCounts[k] = 0;
            foreach (var d in TestData.Db.All.Where(d => d.UnitClass == UnitClass.Brute)) o.CopyCounts[d.Id] = 3;
            foreach (var d in TestData.Db.All.Where(d => d.IsOrder).Take(10)) o.CopyCounts[d.Id] = 1;
        });
        Assert.True(outcome.RoundsPlayed > 0);
    }

    [Fact]
    public async Task Random_draw_mode_plays()
    {
        var (outcome, _, state) = await Play(11, new HeuristicAi(1), new HeuristicAi(2), o => o.DeckMode = DeckMode.RandomDraw);
        Assert.Empty(state.UnitDeck);
        Assert.True(outcome.RoundsPlayed >= 3);
    }

    [Fact]
    public async Task Same_seed_replays_identically()
    {
        static async Task<List<string>> Run()
        {
            var (_, sink, _) = await Play(77, new HeuristicAi(5), new RandomAi(6));
            return sink.Events.Select(Describe).ToList();
        }
        Assert.Equal(await Run(), await Run());
    }

    [Fact]
    public async Task Round_winner_goes_first_next_round()
    {
        var (_, sink, _) = await Play(3, new HeuristicAi(1), new HeuristicAi(2));
        int? lastWinner = null;
        int? lastFirst = null;
        foreach (var e in sink.Events)
        {
            if (e is RoundStartedEvent rs)
            {
                if (rs.Round > 1)
                    Assert.Equal(lastWinner ?? lastFirst, rs.FirstPlayer);
                lastFirst = rs.FirstPlayer;
            }
            if (e is RoundScoredEvent sc) lastWinner = sc.Result.Winner;
        }
    }

    [Fact]
    public async Task Clean_up_retires_scoring_units_only()
    {
        var (_, sink, state) = await Play(21, new HeuristicAi(1), new RandomAi(2));
        Assert.All(state.Players, p => Assert.All(p.ScoringNodes, n => Assert.Null(n.Card)));
        Assert.Contains(sink.Events, e => e is CardMovedToPileEvent { Pile: PileKind.RetiredUnits });
    }

    [Fact]
    public async Task Warzones_reveal_each_round_and_cycle_to_the_bottom()
    {
        var (outcome, sink, state) = await Play(5, new HeuristicAi(1), new RandomAi(2), o => o.Warzones = true);
        int reveals = sink.Events.OfType<WarzoneRevealedEvent>().Count();
        int returns = sink.Events.OfType<WarzoneReturnedEvent>().Count();
        Assert.Equal(outcome.RoundsPlayed, reveals);
        Assert.Equal(reveals, returns);
        Assert.Empty(state.ActiveWarzones);
        Assert.Equal(35, state.WarzoneDeck.Count);

        // The first returned Warzone ends up at the bottom.
        var firstReturned = sink.Events.OfType<WarzoneReturnedEvent>().First().Warzone;
        if (outcome.RoundsPlayed == 1) Assert.Same(firstReturned, state.WarzoneDeck[0]);
    }

    private static string Describe(GameEvent e) => e switch
    {
        CardDrawnEvent d => $"draw {d.Player} {d.Card.Definition.Id}",
        UnitPlayedEvent p => $"play {p.Player} {p.Card.Definition.Id} {p.Node.Id}",
        RoundScoredEvent r => $"score {r.Result.Total0}-{r.Result.Total1} {r.Result.Winner}",
        _ => e.GetType().Name,
    };

    /// <summary>Every card instance is in exactly one place and nothing is lost.</summary>
    private static void AssertCardConservation(GameState s)
    {
        if (s.Options.DeckMode == DeckMode.RandomDraw) return;
        int units = s.UnitDeck.Count + s.RetiredUnits.Count + s.NullifiedUnits.Count
                    + s.Players.Sum(p => p.HandUnitCount + p.AllNodes.Count(n => n.Card is not null && n.Card.Definition.IsUnit));
        int orders = s.OrderDeck.Count + s.IssuedOrders.Count
                     + s.Players.Sum(p => p.HandOrderCount + p.OrderNodes.Count(n => n.Card is not null));
        Assert.Equal(150, units);
        Assert.Equal(80, orders);
    }

    private sealed class StopGame : Exception { }

    /// <summary>Stops the game at the first decision, so tests can inspect the state after Start-up.</summary>
    private sealed class ProbeController : IPlayerController
    {
        public Task<GameAction> ChooseActionAsync(DecisionContext context, CancellationToken ct) => throw new StopGame();
        public Task<GameAction?> ChooseResponseAsync(DecisionContext context, CancellationToken ct) => throw new StopGame();
    }
}
