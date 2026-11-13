using System.IO;
using System.Text.Json;

namespace CosmicCrusade.App.Controls;

/// <summary>How much the heavier ambient layers and particle effects draw.</summary>
public enum FxQuality
{
    Low,
    High,
}

/// <summary>
/// Effects settings (effects pass 3, backlog E). They are kept between games and app runs in
/// %LOCALAPPDATA%\CosmicCrusade\settings.json.
/// <list type="bullet">
/// <item><see cref="ReduceEffects"/>: tones down flashes, screen shake, lightning and camera pushes (accessibility, fast play).</item>
/// <item><see cref="Quality"/>: Low skips the heavier ambient layers, blur effects, idle motion and trails, and thins particles.</item>
/// </list>
/// </summary>
public static class FxSettings
{
    private static bool _reduce;
    private static FxQuality _quality = FxQuality.High;
    private static bool _loaded;

    /// <summary>Raised after either setting changes (the game view restarts its ambient layers).</summary>
    public static event Action? Changed;

    public static bool ReduceEffects
    {
        get { EnsureLoaded(); return _reduce; }
        set { EnsureLoaded(); if (_reduce == value) return; _reduce = value; Save(); Changed?.Invoke(); }
    }

    public static FxQuality Quality
    {
        get { EnsureLoaded(); return _quality; }
        set { EnsureLoaded(); if (_quality == value) return; _quality = value; Save(); Changed?.Invoke(); }
    }

    public static bool HighQuality => Quality == FxQuality.High;

    /// <summary>Multiplier for shake, camera pushes and the strength of flashes (1, or 0.3 when reduced).</summary>
    public static double Intensity => ReduceEffects ? 0.3 : 1;

    /// <summary>Opacity to use for a bright flash that would normally be <paramref name="full"/>.</summary>
    public static double Flash(double full) => ReduceEffects ? full * 0.3 : full;

    /// <summary>A particle count scaled for the quality setting (Low keeps about a third, at least one).</summary>
    public static int Count(int high) => HighQuality ? high : Math.Max(1, (int)Math.Round(high / 3.0));

    // ------------------------------------------------------------------ persistence

    private sealed record Stored(bool ReduceEffects, string Quality);

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CosmicCrusade", "settings.json");

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            if (s is null) return;
            _reduce = s.ReduceEffects;
            _quality = Enum.TryParse<FxQuality>(s.Quality, out var q) ? q : FxQuality.High;
        }
        catch
        {
            // A broken settings file just means defaults.
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored(_reduce, _quality.ToString())));
        }
        catch
        {
            // Settings are a convenience: never let saving break the game.
        }
    }
}
