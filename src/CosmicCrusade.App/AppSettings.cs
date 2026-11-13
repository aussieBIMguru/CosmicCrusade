using System.IO;
using System.Text.Json;

namespace CosmicCrusade.App;

/// <summary>
/// Remembered between runs: your player name, the online server address and the turn clock.
/// Stored in %AppData%\CosmicCrusade\settings.json.
/// </summary>
public sealed class AppSettings
{
    public string PlayerName { get; set; } = "";

    /// <summary>
    /// The relay to use for online games: your Cloudflare Worker address (e.g. cosmic-crusade-relay.yourname.workers.dev),
    /// or ws://localhost:5080 for the local test relay.
    /// </summary>
    public string ServerAddress { get; set; } = "";

    /// <summary>Turn clock: minutes per turn (0 = no clock). Hosting online, your opponent gets yours.</summary>
    public double TurnMinutes { get; set; } = 2;

    /// <summary>The name shown in games: your player name, or "You" if you haven't set one.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(PlayerName) ? "You" : PlayerName.Trim();

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CosmicCrusade");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // A broken settings file just means defaults.
        }
        if (string.IsNullOrWhiteSpace(settings.ServerAddress)) settings.ServerAddress = DefaultServer();
        return settings;
    }

    /// <summary>
    /// The server a build ships with: "defaultServer" in data\online.json (empty = the player types one). Lets the
    /// person who hosts the relay hand out an installer that already points at it.
    /// </summary>
    private static string DefaultServer()
    {
        try
        {
            string path = Path.Combine(AppPaths.DataDir, "online.json");
            if (!File.Exists(path)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("defaultServer", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";
        }
        catch
        {
            return "";
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to save settings isn't worth interrupting the game for.
        }
    }
}
