using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

public class CardDataTests
{
    [Fact]
    public void Loads_all_237_cards()
    {
        // 177 base + Warzones cards, the 12 Archons (2 Leader Units, 10 Orders), and the 48 Spacecraft entries
        // (30 spacecraft, the Fighter token, 12 Orders, 5 Warzones).
        Assert.Equal(237, TestData.Db.All.Count);
        Assert.Equal(120, TestData.Db.All.Count(c => c.IsUnit));
        Assert.Equal(85, TestData.Db.All.Count(c => c.IsOrder));
        Assert.Equal(32, TestData.Db.All.Count(c => c.Kind == CardKind.Warzone));
        Assert.Equal(12, TestData.Db.All.Count(c => c.IsArchon));
        Assert.Equal(48, TestData.Db.All.Count(c => c.IsSpacecraftSet));
        Assert.Equal(31, TestData.Db.All.Count(c => c.IsSpacecraft));
        Assert.Single(TestData.Db.All, c => c.IsToken);
    }

    [Fact]
    public void Cure_Failure_has_X_rank()
    {
        var x = TestData.Db.All.Single(c => c.BaseRankIsX);
        Assert.Null(x.BaseRank);
    }

    [Theory]
    [InlineData(Race.SpaceCowboy, Race.Necrolite)]
    [InlineData(Race.Necrolite, Race.Overmind)]
    [InlineData(Race.Overmind, Race.Blaarg)]
    [InlineData(Race.Blaarg, Race.Synthenoid)]
    [InlineData(Race.Synthenoid, Race.SpaceCowboy)]
    public void Weakness_cycle(Race winner, Race loser)
    {
        Assert.True(TestData.Db.Beats(winner, loser));
        Assert.False(TestData.Db.Beats(loser, winner));
    }

    [Fact]
    public void Galactic_Council_is_neutral()
    {
        foreach (Race r in Enum.GetValues<Race>())
        {
            Assert.False(TestData.Db.Beats(Race.GalacticCouncil, r));
            Assert.False(TestData.Db.Beats(r, Race.GalacticCouncil));
        }
    }

    [Fact]
    public void Standard_pool_builds_150_units_and_80_orders()
    {
        var s = TestData.NewGame();
        Assert.Equal(150, s.UnitDeck.Count);
        Assert.Equal(80, s.OrderDeck.Count);
    }

    [Fact]
    public void Warzones_adds_12_orders()
    {
        var s = TestData.NewGame(configure: o => o.Warzones = true);
        Assert.Equal(92, s.OrderDeck.Count);
    }

    [Fact]
    public void No_leaders_removes_leaders_and_related_cards()
    {
        var s = TestData.NewGame(configure: o => o.Leaders = false);
        Assert.Equal(116, s.UnitDeck.Count);
        Assert.Equal(77, s.OrderDeck.Count);
        Assert.DoesNotContain(s.UnitDeck, c => c.Definition.IsLeader);
        Assert.All(s.Players, p => Assert.Null(p.LeaderNode));
    }

    [Fact]
    public void True_singleton_has_one_of_each()
    {
        var s = TestData.NewGame(configure: o => o.DeckMode = DeckMode.TrueSingleton);
        Assert.Equal(87, s.UnitDeck.Count);
        Assert.Equal(s.UnitDeck.Count, s.UnitDeck.Select(c => c.Definition.Id).Distinct().Count());
    }

    [Fact]
    public void Same_seed_same_deck_order()
    {
        var a = TestData.NewGame(seed: 42).UnitDeck.Select(c => c.Definition.Id).ToList();
        var b = TestData.NewGame(seed: 42).UnitDeck.Select(c => c.Definition.Id).ToList();
        var c = TestData.NewGame(seed: 43).UnitDeck.Select(c => c.Definition.Id).ToList();
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Edited_copy_count_is_respected()
    {
        var brute = TestData.Brute(Race.Blaarg);
        var s = TestData.NewGame(configure: o => o.CopyCounts[brute.Id] = 7);
        Assert.Equal(7, s.UnitDeck.Count(c => c.Definition == brute));
    }
}
