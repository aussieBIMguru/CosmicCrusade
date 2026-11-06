using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

public sealed record PairResult(string Label, int? Rank0, int? Rank1, int? Winner);

/// <param name="Winner">Null when nobody wins the round.</param>
public sealed record RoundResult(
    int Round,
    int Total0,
    int Total1,
    int? Winner,
    bool DecidedByTiebreak,
    int PairsWon0,
    int PairsWon1,
    IReadOnlyList<PairResult> Pairs)
{
    public int Total(int player) => player == 0 ? Total0 : Total1;

    /// <summary>Set when an effect decided the round instead of the totals (The Empress, Senate Chambers).</summary>
    public string? WonBy { get; init; }
}

/// <summary>End of Main scoring (spec §5.3, rulings #3, #7, #32).</summary>
public static class Scoring
{
    public static RoundResult Score(GameState state)
    {
        int t0 = RankCalculator.TotalRank(state, 0);
        int t1 = RankCalculator.TotalRank(state, 1);

        var pairs = new List<PairResult>();
        var p0 = state.Players[0];
        var p1 = state.Players[1];
        for (int i = 0; i < PlayerState.UnitNodeCount; i++)
            pairs.Add(ComparePair(state, p0.UnitNodes[i], p1.UnitNodes[i]));
        if (p0.LeaderNode is not null && p1.LeaderNode is not null)
            pairs.Add(ComparePair(state, p0.LeaderNode, p1.LeaderNode));

        int w0 = pairs.Count(p => p.Winner == 0);
        int w1 = pairs.Count(p => p.Winner == 1);

        int? winner;
        bool tiebreak = false;
        if (t0 != t1)
        {
            winner = t0 > t1 ? 0 : 1;
        }
        else
        {
            tiebreak = true;
            winner = w0 == w1 ? null : (w0 > w1 ? 0 : 1);
        }

        // The Oculus: "The Player with the lowest total Rank wins instead" (the tiebreak is inverted the same way).
        if (winner is int w && StaticLayer.RoundWinnerInverted(state)) winner = GameState.Other(w);

        return new RoundResult(state.Round, t0, t1, winner, tiebreak, w0, w1, pairs);
    }

    private static PairResult ComparePair(GameState state, Node a, Node b)
    {
        int? ra = a.Card is null ? null : RankCalculator.GetRank(state, a.Card);
        int? rb = b.Card is null ? null : RankCalculator.GetRank(state, b.Card);
        int? winner = (ra, rb) switch
        {
            (null, null) => null,
            (not null, null) => 0,
            (null, not null) => 1,
            _ => ra == rb ? null : (ra > rb ? 0 : 1),
        };
        return new PairResult(a.Label, ra, rb, winner);
    }
}
