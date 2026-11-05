using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Ai;

/// <summary>
/// Heuristic answers to ability choices, driven by <see cref="ChoiceRequest.Purpose"/>: hurt the opponent's Units,
/// help your own, lock the opponent's empty nodes, and so on. Only uses public information and its own hand.
/// </summary>
public static class AiChoices
{
    public static IReadOnlyList<int> Choose(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        switch (r.Purpose)
        {
            case "Optional":
                return new[] { WantsOptional(r, s) ? 0 : 1 };

            case "Draw":
                return new[] { IndexOfMax(r, o => o.Value) };

            case "DeckSplit":
            {
                // Units win rounds; keep at least one Order if we have none.
                var ps = s.Players[me];
                int total = r.Options.Max(o => o.Value);
                int units = ps.HandOrderCount == 0 && total > 0 ? total - 1 : total;
                return new[] { IndexOfMax(r, o => -Math.Abs(o.Value - units)) };
            }

            case "ManipulateSign":
            {
                var target = r.Options[0].Card;
                bool ours = target is not null && SideOf(target) == me;
                return new[] { ours ? 0 : 1 };
            }

            case "StandbyNode":
                // Prefer a Standby node whose Unit node is free, so the Unit can advance next turn.
                return new[] { IndexOfMax(r, o => o.Node is { } n && s.Players[n.Side].UnitNodes[n.Index].IsEmpty ? 1 : 0) };

            case "Swap":
                return BestSwap(r, s);
        }

        // Score every option, take the positive ones (within Min..Max), best first.
        var scored = r.Options.Select((o, i) => (i, score: Score(o, r.Purpose, me, s)))
            .OrderByDescending(x => x.score).ThenBy(x => x.i).ToList();
        int take = scored.Count(x => x.score > 0);
        take = Math.Clamp(take, r.Min, r.Max);
        return scored.Take(take).Select(x => x.i).ToList();
    }

    private static double Score(ChoiceOption o, string purpose, int me, GameState s)
    {
        if (o.Card is { } card)
        {
            int side = SideOf(card);
            double value = Value(card, s);
            bool enemy = side == GameState.Other(me);
            bool mine = side == me;
            switch (purpose)
            {
                case "Nullify":
                case "Debuff":
                case "ReturnToHand":
                    return enemy ? value : mine ? -value : 0.1; // piles/standby: harmless
                case var p when p.StartsWith("SetBaseRank:"):
                {
                    int to = int.Parse(p[12..]);
                    double drop = RankCalculator.GetRank(s, card) - to;
                    return enemy ? drop : mine ? -drop : 0;
                }
                case "Buff":
                    return mine ? 5 + OpposingRank(card, s) - RankCalculator.GetRank(s, card) * 0.1 : enemy ? -5 : 0;
                case "Manipulate":
                    // We pick the sign afterwards: our Unit gets +N, theirs −N. Pick the most contested pair.
                    return 1 + Value(card, s) * 0.1;
                case "MoveToStandby":
                case "AssembleSource":
                    return side < 0 ? 1 + value : enemy ? value : -value; // from a pile into our Standby: bigger is better
                case "RetireFromHand":
                    return -1; // keep cards unless forced
                default:
                    return 0;
            }
        }

        if (o.Node is { } node)
        {
            bool ourNode = node.Side == me;
            switch (purpose)
            {
                case "Lock":
                    if (node.Card is { } occupant) // Sisters of Blight also sends it to Standby
                        return ourNode ? -(Value(occupant, s) + 2) : Value(occupant, s) + 2;
                    if (node.IsScoring) return ourNode ? -1.5 : 2;
                    return ourNode ? -0.5 : 0.5;
                case "Unlock":
                    return ourNode ? 2.5 : 0.6; // each Unlock also draws a card (Decode)
                case "Duel":
                {
                    var a = node.Card;
                    var b = s.OpposingNode(node)?.Card;
                    if (a is null || b is null) return -1;
                    var loser = Duel.Loser(s, a, b);
                    if (loser is null) return 0;
                    return SideOf(loser) == me ? -Value(loser, s) : Value(loser, s);
                }
            }
        }
        return 0;
    }

    /// <summary>Arganium: pick the two Unit nodes whose switch gains us the most Rank.</summary>
    private static IReadOnlyList<int> BestSwap(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        double best = double.MinValue;
        (int, int) pick = (0, Math.Min(1, r.Options.Count - 1));
        for (int i = 0; i < r.Options.Count; i++)
        for (int j = i + 1; j < r.Options.Count; j++)
        {
            var a = r.Options[i].Node!;
            var b = r.Options[j].Node!;
            double gain = SwapGain(a.Card, a, b, me, s) + SwapGain(b.Card, b, a, me, s);
            if (gain > best) { best = gain; pick = (i, j); }
        }
        return new[] { pick.Item1, pick.Item2 };
    }

    private static double SwapGain(CardInstance? card, Node from, Node to, int me, GameState s)
    {
        if (card is null || from.Side == to.Side) return 0;
        int rank = RankCalculator.GetRank(s, card);
        return to.Side == me ? rank * 2 : -rank * 2; // it leaves one total and joins the other
    }

    private static bool WantsOptional(ChoiceRequest r, GameState s)
    {
        // Abilities that hurt a chosen Unit are only worth it when the opponent has one in play.
        var src = r.Source;
        if (src is null) return true;
        string raw = src.Definition.Effects.ValueKind == System.Text.Json.JsonValueKind.Array ? src.Definition.Effects.GetRawText() : "";
        bool harmful = raw.Contains("\"SetBaseRank\"") || raw.Contains("\"Nullify\"");
        bool chooses = raw.Contains("\"choose\"");
        if (harmful && chooses) return s.Players[GameState.Other(r.Player)].UnitsInPlay.Any();
        return true;
    }

    private static int IndexOfMax(ChoiceRequest r, Func<ChoiceOption, double> f)
    {
        int best = 0;
        for (int i = 1; i < r.Options.Count; i++)
            if (f(r.Options[i]) > f(r.Options[best])) best = i;
        return best;
    }

    private static int SideOf(CardInstance c) => c.Location.Kind switch
    {
        LocationKind.Node => c.Location.Node!.Side,
        LocationKind.Stack or LocationKind.Hand => c.Location.Player,
        _ => -1,
    };

    private static double Value(CardInstance c, GameState s)
    {
        bool inPlay = c.Location.Kind == LocationKind.Node && c.Location.Node!.IsScoring;
        double rank = inPlay ? RankCalculator.GetRank(s, c) : RankCalculator.BaseRank(c);
        return Math.Max(0.5, rank + (c.Definition.IsLeader ? 1 : 0));
    }

    private static double OpposingRank(CardInstance c, GameState s) =>
        c.Location.Node is { } n && s.OpposingNode(n)?.Card is { } o ? RankCalculator.GetRank(s, o) : 0;
}
