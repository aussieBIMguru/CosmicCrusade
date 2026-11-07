using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>Builds the shared Unit and Order decks from the options' copy counts and deck mode (spec §10).</summary>
public static class DeckBuilder
{
    /// <summary>Is this card part of the game under these options (ignoring copy counts)?</summary>
    public static bool IsEnabled(CardDefinition def, GameOptions options)
    {
        if (def.Kind == CardKind.Warzone && !options.Warzones) return false;
        if (def.Set.Equals("Warzones", StringComparison.OrdinalIgnoreCase) && !options.Warzones) return false;
        if (def.IsArchon && !options.Archons) return false;
        if (!options.Leaders)
        {
            if (def.IsLeader) return false;
            if (options.LeaderRelatedCards.Contains(def.Id)) return false;
        }
        return true;
    }

    public static int CopiesOf(CardDefinition def, GameOptions options)
    {
        if (!IsEnabled(def, options)) return 0;
        options.CopyCounts.TryGetValue(def.Id, out int count);
        if (count <= 0) return 0;
        return options.DeckMode == DeckMode.TrueSingleton ? 1 : count;
    }

    public static void Build(GameState state)
    {
        var options = state.Options;
        state.UnitDeck.Clear();
        state.OrderDeck.Clear();
        state.WarzoneDeck.Clear();
        state.ActiveWarzones.Clear();
        state.ArchonDeck.Clear();
        state.RandomDrawUnits.Clear();
        state.RandomDrawOrders.Clear();

        // Ordered by id so the result depends only on the seed.
        foreach (var def in state.Database.All.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            int copies = CopiesOf(def, options);
            if (copies == 0) continue;

            // Archons always form their own real deck, whatever the deck mode (Archons P14).
            if (def.IsArchon)
            {
                for (int i = 0; i < copies; i++)
                {
                    var a = state.CreateInstance(def);
                    state.ArchonDeck.Add(a);
                    a.Location = CardLocation.InPile(PileKind.ArchonDeck);
                }
                continue;
            }

            // Warzones always form a real (cycling) deck, whatever the deck mode.
            if (def.Kind == CardKind.Warzone)
            {
                for (int i = 0; i < copies; i++)
                {
                    var wz = state.CreateInstance(def);
                    state.WarzoneDeck.Add(wz);
                    wz.Location = CardLocation.InPile(PileKind.WarzoneDeck);
                }
                continue;
            }

            if (options.DeckMode == DeckMode.RandomDraw)
            {
                (def.IsUnit ? state.RandomDrawUnits : state.RandomDrawOrders).Add(def);
                continue;
            }

            var deck = def.IsUnit ? state.UnitDeck : state.OrderDeck;
            for (int i = 0; i < copies; i++)
            {
                var inst = state.CreateInstance(def);
                deck.Add(inst);
                inst.Location = CardLocation.InPile(def.IsUnit ? PileKind.UnitDeck : PileKind.OrderDeck);
            }
        }

        state.Shuffle(state.UnitDeck);
        state.Shuffle(state.OrderDeck);
        state.Shuffle(state.WarzoneDeck);
        state.Shuffle(state.ArchonDeck);
    }
}
