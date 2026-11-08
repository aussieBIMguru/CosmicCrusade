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

    /// <summary>
    /// A player guide (HOW-TO-PLAY.md, RULEBOOK.md...): the docs folder next to the exe, or docs\players in the source
    /// tree when running from Visual Studio. Null if it can't be found.
    /// </summary>
    public static string? PlayerDoc(string file)
    {
        string installed = Path.Combine(AppContext.BaseDirectory, "docs", file);
        if (File.Exists(installed)) return installed;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string source = Path.Combine(dir.FullName, "docs", "players", file);
            if (File.Exists(source)) return source;
        }
        return null;
    }

    private static string? _fingerprint;

    /// <summary>Online play: a short fingerprint of cards.json, so both players are known to have the same cards.</summary>
    public static string DataFingerprint => _fingerprint ??= CosmicCrusade.Core.Net.NetProtocol.DataFingerprint(File.ReadAllText(CardsJson));
}
