using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

public class PlacementTests
{
    private static CardDefinition FirstOfClass(UnitClass cls) => TestData.Db.All.First(c => c.UnitClass == cls);

    [Fact]
    public void Leaders_only_on_leader_node()
    {
        var leader = FirstOfClass(UnitClass.Leader);
        Assert.True(Placement.ClassAllows(leader, NodeKind.Leader));
        Assert.False(Placement.ClassAllows(leader, NodeKind.Unit));
    }

    [Fact]
    public void Champions_on_unit_or_leader_node()
    {
        var champ = FirstOfClass(UnitClass.Champion);
        Assert.True(Placement.ClassAllows(champ, NodeKind.Leader));
        Assert.True(Placement.ClassAllows(champ, NodeKind.Unit));
        Assert.False(Placement.ClassAllows(champ, NodeKind.Standby));
    }

    [Fact]
    public void Minions_only_on_unit_nodes()
    {
        var minion = FirstOfClass(UnitClass.Minion);
        Assert.True(Placement.ClassAllows(minion, NodeKind.Unit));
        Assert.False(Placement.ClassAllows(minion, NodeKind.Leader));
    }

    [Fact]
    public void Cannot_deploy_to_locked_occupied_or_enemy_nodes()
    {
        var s = TestData.EmptyGame();
        var brute = TestData.Brute(Race.Blaarg);
        var card = s.CreateInstance(brute);
        var p0 = s.Players[0];

        p0.UnitNodes[0].IsLocked = true;
        TestData.Place(s, brute, p0.UnitNodes[1]);

        Assert.False(Placement.CanDeploy(s, 0, card, p0.UnitNodes[0]));
        Assert.False(Placement.CanDeploy(s, 0, card, p0.UnitNodes[1]));
        Assert.True(Placement.CanDeploy(s, 0, card, p0.UnitNodes[2]));
        Assert.False(Placement.CanDeploy(s, 0, card, s.Players[1].UnitNodes[2]));
    }

    [Fact]
    public void Locked_node_is_not_empty()
    {
        var n = new Node(0, NodeKind.Unit, 0) { IsLocked = true };
        Assert.False(n.IsEmpty);
    }
}

public class ScoringTests
{
    [Fact]
    public void Higher_total_wins_and_leader_node_counts()
    {
        var s = TestData.EmptyGame();
        var b = TestData.Brute(Race.Blaarg);
        TestData.Place(s, b, s.Players[0].UnitNodes[0], 3);
        TestData.Place(s, b, s.Players[0].LeaderNode!, 3);
        TestData.Place(s, b, s.Players[1].UnitNodes[0], 5);

        var r = Scoring.Score(s);
        Assert.Equal(6, r.Total0);
        Assert.Equal(5, r.Total1);
        Assert.Equal(0, r.Winner);
        Assert.False(r.DecidedByTiebreak);
    }

    [Fact]
    public void Tie_broken_by_opposed_pairs_and_occupied_beats_empty()
    {
        var s = TestData.EmptyGame();
        var b = TestData.Brute(Race.Blaarg);
        // P0: 2 + 2 = 4 across U1, U2.  P1: 4 on U1.  Totals tie at 4.
        TestData.Place(s, b, s.Players[0].UnitNodes[0], 2);
        TestData.Place(s, b, s.Players[0].UnitNodes[1], 2);
        TestData.Place(s, b, s.Players[1].UnitNodes[0], 4);

        var r = Scoring.Score(s);
        Assert.True(r.DecidedByTiebreak);
        Assert.Equal(1, r.PairsWon0); // U2 vs empty
        Assert.Equal(1, r.PairsWon1); // U1 4 vs 2
        Assert.Null(r.Winner);        // pairs tied too → nobody

        TestData.Place(s, b, s.Players[0].UnitNodes[2], 0); // Rank 0 still beats an empty node.
        r = Scoring.Score(s);
        Assert.Equal(2, r.PairsWon0);
        Assert.Equal(0, r.Winner);
    }

    [Fact]
    public void Negative_ranks_are_allowed()
    {
        var s = TestData.EmptyGame();
        var c = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        c.PersistentModifiers.Add(new RankModifier("Manipulate", -6));
        Assert.Equal(-2, RankCalculator.GetRank(s, c));
    }

    [Fact]
    public void No_leaders_mode_pairs_use_unit_nodes_only()
    {
        var s = TestData.EmptyGame(leaders: false);
        Assert.Equal(3, Scoring.Score(s).Pairs.Count);
    }
}

