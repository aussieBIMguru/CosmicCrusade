using System.Text.Json;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Where a unit may be played (spec §3 class table, ruling #5), plus PlacementOverride effects:
///   • Visiting Diplomat – any Unit Node on either side (ruling #25),
///   • The Creeper – its own or the opponent's Leader Node,
///   • The Oculus (Active Warzone) – players may also place Units on their opponent's nodes.
/// The class rule always still applies. A unit scores for, and is controlled by, the side it sits on (ruling #15).
/// </summary>
public static class Placement
{
    /// <summary>Class rule only: does this unit class fit this kind of node?</summary>
    public static bool ClassAllows(CardDefinition def, NodeKind kind)
    {
        if (!def.IsUnit) return false;
        return def.UnitClass switch
        {
            UnitClass.Leader => kind == NodeKind.Leader,
            UnitClass.Champion => kind is NodeKind.Unit or NodeKind.Leader,
            _ => kind == NodeKind.Unit,
        };
    }

    /// <summary>
    /// Ruling #36: a Leader can never go into a Standby zone. An effect that would put one there is not a legal
    /// play: the Leader can't be chosen for it, and if it would happen anyway nothing happens to it.
    /// </summary>
    public static bool CanEnterStandby(CardDefinition def) => def.IsUnit && !def.IsLeader;

    /// <summary>
    /// Ruling #36 for a card instance: a copy has to be a non-Leader both as the copy and as printed, because it
    /// turns back into its printed self the moment it leaves play (Mr Morph copying a Minion is still a Leader card).
    /// </summary>
    public static bool CanEnterStandby(CardInstance card) =>
        CanEnterStandby(card.Definition) && CanEnterStandby(card.PrintedDefinition);

    /// <summary>Can <paramref name="player"/> deploy this unit from hand to <paramref name="node"/> right now?</summary>
    public static bool CanDeploy(GameState state, int player, CardInstance card, Node node)
    {
        if (!node.IsScoring || !node.IsEmpty) return false;
        if (!ClassAllows(card.Definition, node.Kind)) return false;
        if (node.Side != player && !AllowsOpponentNode(state, card, node)) return false;
        // Static deploy rules (Extreme Sanctions, Even Odds, Military Academy) and extra costs (Mechatron).
        return StaticLayer.DeployAllowed(state, player, card, node) && PlayRules.CanPay(state, player, card);
    }

    /// <summary>
    /// Recolonize: may the Unit already being Deployed by <paramref name="player"/> go to <paramref name="node"/>
    /// instead? Same rules as a Deploy (class, side, Even Odds) minus what was settled when it was announced (its cost,
    /// "can this card be Deployed at all", Military Academy's turn count).
    /// </summary>
    public static bool CanRedirectTo(GameState state, int player, CardInstance card, Node node)
    {
        if (!node.IsScoring || !node.IsEmpty) return false;
        if (!ClassAllows(card.Definition, node.Kind)) return false;
        if (node.Side != player && !AllowsOpponentNode(state, card, node)) return false;
        return StaticLayer.NodeAllowsDeploy(state, card, node);
    }

    /// <summary>
    /// One of Us: may a Unit already in play move to <paramref name="node"/> (an Empty Unit/Leader node its class
    /// allows)? It isn't Deployed, so deploy rules don't apply.
    /// </summary>
    public static bool CanMoveInPlayTo(CardInstance card, Node node) =>
        node.IsScoring && node.IsEmpty && ClassAllows(card.Definition, node.Kind);

    public static IEnumerable<Node> LegalDeployNodes(GameState state, int player, CardInstance card) =>
        state.Players[player].ScoringNodes
            .Concat(state.Players[GameState.Other(player)].ScoringNodes)
            .Where(n => CanDeploy(state, player, card, n));

    // ------------------------------------------------------------------ PlacementOverride

    /// <summary>Does a PlacementOverride let this unit go on an opponent's node?</summary>
    private static bool AllowsOpponentNode(GameState state, CardInstance card, Node node)
    {
        // Card's own override (Visiting Diplomat, The Creeper), unless Suppressed (Suppressing Goop reaches hands).
        var def = card.Definition;
        foreach (var allowed in StaticLayer.IsSuppressed(state, card) ? Array.Empty<string>() : SelfOverrides(def))
        {
            switch (allowed)
            {
                case "AnyUnitNodeEitherSide" when node.Kind == NodeKind.Unit:
                case "OpponentLeader" when node.Kind == NodeKind.Leader:
                case "AlsoOpponentNodes":
                    return true;
            }
        }

        // Global overrides from the Active Warzone (The Oculus) and Ongoing Orders on Order Nodes.
        foreach (var wz in state.ActiveWarzones.Where(w => !StaticLayer.IsSuppressed(state, w)))
            foreach (var allowed in GlobalUnitOverrides(wz.Definition))
                if (allowed == "AlsoOpponentNodes") return true;
        foreach (var p in state.Players)
            foreach (var on in p.OrderNodes)
                if (on.Card is not null && !StaticLayer.IsSuppressed(state, on.Card))
                    foreach (var allowed in GlobalUnitOverrides(on.Card.Definition))
                        if (allowed == "AlsoOpponentNodes") return true;

        return false;
    }

    private static readonly Dictionary<string, string[]> _selfCache = new();
    private static readonly Dictionary<string, string[]> _globalCache = new();

    /// <summary>allowedNodes values of PlacementOverride effects targeting "Self".</summary>
    public static IReadOnlyList<string> SelfOverrides(CardDefinition def)
    {
        lock (_selfCache)
        {
            if (!_selfCache.TryGetValue(def.Id, out var list))
                _selfCache[def.Id] = list = ReadOverrides(def, selfTargeted: true);
            return list;
        }
    }

    /// <summary>allowedNodes values of PlacementOverride effects that target all Units (e.g. a Warzone).</summary>
    public static IReadOnlyList<string> GlobalUnitOverrides(CardDefinition def)
    {
        lock (_globalCache)
        {
            if (!_globalCache.TryGetValue(def.Id, out var list))
                _globalCache[def.Id] = list = ReadOverrides(def, selfTargeted: false);
            return list;
        }
    }

    private static string[] ReadOverrides(CardDefinition def, bool selfTargeted)
    {
        var result = new List<string>();
        if (def.Effects.ValueKind != JsonValueKind.Array) return result.ToArray();

        foreach (var effect in def.Effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("trigger", out var trig) || trig.GetString() != "Static") continue;
            if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) continue;

            foreach (var op in ops.EnumerateArray())
            {
                if (!op.TryGetProperty("op", out var name) || name.GetString() != "PlacementOverride") continue;
                bool isSelf = op.TryGetProperty("targets", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "Self";
                if (isSelf != selfTargeted) continue;
                if (!op.TryGetProperty("allowedNodes", out var allowed)) continue;

                if (allowed.ValueKind == JsonValueKind.String) result.Add(allowed.GetString()!);
                else if (allowed.ValueKind == JsonValueKind.Array)
                    foreach (var a in allowed.EnumerateArray()) result.Add(a.GetString()!);
            }
        }
        return result.ToArray();
    }
}
