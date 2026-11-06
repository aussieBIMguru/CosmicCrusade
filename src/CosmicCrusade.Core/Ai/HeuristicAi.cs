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

        // M3.2b: one of our Units is about to be Nullified – save it (Already Dead), or take theirs with it
        // (Respond in Kind).
        var saves = ctx.Legal.OfType<ReactAction>().Where(r => r.RespondingTo.Kind == StackItemKind.UnitNullifying).ToList();
        foreach (var r in saves.OrderByDescending(x => x.Card.Definition.Effects.GetRawText().Contains("\"PreventEvent\"")))
        {
            var unit = r.RespondingTo.Card;
            if (unit.Location.Node?.Side != me) continue;
            string raw = r.Card.Definition.Effects.GetRawText();
            if (raw.Contains("\"PreventEvent\"")) return r;
            if (raw.Contains("Event.Unit.OpposingUnit") && unit.Location.Node is { } n && state.OpposingNode(n)?.Card is { } theirs
                && theirs.Location.Node!.Side == opp)
                return r;
        }

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
                                                && hand.Any(c => c.Definition.IsUnit && StaticLayer.HasRace(ctx.State, c, Race.Synthenoid)));
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
        // M3.2: deploys and Ongoing Orders are valued by how much they change the margin, statics included
        // (an anthem helps the rest of the board, Acid Rain hurts it). Ongoings only count when they help.
        var plays = ctx.Legal
            .Where(a => a is DeployFromHandAction or AdvanceFromStandbyAction or AssembleAction
                        or IssueOrderAction { OrderNode: not null })
            .Select(a => new Candidate(a, RankOf(state, a, me), Target(a)))
            .Concat(ctx.Legal.OfType<DeployFromPileAction>().Select(a => PileCandidate(state, a, me)).OfType<Candidate>())
            .Where(c => c.Target.Side == me)
            .Where(c => c.Action is not IssueOrderAction || c.Rank > 0)
            .Where(c => !LosesItsOwnDuel(state, c))
            .ToList();
        if (plays.Count == 0) return pass;

        // Ahead: hold cards. If the opponent passes too, we take the round.
        if (myTotal > oppTotal) return pass;

        int needed = oppTotal - myTotal + 1;

        // Can we catch up at all with the empty nodes we have left? If not, save the cards.
        int emptyNodes = state.Players[me].ScoringNodes.Count(n => n.IsEmpty);
        int bestPossible = plays
            .Where(c => c.Action is DeployFromHandAction or AdvanceFromStandbyAction or DeployFromPileAction)
            .GroupBy(c => c.PileCard ?? CardOf(c.Action))
            .Select(g => g.Max(c => c.Rank))
            .OrderByDescending(r => r)
            .Take(emptyNodes)
            .Sum()
            + plays.Where(c => c.Action is AssembleAction or IssueOrderAction)
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

    private sealed record Candidate(GameAction Action, int Rank, Node Target, CardInstance? PileCard = null);

    /// <summary>
    /// Barkeep Bill & co.: the AI's choices then take the biggest Unit and an open node (AiChoices "PlayFromPile" /
    /// "DeployNode"), so value the action as that Unit on that node.
    /// </summary>
    private static Candidate? PileCandidate(GameState state, DeployFromPileAction a, int me)
    {
        var card = StaticLayer.PlayableFromPile(state, me, a.Pile)
            .Where(c => LegalActions.DeployNodes(state, me, c).Count > 0)
            .OrderByDescending(RankCalculator.BaseRank).FirstOrDefault();
        if (card is null) return null;
        var nodes = LegalActions.DeployNodes(state, me, card).Where(n => n.Side == me).ToList();
        if (nodes.Count == 0) return null;
        var node = nodes.OrderBy(n => state.OpposingNode(n)?.Card is null ? 0 : 1).ThenBy(n => n.Kind == NodeKind.Leader ? 0 : 1).First();
        int opp = GameState.Other(me);
        int Margin() => RankCalculator.TotalRank(state, me) - RankCalculator.TotalRank(state, opp);
        int before = Margin();
        return new Candidate(a, state.WithTemporaryPlacement(card, node, Margin) - before, node, card);
    }

    /// <summary>
    /// Steady Shooter / Crimson Redeemer Duel the opposing Unit when played. Don't deploy one where it would lose
    /// (Crimson Redeemer can't be Nullified in that Duel, so it is always safe).
    /// </summary>
    private static bool LosesItsOwnDuel(GameState state, Candidate c)
    {
        if (c.Action is AssembleAction or IssueOrderAction) return false;
        var card = c.PileCard ?? CardOf(c.Action);
        var def = card.Definition;
        if (def.Effects.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
        string raw = def.Effects.GetRawText();
        if (!raw.Contains("\"Duel\"") || !raw.Contains("\"OnPlay\"") || raw.Contains("\"protect\"")) return false;
        var opposing = state.OpposingNode(c.Target)?.Card;
        return opposing is not null && state.WithTemporaryPlacement(card, c.Target, () => Duel.Losers(state, card, opposing).Contains(card));
    }

    private static CardInstance CardOf(GameAction a) => a switch
    {
        DeployFromHandAction d => d.Card,
        AdvanceFromStandbyAction s => s.From.Card!,
        AssembleAction x => x.Card,
        IssueOrderAction o => o.Card,
        _ => throw new ArgumentException(nameof(a)),
    };

    private static Node Target(GameAction a) => a switch
    {
        DeployFromHandAction d => d.Target,
        AdvanceFromStandbyAction s => s.To,
        AssembleAction x => x.Target.Location.Node!,
        IssueOrderAction o => o.OrderNode!,
        _ => throw new ArgumentException(nameof(a)),
    };

    /// <summary>
    /// How much the action improves our margin (our total minus theirs). Assemble adds the card's printed Rank to an
    /// existing unit; deploys and Ongoings are tried on the board so static effects count.
    /// </summary>
    private static int RankOf(GameState state, GameAction a, int me)
    {
        if (a is AssembleAction x) return AssembleRules.AssembledRank(x.Card);
        int opp = GameState.Other(me);
        int Margin() => RankCalculator.TotalRank(state, me) - RankCalculator.TotalRank(state, opp);
        int before = Margin();
        return state.WithTemporaryPlacement(CardOf(a), Target(a), Margin) - before;
    }

    private static bool WinsPair(GameState state, Candidate c)
    {
        var opposing = state.OpposingNode(c.Target);
        if (c.Action is IssueOrderAction) return false;
        if (opposing?.Card is null) return c.Action is not AssembleAction; // Assembling onto an unopposed unit wins nothing new.
        var theirs = opposing.Card;
        if (c.Action is AssembleAction x) return RankCalculator.GetRank(state, x.Target) + c.Rank > RankCalculator.GetRank(state, theirs);
        // Try the Unit on the node so static effects on both Units count.
        var card = c.PileCard ?? CardOf(c.Action);
        return state.WithTemporaryPlacement(card, c.Target, () => RankCalculator.GetRank(state, card) > RankCalculator.GetRank(state, theirs));
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
