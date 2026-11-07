using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Duel resolution (spec §6, ruling #37). Returns losers; the caller nullifies them (all at once for simultaneous duels).
///   1. Different Ranks: the lower Rank loses.
///   2. Equal Ranks: weaknesses on CURRENT races decide – a Unit that beats the other wins.
///   3. Equal Ranks and no weakness decides it (or each beats the other, possible with Hybrids): BOTH are Nullified.
/// </summary>
public static class Duel
{
    /// <summary>Everything Nullified by this duel: none, one Unit, or both on an undecided tie.</summary>
    public static IReadOnlyList<CardInstance> Losers(GameState state, CardInstance a, CardInstance b)
    {
        int ra = RankCalculator.GetRank(state, a);
        int rb = RankCalculator.GetRank(state, b);
        if (ra != rb) return new[] { ra < rb ? a : b };

        // Current races (§6.3): static race changes (Consortium, The Voidslime) included. A crewed spacecraft
        // counts as having its crew's races too (Spacecraft S7).
        var racesA = Spacecraft.DuelRaces(state, a);
        var racesB = Spacecraft.DuelRaces(state, b);
        bool aBeatsB = racesA.Any(x => racesB.Any(y => state.Database.Beats(x, y)));
        bool bBeatsA = racesB.Any(x => racesA.Any(y => state.Database.Beats(x, y)));
        if (aBeatsB != bBeatsA) return new[] { aBeatsB ? b : a };
        return new[] { a, b }; // Matched Ranks with no deciding weakness: both are Nullified (ruling #37).
    }

    /// <summary>The single loser, or null when both lose (undecided tie).</summary>
    public static CardInstance? Loser(GameState state, CardInstance a, CardInstance b)
    {
        var l = Losers(state, a, b);
        return l.Count == 1 ? l[0] : null;
    }

    /// <summary>Resolve several duels simultaneously: compare all first, then return every loser (ruling #13).</summary>
    public static IReadOnlyList<CardInstance> LosersOfSimultaneous(GameState state, IEnumerable<(CardInstance A, CardInstance B)> duels) =>
        duels.ToList().SelectMany(d => Losers(state, d.A, d.B)).Distinct().ToList();
}
