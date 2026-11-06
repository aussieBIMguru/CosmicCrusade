using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// M3.2c: the last seven statics – revealed hands (The All-knowing, Transparency, Oracle's Arch), who picks the
/// deploy node (Shoot First, Think Second; Psychic Coercion), Abominations' granted action and Stagnation – plus the
/// implementation-status watermark data.
/// </summary>
public class M32cTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static CardInstance Place(GameState s, string name, Node node) => TestData.Place(s, Def(name), node);

    private static CardInstance Brute(GameState s, Race race, Node node) => TestData.Place(s, TestData.Brute(race), node);

    private static CardInstance Give(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance ActivateWarzone(GameState s, string name)
    {
        var wz = s.CreateInstance(Def(name));
        s.MoveToPile(wz, PileKind.ActiveWarzone);
        return wz;
    }

    private static Node U(GameState s, int player, int i) => s.Players[player].UnitNodes[i];
    private static Node L(GameState s, int player) => s.Players[player].LeaderNode!;
    private static Node O(GameState s, int player, int i = 0) => s.Players[player].OrderNodes[i];

    private static GameState Board()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        return s;
    }

    // ------------------------------------------------------------------ implementation status

    [Fact]
    public void Only_the_M3_3_cards_are_still_unimplemented()
    {
        var none = TestData.Db.All.Where(c => CardStatus.Of(c) == Implementation.None).Select(c => c.Name)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[]
        {
            "Ace, Council Tactician", "Commandeer", "Forced Mitosis", "Mr Morph", "One of Us", "Recolonize", "TR-1993.r",
            "Terraform",
        }.OrderBy(x => x, StringComparer.Ordinal), none);
        Assert.DoesNotContain(TestData.Db.All, c => CardStatus.Of(c) == Implementation.Partial);
        Assert.Equal(Implementation.Full, CardStatus.Of(TestData.Brute(Race.Blaarg))); // no effects at all
    }

    // ------------------------------------------------------------------ revealed hands

    [Fact]
    public void Transparency_reveals_only_the_opponents_hand()
    {
        var s = Board();
        Place(s, "Transparency", O(s, 0));
        Assert.True(StaticLayer.IsHandRevealed(s, 1));
        Assert.False(StaticLayer.IsHandRevealed(s, 0));
        Assert.NotNull(AiVision.VisibleHand(s, 0, 1));
        Assert.Null(AiVision.VisibleHand(s, 1, 0));
    }

    [Fact]
    public void The_All_knowing_on_the_AI_side_reveals_your_hand()
    {
        var s = Board();
        Place(s, "The All-knowing", L(s, 1));
        Assert.True(StaticLayer.IsHandRevealed(s, 0));
        Assert.False(StaticLayer.IsHandRevealed(s, 1));
        var card = Give(s, 0, TestData.Brute(Race.Blaarg));
        Assert.True(StaticLayer.CanSee(s, 1, card));
    }

    [Fact]
    public void Oracles_Arch_reveals_both_hands_and_stops_when_it_leaves()
    {
        var s = Board();
        var arch = ActivateWarzone(s, "Oracle's Arch");
        Assert.True(StaticLayer.IsHandRevealed(s, 0));
        Assert.True(StaticLayer.IsHandRevealed(s, 1));
        s.MoveToPileBottom(arch, PileKind.WarzoneDeck);
        Assert.False(StaticLayer.IsHandRevealed(s, 0));
    }

    // ------------------------------------------------------------------ Shoot First / Psychic Coercion

    [Fact]
    public async Task Shoot_first_makes_the_opponent_deploy_to_a_random_node()
    {
        var s = Board();
        Place(s, "Shoot First, Think Second", O(s, 0));
        var brute = Give(s, 1, TestData.Brute(Race.Blaarg));

        var legal = LegalActions.ForTurn(s, 1);
        Assert.DoesNotContain(legal, a => a is DeployFromHandAction);
        Assert.Contains(legal, a => a is DeployAtRandomAction r && r.Card == brute);
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is PassAction); // its controller isn't affected

        var p1 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployAtRandomAction>().Single());
        await OrderReactAssembleTests.Run(s, ScriptedController.PassForever(), p1);

        Assert.Equal(LocationKind.Node, brute.Location.Kind);
        Assert.Contains(brute.Location.Node!, s.Players[1].UnitNodes);
    }

    [Fact]
    public async Task Psychic_coercion_picks_the_node_before_the_unit()
    {
        var s = Board();
        Place(s, "Psychic Coercion", O(s, 0));
        var brute = Give(s, 1, TestData.Brute(Race.Blaarg));
        var leader = Give(s, 1, Def("The All-knowing"));

        var legal = LegalActions.ForTurn(s, 1);
        Assert.DoesNotContain(legal, a => a is DeployFromHandAction);
        Assert.Single(legal.OfType<CoercedDeployAction>());

        // The coercer sends them to the Leader Node: only the Leader fits there, so they can't choose.
        var p0 = new ScriptedController { StopWhenDone = false };
        p0.Chooser = r => r.Purpose == "CoerceNode"
            ? new[] { r.Options.Select((o, i) => (o, i)).First(x => x.o.Node!.Kind == NodeKind.Leader).i }
            : null;
        var p1 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<CoercedDeployAction>().Single());
        await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Contains(p0.ChoicesAsked, r => r.Purpose == "CoerceNode" && r.Options.All(o => o.Node!.Side == 1));
        Assert.Equal(L(s, 1), leader.Location.Node);
        Assert.Equal(LocationKind.Hand, brute.Location.Kind);
    }

    [Fact]
    public async Task Psychic_coercion_then_lets_the_deployer_choose_among_units_that_fit()
    {
        var s = Board();
        Place(s, "Psychic Coercion", O(s, 0));
        var a = Give(s, 1, TestData.Brute(Race.Blaarg));
        var b = Give(s, 1, TestData.Brute(Race.Synthenoid));

        var p0 = new ScriptedController { StopWhenDone = false };
        var p1 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<CoercedDeployAction>().Single());
        p1.Chooser = r => r.Purpose == "CoercedUnit" ? new[] { r.Options.Select((o, i) => (o, i)).First(x => x.o.Card == b).i } : null;
        await OrderReactAssembleTests.Run(s, p0, p1);

        var asked = Assert.Single(p1.ChoicesAsked, r => r.Purpose == "CoercedUnit");
        Assert.Equal(2, asked.Options.Count);
        Assert.Equal(LocationKind.Node, b.Location.Kind);
        Assert.Equal(LocationKind.Hand, a.Location.Kind);
    }

    [Fact]
    public void Psychic_coercion_beats_shoot_first_on_the_same_player()
    {
        var s = Board();
        Place(s, "Shoot First, Think Second", O(s, 0, 0));
        Place(s, "Psychic Coercion", O(s, 0, 1));
        Give(s, 1, TestData.Brute(Race.Blaarg));
        var legal = LegalActions.ForTurn(s, 1);
        Assert.Single(legal.OfType<CoercedDeployAction>());
        Assert.Empty(legal.OfType<DeployAtRandomAction>());
    }

    // ------------------------------------------------------------------ Abominations

    [Fact]
    public async Task Abominations_trades_a_necrolite_card_for_plus_two_and_a_new_race()
    {
        var s = Board();
        Place(s, "Abominations", O(s, 1));
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var cost = Give(s, 0, TestData.Brute(Race.Necrolite));

        var granted = LegalActions.ForTurn(s, 0).OfType<GrantedAction>().Single(); // "Players may": each player
        var p0 = new ScriptedController().Turn(_ => granted);
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(PileKind.NullifiedUnits, cost.Location.Pile);
        Assert.Equal(6, RankCalculator.GetRank(s, cowboy));
        Assert.True(StaticLayer.HasRace(s, cowboy, Race.Necrolite));
    }

    [Fact]
    public void Abominations_needs_a_necrolite_card_and_a_non_necrolite_unit()
    {
        var s = Board();
        Place(s, "Abominations", O(s, 0));
        Brute(s, Race.Necrolite, U(s, 0, 0));
        Give(s, 0, TestData.Brute(Race.Blaarg));
        Assert.Empty(LegalActions.ForTurn(s, 0).OfType<GrantedAction>()); // no Necrolite card in hand

        Give(s, 0, TestData.Brute(Race.Necrolite));
        Assert.Empty(LegalActions.ForTurn(s, 0).OfType<GrantedAction>()); // only a Necrolite Unit in play

        Brute(s, Race.Overmind, U(s, 1, 0));
        Assert.Single(LegalActions.ForTurn(s, 0).OfType<GrantedAction>());
    }

    // ------------------------------------------------------------------ Stagnation

    [Fact]
    public void Stagnation_makes_every_card_a_blaarg()
    {
        var s = Board();
        var synth = Brute(s, Race.Synthenoid, U(s, 0, 0));
        var inHand = Give(s, 1, TestData.Brute(Race.Overmind));
        var vigan = Place(s, "Vigan the Untouchable", L(s, 1));
        Place(s, "Stagnation", O(s, 0));

        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, synth));
        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, inHand));
        Assert.Contains(Race.GalacticCouncil, StaticLayer.Races(s, vigan)); // Immune
    }

    [Fact]
    public void Stagnation_rewrites_race_words_in_card_text()
    {
        var s = Board();
        var synth = Brute(s, Race.Synthenoid, U(s, 0, 0));
        Place(s, "Blazing Saddles", O(s, 0, 0)); // "Space Cowboy Units get +1 Rank"
        Assert.Equal(4, RankCalculator.GetRank(s, synth));

        var stagnation = Place(s, "Stagnation", O(s, 0, 1)); // ... now "Blaarg Units get +1 Rank", and all Units are Blaargs
        Assert.Equal(5, RankCalculator.GetRank(s, synth));

        s.MoveToPile(stagnation, PileKind.IssuedOrders);
        Assert.Equal(4, RankCalculator.GetRank(s, synth));
    }

    [Fact]
    public void Under_stagnation_no_weakness_decides_a_tie()
    {
        var s = Board();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var necro = Brute(s, Race.Necrolite, U(s, 1, 0));
        Assert.Equal(new[] { necro }, Duel.Losers(s, cowboy, necro)); // Space Cowboy ▶ Necrolite

        Place(s, "Stagnation", O(s, 0));
        Assert.Equal(2, Duel.Losers(s, cowboy, necro).Count); // both Blaarg: ruling #37
    }
}
