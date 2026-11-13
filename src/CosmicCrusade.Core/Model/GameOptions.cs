using System.Text.Json;

namespace CosmicCrusade.Core.Model;

/// <summary>Pre-game options (spec §10, game-modes.json).</summary>
public sealed class GameOptions
{
    public int RoundsToWin { get; set; } = 3;
    public EmptyDeckRule EmptyDeck { get; set; } = EmptyDeckRule.Reshuffle;
    public bool Warzones { get; set; }

    /// <summary>Archons module (optional, off by default): once per game each player may take an Archon.</summary>
    public bool Archons { get; set; }

    /// <summary>Spacecraft module (optional, off by default): spacecraft, their Orders and Warzones (design/spacecraft.md).</summary>
    public bool Spacecraft { get; set; }
    public bool Leaders { get; set; } = true;
    public DeckMode DeckMode { get; set; } = DeckMode.Standard;

    /// <summary>RNG seed. Same seed + same decisions = identical game.</summary>
    public int Seed { get; set; } = Environment.TickCount;

    /// <summary>Copy counts per card id (base section, plus warzones section when enabled). Editable in setup.</summary>
    public Dictionary<string, int> CopyCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Card ids removed in No Leaders mode (besides Leader-class units).</summary>
    public HashSet<string> LeaderRelatedCards { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>M4: how strong the AI seat plays (Easy = heuristic, Normal/Hard = Monte Carlo search).</summary>
    public CosmicCrusade.Core.Ai.AiDifficulty AiDifficulty { get; set; } = CosmicCrusade.Core.Ai.AiDifficulty.Normal;

    /// <summary>
    /// Turn clock: seconds each player has to decide on their turn (0 = no clock). App-only: the engine never reads it,
    /// so it can't affect the rules or the shuffle. A timed-out turn simply submits a Pass (see HumanController).
    /// </summary>
    public int TurnSeconds { get; set; } = 120;

    public int HandUnitTarget { get; set; } = 5;
    public int HandOrderTarget { get; set; } = 3;

    public GameOptions Clone()
    {
        var c = (GameOptions)MemberwiseClone();
        c.CopyCounts = new Dictionary<string, int>(CopyCounts, StringComparer.OrdinalIgnoreCase);
        c.LeaderRelatedCards = new HashSet<string>(LeaderRelatedCards, StringComparer.OrdinalIgnoreCase);
        return c;
    }

    /// <summary>
    /// Loads defaults from game-modes.json and the pool file it references (paths relative to <paramref name="dataDir"/>).
    /// </summary>
    public static GameOptions LoadDefaults(string dataDir)
    {
        var options = new GameOptions();
        var modesPath = Path.Combine(dataDir, "game-modes.json");
        string poolRel = "pools/standard.json";

        if (File.Exists(modesPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(modesPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("defaults", out var d))
            {
                if (d.TryGetProperty("pool", out var p)) poolRel = p.GetString() ?? poolRel;
                if (d.TryGetProperty("roundsToWin", out var r)) options.RoundsToWin = r.GetInt32();
                if (d.TryGetProperty("emptyDeck", out var e)) options.EmptyDeck = CardDatabase.ParseEnum<EmptyDeckRule>(e.GetString()!);
                if (d.TryGetProperty("warzones", out var w)) options.Warzones = w.GetBoolean();
                if (d.TryGetProperty("archons", out var ar)) options.Archons = ar.GetBoolean();
                if (d.TryGetProperty("spacecraft", out var sc)) options.Spacecraft = sc.GetBoolean();
                if (d.TryGetProperty("leaders", out var l)) options.Leaders = l.GetBoolean();
                if (d.TryGetProperty("deckMode", out var m)) options.DeckMode = CardDatabase.ParseEnum<DeckMode>(m.GetString()!);
            }
            if (root.TryGetProperty("leaderRelatedCards", out var lr))
                foreach (var id in lr.EnumerateArray()) options.LeaderRelatedCards.Add(id.GetString()!);
        }

        var pool = PoolDefinition.Load(Path.Combine(dataDir, poolRel));
        options.CopyCounts = pool.MergedCounts(includeWarzones: true);
        foreach (var kv in pool.Archons) options.CopyCounts[kv.Key] = kv.Value;
        // Spacecraft counts are always loaded; DeckBuilder only uses them when the module (and Warzones) is on.
        foreach (var kv in pool.Spacecraft) options.CopyCounts[kv.Key] = kv.Value;
        foreach (var kv in pool.SpacecraftWarzones) options.CopyCounts[kv.Key] = kv.Value;
        return options;
    }
}

/// <summary>A copy-count pool file (pools/standard.json).</summary>
public sealed class PoolDefinition
{
    public string Name { get; init; } = "";
    public Dictionary<string, int> Base { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Warzones { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Archons { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Spacecraft { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> SpacecraftWarzones { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> MergedCounts(bool includeWarzones)
    {
        var all = new Dictionary<string, int>(Base, StringComparer.OrdinalIgnoreCase);
        if (includeWarzones)
            foreach (var kv in Warzones) all[kv.Key] = kv.Value;
        return all;
    }

    public static PoolDefinition Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var pool = new PoolDefinition { Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "" };
        if (root.TryGetProperty("base", out var b))
            foreach (var p in b.EnumerateObject()) pool.Base[p.Name] = p.Value.GetInt32();
        if (root.TryGetProperty("warzones", out var w))
            foreach (var p in w.EnumerateObject()) pool.Warzones[p.Name] = p.Value.GetInt32();
        if (root.TryGetProperty("archons", out var a))
            foreach (var p in a.EnumerateObject()) pool.Archons[p.Name] = p.Value.GetInt32();
        if (root.TryGetProperty("spacecraft", out var sc))
            foreach (var p in sc.EnumerateObject()) pool.Spacecraft[p.Name] = p.Value.GetInt32();
        if (root.TryGetProperty("spacecraftWarzones", out var sw))
            foreach (var p in sw.EnumerateObject()) pool.SpacecraftWarzones[p.Name] = p.Value.GetInt32();
        return pool;
    }
}
