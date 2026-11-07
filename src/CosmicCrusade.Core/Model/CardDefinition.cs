using System.Text.Json;

namespace CosmicCrusade.Core.Model;

/// <summary>
/// Immutable printed card, loaded from cards.json. Game-time state lives on <see cref="CardInstance"/>.
/// </summary>
public sealed class CardDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? PrintedName { get; init; }
    public required string Set { get; init; }
    public required CardKind Kind { get; init; }
    public UnitClass? UnitClass { get; init; }
    public OrderType? OrderType { get; init; }

    /// <summary>Printed base Rank. Null for Orders/Warzones, and for "X" cards (see <see cref="BaseRankIsX"/>).</summary>
    public int? BaseRank { get; init; }
    public bool BaseRankIsX { get; init; }

    public required IReadOnlyList<Race> Races { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = "";
    public string Flavor { get; init; } = "";
    public string Image { get; init; } = "";

    /// <summary>Raw machine-readable effects (interpreted by the effect engine in a later milestone).</summary>
    public JsonElement Effects { get; init; }

    public bool IsUnit => Kind == CardKind.Unit;
    public bool IsOrder => Kind == CardKind.Order;
    public bool IsLeader => UnitClass == Model.UnitClass.Leader;
    public bool IsChampion => UnitClass == Model.UnitClass.Champion;

    /// <summary>Archons module card (set "Archons"): lives in the Archon deck, never in the normal decks or piles.</summary>
    public bool IsArchon => Set.Equals("Archons", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the card has no effects at all (e.g. Brutes). Useful while the effect engine is incomplete.</summary>
    public bool IsVanilla => Effects.ValueKind != JsonValueKind.Array || Effects.GetArrayLength() == 0;

    public override string ToString() => $"{Name} [{Id}]";
}
