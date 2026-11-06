using System.Text.Json;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Rules;

/// <summary>
/// Assemble (spec §1, ruling #18): a Main Phase action that uses your turn. Retire a card with Assemble from your
/// hand and add its Rank to an allied Synthenoid in play; the Rank stays while that Unit stays in play.
///
/// The keyword comes from the card itself (Sprocket Spider, Cogwork Soldier) or from a GrantKeyword static on an
/// Ongoing Order in play or the Active Warzone (Cybernetic Enhancements, Production Facility).
/// <c>"assembleOnto": "AnyUnit"</c> (Cybernetic Enhancements) also allows non-Synthenoid targets.
/// A Suppressed card neither has nor grants Assemble (Suppressing Goop: "even when not in play"). Immune Units
/// can't be Assembled onto.
/// </summary>
public static class AssembleRules
{
    public const string Keyword = "Assemble";

    private sealed record Grant(Race? Race, bool AnyUnitTarget);

    private static readonly Dictionary<string, Grant[]> _grants = new();

    /// <summary>Does this card currently have Assemble?</summary>
    public static bool HasAssemble(GameState state, CardInstance card)
    {
        if (!card.Definition.IsUnit) return false;
        if (StaticLayer.IsSuppressed(state, card)) return false;
        if (card.Definition.Keywords.Contains(Keyword, StringComparer.OrdinalIgnoreCase)) return true;
        return ActiveGrants(state).Any(g => g.Race is null || StaticLayer.HasRace(state, card, g.Race.Value));
    }

    /// <summary>Can <paramref name="player"/> Assemble <paramref name="card"/> (in hand) onto <paramref name="target"/>?</summary>
    public static bool CanAssembleOnto(GameState state, int player, CardInstance card, CardInstance target)
    {
        if (card.Location.Kind != LocationKind.Hand || card.Location.Player != player) return false;
        if (!HasAssemble(state, card)) return false;
        if (ReferenceEquals(card, target)) return false;

        // Allied (same controller = same side, ruling #15) and in play (Unit or Leader node; Standby is not in play).
        var node = target.Location.Node;
        if (target.Location.Kind != LocationKind.Node || node is null || !node.IsScoring || node.Side != player) return false;

        if (StaticLayer.IsImmune(state, target)) return false;

        if (StaticLayer.HasRace(state, target, Race.Synthenoid)) return true;
        return ActiveGrants(state).Any(g => g.AnyUnitTarget && (g.Race is null || StaticLayer.HasRace(state, card, g.Race.Value)));
    }

    public static IEnumerable<CardInstance> Targets(GameState state, int player, CardInstance card) =>
        state.Players[player].UnitsInPlay.Where(t => CanAssembleOnto(state, player, card, t));

    /// <summary>Rank added by assembling this card: its printed Rank (it is in hand, so nothing modifies it).</summary>
    public static int AssembledRank(CardInstance card) => card.Definition.BaseRank ?? 0;

    // ------------------------------------------------------------------ granted keyword

    /// <summary>Does this card (an Ongoing or Warzone) grant Assemble while it is active?</summary>
    public static bool GrantsAssemble(CardDefinition def) => GrantsOf(def).Length > 0;

    private static IEnumerable<Grant> ActiveGrants(GameState state)
    {
        foreach (var p in state.Players)
            foreach (var n in p.OrderNodes)
                if (n.Card is not null && !StaticLayer.IsSuppressed(state, n.Card))
                    foreach (var g in GrantsOf(n.Card.Definition)) yield return g;
        foreach (var wz in state.ActiveWarzones)
            if (!StaticLayer.IsSuppressed(state, wz))
                foreach (var g in GrantsOf(wz.Definition)) yield return g;
    }

    private static Grant[] GrantsOf(CardDefinition def)
    {
        lock (_grants)
        {
            if (!_grants.TryGetValue(def.Id, out var list))
                _grants[def.Id] = list = Parse(def);
            return list;
        }
    }

    private static Grant[] Parse(CardDefinition def)
    {
        var result = new List<Grant>();
        if (def.Effects.ValueKind != JsonValueKind.Array) return result.ToArray();
        foreach (var effect in def.Effects.EnumerateArray())
        {
            if (!effect.TryGetProperty("trigger", out var t) || t.GetString() != "Static") continue;
            if (!effect.TryGetProperty("do", out var ops) || ops.ValueKind != JsonValueKind.Array) continue;
            foreach (var op in ops.EnumerateArray())
            {
                if (!op.TryGetProperty("op", out var name) || name.GetString() != "GrantKeyword") continue;
                if (!op.TryGetProperty("keyword", out var kw) || !string.Equals(kw.GetString(), Keyword, StringComparison.OrdinalIgnoreCase)) continue;

                Race? race = null;
                if (op.TryGetProperty("targets", out var tg) && tg.ValueKind == JsonValueKind.Object
                    && tg.TryGetProperty("race", out var r) && r.ValueKind == JsonValueKind.String)
                    race = CardDatabase.ParseEnum<Race>(r.GetString()!);
                bool any = op.TryGetProperty("assembleOnto", out var onto) && onto.GetString() == "AnyUnit";
                result.Add(new Grant(race, any));
            }
        }
        return result.ToArray();
    }
}
