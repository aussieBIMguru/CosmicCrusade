using System.Text.Json;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// When a React may be issued (spec §3, §7.2). Reads the <c>when</c> object of the card's React effect:
/// <code>
///   { "event": "UnitDeploying" }                                   Shadow Strike, Recolonize, Spreading Sickness, Gloop!, Forced Mitosis
///   { "event": "UnitDeploying", "targetNodeOpposed": true }        Target Practice
///   { "event": "OrderIssuing", "orderType": ["React", "Action"] }   Second Thought, Commandeer
///   { "event": "UnitNullifying" }                                  Respond in Kind, Already Dead
/// </code>
/// Either player may React to anything whose condition is met, including their own cards (spec §7.2).
/// </summary>
public static class ReactConditions
{
    /// <param name="MovesUnitToStandby">The React puts the deploying Unit into Standby (Spreading Sickness): it can't
    /// answer a Leader's deploy (ruling #36).</param>
    /// <param name="NeedsRedirectNode">Recolonize: there must be another legal node to send the Unit to.</param>
    /// <param name="NeedsCopySource">Forced Mitosis: there must be a Unit in play to copy.</param>
    public sealed record Condition(string Event, bool TargetNodeOpposed, IReadOnlyList<OrderType>? OrderTypes,
        bool MovesUnitToStandby = false, bool NeedsOpposingUnit = false, bool NeedsRedirectNode = false,
        bool NeedsCopySource = false);

    private static readonly Dictionary<string, Condition?> _cache = new();

    /// <summary>The parsed React condition, or null when the card is not a React (or has no condition).</summary>
    public static Condition? Of(CardDefinition def)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(def.Id, out var c))
                _cache[def.Id] = c = Parse(def);
            return c;
        }
    }

    /// <summary>Can <paramref name="react"/> respond to <paramref name="item"/> right now?</summary>
    public static bool IsMet(GameState state, CardInstance react, StackItem item)
    {
        if (react.Definition.OrderType != OrderType.React) return false;
        var c = Of(react.Definition);
        if (c is null || item.Cancelled) return false;

        // "As a Unit is Nullified": the Unit is still on its node while the window is open.
        if (item.Kind == StackItemKind.UnitNullifying)
        {
            if (c.Event != "UnitNullifying") return false;
            if (item.Card.Location is not { Kind: LocationKind.Node, Node.IsScoring: true }) return false;
            if (c.NeedsOpposingUnit && state.OpposingNode(item.Card.Location.Node!)?.Card is null) return false;
            return true;
        }
        if (item.Card.Location.Kind != LocationKind.Stack) return false;

        switch (c.Event)
        {
            case "UnitDeploying":
                if (item.Kind != StackItemKind.DeployUnit || item.TargetNode is null) return false;
                if (c.TargetNodeOpposed && state.OpposingNode(item.TargetNode)?.Card is null) return false;
                if (c.MovesUnitToStandby && !Placement.CanEnterStandby(item.Card)) return false;
                if (c.NeedsCopySource && !state.AllUnitsInPlay.Any()) return false;
                if (c.NeedsRedirectNode && !state.AllNodes.Any(n => !ReferenceEquals(n, item.TargetNode)
                                                                   && Placement.CanRedirectTo(state, item.Controller, item.Card, n)))
                    return false;
                return true;

            case "OrderIssuing":
                if (item.Kind != StackItemKind.IssueOrder) return false;
                return c.OrderTypes is null
                       || (item.Card.Definition.OrderType is { } t && c.OrderTypes.Contains(t));

            default:
                return false;
        }
    }

    private static Condition? Parse(CardDefinition def)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return null;
        foreach (var effect in def.Effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("trigger", out var t) || t.GetString() != "React") continue;
            if (!effect.TryGetProperty("when", out var when) || when.ValueKind != JsonValueKind.Object) continue;

            string ev = when.TryGetProperty("event", out var e) ? e.GetString() ?? "" : "";
            bool opposed = when.TryGetProperty("targetNodeOpposed", out var o) && o.ValueKind == JsonValueKind.True;
            List<OrderType>? types = null;
            if (when.TryGetProperty("orderType", out var ot))
            {
                types = new List<OrderType>();
                if (ot.ValueKind == JsonValueKind.String) types.Add(CardDatabase.ParseEnum<OrderType>(ot.GetString()!));
                else if (ot.ValueKind == JsonValueKind.Array)
                    foreach (var x in ot.EnumerateArray()) types.Add(CardDatabase.ParseEnum<OrderType>(x.GetString()!));
            }
            bool toStandby = effect.TryGetProperty("do", out var ops) && ops.ValueKind == JsonValueKind.Array
                && ops.EnumerateArray().Any(op => op.TryGetProperty("op", out var n) && n.GetString() == "MoveToStandby"
                                                  && op.TryGetProperty("targets", out var tg) && tg.ValueKind == JsonValueKind.String
                                                  && tg.GetString() == "Event.Unit");
            bool needsOpposing = ops.ValueKind == JsonValueKind.Array
                && ops.EnumerateArray().Any(op => op.TryGetProperty("targets", out var tg) && tg.ValueKind == JsonValueKind.String
                                                  && tg.GetString() == "Event.Unit.OpposingUnit");
            bool redirect = ops.ValueKind == JsonValueKind.Array
                && ops.EnumerateArray().Any(op => op.TryGetProperty("op", out var n) && n.GetString() == "RedirectDeploy");
            bool copy = effect.TryGetProperty("script", out var sc) && sc.GetString() == "EnterAsCopy";
            return new Condition(ev, opposed, types, toStandby, needsOpposing, redirect, copy);
        }
        return null;
    }
}

/// <summary>
/// The part of React resolution that M2 already performs: counters ("Nullify ... as it is being deployed / issued
/// (it is not played)"). Read from ops <c>{ "op": "Nullify", "targets": "Event.Unit" | "Event.Order", "cancelsPlay": true }</c>.
/// Everything else waits for the effect interpreter (M3).
/// </summary>
public static class ReactCounters
{
    private static readonly Dictionary<string, bool> _cache = new();

    /// <summary>True when the React's effect is a pure counter that M2 can resolve.</summary>
    public static bool IsCounter(CardDefinition def)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(def.Id, out bool v))
                _cache[def.Id] = v = Parse(def);
            return v;
        }
    }

    private static bool Parse(CardDefinition def)
    {
        if (def.Effects.ValueKind != JsonValueKind.Array) return false;
        foreach (var effect in def.Effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("trigger", out var t) || t.GetString() != "React") continue;
            if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) continue;
            if (ops.GetArrayLength() != 1) return false;
            var op = ops[0];
            return op.TryGetProperty("op", out var name) && name.GetString() == "Nullify"
                   && op.TryGetProperty("cancelsPlay", out var cp) && cp.ValueKind == JsonValueKind.True
                   && op.TryGetProperty("targets", out var tg) && tg.GetString() is "Event.Unit" or "Event.Order";
        }
        return false;
    }
}
