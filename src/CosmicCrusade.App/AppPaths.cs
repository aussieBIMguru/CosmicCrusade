using System.IO;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App;

/// <summary>Locations of the data folder copied next to the exe, plus the shared card database.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public static string CardsJson => Path.Combine(DataDir, "cards.json");
    public static string LayoutJson => Path.Combine(DataDir, "card-layout.json");

    public static string Art(string id) => Path.Combine(DataDir, "art", id + ".jpg");
    public static string PrintedImage(string id) => Path.Combine(DataDir, "images", id + ".jpg");
    public static string Strip(string style) => Path.Combine(DataDir, "frames", $"strip_{style}.png");

    private static CardDatabase? _db;
    public static CardDatabase Database => _db ??= CardDatabase.Load(CardsJson);
}
