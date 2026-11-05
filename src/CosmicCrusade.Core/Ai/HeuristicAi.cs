using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Ai;

/// <summary>
/// First-pass bot. Hands carry over between rounds, so it tries to win each round by the smallest margin and
/// concedes rounds it cannot catch up in. Only reads its own hand and the public board (no peeking).
/// MCTS replaces this later (see README).
/// </summary>
public sealed class HeuristicAi : IPlayerController
{
    private readonly Random _rng;

    public HeuristicAi(int seed = 12345) => _rng = new Random(seed);

    public Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct) =>
        Task.FromResult(Choose(ctx));

    public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct) =>
        Task.FromResult(Respond(ctx));

    public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest request, GameState state, CancellationToken ct) =>
        Task.FromResult(AiChoices.Choose(request, state));

    /// <summary>
    /// M2 only: chance of issuing an (effect-less) Order instead of passing while the opponent hasn't passed.
    /// Orders don't do anything until the effect interpreter (M3), so this mostly exercises the UI. Set 0 to disable.
    /// </summary>
    public double FillerOrderChance { get; set; } = 0.25;

    // ------------------------------------------------------------------ priority windows

    /// <summary>
    /// Only counter-Reacts do something in M2. Counter an opposing deploy that matters, or counter a React
    /// that is countering one of our own items. Never responds to our own items.
    /// </summary>
    private GameAction? Respond(DecisionContext ctx)
    {
        var state = ctx.State;
        int me = ctx.Player;
        int opp = GameState.Other(me);
        int myTotal = RankCalculator.TotalRank(state, me);
        int oppTotal = RankCalculator.TotalRank(state, opp);

        foreach (var r in ctx.Legal.OfType<ReactAction>())
        {
            if (!ReactCounters.IsCounter(r.Card.Definition)) continue;
            var victim = r.RespondingTo;
            if (victim.Controller == me) continue;

            switch (victim.Kind)
            {
                case StackItemKind.DeployUnit:
                    int rank = RankCalculator.BaseRank(victim.Card);
                    bool scoresForOpp = victim.TargetNode?.Side == opp;
                    if (!scoresForOpp) continue;
                    if (rank >= 3 || (oppTotal <= myTotal && oppTotal + rank > myTotal)) return r;
                    break;

                case StackItemKind.IssueOrder when victim.IsReact && victim.RespondingTo?.Controller == me:
                    return r; // Protect our own play.
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ turns

    private GameAction Choose(DecisionContext ctx)
    {
        var action = ChooseCore(ctx);
        if (action is not PassAction) return action;

        var orders = ctx.Legal.OfType<IssueOrderAction>().ToList();
        if (orders.Count == 0) return action;

        // A live Ongoing (Cybernetic Enhancements) is worth issuing when we hold Synthenoid units.
        var hand = ctx.State.Players[ctx.Player].Hand;
        var useful = orders.FirstOrDefault(o => o.OrderNode is not null
                                                && AssembleRules.GrantsAssemble(o.Card.Definition)
                                                && hand.Any(c => c.Definition.IsUnit && c.Races.Contains(Race.Synthenoid)));
        if (useful is not null) return useful;

        if (!ctx.OpponentJustPassed && _rng.NextDouble() < FillerOrderChance)
            return orders[_rng.Next(orders.Count)];
        return action;
    }

    private GameAction ChooseCore(DecisionContext ctx)
    {
        var state = ctx.State;
        int me = ctx.Player;
        int opp = GameState.Other(me);
        var pass = ctx.Legal.OfType<PassAction>().First();

        int myTotal = RankCalculator.TotalRank(state, me);
        int oppTotal = RankCalculator.TotalRank(state, opp);

        // Units score for the side they sit on, so only consider our own nodes for now
        // (cross-side plays such as Visiting Diplomat matter once abilities exist – M3).
        var plays = ctx.Legal
            .Where(a => a is DeployFromHandAction or AdvanceFromStandbyAction or AssembleAction)
            .Select(a => new Candidate(a, RankOf(state, a), Target(a)))
            .Where(c => c.Target.Side == me)
            .Where(c => !LosesItsOwnDuel(state, c))
            .ToList();
        if (plays.Count == 0) return pass;

        // Ahead: hold cards. If the opponent passes too, we take the round.
        if (myTotal > oppTotal) return pass;

        int needed = oppTotal - myTotal + 1;

        // Can we catch up at all with the empty nodes we have left? If not, save the cards.
        int emptyNodes = state.Players[me].ScoringNodes.Count(n => n.IsEmpty);
        int bestPossible = plays
            .Where(c => c.Action is not AssembleAction)
            .GroupBy(c => CardOf(c.Action))
            .Select(g => g.Max(c => c.Rank))
            .OrderByDescending(r => r)
            .Take(emptyNodes)
            .Sum()
            + plays.OfType<Candidate>().Where(c => c.Action is AssembleAction)
                .GroupBy(c => CardOf(c.Action)).Sum(g => g.Max(c => c.Rank));
        if (bestPossible < needed && myTotal < oppTotal)
            return pass;

        // Prefer the cheapest single play that takes the lead; break ties by winning an opposed pair.
        var winning = plays.Where(c => c.Rank >= needed).ToList();
        if (winning.Count > 0)
        {
            return winning
                .OrderBy(c => c.Rank)
                .ThenByDescending(c => WinsPair(state, c))
                .ThenBy(c => c.Target.Kind == NodeKind.Leader ? 0 : 1) // Fill the Leader Node when we can.
                .ThenBy(_ => _rng.Next())
                .First().Action;
        }

        // Otherwise push our biggest unit and keep going next turn.
        return plays
            .OrderByDescending(c => c.Rank)
            .ThenByDescending(c => WinsPair(state, c))
            .ThenBy(_ => _rng.Next())
            .First().Action;
    }

    private sealed record Candidate(GameAction Action, int Rank, Node Target);

    /// <summary>
    /// Steady Shooter / Crimson Redeemer Duel the opposing Unit when played. Don't deploy one where it would lose
    /// (Crimson Redeemer can't be Nullified in that Duel, so it is always safe).
    /// </summary>
    private static bool LosesItsOwnDuel(GameState state, Candidate c)
    {
        if (c.Action is AssembleAction) return false;
        var card = CardOf(c.Action);
        var def = card.Definition;
        if (def.Effects.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
        string raw = def.Effects.GetRawText();
        if (!raw.Contains("\"Duel\"") || !raw.Contains("\"OnPlay\"") || raw.Contains("\"protect\"")) return false;
        var opposing = state.OpposingNode(c.Target)?.Card;
        return opposing is not null && Duel.Loser(state, card, opposing) == card;
    }

    private static CardInstance CardOf(GameAction a) => a switch
    {
        DeployFromHandAction d => d.Card,
        AdvanceFromStandbyAction s => s.From.Card!,
        AssembleAction x => x.Card,
        _ => throw new ArgumentException(nameof(a)),
    };

    private static Node Target(GameAction a) => a switch
    {
        DeployFromHandAction d => d.Target,
        AdvanceFromStandbyAction s => s.To,
        AssembleAction x => x.Target.Location.Node!,
        _ => throw new ArgumentException(nameof(a)),
    };

    /// <summary>Rank the action adds to our total (Assemble adds the card's printed Rank to an existing unit).</summary>
    private static int RankOf(GameState state, GameAction a) => a is AssembleAction x
        ? AssembleRules.AssembledRank(x.Card)
        : RankCalculator.GetRank(state, CardOf(a));

    private static bool WinsPair(GameState state, Candidate c)
    {
        var opposing = state.OpposingNode(c.Target);
        int mine = c.Action is AssembleAction x ? RankCalculator.GetRank(state, x.Target) + c.Rank : c.Rank;
        if (opposing?.Card is null) return c.Action is not AssembleAction; // Assembling onto an unopposed unit wins nothing new.
        return mine > RankCalculator.GetRank(state, opposing.Card);
    }
}

/// <summary>Plays random legal moves (passes rarely). For soak tests.</summary>
public sealed class RandomAi : IPlayerController
{
    private readonly Random _rng;
    private readonly double _passChance;

    public RandomAi(int seed, double passChance = 0.2)
    {
        _rng = new Random(seed);
        _passChance = passChance;
    }

    public Task<GameAction> ChooseActionAsync(DecisionContext ctx, CancellationToken ct)
    {
        var nonPass = ctx.Legal.Where(a => a is not PassAction).ToList();
        if (nonPass.Count == 0 || _rng.NextDouble() < _passChance)
            return Task.FromResult(ctx.Legal.First(a => a is PassAction));
        return Task.FromResult(nonPass[_rng.Next(nonPass.Count)]);
    }

    /// <summary>Random legal answer to an ability choice.</summary>
    public Task<IReadOnlyList<int>> ChooseAsync(ChoiceRequest r, GameState state, CancellationToken ct)
    {
        int n = _rng.Next(r.Min, r.Max + 1);
        var picks = Enumerable.Range(0, r.Options.Count).OrderBy(_ => _rng.Next()).Take(n).ToList();
        return Task.FromResult<IReadOnlyList<int>>(picks);
    }

    /// <summary>Responds with a random React half the time.</summary>
    public Task<GameAction?> ChooseResponseAsync(DecisionContext ctx, CancellationToken ct)
    {
        var reacts = ctx.Legal.OfType<ReactAction>().ToList();
        if (reacts.Count == 0 || _rng.NextDouble() < 0.5) return Task.FromResult<GameAction?>(null);
        return Task.FromResult<GameAction?>(reacts[_rng.Next(reacts.Count)]);
    }
}
