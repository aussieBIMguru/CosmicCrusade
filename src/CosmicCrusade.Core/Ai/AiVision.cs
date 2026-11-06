using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Ai;

/// <summary>
/// What an AI seat may know, and quick margin estimates. Hands are hidden unless an effect reveals them (The
/// All-knowing, Transparency, Oracle's Arch), so every read of an opponent's hand goes through
/// <see cref="VisibleHand"/>.
/// </summary>
public static class AiVision
{
    /// <summary><paramref name="owner"/>'s hand if <paramref name="viewer"/> may see it, otherwise null.</summary>
    public static IReadOnlyList<CardInstance>? VisibleHand(GameState s, int viewer, int owner) =>
        owner == viewer || StaticLayer.IsHandRevealed(s, owner) ? s.Players[owner].Hand : null;

    /// <summary>How much <paramref name="player"/>'s margin (their total minus the other's) changes if the Unit lands there.</summary>
    public static int DeployGain(GameState s, int player, CardInstance card, Node node)
    {
        int other = GameState.Other(player);
        int Margin() => RankCalculator.TotalRank(s, player) - RankCalculator.TotalRank(s, other);
        int before = Margin();
        return s.WithTemporaryPlacement(card, node, Margin) - before;
    }

    /// <summary>
    /// The best single Deploy the opponent could make from their hand right now, as a gain to their margin – only
    /// when their hand is revealed to <paramref name="me"/>. Null when it is hidden (or they have no Deploy).
    /// </summary>
    public static int? OpponentBestDeploy(GameState s, int me)
    {
        int opp = GameState.Other(me);
        if (VisibleHand(s, me, opp) is null) return null;
        int? best = null;
        foreach (var d in LegalActions.Deploys(s, opp).Hand)
        {
            int gain = DeployGain(s, opp, d.Card, d.Target);
            if (best is null || gain > best) best = gain;
        }
        return best;
    }
}
