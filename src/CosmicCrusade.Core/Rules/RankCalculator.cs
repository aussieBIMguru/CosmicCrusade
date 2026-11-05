using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Current Rank per spec §7.4:
///   1. printed base rank, 2. base-rank overrides, 3. static modifiers from cards in play,
///   4. persistent modifiers (Manipulate / Assemble). No general floor.
/// Layer 3 (static effects) arrives with the effect interpreter (milestone 3).
/// </summary>
public static class RankCalculator
{
    public static int BaseRank(CardInstance card) =>
        card.BaseRankOverride ?? card.Definition.BaseRank ?? 0;

    public static int GetRank(GameState state, CardInstance card)
    {
        int rank = BaseRank(card);
        // TODO(M3): layer 3 – static modifiers from cards in play (skip for Immune units).
        foreach (var m in card.PersistentModifiers) rank += m.Amount;
        return rank;
    }

    public static int TotalRank(GameState state, int player) =>
        state.Players[player].UnitsInPlay.Sum(u => GetRank(state, u));
}
