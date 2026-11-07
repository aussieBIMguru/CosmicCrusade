using System.Text.Json;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.Core.Effects;

/// <summary>
/// Decides whether an encoded effect can run with the ops implemented so far. An effect runs only when EVERY part
/// of it (keys, ops, selectors, filters, conditions) is understood. Anything else stays inert and is logged as
/// "not implemented yet", so a half-understood card never does half of what it says.
/// </summary>
public static class EffectSupport
{
    private static readonly HashSet<string> EffectKeys = new()
    {
        "trigger", "do", "optional", "condition", "choose", "note", "when",
        // M3.2b: phase / "whenever" triggers and play conditions.
        "event", "filter", "includeSelf", "oncePerRound", "priority", "ordering", "playCondition",
        "script", // scripted handlers the engine has (GameEngine.Scripts)
        "params", // M3.3: a script's parameters (Mr Morph, Forced Mitosis, TR-1993.r)
    };

    /// <summary>Events the engine raises for OnEvent abilities.</summary>
    internal static readonly HashSet<string> Events = new()
    {
        "UnitPlayed", "OrderPlayed", "CardPlayed", "CardPlayedFromHand", "CardRetiredFromHand", "UnitNullified",
        "CardDrawn", "Assembled",
    };

    private static readonly HashSet<string> EventFilterKeys = new() { "player", "targetNot", "sourceIsOpponentOfUnitController" };

    private static readonly Dictionary<string, HashSet<string>> OpKeys = new()
    {
        ["Nullify"] = new() { "op", "targets", "bind", "cancelsPlay", "simultaneous", "note" },
        ["ModifyRank"] = new() { "op", "targets", "amount", "duration", "bind", "note" },
        ["SetBaseRank"] = new() { "op", "targets", "value", "persistent", "note" },
        ["Lock"] = new() { "op", "targets", "remember", "note" },
        ["Unlock"] = new() { "op", "targets", "countAs", "note" },
        ["MoveToStandby"] = new() { "op", "targets", "standbyOf", "ifNoRoom", "gainControl", "ifHasKeyword", "countAs", "cancelsPlay", "replaces", "note" },
        ["Draw"] = new() { "op", "deck", "min", "max", "count", "deckChoice", "distribution", "player", "note" },
        ["Duel"] = new() { "op", "a", "b", "protect", "node", "note" },
        ["Manipulate"] = new() { "op", "amount", "filter", "player", "note" },
        ["ReturnToHand"] = new() { "op", "targets", "hand", "note" },
        ["RetireFromHand"] = new() { "op", "min", "max", "filter", "countAs", "note" },
        ["RetireHand"] = new() { "op", "countAs", "note" },
        ["SwapNodeContents"] = new() { "op", "nodes", "controlFollowsSide", "note" },
        ["Suppress"] = new() { "op", "targets", "what", "duration", "note" },
        ["Assemble"] = new() { "op", "source", "onto", "ignoreKeywordRequirement", "note" },
        ["ChooseRace"] = new() { "op", "remember", "note" },
        // M3.2b
        ["PreventEvent"] = new() { "op", "note" },
        ["SetRaces"] = new() { "op", "races", "targets", "note" },
        ["WinRound"] = new() { "op", "player", "note" },
        ["DuelAllOpposed"] = new() { "op", "simultaneous", "note" },
        ["RemainInPlay"] = new() { "op", "targets", "replaces", "note" },
        ["AddAssembledRank"] = new() { "op", "targets", "amount", "note" },
        ["RevealWarzones"] = new() { "op", "count", "add", "note" },
        // M3.3
        ["RedirectDeploy"] = new() { "op", "node", "note" },
        ["GainControl"] = new() { "op", "targets", "moveTo", "entersPlay", "note" },
        ["Retarget"] = new() { "op", "targets", "optional", "note" },
        // Spacecraft
        ["Retire"] = new() { "op", "targets", "note" },
        ["DuelOpposedSpacecraft"] = new() { "op", "simultaneous", "note" },
    };

    private static readonly HashSet<string> NamedSelectors = new()
    {
        "Self", "OpposingUnit", "OpposingLeader", "Event.Unit", "Event.Order", "Event.TargetNode", "Event.Unit.OpposingUnit",
    };

    private static readonly HashSet<string> ChooseKeys = new() { "choose", "filter", "min", "max", "count", "perPlayer", "name" };

