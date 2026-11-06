using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Current Rank per spec §7.4:
///   1. printed Base Rank ("X" counts as 0),
///   2. Base Rank overrides: static ones first (the card's own, e.g. Cure Failure 349, then others', e.g. Primordium,
///      The Voidslime), then persistent ones on top (Shot in the Dark, Corporate Detainer...),
///   3. static Rank modifiers from cards in play (<see cref="StaticLayer"/>), recalculated on every read,
///   4. persistent modifiers (Manipulate, Assemble, "+2 while in play").
/// Polarity Storm inverts layers 3–4. There is no general floor: only a static with a floor (Acid Rain) sets one.
/// Immune Units ignore static effects from other cards (others can't target them, so layers 2/4 stay their own).
/// </summary>
public static class RankCalculator
{
    /// <summary>Printed Base Rank or a persistent override. Use this for cards that are not in play.</summary>
    public static int BaseRank(CardInstance card) =>
        card.BaseRankOverride ?? card.Definition.BaseRank ?? 0;

    [ThreadStatic] private static HashSet<CardInstance>? _computing;

    /// <summary>Base Rank after layer 2 (static and persistent overrides). Even Odds and Overwhelm read this.</summary>
    public static int CurrentBaseRank(GameState state, CardInstance card)
    {
        if (!InPlay(card)) return BaseRank(card);
        int rank = StaticLayer.ApplyBaseOverrides(state, card, card.Definition.BaseRank ?? 0);
        return card.BaseRankOverride ?? rank;
    }

    public static int GetRank(GameState state, CardInstance card)
    {
        int persistent = card.PersistentModifiers.Sum(m => m.Amount);
        if (!InPlay(card)) return BaseRank(card) + persistent;

        // A loop of Ranks that depend on each other (two Resonators): the inner read ignores static effects.
        _computing ??= new HashSet<CardInstance>();
        if (!_computing.Add(card)) return BaseRank(card) + persistent;
        try
        {
            int rank = CurrentBaseRank(state, card);
            var (staticSum, floor) = StaticLayer.RankModifiers(state, card);
            int mods = staticSum + persistent;
            if (StaticLayer.RankModifiersInverted(state, card)) mods = -mods;
            rank += mods;
            if (floor is int f && rank < f) rank = f;
            return rank;
        }
        finally
        {
            _computing.Remove(card);
        }
    }

    public static int TotalRank(GameState state, int player) =>
        state.Players[player].UnitsInPlay.Sum(u => GetRank(state, u));

    private static bool InPlay(CardInstance c) =>
        c.Definition.IsUnit && c.Location.Kind == LocationKind.Node && c.Location.Node is { IsScoring: true };
}
