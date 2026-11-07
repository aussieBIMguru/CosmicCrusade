using System.Diagnostics;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Ai;

/// <summary>Setup-screen difficulty (M4).</summary>
public enum AiDifficulty
{
    /// <summary>The rule-of-thumb bot (<see cref="HeuristicAi"/>).</summary>
    Easy,
    /// <summary>Monte Carlo search with a small budget.</summary>
    Normal,
    /// <summary>Monte Carlo search with a larger budget.</summary>
    Hard,
}

/// <summary>Search budget and scoring knobs for <see cref="MonteCarloAi"/>.</summary>
public sealed record MonteCarloSettings
{
    /// <summary>Wall-clock budget per Main Phase decision. 0 = no time limit (tests: only <see cref="MaxRollouts"/> counts).</summary>
    public int TimeBudgetMs { get; init; } = 1200;

    /// <summary>Hard cap on simulations per decision.</summary>
    public int MaxRollouts { get; init; } = 600;

    /// <summary>Every candidate gets at least this many simulations before UCB starts choosing (budget permitting).</summary>
    public int MinRolloutsPerAction { get; init; } = 3;

    /// <summary>UCB1 exploration constant (values are roughly 0..1).</summary>
    public double Exploration { get; init; } = 0.6;

    /// <summary>
    /// Value of one card kept (in hand or Standby) relative to the opponent, in round-wins. Hands carry over between
    /// rounds, so a round won with fewer cards is better. Ignored when the round can decide the game.
    /// </summary>
    public double CardValue { get; init; } = 0.05;

    /// <summary>Chance per turn that the rollout bots issue a random Action/Order (varies the simulations).</summary>
    public double RolloutOrderChance { get; init; } = 0.15;

    /// <summary>Run the search on a thread-pool thread so the UI stays responsive.</summary>
    public bool Background { get; init; } = true;

    public static MonteCarloSettings For(AiDifficulty d) => d switch
    {
        AiDifficulty.Hard => new MonteCarloSettings { TimeBudgetMs = 2500, MaxRollouts = 2000, MinRolloutsPerAction = 4 },
        _ => new MonteCarloSettings { TimeBudgetMs = 900, MaxRollouts = 500, MinRolloutsPerAction = 2 },
    };
}

/// <summary>What the last search found (for logs, tests and the benchmark).</summary>
public sealed record SearchReport(int Rollouts, int Failures, long ElapsedMs, IReadOnlyList<(string Action, int Visits, double Mean)> Candidates);

/// <summary>
/// M4: flat Monte Carlo search (UCB1 over the legal Main Phase actions). For each simulation it clones the state,
/// re-deals what it can't see (<see cref="Determinizer"/>), plays the candidate action, lets two
/// <see cref="HeuristicAi"/>s play out the rest of the round (<see cref="GameEngine.PlayOutRoundAsync"/>), and
/// scores the result: round won / tied / lost, plus a small card-economy term.
/// Priority windows and ability choices still use the heuristic (<see cref="HeuristicAi"/>, <see cref="AiChoices"/>).
/// Plays fair: it only uses what its seat can see.
/// </summary>
public sealed class MonteCarloAi : IPlayerController
{
    private readonly HeuristicAi _heuristic;
    private readonly Random _rng;

    public MonteCarloAi(int seed = 12345, MonteCarloSettings? settings = null)
    {
        _rng = new Random(seed);
        _heuristic = new HeuristicAi(seed ^ 0x3C3C);
        Settings = settings ?? new MonteCarloSettings();
    }

    public MonteCarloSettings Settings { get; }

    public SearchReport? LastReport { get; private set; }

    /// <summary>Easy → <see cref="HeuristicAi"/>; Normal/Hard → <see cref="MonteCarloAi"/>.</summary>
    public static IPlayerController Create(AiDifficulty difficulty, int seed) => difficulty == AiDifficulty.Easy
        ? new HeuristicAi(seed)
        : new MonteCarloAi(seed, MonteCarloSettings.For(difficulty));

