using System.Text.Json;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Effects;

/// <summary>
/// Decides whether an encoded effect can run with the ops implemented so far. An effect runs only when EVERY part
/// of it (keys, ops, selectors, filters, conditions) is understood. Anything else stays inert and is logged as
/// "not implemented yet", so a half-understood card never does half of what it says.
/// </summary>
public static class EffectSupport
{
    private static readonly HashSet<string> EffectKeys = new() { "trigger", "do", "optional", "condition", "choose", "note", "when" };

    private static readonly Dictionary<string, HashSet<string>> OpKeys = new()
    {
        ["Nullify"] = new() { "op", "targets", "bind", "cancelsPlay", "note" },
        ["ModifyRank"] = new() { "op", "targets", "amount", "duration", "note" },
        ["SetBaseRank"] = new() { "op", "targets", "value", "persistent", "note" },
        ["Lock"] = new() { "op", "targets", "remember", "note" },
        ["Unlock"] = new() { "op", "targets", "countAs", "note" },
        ["MoveToStandby"] = new() { "op", "targets", "standbyOf", "ifNoRoom", "gainControl", "ifHasKeyword", "countAs", "cancelsPlay", "note" },
        ["Draw"] = new() { "op", "deck", "min", "max", "count", "deckChoice", "distribution", "note" },
        ["Duel"] = new() { "op", "a", "b", "protect", "node", "note" },
        ["Manipulate"] = new() { "op", "amount", "filter", "note" },
        ["ReturnToHand"] = new() { "op", "targets", "hand", "note" },
        ["RetireFromHand"] = new() { "op", "min", "max", "filter", "countAs", "note" },
        ["RetireHand"] = new() { "op", "countAs", "note" },
        ["SwapNodeContents"] = new() { "op", "nodes", "controlFollowsSide", "note" },
        ["Suppress"] = new() { "op", "targets", "what", "duration", "note" },
        ["Assemble"] = new() { "op", "source", "onto", "ignoreKeywordRequirement", "note" },
    };

    private static readonly HashSet<string> NamedSelectors = new()
    {
        "Self", "OpposingUnit", "OpposingLeader", "Event.Unit", "Event.Order", "Event.TargetNode",
    };

    private static readonly HashSet<string> ChooseKeys = new() { "choose", "filter", "min", "max", "count", "perPlayer", "name" };

    internal static readonly HashSet<string> FilterKeys = new()
    {
        "zone", "kind", "controller", "race", "notRace", "unitClass", "excludeSelf", "opposed", "sharesRaceWith",
        "excludeBound", "baseRankLessThan", "opposingUnitRace", "orderType", "empty", "locked", "nodeKind", "side", "nodeOf",
    };

    internal static readonly HashSet<string> Zones = new() { "InPlay", "Standby", "RetiredPile", "NullifiedPile", "IssuedPile", "OrderNode" };

    private static readonly Dictionary<string, bool> _cardCache = new();

    /// <summary>
    /// True when every effect of <paramref name="def"/> with this trigger can run, and the card has no unsupported
    /// play cost. False when the card has no such effect.
    /// </summary>
    public static bool IsLive(CardDefinition def, string trigger)
    {
        string key = def.Id + "|" + trigger;
        lock (_cardCache)
        {
            if (_cardCache.TryGetValue(key, out bool v)) return v;
            v = Compute(def, trigger);
            _cardCache[key] = v;
            return v;
        }
    }

