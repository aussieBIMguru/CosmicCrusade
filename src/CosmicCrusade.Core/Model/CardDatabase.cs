using System.Text.Json;

namespace CosmicCrusade.Core.Model;

/// <summary>All card definitions plus the race weakness cycle, loaded from cards.json.</summary>
public sealed class CardDatabase
{
    private readonly Dictionary<string, CardDefinition> _cards;
    private readonly Dictionary<Race, Race> _beats;

    public CardDatabase(IEnumerable<CardDefinition> cards, IReadOnlyDictionary<Race, Race> beats)
    {
        _cards = cards.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _beats = new Dictionary<Race, Race>(beats);
    }

    public IReadOnlyCollection<CardDefinition> All => _cards.Values;

    public CardDefinition this[string id] => _cards[id];

    public bool TryGet(string id, out CardDefinition card) => _cards.TryGetValue(id, out card!);

    /// <summary>True when <paramref name="a"/> wins ties against <paramref name="b"/> (weakness cycle).</summary>
    public bool Beats(Race a, Race b) => _beats.TryGetValue(a, out var target) && target == b;

    public static CardDatabase Load(string cardsJsonPath)
    {
        using var stream = File.OpenRead(cardsJsonPath);
        return Load(stream);
    }

    public static CardDatabase Load(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var beats = new Dictionary<Race, Race>();
        if (root.TryGetProperty("weakness", out var weakness))
        {
            foreach (var p in weakness.EnumerateObject())
                beats[ParseEnum<Race>(p.Name)] = ParseEnum<Race>(p.Value.GetString()!);
        }

        var cards = new List<CardDefinition>();
        foreach (var c in root.GetProperty("cards").EnumerateArray())
            cards.Add(ParseCard(c));

        return new CardDatabase(cards, beats);
    }

    private static CardDefinition ParseCard(JsonElement c)
    {
        int? baseRank = null;
        bool isX = false;
        if (c.TryGetProperty("baseRank", out var br))
        {
            if (br.ValueKind == JsonValueKind.Number) baseRank = br.GetInt32();
            else if (br.ValueKind == JsonValueKind.String) isX = true;
        }

        return new CardDefinition
        {
            Id = c.GetProperty("id").GetString()!,
            Name = c.GetProperty("name").GetString()!,
            PrintedName = OptString(c, "printedName"),
            Set = OptString(c, "set") ?? "Base",
            Kind = ParseEnum<CardKind>(c.GetProperty("kind").GetString()!),
            UnitClass = OptString(c, "unitClass") is { } uc ? ParseEnum<UnitClass>(uc) : null,
            OrderType = OptString(c, "orderType") is { } ot ? ParseEnum<OrderType>(ot) : null,
            BaseRank = baseRank,
            BaseRankIsX = isX,
            Races = c.TryGetProperty("races", out var races)
                ? races.EnumerateArray().Select(r => ParseEnum<Race>(r.GetString()!)).ToList()
                : new List<Race>(),
            Keywords = c.TryGetProperty("keywords", out var kw)
                ? kw.EnumerateArray().Select(k => k.GetString()!).ToList()
                : new List<string>(),
            Text = OptString(c, "text") ?? "",
            Flavor = OptString(c, "flavor") ?? "",
            Image = OptString(c, "image") ?? "",
            Effects = c.TryGetProperty("effects", out var fx) ? fx.Clone() : default,
        };
    }

    private static string? OptString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.Parse<T>(value, ignoreCase: true);
}