public class DuelTests
{
    [Fact]
    public void Lower_rank_loses()
    {
        var s = TestData.EmptyGame();
        var a = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0], 3);
        var b = TestData.Place(s, TestData.Brute(Race.Synthenoid), s.Players[1].UnitNodes[0], 5);
        Assert.Same(a, Duel.Loser(s, a, b));
    }

    [Fact]
    public void Tie_uses_current_races_weakness()
    {
        var s = TestData.EmptyGame();
        var blaarg = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);
        var synth = TestData.Place(s, TestData.Brute(Race.Synthenoid), s.Players[1].UnitNodes[0]);
        Assert.Same(synth, Duel.Loser(s, blaarg, synth)); // Blaarg ▶ Synthenoid

        blaarg.Races.Clear(); // e.g. The Voidslime removed its race
        Assert.Null(Duel.Loser(s, blaarg, synth));
    }

    [Fact]
    public void Simultaneous_duels_compare_first()
    {
        var s = TestData.EmptyGame();
        var b = TestData.Brute(Race.Blaarg);
        var a1 = TestData.Place(s, b, s.Players[0].UnitNodes[0], 1);
        var b1 = TestData.Place(s, b, s.Players[1].UnitNodes[0], 2);
        var a2 = TestData.Place(s, b, s.Players[0].UnitNodes[1], 4);
        var b2 = TestData.Place(s, b, s.Players[1].UnitNodes[1], 3);
        var losers = Duel.LosersOfSimultaneous(s, new[] { (a1, b1), (a2, b2) });
        Assert.Equal(new[] { a1, b2 }, losers);
    }
}

public class PlacementOverrideTests
{
    private static CardInstance InHand(GameState s, string id, int player = 0)
    {
        var c = s.CreateInstance(TestData.Db[id]);
        s.MoveToHand(c, player);
        return c;
    }

    [Fact]
    public void Visiting_Diplomat_can_go_on_any_unit_node_but_not_leader_nodes()
    {
        var s = TestData.EmptyGame();
        var diplomat = InHand(s, "0U01");
        var nodes = Placement.LegalDeployNodes(s, 0, diplomat).ToList();
        Assert.Equal(6, nodes.Count);
        Assert.All(nodes, n => Assert.Equal(NodeKind.Unit, n.Kind));
        Assert.Contains(nodes, n => n.Side == 1);
    }

    [Fact]
    public void The_Creeper_can_go_on_either_leader_node()
    {
        var s = TestData.EmptyGame();
        var creeper = InHand(s, "2L06");
        var nodes = Placement.LegalDeployNodes(s, 0, creeper).ToList();
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Equal(NodeKind.Leader, n.Kind));
    }

    [Fact]
    public void Ordinary_units_stay_on_their_own_side()
    {
        var s = TestData.EmptyGame();
        var brute = InHand(s, TestData.Brute(Race.Blaarg).Id);
        Assert.All(Placement.LegalDeployNodes(s, 0, brute), n => Assert.Equal(0, n.Side));
    }

    [Fact]
    public void The_Oculus_lets_everyone_use_opponent_nodes()
    {
        var s = TestData.EmptyGame();
        var oculus = s.CreateInstance(TestData.Db["3W04"]);
        s.MoveToPile(oculus, PileKind.ActiveWarzone);
        var brute = InHand(s, TestData.Brute(Race.Blaarg).Id);
        Assert.Equal(6, Placement.LegalDeployNodes(s, 0, brute).Count());
    }

    [Fact]
    public void Unit_on_opponent_node_scores_for_that_side()
    {
        var s = TestData.EmptyGame();
        TestData.Place(s, TestData.Db["0U01"], s.Players[1].UnitNodes[0], 3);
        var r = Scoring.Score(s);
        Assert.Equal(0, r.Total0);
        Assert.Equal(3, r.Total1);
        Assert.Equal(1, s.Players[1].UnitNodes[0].Card!.Location.Player);
    }
}

public class WarzoneTests
{
    [Fact]
    public void Warzone_deck_has_35_cards_when_enabled()
    {
        Assert.Empty(TestData.NewGame().WarzoneDeck);
        Assert.Equal(35, TestData.NewGame(configure: o => o.Warzones = true).WarzoneDeck.Count);
        Assert.Equal(34, TestData.NewGame(configure: o => { o.Warzones = true; o.Leaders = false; }).WarzoneDeck.Count);
    }
}