    public async Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct)
    {
        var fallback = await _heuristic.ChooseActionAsync(ctx, ct);
        if (ctx.IsResponse || ctx.ExtraAction || ctx.Legal.Count <= 1 || ctx.State.Phase != GamePhase.Main) return fallback;

        // Clone on the calling thread; the search only ever touches clones.
        var root = ctx.State.Clone(_rng.Next());
        // The heuristic's own pick and Pass are simulated first, so a tight budget still compares against them.
        string preferred = ActionKey.Of(fallback);
        var keys = ctx.Legal.Select(ActionKey.Of)
            .OrderBy(key => key == preferred ? 0 : key == "Pass" ? 1 : 2)
            .ToList();
        int seed = _rng.Next();
        int me = ctx.Player;
        bool oppPassed = ctx.OpponentJustPassed;

        var report = Settings.Background
            ? await Task.Run(() => SearchAsync(root, me, oppPassed, keys, preferred, seed, ct), ct)
            : await SearchAsync(root, me, oppPassed, keys, preferred, seed, ct);
        LastReport = report;

        // Only trust candidates that got their minimum number of simulations (a slow machine may not reach them all).
        int min = Math.Max(1, Settings.MinRolloutsPerAction);
        var best = report.Candidates.Where(c => c.Visits >= min)
            .OrderByDescending(c => c.Mean).ThenByDescending(c => c.Visits).FirstOrDefault();
        if (best.Action is null) return fallback;
        return ctx.Legal.FirstOrDefault(a => ActionKey.Of(a) == best.Action) ?? fallback;
    }

    public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct) =>
        _heuristic.ChooseResponseAsync(ctx, ct);

    public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct) =>
        Task.FromResult(AiChoices.Choose(request, state));

    // ------------------------------------------------------------------ search

    private async Task<SearchReport> SearchAsync(GameState root, int me, bool oppPassed, IReadOnlyList<string> keys,
        string preferred, int seed, CancellationToken ct)
    {
        var rng = new Random(seed);
        var sw = Stopwatch.StartNew();
        int k = keys.Count;
        var visits = new int[k];
        var sums = new double[k];
        var fails = new int[k];
        var excluded = new bool[k];
        int total = 0, failures = 0;

        bool OutOfBudget() =>
            total + failures >= Settings.MaxRollouts
            || (Settings.TimeBudgetMs > 0 && sw.ElapsedMilliseconds >= Settings.TimeBudgetMs);

        while (!OutOfBudget())
        {
            ct.ThrowIfCancellationRequested();
            int i = Select(visits, sums, excluded, total);
            if (i < 0) break;

            double? value;
            try
            {
                value = await RolloutAsync(root, me, oppPassed, keys[i], rng, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                value = null; // A simulation the engine couldn't finish (runaway loop guard etc.): ignore it.
            }

            if (value is double v)
            {
                visits[i]++;
                sums[i] += v + (keys[i] == preferred ? 0.01 : 0); // The heuristic's pick wins near-ties.
                total++;
            }
            else
            {
                failures++;
                if (++fails[i] >= 3 && visits[i] == 0) excluded[i] = true;
            }
        }

        var cands = Enumerable.Range(0, k)
            .Select(i => (Action: keys[i], Visits: visits[i], Mean: visits[i] == 0 ? double.NaN : sums[i] / visits[i]))
            .ToList();
        return new SearchReport(total, failures, sw.ElapsedMilliseconds, cands);
    }

    /// <summary>Round-robin until every candidate has its minimum, then UCB1.</summary>
    private int Select(int[] visits, double[] sums, bool[] excluded, int total)
    {
        int best = -1;
        int fewest = int.MaxValue;
        for (int i = 0; i < visits.Length; i++)
            if (!excluded[i] && visits[i] < Settings.MinRolloutsPerAction && visits[i] < fewest)
            {
                fewest = visits[i];
                best = i;
            }
        if (best >= 0) return best;

        double bestScore = double.NegativeInfinity;
        double logN = Math.Log(Math.Max(1, total));
        for (int i = 0; i < visits.Length; i++)
        {
            if (excluded[i]) continue;
            double score = visits[i] == 0
                ? double.PositiveInfinity
                : sums[i] / visits[i] + Settings.Exploration * Math.Sqrt(logN / visits[i]);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    /// <summary>One simulation. Null when the candidate couldn't be played in this world.</summary>
    private async Task<double?> RolloutAsync(GameState root, int me, bool oppPassed, string key, Random rng, CancellationToken ct)
    {
        var sim = root.Clone(rng.Next());
        Determinizer.Determinize(sim, me, rng);

        int opp = GameState.Other(me);
        var mine = new HeuristicAi(rng.Next()) { FillerOrderChance = Settings.RolloutOrderChance };
        var theirs = new HeuristicAi(rng.Next()) { FillerOrderChance = Settings.RolloutOrderChance };
        var first = new FirstMoveController(key, mine);
        var controllers = new IPlayerController[2];
        controllers[me] = first;
        controllers[opp] = theirs;

        int myBefore = Resources(sim, me), oppBefore = Resources(sim, opp);
        var engine = new GameEngine(sim, controllers[0], controllers[1]) { MaxTurnsPerRound = 200 };
        var result = await engine.PlayOutRoundAsync(me, oppPassed, ct);
        if (!first.Played) return null;

        double outcome = result.Winner == me ? 1 : result.Winner is null ? 0.5 : 0;
        int target = sim.Options.RoundsToWin;
        bool decisive = root.Players[me].RoundsWon + 1 >= target || root.Players[opp].RoundsWon + 1 >= target;
        if (decisive) return outcome;

        int myDelta = Resources(sim, me) - myBefore;
        int oppDelta = Resources(sim, opp) - oppBefore;
        return outcome + Settings.CardValue * (myDelta - oppDelta);
    }

    /// <summary>Cards a player keeps for later rounds: their hand plus Units waiting in Standby.</summary>
    private static int Resources(GameState s, int player) =>
        s.Players[player].Hand.Count + s.Players[player].StandbyNodes.Count(n => n.Card is not null);

    /// <summary>Plays the candidate action on the first turn of the simulation, then hands over to the rollout bot.</summary>
    private sealed class FirstMoveController(string key, IPlayerController next) : IPlayerController
    {
        public bool Played { get; private set; }

        public Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct)
        {
            if (Played) return next.ChooseActionAsync(ctx, ct);
            Played = true;
            var action = ctx.Legal.FirstOrDefault(a => ActionKey.Of(a) == key)
                         ?? throw new InvalidOperationException($"Candidate {key} is not legal in this simulation.");
            return Task.FromResult(action);
        }

        public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct) => next.ChooseResponseAsync(ctx, ct);

        public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct) =>
            next.ChooseAsync(request, state, ct);
    }
}

/// <summary>
/// A state-independent name for a Main Phase action (card instance ids and node ids), so an action can be found
/// again in a cloned state.
/// </summary>
public static class ActionKey
{
    public static string Of(GameAction a) => a switch
    {
        PassAction => "Pass",
        DeployFromHandAction d => $"Deploy:{d.Card.InstanceId}:{d.Target.Id}",
        AdvanceFromStandbyAction s => $"Advance:{s.From.Id}:{s.To.Id}",
        IssueOrderAction o => $"Issue:{o.Card.InstanceId}:{o.OrderNode?.Id}",
        DeployFromPileAction p => $"Pile:{p.Pile}",
        DeployAtRandomAction r => $"Random:{r.Card.InstanceId}",
        CoercedDeployAction => "Coerced",
        GrantedAction g => $"Granted:{g.Source.InstanceId}:{g.Name}",
        AssembleAction x => $"Assemble:{x.Card.InstanceId}:{x.Target.InstanceId}",
        ReactAction r => $"React:{r.Card.InstanceId}",
        _ => a.Describe(),
    };
}
