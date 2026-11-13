using System.Diagnostics;
using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;
using Xunit.Abstractions;

namespace CosmicCrusade.Core.Tests;

/// <summary>M4: state cloning, playing out a round from a decision, determinization and the Monte Carlo AI.</summary>
public class M4Tests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ helpers

    private sealed class Captured : Exception { }

    /// <summary>Plays heuristic vs heuristic and stops at the <paramref name="turn"/>-th decision someone is asked for.</summary>
    private sealed class CaptureAt(int turn, IPlayerController inner) : IPlayerController
    {
        public Counter Shared { get; init; } = new();

        public Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct)
        {
            if (++Shared.Value == turn)
            {
                Shared.Context = ctx;
                throw new Captured();
            }
            return inner.ChooseActionAsync(ctx, ct);
        }

        public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct) => inner.ChooseResponseAsync(ctx, ct);

        public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest r, GameState s, CancellationToken ct) => inner.ChooseAsync(r, s, ct);
    }

    /// <summary>Turn count shared by both seats, and the decision that was captured.</summary>
    private sealed class Counter
    {
        public int Value;
        public DecisionContext? Context;
    }

    /// <summary>A live game paused at a Main Phase decision (the state an AI sees when it is asked for a turn).</summary>
    private static async Task<(GameState State, DecisionContext Ctx)> PausedGame(int seed, int turn, Action<GameOptions>? configure = null)
    {
        var state = TestData.NewGame(seed, configure);
        var shared = new Counter();
        var p0 = new CaptureAt(turn, new HeuristicAi(seed)) { Shared = shared };
        var p1 = new CaptureAt(turn, new HeuristicAi(seed + 1)) { Shared = shared };
        DecisionContext? ctx = null;
        try
        {
            await new GameEngine(state, p0, p1).RunAsync();
        }
        catch (Captured)
        {
            ctx = shared.Context;
        }
        Assert.NotNull(ctx); // The game lasted long enough.
        return (state, ctx!);
    }

    /// <summary>Where every card is and what it is, as text (instance ids are kept by clones).</summary>
    private static List<string> Snapshot(GameState s)
    {
        var lines = new List<string>
        {
            $"round {s.Round} phase {s.Phase} first {s.FirstPlayer} won {s.Players[0].RoundsWon}-{s.Players[1].RoundsWon}",
        };
        foreach (var c in s.AllCards)
        {
            string where = c.Location.Kind switch
            {
                LocationKind.Node => c.Location.Node!.Id,
                LocationKind.Hand => $"hand{c.Location.Player}",
                LocationKind.Pile => c.Location.Pile!.ToString()!,
                _ => c.Location.Kind.ToString(),
            };
            string rank = c.Location.Node is { IsScoring: true } ? RankCalculator.GetRank(s, c).ToString() : "-";
            lines.Add($"{c.InstanceId} {c.Name} @{where} r{rank} [{string.Join(",", c.Races.OrderBy(r => r))}] mem{c.Memory.Count}");
        }
        foreach (var n in s.AllNodes.Where(n => n.IsLocked)) lines.Add($"locked {n.Id}");
        return lines;
    }

    private static void AssertConservation(GameState s)
    {
        if (s.Options.DeckMode == DeckMode.RandomDraw) return;
        Assert.Equal(150, s.AllCards.Count(c => c.PrintedDefinition.IsUnit));
        Assert.Equal(80, s.AllCards.Count(c => c.PrintedDefinition.IsOrder));
        Assert.Equal(s.AllCards.Count(), s.AllCards.Select(c => c.InstanceId).Distinct().Count());
    }

    private static MonteCarloAi FastMc(int seed, int rollouts = 40) =>
        new(seed, new MonteCarloSettings { TimeBudgetMs = 0, MaxRollouts = rollouts, MinRolloutsPerAction = 1, Background = false });

    // ------------------------------------------------------------------ clone

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 15)]
    [InlineData(3, 18)]
    public async Task Clone_is_an_exact_and_separate_copy(int seed, int turn)
    {
        var (state, _) = await PausedGame(seed, turn);
        var clone = state.Clone(99);

        Assert.Equal(Snapshot(state), Snapshot(clone));

        var originals = new HashSet<object>(state.AllCards, ReferenceEqualityComparer.Instance);
        Assert.DoesNotContain(clone.AllCards, c => originals.Contains(c));
        Assert.All(clone.AllNodes, n => Assert.DoesNotContain(n, state.AllNodes));
        Assert.All(clone.AllCards.Where(c => c.Location.Kind == LocationKind.Node),
            c => Assert.Same(c, c.Location.Node!.Card));
        AssertConservation(clone);

        // Changing the clone leaves the original alone.
        var before = Snapshot(state);
        foreach (var c in clone.Players[0].Hand.ToList()) clone.MoveToPile(c, PileKind.RetiredUnits);
        foreach (var n in clone.AllNodes) n.IsLocked = true;
        Assert.Equal(before, Snapshot(state));
    }

    [Theory]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    public async Task Clones_with_the_same_seed_play_out_identically(int seed, int turn)
    {
        var (state, ctx) = await PausedGame(seed, turn);

        async Task<(RoundResult Result, List<string> Board)> PlayOut()
        {
            var sim = state.Clone(1234);
            var engine = new GameEngine(sim, new HeuristicAi(7), new HeuristicAi(8));
            var result = await engine.PlayOutRoundAsync(ctx.Player, ctx.OpponentJustPassed);
            return (result, Snapshot(sim));
        }

        var a = await PlayOut();
        var b = await PlayOut();
        Assert.Equal(a.Result.Total0, b.Result.Total0);
        Assert.Equal(a.Result.Total1, b.Result.Total1);
        Assert.Equal(a.Result.Winner, b.Result.Winner);
        Assert.Equal(a.Board, b.Board);
    }

    [Fact]
    public async Task Play_out_round_scores_the_round_and_stops_before_clean_up()
    {
        var (state, ctx) = await PausedGame(6, 10);
        var sim = state.Clone(5);
        int round = sim.Round;
        var result = await new GameEngine(sim, new HeuristicAi(1), new HeuristicAi(2)).PlayOutRoundAsync(ctx.Player, ctx.OpponentJustPassed);

        Assert.Equal(round, sim.Round);
        Assert.Equal(GamePhase.EndOfMain, sim.Phase);
        Assert.Equal(RankCalculator.TotalRank(sim, 0), result.Total0);
        Assert.Equal(RankCalculator.TotalRank(sim, 1), result.Total1);
        AssertConservation(sim);
    }

    [Fact]
    public async Task Play_out_round_needs_a_main_phase_turn()
    {
        var state = TestData.NewGame(1); // Not started.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GameEngine(state, new HeuristicAi(1), new HeuristicAi(2)).PlayOutRoundAsync(0, false));
    }

    // ------------------------------------------------------------------ determinization

    [Theory]
    [InlineData(7, 5)]
    [InlineData(8, 12)]
    public async Task Determinize_redeals_only_what_the_viewer_cannot_see(int seed, int turn)
    {
        var (state, ctx) = await PausedGame(seed, turn);
        int me = ctx.Player, opp = GameState.Other(me);
        var realOppHand = state.Players[opp].Hand.Select(c => c.InstanceId).OrderBy(i => i).ToList();
        bool changed = false;

        for (int i = 0; i < 10; i++)
        {
            var sim = state.Clone(i);
            Determinizer.Determinize(sim, me, new Random(i));

            // My hand and the board are untouched; the opponent keeps the same number of Units and Orders.
            Assert.Equal(state.Players[me].Hand.Select(c => c.InstanceId), sim.Players[me].Hand.Select(c => c.InstanceId));
            Assert.Equal(state.Players[opp].HandUnitCount, sim.Players[opp].HandUnitCount);
            Assert.Equal(state.Players[opp].HandOrderCount, sim.Players[opp].HandOrderCount);
            Assert.Equal(Snapshot(state).Where(l => l.Contains("@P")), Snapshot(sim).Where(l => l.Contains("@P")));
            Assert.Equal(state.NullifiedUnits.Count, sim.NullifiedUnits.Count);
            Assert.Equal(state.RetiredUnits.Count, sim.RetiredUnits.Count);
            AssertConservation(sim);

            // Their hand now only holds cards that were in their hand or a deck.
            var unseen = state.Players[opp].Hand.Concat(state.UnitDeck).Concat(state.OrderDeck).Select(c => c.InstanceId).ToHashSet();
            Assert.All(sim.Players[opp].Hand, c => Assert.Contains(c.InstanceId, unseen));

            if (!sim.Players[opp].Hand.Select(c => c.InstanceId).OrderBy(x => x).SequenceEqual(realOppHand)) changed = true;
        }
        Assert.True(changed, "The opponent's hidden hand should be re-dealt.");
    }

    [Fact]
    public async Task Determinize_in_random_draw_mode_keeps_hand_sizes()
    {
        var (state, ctx) = await PausedGame(9, 6, o => o.DeckMode = DeckMode.RandomDraw);
        int opp = GameState.Other(ctx.Player);
        var sim = state.Clone(3);
        Determinizer.Determinize(sim, ctx.Player, new Random(3));
        Assert.Equal(state.Players[opp].HandUnitCount, sim.Players[opp].HandUnitCount);
        Assert.Equal(state.Players[opp].HandOrderCount, sim.Players[opp].HandOrderCount);
    }

    // ------------------------------------------------------------------ Monte Carlo AI

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, false, true)]
    public async Task Monte_carlo_plays_full_games(int seed, bool warzones, bool noLeaders)
    {
        var state = TestData.NewGame(seed, o =>
        {
            o.Warzones = warzones;
            o.Leaders = !noLeaders;
        });
        var mc = FastMc(seed);
        var outcome = await new GameEngine(state, mc, new HeuristicAi(seed + 50)).RunAsync();

        Assert.True(outcome.RoundsPlayed > 0);
        if (!warzones && !noLeaders) AssertConservation(state);
        Assert.NotNull(mc.LastReport);
        Assert.True(mc.LastReport!.Rollouts > 0);
    }

    [Fact]
    public async Task Monte_carlo_with_a_rollout_budget_is_deterministic()
    {
        static async Task<List<string>> Run()
        {
            var state = TestData.NewGame(31);
            var sink = new RecordingEventSink();
            await new GameEngine(state, FastMc(5, 25), new HeuristicAi(6), sink).RunAsync();
            return sink.Events.OfType<RoundScoredEvent>().Select(e => $"{e.Result.Round}:{e.Result.Total0}-{e.Result.Total1}:{e.Result.Winner}").ToList();
        }
        Assert.Equal(await Run(), await Run());
    }

    [Fact]
    public async Task Monte_carlo_leaves_the_live_state_untouched_while_searching()
    {
        var (state, ctx) = await PausedGame(12, 9);
        var before = Snapshot(state);
        var mc = FastMc(1, 60);
        var action = await mc.ChooseActionAsync(ctx, CancellationToken.None);

        Assert.Contains(action, ctx.Legal);
        Assert.Equal(before, Snapshot(state));
        Assert.True(mc.LastReport!.Rollouts > 0);
    }

    [Fact]
    public async Task Monte_carlo_takes_a_winning_play_when_one_exists()
    {
        // Behind 0–4 with a Brute in hand and an empty Unit Node opposite theirs: deploying wins (or ties) the round;
        // passing after they passed loses it.
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        var brute = TestData.Brute(Race.Blaarg);
        TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[0].UnitNodes[0]);
        var mine = s.CreateInstance(brute);
        s.MoveToHand(mine, 1);
        s.Phase = GamePhase.Main;
        s.Round = 1;

        var legal = LegalActions.ForTurn(s, 1);
        var ctx = new DecisionContext(s, 1, legal, OpponentJustPassed: true);
        var mc = FastMc(2, 30);
        var action = await mc.ChooseActionAsync(ctx, CancellationToken.None);

        Assert.IsType<DeployFromHandAction>(action);
        output.WriteLine(string.Join("\n", mc.LastReport!.Candidates.Select(c => $"{c.Action}: {c.Visits} × {c.Mean:0.00}")));
    }

    [Fact]
    public void Action_keys_match_between_clones()
    {
        var s = TestData.EmptyGame();
        var u = s.CreateInstance(TestData.Brute(Race.Blaarg));
        s.MoveToHand(u, 0);
        s.Phase = GamePhase.Main;
        var clone = s.Clone(1);
        var a = LegalActions.ForTurn(s, 0).Select(ActionKey.Of).ToList();
        var b = LegalActions.ForTurn(clone, 0).Select(ActionKey.Of).ToList();
        Assert.Equal(a, b);
        Assert.Equal(a.Count, a.Distinct().Count());
    }

    // ------------------------------------------------------------------ benchmark (manual)

    /// <summary>
    /// AI-vs-AI benchmark: Monte Carlo (Normal settings) against the heuristic over many seeded games, alternating
    /// seats. Slow, so it only runs when the COSMIC_BENCH environment variable is set (e.g. COSMIC_BENCH=40 games).
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task Benchmark_monte_carlo_vs_heuristic()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("COSMIC_BENCH"), out int games) || games <= 0) return;

        int mcWins = 0, heuristicWins = 0, draws = 0, decisions = 0;
        long searchMs = 0;
        var sw = Stopwatch.StartNew();
        for (int g = 0; g < games; g++)
        {
            var state = TestData.NewGame(1000 + g);
            var mc = new MonteCarloAi(g, MonteCarloSettings.For(AiDifficulty.Normal) with { Background = false });
            var counting = new CountingController(mc);
            int mcSeat = g % 2;
            var p0 = mcSeat == 0 ? (IPlayerController)counting : new HeuristicAi(g + 7);
            var p1 = mcSeat == 1 ? (IPlayerController)counting : new HeuristicAi(g + 7);
            var outcome = await new GameEngine(state, p0, p1).RunAsync();
            if (outcome.Winner == mcSeat) mcWins++;
            else if (outcome.Winner is null) draws++;
            else heuristicWins++;
            decisions += counting.Decisions;
            searchMs += counting.Ms;
        }
        output.WriteLine($"{games} games in {sw.Elapsed}: Monte Carlo {mcWins}, Heuristic {heuristicWins}, draws {draws}. " +
                         $"MC win rate {100.0 * mcWins / games:0.0}%. Avg {(decisions == 0 ? 0 : searchMs / decisions)} ms per Main Phase decision.");
    }

    private sealed class CountingController(IPlayerController inner) : IPlayerController
    {
        public int Decisions;
        public long Ms;

        public async Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var a = await inner.ChooseActionAsync(ctx, ct);
            Decisions++;
            Ms += sw.ElapsedMilliseconds;
            return a;
        }

        public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct) => inner.ChooseResponseAsync(ctx, ct);

        public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest r, GameState s, CancellationToken ct) => inner.ChooseAsync(r, s, ct);
    }
}
