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
        if (r.Purpose.StartsWith("OptionalDuel:"))
            return new[] { WantsDuel(r, s) ? 0 : 1 };

        // M3.3
        if (r.Purpose.StartsWith("CopyFor:"))
        {
            // Mr Morph / Forced Mitosis: our copy should be big, theirs small (printed Rank: a copy keeps no changes).
            bool ours = int.Parse(r.Purpose[8..]) == me;
            return new[] { IndexOfMax(r, o => (ours ? 1 : -1) * CopyValue(o.Card)) };
        }
        if (r.Purpose.StartsWith("RearrangeTo:"))
        {
            var from = s.FindNode(r.Purpose[12..]);
            return new[] { from is null ? 0 : IndexOfMax(r, o => Rearrange.WithSwap(s, from, o.Node!, () => Margin(s, me))) };
        }
        if (r.Purpose.StartsWith("Redirect:"))
            return new[] { BestRedirect(r, s, int.Parse(r.Purpose[9..])) };
        switch (r.Purpose)
        {
            case "PayCost":
            case "PlayFromPile":
            case "DeployNode":
                break; // scored below

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

            case "ChooseRace":
                return new[] { BestRace(r, s) };

            case "Wormhole":
                return new[] { BestWarzone(r, s) };

            case "CoerceNode":
                return new[] { BestCoerceNode(r, s) };

            case "RearrangeFrom":
                return BestRearrangeStart(r, s);

            case "Terraform":
                return new[] { IndexOfMax(r, o => TerraformValue(o.Node!, me, s)) };

            case "TerraformKeep":
            case "Retarget":
                return new[] { 0 }; // keep our Unit in Standby rather than lose it; keep a commandeered React's target

            case "Retrigger":
                // Each Unit's ability runs as its controller: only re-run our own.
                return r.Options.Select((o, i) => (o, i)).Where(x => x.o.Card is { } c && SideOf(c) == me).Select(x => x.i).ToList();

            case "GainControlNode":
                return new[] { IndexOfMax(r, o => o.Node is { } n ? (s.OpposingNode(n)?.Card is null ? 2 : 1) + (n.Kind == NodeKind.Leader ? 0.5 : 0) : 0) };

            // ------------------------------------------------------------ Archons
            case "ArchonDraw":
                return new[] { WantsArchonDraw(s, me) ? 0 : 1 };

            case "ArchonSkip":
                // Units win rounds: give up an Order unless we hold none.
                return new[] { s.Players[me].HandOrderCount == 0 ? 0 : 1 };

            case "ArchonPick":
                return new[] { IndexOfMax(r, o => o.Card is { } c ? ArchonValue(c, s, me) : 0) };

            case "CopyOfSelf":
            {
                // Androsiar: our small Units become Rank-4 copies, the opponent's big ones shrink to Rank 4.
                var entering = r.Options[0].Card;
                var src = r.Source;
                if (entering is null || src?.Definition.BaseRank is not int copyRank) return new[] { 1 };
                int now = RankCalculator.GetRank(s, entering);
                bool ours = SideOf(entering) == me;
                return new[] { (ours ? copyRank > now : copyRank < now) ? 0 : 1 };
            }

            case "QuantumLeapDraw":
                return new[] { IndexOfMax(r, o => o.Value) };

            case "QuantumLeapIssue":
                // Issue drawn Ongoings that help (they stay up for the round); Actions: the first one.
                return r.Options.Count == 0 ? Array.Empty<int>() : new[] { 0 };

            case "CoercedUnit":
                // Psychic Coercion chose the node (each option carries it): send the Unit that helps us most there.
                return new[] { IndexOfMax(r, o => o.Card is { } c && o.Node is { } n ? AiVision.DeployGain(s, me, c, n) : 0) };
        }

        // Score every option, take the positive ones (within Min..Max), best first.
        var scored = r.Options.Select((o, i) => (i, score: Score(o, r.Purpose, me, s)))
            .OrderByDescending(x => x.score).ThenBy(x => x.i).ToList();
        int take = scored.Count(x => x.score > 0);
        take = Math.Clamp(take, r.Min, r.Max);
        return scored.Take(take).Select(x => x.i).ToList();
    }

    /// <summary>
    /// Archons: take the draw at the first chance (an unused Archon is wasted), except when we already lead on round
    /// wins by 2 or more and can afford to wait for a more decisive round.
    /// </summary>
    private static bool WantsArchonDraw(GameState s, int me) =>
        s.Players[me].RoundsWon - s.Players[GameState.Other(me)].RoundsWon < 2;

    /// <summary>
    /// Rough value of each Archon for the heuristic seat. Leaders need our Leader Node free; Ceasefire needs both
    /// sides to field a Leader.
    /// </summary>
    private static double ArchonValue(CardInstance c, GameState s, int me)
    {
        bool leaderNodeFree = s.Players[me].LeaderNode is { Card: null };
        return c.Definition.Name switch
        {
            "Archon Arsenal" => 9,
            "Planetscape" => 7,
            "The Unnameable" => leaderNodeFree ? 8 : 2,
            "Deify" => s.Options.Leaders ? 6 : 0,
            "Galactic Siege" => 6,
            "Nebula Cannon" => 6,
            "Unstoppable" => 5,
            "Open Source" => 5,
            "Quantum Leap" => 5,
            "Androsiar" => leaderNodeFree ? 5 : 1,
            "Insurrection" => 4,
            "Ceasefire" => s.Options.Leaders ? 3 : 0,
            _ => 1,
        };
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
                case "PayCost":
                    return 10 - value; // pay with the least valuable cards
                case "PlayFromPile":
                    return 1 + value;  // bring back the biggest Unit
                case "GainControl":
                    return enemy ? value : -value; // One of Us: their best Unit becomes ours
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
                case "DeployNode":
                {
                    // Our own nodes; unopposed or a Leader node first.
                    if (!ourNode) return 0.1;
                    double score = s.OpposingNode(node)?.Card is null ? 2 : 1;
                    return node.Kind == NodeKind.Leader ? score + 0.5 : score;
                }
                case "Duel":
                {
                    var a = node.Card;
                    var b = s.OpposingNode(node)?.Card;
                    if (a is null || b is null) return -1;
                    return Duel.Losers(s, a, b).Sum(l => SideOf(l) == me ? -Value(l, s) : Value(l, s));
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Extreme Sanctions / Containment Network: name the race that hurts the opponent most and us least – their Units
    /// in play count double, our own Units in play and in hand count against it.
    /// </summary>
    private static int BestRace(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        int opp = GameState.Other(me);
        return IndexOfMax(r, o =>
        {
            var race = (Race)o.Value;
            int theirs = s.Players[opp].UnitsInPlay.Count(u => StaticLayer.HasRace(s, u, race));
            int ours = s.Players[me].UnitsInPlay.Count(u => StaticLayer.HasRace(s, u, race))
                       + s.Players[me].Hand.Count(c => StaticLayer.HasRace(s, c, race));
            return theirs * 2 - ours;
        });
    }

    /// <summary>
    /// Psychic Coercion: we pick the node the opponent must Deploy to before they pick the Unit. A node on our side
    /// is best (the Unit scores for us). With their hand revealed we know their best answer for each node and take the
    /// node where it is weakest; otherwise we send them where our strongest Unit opposes them, and away from an empty
    /// Leader Node (only strong Units go there).
    /// </summary>
    private static int BestCoerceNode(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        int victim = GameState.Other(me);
        var hand = AiVision.VisibleHand(s, me, victim);
        return IndexOfMax(r, o =>
        {
            if (o.Node is not { } n) return double.MinValue;
            if (n.Side == me) return 1000;
            if (hand is not null)
            {
                var fits = hand.Where(c => Placement.CanDeploy(s, victim, c, n)).ToList();
                return fits.Count == 0 ? 0 : -fits.Max(c => AiVision.DeployGain(s, victim, c, n));
            }
            double opposing = s.OpposingNode(n)?.Card is { } ours ? RankCalculator.GetRank(s, ours) : -1;
            return opposing - (n.Kind == NodeKind.Leader ? 2 : 0);
        });
    }

    /// <summary>Wormhole: try each Warzone as the only Active one and keep the one that helps our margin most.</summary>
    private static int BestWarzone(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        int opp = GameState.Other(me);
        var saved = s.ActiveWarzones.ToList();
        try
        {
            return IndexOfMax(r, o =>
            {
                s.ActiveWarzones.Clear();
                if (o.Card is not null) s.ActiveWarzones.Add(o.Card);
                return RankCalculator.TotalRank(s, me) - RankCalculator.TotalRank(s, opp);
            });
        }
        finally
        {
            s.ActiveWarzones.Clear();
            s.ActiveWarzones.AddRange(saved);
        }
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

    // ------------------------------------------------------------------ M3.3

    /// <summary>Our total minus theirs, plus a little for winning the round as it stands.</summary>
    internal static double Margin(GameState s, int me)
    {
        var result = Scoring.Score(s);
        double m = result.Total(me) - result.Total(GameState.Other(me));
        if (result.Winner == me) m += 0.5;
        else if (result.Winner is not null) m -= 0.5;
        return m;
    }

    /// <summary>
    /// Ace: find the single switch that helps our margin most (both sides count: shoving their Units into Standby
    /// is as good as bringing ours forward). Answer that switch's first node, or NONE when nothing helps.
    /// </summary>
    private static IReadOnlyList<int> BestRearrangeStart(ChoiceRequest r, GameState s)
    {
        int me = r.Player;
        double now = Margin(s, me);
        double best = 0.01;
        int pick = -1;
        for (int i = 0; i < r.Options.Count; i++)
        {
            var a = r.Options[i].Node!;
            foreach (var b in Rearrange.Partners(s, a))
            {
                double gain = Rearrange.WithSwap(s, a, b, () => Margin(s, me)) - now;
                if (gain > best) { best = gain; pick = i; }
            }
        }
        return pick < 0 ? Array.Empty<int>() : new[] { pick };
    }

    /// <summary>Terraform: both Units on the pair leave scoring (ours can be saved in Standby); empty nodes get Locked.</summary>
    private static double TerraformValue(Node node, int me, GameState s)
    {
        double v = 0;
        foreach (var n in new[] { node, s.OpposingNode(node) })
        {
            if (n is null) continue;
            bool ours = n.Side == me;
            if (n.Card is { } c)
            {
                if (StaticLayer.IsImmune(s, c)) continue;
                double val = Value(c, s);
                bool saved = Placement.CanEnterStandby(c) && s.Players[n.Side].StandbyNodes.Any(x => x.IsEmpty);
                v += ours ? -(saved ? val * 0.6 : val) : (saved ? val * 0.8 : val);
            }
            else if (!n.IsLocked)
            {
                v += ours ? -0.5 : 0.5; // Locking an empty node denies it
            }
        }
        return v;
    }

    private static double CopyValue(CardInstance? c) =>
        c is null ? 0 : (c.Definition.BaseRank ?? 0) + (c.Definition.IsVanilla ? 0 : 0.5);

    /// <summary>
    /// Recolonize: our own Deploy goes where it is unopposed; theirs goes to our side if it can (it then scores for
    /// us), otherwise where our strongest Unit opposes it.
    /// </summary>
    private static int BestRedirect(ChoiceRequest r, GameState s, int deployer)
    {
        int me = r.Player;
        return IndexOfMax(r, o =>
        {
            if (o.Node is not { } n) return double.MinValue;
            double opposing = s.OpposingNode(n)?.Card is { } x ? RankCalculator.GetRank(s, x) : 0;
            if (deployer == me) return n.Side == me ? 10 - opposing : -10;
            return n.Side == me ? 100 : opposing;
        });
    }

    /// <summary>Fast Eddie: only Duel when it doesn't cost us more than it costs them.</summary>
    private static bool WantsDuel(ChoiceRequest r, GameState s)
    {
        var ids = r.Purpose.Split(':').Skip(1).Select(int.Parse).ToList();
        var units = s.AllUnitsInPlay.ToList();
        var a = units.FirstOrDefault(u => u.InstanceId == ids[0]);
        var b = units.FirstOrDefault(u => u.InstanceId == ids[1]);
        if (a is null || b is null) return false;
        double gain = Duel.Losers(s, a, b).Sum(l => SideOf(l) == r.Player ? -Value(l, s) : Value(l, s));
        return gain > 0;
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
