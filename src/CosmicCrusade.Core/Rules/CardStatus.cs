using System.Text.Json;
using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>How much of a card's text the engine can run.</summary>
public enum Implementation
{
    /// <summary>Every effect runs (or the card has none, e.g. a vanilla Brute).</summary>
    Full,

    /// <summary>Some effects run, some don't yet.</summary>
    Partial,

    /// <summary>The card has effects and none of them run yet.</summary>
    None,
}

/// <summary>
/// Implementation status per card, for the "not implemented yet" watermark on card faces and in the setup card list.
/// Each <c>effects[]</c> entry is checked the way the engine will treat it:
///   • Static → <see cref="StaticLayer"/> compiles it (all-or-nothing per entry);
///   • PlayCost → <see cref="PlayRules"/> supports it;
///   • Replacement → one of the events the engine replaces (Glorpaglorp, Fallout Shelter, Black Hole);
///   • StateCheck → Senate Chambers' "fills every node" check;
///   • every other trigger the engine fires → <see cref="EffectSupport.IsEffectLive"/> (scripts count once the
///     engine has them).
/// </summary>
public static class CardStatus
{
    /// <summary>Triggers the engine fires through the effect interpreter.</summary>
    private static readonly HashSet<string> InterpretedTriggers = new()
    {
        "OnPlay", "OnResolve", "React", "OnEvent", "CleanUp", "EndOfMain", "OnLeavePlay", "OnSelfNullified", "OnIssue",
        "AsEnters",
        "OnCrewed", "OnCrewNullified", // Spacecraft
    };

    private static readonly HashSet<string> ReplacedEvents = new() { "UnitNullifying", "UnitRetiring", "UnitMovingToStandby" };

    private static readonly Dictionary<string, (Implementation Status, string[] Missing)> _cache = new();

    public static Implementation Of(CardDefinition def) => Get(def).Status;

    /// <summary>Short descriptions of the parts that don't run yet (for tooltips).</summary>
    public static IReadOnlyList<string> Missing(CardDefinition def) => Get(def).Missing;

    /// <summary>A one-line description for tooltips: "Not implemented yet", "Partly implemented: ...".</summary>
    public static string Describe(CardDefinition def) => Of(def) switch
    {
        Implementation.None => "Not implemented yet: the card has no effect in the app.",
        Implementation.Partial => "Partly implemented. Not yet: " + string.Join(", ", Missing(def)) + ".",
        _ => "Fully implemented.",
    };

    private static (Implementation Status, string[] Missing) Get(CardDefinition def)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(def.Id, out var v)) _cache[def.Id] = v = Compute(def);
            return v;
        }
    }

    private static (Implementation Status, string[] Missing) Compute(CardDefinition def)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return (Implementation.Full, Array.Empty<string>());
        int total = 0, live = 0;
        var missing = new List<string>();
        foreach (var e in def.Effects.EnumerateArray())
        {
            string trigger = e.TryGetProperty("trigger", out var t) ? t.GetString() ?? "" : "";
            total++;
            if (IsLive(e, trigger, def)) live++;
            else missing.Add(Label(e, trigger));
        }
        var status = total == 0 || live == total ? Implementation.Full
            : live == 0 ? Implementation.None
            : Implementation.Partial;
        return (status, missing.Distinct().ToArray());
    }

    private static bool IsLive(JsonElement e, string trigger, CardDefinition def)
    {
        switch (trigger)
        {
            case "Static":
                return StaticLayer.IsStaticEntryLive(e, def);
            case "PlayCost":
                return PlayRules.IsCostSupported(e);
            case "Replacement":
                if (!e.TryGetProperty("replaces", out var r) || !r.TryGetProperty("event", out var ev)) return false;
                return ev.ValueKind == JsonValueKind.Array
                    ? ev.EnumerateArray().All(x => ReplacedEvents.Contains(x.GetString() ?? ""))
                    : ReplacedEvents.Contains(ev.GetString() ?? "");
            case "StateCheck":
                return e.TryGetProperty("condition", out var c) && c.TryGetProperty("playerFillsAll", out _);
            default:
                return InterpretedTriggers.Contains(trigger) && EffectSupport.IsEffectLive(e);
        }
    }

    private static string Label(JsonElement e, string trigger)
    {
        if (e.TryGetProperty("script", out var s) && s.ValueKind == JsonValueKind.String) return s.GetString()!;
        if (e.TryGetProperty("do", out var ops) && ops.ValueKind == JsonValueKind.Array)
        {
            var names = ops.EnumerateArray()
                .Select(o => o.TryGetProperty("op", out var n) ? n.GetString() : null)
                .Where(n => n is not null).Distinct().ToList();
            if (names.Count > 0) return $"{trigger} ({string.Join("+", names)})";
        }
        return trigger;
    }
}
