using System.IO;
using System.Text.Json;
using System.Windows.Media;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Rendering;

/// <summary>Race display names and colours from card-layout.json (with built-in fallbacks).</summary>
public sealed class CardLayout
{
    public sealed record RaceStyle(string Display, Color StripColor, Color TextColor);

    private readonly Dictionary<string, RaceStyle> _races = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GalacticCouncil"] = new("Galactic Council", Hex("#C86500"), Hex("#C86400")),
        ["SpaceCowboy"] = new("Space Cowboy", Hex("#FE0000"), Hex("#FE0101")),
        ["Necrolite"] = new("Necrolite", Hex("#006401"), Hex("#007D01")),
        ["Overmind"] = new("Overmind", Hex("#0000FE"), Hex("#0101FE")),
        ["Blaarg"] = new("Blaarg", Hex("#AFAF01"), Hex("#7D7E01")),
        ["Synthenoid"] = new("Synthenoid", Hex("#7D7D7D"), Hex("#4B4B4B")),
        ["Neutral"] = new("Neutral", Hex("#321A1A"), Hex("#4B4B4B")),
        ["Warzone"] = new("Warzone", Hex("#321A1A"), Hex("#000000")),
    };

    private static CardLayout? _instance;
    public static CardLayout Instance => _instance ??= Load(AppPaths.LayoutJson);

    public RaceStyle Style(Race race) => _races[race.ToString()];
    public RaceStyle Style(string key) => _races.TryGetValue(key, out var s) ? s : _races["Neutral"];

    /// <summary>Glow/accent colour for a card (its first race's strip colour, brightened for dark backgrounds).</summary>
    public Color Accent(CardDefinition def)
    {
        if (def.Kind == CardKind.Warzone || def.Races.Count == 0) return Hex("#B08CFF");
        var c = Style(def.Races[0]).StripColor;
        return def.Races[0] switch
        {
            Race.Overmind => Hex("#4A7DFF"),
            Race.Necrolite => Hex("#22C55E"),
            Race.Synthenoid => Hex("#C0C8D0"),
            Race.Neutral => Hex("#B08CFF"),
            _ => c,
        };
    }

    private static CardLayout Load(string path)
    {
        var layout = new CardLayout();
        if (!File.Exists(path)) return layout;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("races", out var races))
            {
                foreach (var r in races.EnumerateObject())
                {
                    layout._races[r.Name] = new RaceStyle(
                        r.Value.GetProperty("display").GetString() ?? r.Name,
                        Hex(r.Value.GetProperty("stripColor").GetString()!),
                        Hex(r.Value.GetProperty("textColor").GetString()!));
                }
            }
        }
        catch (Exception)
        {
            // Keep the built-in fallbacks if the file is malformed.
        }
        return layout;
    }

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
