using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>Duel resolution (spec §6). Returns losers; the caller nullifies them (all at once for simultaneous duels).</summary>
public static class Duel
{
    /// <summary>The loser of a single duel, or null when nothing happens.</summary>
    public static CardInstance? Loser(GameState state, CardInstance a, CardInstance b)
    {
        int ra = RankCalculator.GetRank(state, a);
        int rb = RankCalculator.GetRank(state, b);
        if (ra != rb) return ra < rb ? a : b;

        bool aBeatsB = a.Races.Any(x => b.Races.Any(y => state.Database.Beats(x, y)));
        bool bBeatsA = b.Races.Any(x => a.Races.Any(y => state.Database.Beats(x, y)));
        if (aBeatsB == bBeatsA) return null; // Neither, or both (possible with Hybrids): nothing happens.
        return aBeatsB ? b : a;
    }

    /// <summary>Resolve several duels simultaneously: compare all first, then return every loser (ruling #13).</summary>
    public static IReadOnlyList<CardInstance> LosersOfSimultaneous(GameState state, IEnumerable<(CardInstance A, CardInstance B)> duels) =>
        duels.Select(d => Loser(state, d.A, d.B)).Where(l => l is not null).Select(l => l!).Distinct().ToList();
}
