using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.Core.Tests;

internal static class TestData
{
    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly Lazy<CardDatabase> _db = new(() => CardDatabase.Load(Path.Combine(DataDir, "cards.json")));
    public static CardDatabase Db => _db.Value;

    public static GameOptions DefaultOptions(int seed = 1)
    {
        var o = GameOptions.LoadDefaults(DataDir);
        o.Seed = seed;
        return o;
    }

    /// <summary>An empty game (no decks) for hand-built board scenarios.</summary>
    public static GameState EmptyGame(bool leaders = true)
    {
        var o = DefaultOptions();
        o.Leaders = leaders;
        return new GameState(Db, o);
    }

    public static GameState NewGame(int seed = 1, Action<GameOptions>? configure = null)
    {
        var o = DefaultOptions(seed);
        configure?.Invoke(o);
        return GameEngine.CreateGame(Db, o);
    }

    /// <summary>A vanilla Brute (Rank 4, no abilities) of the given race, for scoring tests.</summary>
    public static CardDefinition Brute(Race race) =>
        Db.All.First(c => c.UnitClass == UnitClass.Brute && c.Races.Contains(race));

    /// <summary>Puts a fresh copy of <paramref name="def"/> on a node, with an optional fixed rank.</summary>
    public static CardInstance Place(GameState s, CardDefinition def, Node node, int? rank = null)
    {
        var c = s.CreateInstance(def);
        if (rank is not null) c.BaseRankOverride = rank;
        s.MoveToNode(c, node);
        return c;
    }
}