    private static bool Compute(CardDefinition def, string trigger)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return false;
        bool any = false;
        foreach (var e in def.Effects.EnumerateArray())
        {
            string t = e.TryGetProperty("trigger", out var te) ? te.GetString() ?? "" : "";
            if (t == "PlayCost") return false; // costs (Mechatron, Scrapped Metal) aren't implemented yet
            if (t != trigger) continue;
            any = true;
            if (!IsEffectLive(e)) return false;
        }
        return any;
    }

    /// <summary>Can this single effect entry run?</summary>
    public static bool IsEffectLive(JsonElement effect)
    {
        if (effect.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in effect.EnumerateObject())
            if (!EffectKeys.Contains(p.Name)) return false; // script, playCondition, params...

        if (effect.TryGetProperty("condition", out var c) && !ConditionOk(c)) return false;
        if (effect.TryGetProperty("choose", out var ch) && !SelectorOk(ch)) return false;
        if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) return false;
        foreach (var op in ops.EnumerateArray())
            if (!OpOk(op)) return false;
        return true;
    }

    private static bool OpOk(JsonElement op)
    {
        string name = op.TryGetProperty("op", out var n) ? n.GetString() ?? "" : "";
        if (!OpKeys.TryGetValue(name, out var keys)) return false;
        foreach (var p in op.EnumerateObject())
            if (!keys.Contains(p.Name)) return false;

        foreach (var selKey in new[] { "targets", "nodes", "a", "b", "node", "source", "onto" })
            if (op.TryGetProperty(selKey, out var sel) && !SelectorOk(sel)) return false;
        if (op.TryGetProperty("filter", out var f) && !FilterOk(f)) return false;

        switch (name)
        {
            case "ModifyRank":
                if (op.TryGetProperty("duration", out var d) && d.GetString() != "WhileInPlay") return false;
                return op.TryGetProperty("amount", out var amt) && AmountOk(amt);
            case "SetBaseRank":
                return op.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number;
            case "Manipulate":
                return op.TryGetProperty("amount", out var m) && m.ValueKind == JsonValueKind.Number;
            case "Suppress":
                return op.TryGetProperty("what", out var w) && w.GetString() == "AllAbilities";
            case "Draw":
                if (op.TryGetProperty("distribution", out var dist) && dist.GetString() != "SameAsRetired") return false;
                if (op.TryGetProperty("deckChoice", out var dc) && dc.GetString() != "PerCard") return false;
                if (op.TryGetProperty("deck", out var deck) && deck.GetString() is not ("Units" or "Orders")) return false;
                return op.TryGetProperty("deck", out _) || op.TryGetProperty("count", out _);
            case "MoveToStandby":
                if (op.TryGetProperty("ifHasKeyword", out var hk))
                {
                    if (hk.ValueKind != JsonValueKind.Object) return false;
                    foreach (var p in hk.EnumerateObject())
                        if (p.Name != "Assemble" || p.Value.GetString() != "ReturnToHand") return false;
                }
                if (op.TryGetProperty("ifNoRoom", out var nr) && nr.GetString() != "Retire") return false;
                return true;
            case "Duel":
                return op.TryGetProperty("node", out _) || (op.TryGetProperty("a", out _) && op.TryGetProperty("b", out _));
            default:
                return true;
        }
    }

    private static bool AmountOk(JsonElement a) =>
        a.ValueKind == JsonValueKind.Number
        || (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ifRace", out _)
            && a.TryGetProperty("then", out var t) && t.ValueKind == JsonValueKind.Number
            && a.TryGetProperty("else", out var e) && e.ValueKind == JsonValueKind.Number);

    internal static bool SelectorOk(JsonElement sel)
    {
        switch (sel.ValueKind)
        {
            case JsonValueKind.String:
            {
                string s = sel.GetString()!;
                if (NamedSelectors.Contains(s)) return true;
                if (s.StartsWith("Bound.") || s.StartsWith("Remembered.")) return s.Count(ch => ch == '.') == 1;
                if (s.StartsWith("Chosen.")) return s.Count(ch => ch == '.') == 1 || s.EndsWith(".Unit");
                return false;
            }
            case JsonValueKind.Object:
                if (sel.TryGetProperty("choose", out var what))
                {
                    if (what.GetString() is not ("Unit" or "Card" or "Node")) return false;
                    foreach (var p in sel.EnumerateObject())
                        if (!ChooseKeys.Contains(p.Name)) return false;
                    if (sel.TryGetProperty("filter", out var f) && !FilterOk(f)) return false;
                    return true;
                }
                return FilterOk(sel); // a plain filter: "all cards matching"
            default:
                return false;
        }
    }

    internal static bool FilterOk(JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in f.EnumerateObject())
        {
            if (!FilterKeys.Contains(p.Name)) return false;
            if (p.Name == "zone")
            {
                if (p.Value.ValueKind == JsonValueKind.String && !Zones.Contains(p.Value.GetString()!)) return false;
                if (p.Value.ValueKind == JsonValueKind.Array && p.Value.EnumerateArray().Any(z => !Zones.Contains(z.GetString() ?? ""))) return false;
            }
            if (p.Name == "baseRankLessThan"
                && !(p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("countOf", out var co) && FilterOk(co)))
                return false;
        }
        return true;
    }

    private static bool ConditionOk(JsonElement c)
    {
        if (c.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in c.EnumerateObject())
        {
            switch (p.Name)
            {
                case "unit": if (p.Value.GetString() is not ("OpposingUnit" or "OpposingLeader")) return false; break;
                case "race":
                case "notRace":
                    break;
                case "exists": if (!FilterOk(p.Value)) return false; break;
                case "selfOpposed": break;
                default: return false;
            }
        }
        return true;
    }
}
