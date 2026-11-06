using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// The effects pass: the engine tells the UI enough to pick an effect – which draws came from a card, which Rank
/// changes are Manipulates, and when a Unit's races change.
/// </summary>
public class EffectsPassTests
{
    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static CardInstance Give(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static GameState Board()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        return s;
    }

    private static ScriptedController Deploys(CardInstance card, Node node) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == card && a.Target == node));

    [Fact]
    public async Task Draws_from_an_ability_carry_their_source_and_start_up_draws_do_not()
    {
        var s = Board();
        var neuroling = Give(s, 0, Def("Neuroling")); // When you play: draw up to 2 Units
        while (s.Players[0].HandUnitCount < 5) Give(s, 0, TestData.Brute(Race.Blaarg));
        while (s.Players[1].HandUnitCount < 4) Give(s, 1, TestData.Brute(Race.Blaarg));
        for (int i = 0; i < 4; i++) s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Overmind)), PileKind.UnitDeck);

        var p0 = Deploys(neuroling, s.Players[0].UnitNodes[0]);
        p0.Chooser = r => r.Purpose == "Draw" ? new[] { r.Options.Count - 1 } : null;
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        var draws = sink.Events.OfType<CardDrawnEvent>().ToList();
        Assert.Contains(draws, d => d.Player == 1 && d.Source is null);        // Start-up refill
        Assert.Equal(2, draws.Count(d => d.Player == 0 && d.Source == neuroling));
    }

    [Fact]
    public async Task Manipulate_is_reported_as_a_manipulate()
    {
        var s = Board();
        var graftling = Give(s, 0, Def("Graftling")); // When you play: you may Manipulate 2
        var target = TestData.Place(s, TestData.Brute(Race.Necrolite), s.Players[1].UnitNodes[0]);

        var p0 = Deploys(graftling, s.Players[0].UnitNodes[0]);
        p0.Chooser = r => r.Purpose == "Manipulate" ? new[] { r.Options.ToList().FindIndex(o => o.Card == target) } : null;
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Contains(sink.Events, e => e is RankChangedEvent { Kind: RankChangeKind.Manipulate } r && r.Card == target);
    }

    [Fact]
    public async Task Becoming_a_necrolite_raises_a_races_changed_event()
    {
        // Abominations' granted action: Nullify a Necrolite from hand → a non-Necrolite Unit gets +2 and becomes a Necrolite.
        var s = Board();
        TestData.Place(s, Def("Abominations"), s.Players[0].OrderNodes[0]);
        var cost = Give(s, 0, TestData.Brute(Race.Necrolite));
        var unit = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[0].UnitNodes[0]);

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<GrantedAction>().First());
        p0.Chooser = r => r.Options.ToList().FindIndex(o => o.Card == cost || o.Card == unit) is >= 0 and var i ? new[] { i } : null;
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Contains(sink.Events, e => e is RacesChangedEvent r && r.Card == unit && r.Races.Contains(Race.Necrolite));
        Assert.Contains(Race.Necrolite, StaticLayer.Races(s, unit));
    }

    [Fact]
    public async Task Shoot_first_reports_the_random_node_it_picked()
    {
        var s = Board();
        TestData.Place(s, Def("Shoot First, Think Second"), s.Players[1].OrderNodes[0]); // the AI's: it hits our deploys
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployAtRandomAction>().First(a => a.Card == brute));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        var roll = Assert.Single(sink.Events.OfType<RandomDeployNodeChosenEvent>());
        Assert.Same(brute, roll.Card);
        Assert.Contains(roll.Chosen, roll.Candidates);
        Assert.Same(roll.Chosen, brute.Location.Node);
    }
}