    internal static readonly HashSet<string> FilterKeys = new()
    {
        "zone", "kind", "controller", "race", "notRace", "unitClass", "excludeSelf", "opposed", "sharesRaceWith",
        "excludeBound", "baseRankLessThan", "opposingUnitRace", "orderType", "empty", "locked", "nodeKind", "side", "nodeOf",
        "legalFor", // M3.3: a node the Unit being Deployed (Event.Unit) / the op's chosen Unit (Target) may legally go to
        "spacecraft", "crewed", "inSpacecraft", // Spacecraft module
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
            if (t == "PlayCost" && !PlayRules.IsCostSupported(e)) return false;
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
            if (!EffectKeys.Contains(p.Name)) return false; // params...

        // A scripted handler is live once the engine has it (Wormhole so far; Ace, Terraform... in M3.3).
        if (Str(effect, "script") is { } script) return Engine.GameEngine.Scripts.Contains(script);

        if (effect.TryGetProperty("condition", out var c) && !ConditionOk(c)) return false;
        if (effect.TryGetProperty("playCondition", out var pc) && !PlayRules.IsConditionSupported(pc)) return false;
        if (effect.TryGetProperty("event", out var ev))
        {
            var names = ev.ValueKind == JsonValueKind.Array ? ev.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : new List<string> { ev.GetString() ?? "" };
            if (names.Any(n => !Events.Contains(n))) return false;
        }
        if (effect.TryGetProperty("filter", out var ef))
        {
            if (ef.ValueKind != JsonValueKind.Object) return false;
            foreach (var p in ef.EnumerateObject())
            {
                if (EventFilterKeys.Contains(p.Name)) continue;
                if (!FilterKeys.Contains(p.Name)) return false;
            }
        }
        if (effect.TryGetProperty("choose", out var ch) && !SelectorOk(ch)) return false;
        if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) return false;
        foreach (var op in ops.EnumerateArray())
            if (!OpOk(op)) return false;
        return true;
    }

    /// <summary>Can every op of this "do" list run? (Abominations' granted action body.)</summary>
    internal static bool OpsLive(JsonElement ops) =>
        ops.ValueKind == JsonValueKind.Array && ops.GetArrayLength() > 0 && ops.EnumerateArray().All(OpOk);

    private static bool OpOk(JsonElement op)
    {
        string name = op.TryGetProperty("op", out var n) ? n.GetString() ?? "" : "";
        if (!OpKeys.TryGetValue(name, out var keys)) return false;
        foreach (var p in op.EnumerateObject())
            if (!keys.Contains(p.Name)) return false;

        foreach (var selKey in new[] { "targets", "nodes", "a", "b", "node", "source", "onto", "moveTo" })
            if (op.TryGetProperty(selKey, out var sel) && !SelectorOk(sel)) return false;
        if (op.TryGetProperty("filter", out var f) && !FilterOk(f)) return false;

        switch (name)
        {
            case "ModifyRank":
                if (op.TryGetProperty("duration", out var d) && d.GetString() != "WhileInPlay") return false;
                return op.TryGetProperty("amount", out var amt) && AmountOk(amt);
            case "SetBaseRank":
                return op.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number;
            case "SetRaces":
                return op.TryGetProperty("races", out var rs) && rs.ValueKind == JsonValueKind.Array
                       && rs.EnumerateArray().All(x => Enum.TryParse<Race>(x.GetString(), out _));
            case "WinRound":
                return Str(op, "player") is "You" or "Event.Player";
            case "RemainInPlay":
                return Str(op, "targets") == "Self";
            case "AddAssembledRank":
                return Str(op, "amount") == "Event.AssembledRank";
            case "RedirectDeploy":
                return op.TryGetProperty("node", out var rn) && rn.ValueKind == JsonValueKind.Object;
            case "GainControl":
            {
                // Commandeer: the Order being Issued. One of Us: a chosen Unit in play plus the node it moves to.
                if (op.TryGetProperty("entersPlay", out var ep) && ep.ValueKind != JsonValueKind.False) return false;
                if (Str(op, "targets") == "Event.Order") return !op.TryGetProperty("moveTo", out _);
                return op.TryGetProperty("targets", out var gt) && gt.ValueKind == JsonValueKind.Object
                       && op.TryGetProperty("moveTo", out var mt) && mt.ValueKind == JsonValueKind.Object;
            }
            case "Retarget":
                return Str(op, "targets") == "Event.Order";
            case "RevealWarzones":
                return op.TryGetProperty("count", out var rc) && rc.ValueKind == JsonValueKind.Number;
            case "Manipulate":
                if (Str(op, "player") is { } mp && mp != "Event.Card.Controller") return false;
                return op.TryGetProperty("amount", out var m) && m.ValueKind == JsonValueKind.Number;
            case "Suppress":
                return op.TryGetProperty("what", out var w) && w.GetString() == "AllAbilities";
            case "Draw":
                if (op.TryGetProperty("distribution", out var dist) && dist.GetString() != "SameAsRetired") return false;
                if (op.TryGetProperty("deckChoice", out var dc) && dc.GetString() != "PerCard") return false;
                if (op.TryGetProperty("deck", out var deck) && deck.GetString() is not ("Units" or "Orders")) return false;
                if (Str(op, "player") is { } dp && dp != "Event.Source.Controller") return false;
                return op.TryGetProperty("deck", out _) || op.TryGetProperty("count", out _);
            case "MoveToStandby":
                if (op.TryGetProperty("ifHasKeyword", out var hk))
                {
                    if (hk.ValueKind != JsonValueKind.Object) return false;
                    foreach (var p in hk.EnumerateObject())
                        if (p.Name != "Assemble" || p.Value.GetString() != "ReturnToHand") return false;
                }
                if (op.TryGetProperty("ifNoRoom", out var nr) && nr.GetString() != "Retire") return false;
                if (op.TryGetProperty("replaces", out var rp) && rp.GetString() != "Retire") return false;
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
                if (sel.TryGetProperty("lowestRank", out var low))
                {
                    foreach (var p in sel.EnumerateObject())
                        if (p.Name is not ("lowestRank" or "ties")) return false;
                    return FilterOk(low) && (!sel.TryGetProperty("ties", out var ties) || ties.GetString() == "All");
                }
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
                case "exists":
                case "notExists":
                    if (!FilterOk(p.Value)) return false;
                    break;
                case "selfOpposed": break;
                case "crewed": // Spacecraft: the source spacecraft has / hasn't a crew
                    if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                    break;
                default: return false;
            }
        }
        return true;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
