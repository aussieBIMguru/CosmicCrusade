using System.Text.Json;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// M3.2b: extra costs and play conditions.
///   • PlayCost (Mechatron: Retire 2 other Synthenoid Units from hand; Scrapped Metal: Retire 1 Synthenoid card).
///     The cost is paid as the card is Deployed / Issued. A card whose cost can't be paid can't be played
///     (Mechatron errata: "cannot be Deployed unless it can be Played"). Suppressing Goop waives costs (errata).
///   • playCondition (Caustic Creek: each player has an Empty node; Dimensional Rift: exactly 1 Active Warzone).
/// </summary>
public static class PlayRules
{
    public sealed record Cost(int Count, CardKind? Kind, Race? Race);

    private static readonly Dictionary<string, Cost?> _costs = new();

    /// <summary>The card's extra cost, or null when it has none (or it is waived by suppression).</summary>
    public static Cost? CostOf(GameState state, CardInstance card)
    {
        var cost = CostOf(card.Definition);
        if (cost is null || StaticLayer.IsSuppressed(state, card)) return null;
        return cost;
    }

    public static Cost? CostOf(CardDefinition def)
    {
        lock (_costs)
        {
            if (!_costs.TryGetValue(def.Id, out var c)) _costs[def.Id] = c = ParseCost(def);
            return c;
        }
    }

    /// <summary>Hand cards that could pay <paramref name="card"/>'s cost (never the card itself).</summary>
    public static List<CardInstance> CostCandidates(GameState state, CardInstance card, int player)
    {
        var cost = CostOf(card.Definition);
        if (cost is null) return new List<CardInstance>();
        return state.Players[player].Hand
            .Where(c => !ReferenceEquals(c, card))
            .Where(c => cost.Kind is null || c.Definition.Kind == cost.Kind)
            .Where(c => cost.Race is null || StaticLayer.HasRace(state, c, cost.Race.Value))
            .ToList();
    }

    /// <summary>Can <paramref name="player"/> pay the card's extra cost (true when there is none)?</summary>
    public static bool CanPay(GameState state, int player, CardInstance card) =>
        CostOf(state, card) is not { } cost || CostCandidates(state, card, player).Count >= cost.Count;

    /// <summary>Is the card's playCondition met (true when it has none)?</summary>
    public static bool ConditionMet(GameState state, int player, CardDefinition def)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return true;
        foreach (var e in def.Effects.EnumerateArray())
        {
            if (!e.TryGetProperty("playCondition", out var pc)) continue;
            if (pc.TryGetProperty("eachPlayerHas", out var each))
            {
                int min = each.TryGetProperty("minCount", out var m) ? m.GetInt32() : 1;
                if (state.Players.Any(p => p.AllNodes.Count(n => n.IsEmpty) < min)) return false;
            }
            if (pc.TryGetProperty("activeWarzones", out var aw) && state.ActiveWarzones.Count != aw.GetInt32()) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ support (EffectSupport)

    internal static bool IsCostSupported(JsonElement effect)
    {
        if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array || ops.GetArrayLength() != 1) return false;
        var op = ops[0];
        foreach (var p in op.EnumerateObject())
            if (p.Name is not ("op" or "count" or "filter" or "note")) return false;
        if (!op.TryGetProperty("op", out var n) || n.GetString() != "RetireFromHand") return false;
        if (!op.TryGetProperty("count", out var c) || c.ValueKind != JsonValueKind.Number) return false;
        if (op.TryGetProperty("filter", out var f))
            foreach (var p in f.EnumerateObject())
                if (p.Name is not ("kind" or "race")) return false;
        return true;
    }

    internal static bool IsConditionSupported(JsonElement pc)
    {
        if (pc.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in pc.EnumerateObject())
        {
            switch (p.Name)
            {
                case "eachPlayerHas":
                    foreach (var q in p.Value.EnumerateObject())
                        if (q.Name is not ("empty" or "minCount")) return false;
                    break;
                case "activeWarzones":
                    if (p.Value.ValueKind != JsonValueKind.Number) return false;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static Cost? ParseCost(CardDefinition def)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return null;
        foreach (var e in def.Effects.EnumerateArray())
        {
            if (!e.TryGetProperty("trigger", out var t) || t.GetString() != "PlayCost" || !IsCostSupported(e)) continue;
            var op = e.GetProperty("do")[0];
            CardKind? kind = null;
            Race? race = null;
            if (op.TryGetProperty("filter", out var f))
            {
                if (f.TryGetProperty("kind", out var k)) kind = CardDatabase.ParseEnum<CardKind>(k.GetString()!);
                if (f.TryGetProperty("race", out var r)) race = CardDatabase.ParseEnum<Race>(r.GetString()!);
            }
            return new Cost(op.GetProperty("count").GetInt32(), kind, race);
        }
        return null;
    }
}
