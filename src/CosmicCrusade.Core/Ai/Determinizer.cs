using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Ai;

/// <summary>
/// Hidden information for AI search (M4). Turns a cloned state into one "possible world" consistent with what
/// <c>viewer</c> can see: the opponent's hidden hand is re-dealt from the cards the viewer hasn't seen (their hand
/// plus the decks), keeping how many Units and Orders they hold, and the deck order is reshuffled.
/// Never call it on the live state: it moves cards around.
/// </summary>
public static class Determinizer
{
    public static void Determinize(GameState s, int viewer, Random rng)
    {
        int opp = GameState.Other(viewer);
        bool handKnown = AiVision.VisibleHand(s, viewer, opp) is not null;

        if (s.Options.DeckMode == DeckMode.RandomDraw)
        {
            // Virtual decks: every unseen card is an independent random pick per deck type.
            if (!handKnown) RedrawRandom(s, opp, rng);
            return;
        }

        Redeal(s, opp, handKnown, PileKind.UnitDeck, c => c.Definition.IsUnit, rng);
        Redeal(s, opp, handKnown, PileKind.OrderDeck, c => c.Definition.IsOrder, rng);
    }

    /// <summary>Pools the opponent's hidden cards of one type with the deck, shuffles, and deals them back out.</summary>
    private static void Redeal(GameState s, int opp, bool handKnown, PileKind deck, Func<CardInstance, bool> ofType, Random rng)
    {
        var hand = s.Players[opp].Hand;
        var fromHand = handKnown ? new List<CardInstance>() : hand.Where(ofType).ToList();
        var pool = fromHand.Concat(s.GetPile(deck)).ToList();
        if (pool.Count == 0) return;

        foreach (var c in pool) s.RemoveFromCurrentLocation(c);
        Shuffle(pool, rng);

        for (int i = 0; i < pool.Count; i++)
        {
            if (i < fromHand.Count) s.MoveToHand(pool[i], opp);
            else s.MoveToPile(pool[i], deck);
        }
    }

    private static void RedrawRandom(GameState s, int opp, Random rng)
    {
        foreach (var card in s.Players[opp].Hand.ToList())
        {
            var pool = card.Definition.IsUnit ? s.RandomDrawUnits : card.Definition.IsOrder ? s.RandomDrawOrders : null;
            if (pool is null || pool.Count == 0) continue;
            s.RemoveFromCurrentLocation(card);
            s.MoveToHand(s.CreateInstance(pool[rng.Next(pool.Count)]), opp);
        }
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
