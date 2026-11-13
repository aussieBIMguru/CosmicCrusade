using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// M3.2b: the Nullify React window and replacements, End of Main / Clean-up / "whenever" triggers, Senate Chambers,
/// play costs and conditions, deploying from piles, Leaders and Standby (ruling #36), matched Duels (ruling #37).
/// </summary>
public class TriggerTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static CardInstance Place(GameState s, string name, Node node) => TestData.Place(s, Def(name), node);

    private static CardInstance Brute(GameState s, Race race, Node node, int? rank = null) =>
        TestData.Place(s, TestData.Brute(race), node, rank);

    private static CardInstance Give(GameState s, int player, string name) => Give(s, player, Def(name));

    private static CardInstance Give(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance InPile(GameState s, CardDefinition def, PileKind pile)
    {
        var c = s.CreateInstance(def);
        s.MoveToPile(c, pile);
        return c;
    }

    private static CardInstance ActivateWarzone(GameState s, string name) => InPile(s, Def(name), PileKind.ActiveWarzone);

    private static int Rank(GameState s, CardInstance c) => RankCalculator.GetRank(s, c);

    private static Node U(GameState s, int player, int i) => s.Players[player].UnitNodes[i];
    private static Node L(GameState s, int player) => s.Players[player].LeaderNode!;
    private static Node O(GameState s, int player, int i = 0) => s.Players[player].OrderNodes[i];

    private static Task<StoppingSink> Run(GameState s, ScriptedController p0, ScriptedController p1, bool throughCleanUp = false)
    {
        if (throughCleanUp) s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        return OrderReactAssembleTests.Run(s, p0, p1, throughCleanUp);
    }

    private static ScriptedController Deploys(CardInstance card, Node node) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == card && a.Target == node));

    private static ScriptedController Issues(CardInstance card) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == card));

    private static StackItem FakeDeploy(GameState s, int controller, CardInstance card, Node target)
    {
        s.MoveToStack(card, controller);
        return new StackItem(controller, StackItemKind.DeployUnit, card, "test", (_, _) => Task.CompletedTask) { TargetNode = target };
    }

    private static RoundResult Scored(StoppingSink sink) => sink.Events.OfType<RoundScoredEvent>().First().Result;

    // ------------------------------------------------------------------ ruling #36: Leaders never go to Standby

    [Fact]
    public async Task Sisters_of_Blight_cannot_choose_a_node_holding_a_Leader()
    {
        var s = TestData.EmptyGame();
        var murphy = Place(s, "Quickraw Murphy", L(s, 1));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        var sisters = Give(s, 0, "Sisters of Blight");
        var p0 = Deploys(sisters, L(s, 0));

        await Run(s, p0, ScriptedController.PassForever());
        var nodeChoice = p0.ChoicesAsked.First(r => r.Kind == ChoiceKind.Nodes);
        Assert.DoesNotContain(nodeChoice.Options, o => o.Node == L(s, 1));
        Assert.Contains(nodeChoice.Options, o => o.Node == U(s, 1, 0));
        Assert.Same(murphy, L(s, 1).Card);
    }

    [Fact]
    public void Spreading_Sickness_cannot_answer_a_Leader_deploy()
    {
        var s = TestData.EmptyGame();
        Give(s, 1, "Spreading Sickness");
        var leader = FakeDeploy(s, 0, s.CreateInstance(Def("Quickraw Murphy")), L(s, 0));
        var brute = FakeDeploy(s, 0, s.CreateInstance(TestData.Brute(Race.Blaarg)), U(s, 0, 0));

        Assert.Empty(LegalActions.ForResponse(s, 1, new[] { leader }));
        Assert.Single(LegalActions.ForResponse(s, 1, new[] { brute }));
    }

    [Fact]
    public void A_Leader_is_never_moved_to_Standby()
    {
        Assert.False(Placement.CanEnterStandby(Def("Quickraw Murphy")));
        Assert.True(Placement.CanEnterStandby(Def("Dusty Trailblazer"))); // Champions can
    }

    // ------------------------------------------------------------------ ruling #37: matched Duels

    [Fact]
    public async Task A_matched_duel_without_weakness_nullifies_both()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 1, 0), rank: 2);
        var shooter = Give(s, 0, "Steady Shooter"); // Space Cowboy, Rank 2

        var sink = await Run(s, Deploys(shooter, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, shooter.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, cowboy.Location.Pile);
        Assert.Contains(sink.Events, e => e is DuelEvent { Losers.Count: 2 });
    }

    // ------------------------------------------------------------------ the Nullify window

    [Fact]
    public async Task Nobody_responding_leaves_no_trace_on_the_stack()
    {
        var s = TestData.EmptyGame();
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        var covert = Give(s, 0, "Covert Operative");

        var sink = await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, enemy.Location.Pile);
        Assert.DoesNotContain(sink.Events, e => e is StackPushedEvent { Item.Kind: StackItemKind.UnitNullifying });
    }

    [Fact]
    public async Task Already_Dead_prevents_the_nullify_and_makes_it_a_Necrolite()
    {
        var s = TestData.EmptyGame();
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        Give(s, 1, "Already Dead");
        var covert = Give(s, 0, "Covert Operative");

        var sink = await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever().React("Already Dead"));
        Assert.Same(enemy, U(s, 1, 0).Card);
        Assert.Equal(new[] { Race.Necrolite }, StaticLayer.Races(s, enemy));
        Assert.Contains(sink.Events, e => e is NullifyPreventedEvent p && p.Card == enemy);
        Assert.Contains(sink.Events, e => e is StackPushedEvent { Item.Kind: StackItemKind.UnitNullifying }); // shown once answered
    }

    [Fact]
    public async Task Respond_in_Kind_takes_the_opposing_unit_too()
    {
        var s = TestData.EmptyGame();
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        Give(s, 1, "Respond in Kind");
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever().React("Respond in Kind"));
        Assert.Equal(PileKind.NullifiedUnits, enemy.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, covert.Location.Pile);
    }

    [Fact]
    public async Task Glorpaglorp_sends_an_allied_unit_to_standby_instead()
    {
        var s = TestData.EmptyGame();
        Place(s, "Glorpaglorp", L(s, 1));
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Same(enemy, s.Players[1].StandbyNodes[0].Card);
    }

    [Fact]
    public async Task Fallout_Shelter_lets_the_controller_save_any_unit()
    {
        var s = TestData.EmptyGame();
        ActivateWarzone(s, "Fallout Shelter");
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Same(enemy, s.Players[1].StandbyNodes[0].Card);
    }

    [Fact]
    public async Task Black_Hole_nullifies_units_instead_of_retiring_them()
    {
        var s = TestData.EmptyGame();
        ActivateWarzone(s, "Black Hole");
        var brute = Brute(s, Race.Blaarg, U(s, 0, 0));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);
        Assert.Equal(PileKind.NullifiedUnits, brute.Location.Pile);
    }

    // ------------------------------------------------------------------ End of Main

    [Fact]
    public async Task The_Empress_wins_when_the_opponent_has_no_leader()
    {
        var s = TestData.EmptyGame();
        Place(s, "The Empress", L(s, 0));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        Brute(s, Race.Blaarg, U(s, 1, 1));

        var sink = await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever());
        var result = Scored(sink);
        Assert.Equal(0, result.Winner);
        Assert.Equal("The Empress", result.WonBy);
    }

    [Fact]
    public async Task High_Noon_duels_every_opposed_pair()
    {
        var s = TestData.EmptyGame();
        var big = Brute(s, Race.Blaarg, U(s, 0, 0));
        var small = Brute(s, Race.Blaarg, U(s, 1, 0), rank: 3);
        var a = Brute(s, Race.SpaceCowboy, U(s, 0, 1), rank: 2);
        var b = Brute(s, Race.SpaceCowboy, U(s, 1, 1), rank: 2);
        var alone = Brute(s, Race.Blaarg, U(s, 0, 2), rank: 1);
        Place(s, "High Noon", O(s, 0));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever());
        Assert.Same(big, U(s, 0, 0).Card);
        Assert.Equal(PileKind.NullifiedUnits, small.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, a.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, b.Location.Pile);
        Assert.Same(alone, U(s, 0, 2).Card); // unopposed: no Duel
    }

    [Fact]
    public async Task Weed_out_the_Weak_nullifies_every_lowest_unit()
    {
        var s = TestData.EmptyGame();
        var x = Brute(s, Race.Blaarg, U(s, 0, 0), rank: 1);
        var y = Brute(s, Race.Blaarg, U(s, 1, 2), rank: 1);
        var z = Brute(s, Race.Blaarg, U(s, 0, 1));
        Place(s, "Weed out the Weak", O(s, 1));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, x.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, y.Location.Pile);
        Assert.Same(z, U(s, 0, 1).Card);
    }

    // ------------------------------------------------------------------ Clean-up

    [Fact]
    public async Task Preferred_Candidate_remains_in_play_with_plus_2()
    {
        var s = TestData.EmptyGame();
        var candidate = Place(s, "Preferred Candidate", L(s, 0));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);
        Assert.Same(candidate, L(s, 0).Card);
        Assert.Equal(4, Rank(s, candidate));
    }

    [Fact]
    public async Task Lone_Wolf_goes_to_standby_instead_of_retiring()
    {
        var s = TestData.EmptyGame();
        var wolf = Place(s, "Lone Wolf", U(s, 0, 0));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);
        Assert.Same(wolf, s.Players[0].StandbyNodes[0].Card);
    }

    [Fact]
    public async Task Ynosh_takes_an_enemy_unit_into_its_standby()
    {
        var s = TestData.EmptyGame();
        Place(s, "Y'nosh, Galactic Slaver", L(s, 0));
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));

        await Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);
        Assert.Equal(LocationKind.Node, enemy.Location.Kind);
        Assert.Equal(NodeKind.Standby, enemy.Location.Node!.Kind);
        Assert.Equal(0, enemy.Location.Node.Side);
    }

    // ------------------------------------------------------------------ "Whenever ..."

    [Fact]
    public async Task Lance_Corporal_grows_when_you_play_an_order()
    {
        var s = TestData.EmptyGame();
        var lance = Place(s, "Lance Corporal", L(s, 0));
        var saddles = Give(s, 0, "Blazing Saddles");

        await Run(s, Issues(saddles), ScriptedController.PassForever());
        Assert.Equal(1 + 2, Rank(s, lance));
    }

    [Fact]
    public async Task Xeno_Laser_hits_only_the_first_non_space_cowboy_each_round()
    {
        var s = TestData.EmptyGame();
        Place(s, "Xeno Laser", O(s, 0));
        var first = Give(s, 1, TestData.Brute(Race.Blaarg));
        var second = Give(s, 1, TestData.Brute(Race.Blaarg));
        var p1 = new ScriptedController()
            .Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(a => a.Card == first && a.Target == U(s, 1, 0)))
            .Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(a => a.Card == second && a.Target == U(s, 1, 1)));

        await Run(s, ScriptedController.PassForever(), p1);
        Assert.Equal(PileKind.NullifiedUnits, first.Location.Pile);
        Assert.Same(second, U(s, 1, 1).Card);
    }

    [Fact]
    public async Task Disturberus_grows_whenever_a_unit_is_nullified()
    {
        var s = TestData.EmptyGame();
        var disturberus = Place(s, "Disturberus", L(s, 0));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Equal(1 + 3, Rank(s, disturberus));
    }

    [Fact]
    public async Task Fast_Eddie_lets_a_played_space_cowboy_duel()
    {
        var s = TestData.EmptyGame();
        Place(s, "Fast Eddie", L(s, 0));
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0), rank: 3);
        var cowboy = Give(s, 0, TestData.Brute(Race.SpaceCowboy));

        await Run(s, Deploys(cowboy, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, enemy.Location.Pile);
        Assert.Same(cowboy, U(s, 0, 0).Card);
    }

    [Fact]
    public async Task Cntrl_C_Cntrl_V_copies_assembles()
    {
        var s = TestData.EmptyGame();
        var copier = Place(s, "Cntrl-C Cntrl-V", L(s, 0));
        var host = Brute(s, Race.Synthenoid, U(s, 0, 0));
        var spider = Give(s, 0, "Sprocket Spider");
        int before = Rank(s, copier);
        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<AssembleAction>().Single(a => a.Card == spider && a.Target == host));

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(before + 1, Rank(s, copier));
        Assert.Equal(4 + 1, Rank(s, host));
    }

    [Fact]
    public async Task Salvage_for_Parts_only_assembles_onto_your_own_units_including_a_Synthenoid_Leader()
    {
        var s = TestData.EmptyGame();
        var leader = Place(s, "Mechatron", L(s, 0));                  // Synthenoid Leader, Rank 8
        var enemy = Brute(s, Race.Synthenoid, U(s, 1, 0));
        InPile(s, TestData.Brute(Race.Synthenoid), PileKind.NullifiedUnits); // Rank 4 source
        var salvage = Give(s, 0, "Salvage for Parts");
        var p0 = Issues(salvage);

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(8 + 4, Rank(s, leader));
        Assert.Equal(4, Rank(s, enemy));
        var ontoChoice = p0.ChoicesAsked.LastOrDefault(r => r.Purpose == "Buff");
        Assert.True(ontoChoice is null || ontoChoice.Options.All(o => o.Card != enemy));
    }

    // ------------------------------------------------------------------ Senate Chambers

    [Fact]
    public async Task Senate_Chambers_ends_the_round_when_all_four_nodes_are_filled()
    {
        var s = TestData.EmptyGame();
        ActivateWarzone(s, "Senate Chambers");
        Place(s, "Quickraw Murphy", L(s, 0));
        Brute(s, Race.Blaarg, U(s, 0, 0));
        Brute(s, Race.Blaarg, U(s, 0, 1));
        for (int i = 0; i < 3; i++) Brute(s, Race.Blaarg, U(s, 1, i), rank: 9);
        var last = Give(s, 0, TestData.Brute(Race.Blaarg));

        var sink = await Run(s, Deploys(last, U(s, 0, 2)), ScriptedController.PassForever());
        var result = Scored(sink);
        Assert.Equal(0, result.Winner);
        Assert.Equal("Senate Chambers", result.WonBy);
    }

    // ------------------------------------------------------------------ costs and conditions

    [Fact]
    public async Task Mechatron_needs_two_other_synthenoid_units_and_retires_them()
    {
        var s = TestData.EmptyGame();
        var mech = Give(s, 0, "Mechatron");
        var one = Give(s, 0, TestData.Brute(Race.Synthenoid));
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is DeployFromHandAction d && d.Card == mech);

        var two = Give(s, 0, TestData.Brute(Race.Synthenoid));
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is DeployFromHandAction d && d.Card == mech);

        await Run(s, Deploys(mech, L(s, 0)), ScriptedController.PassForever());
        Assert.Same(mech, L(s, 0).Card);
        Assert.Equal(PileKind.RetiredUnits, one.Location.Pile);
        Assert.Equal(PileKind.RetiredUnits, two.Location.Pile);
    }

    [Fact]
    public void Scrapped_Metal_needs_a_synthenoid_card_to_retire()
    {
        var s = TestData.EmptyGame();
        var scrap = Give(s, 0, "Scrapped Metal");
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction o && o.Card == scrap);
        Give(s, 0, TestData.Brute(Race.Synthenoid));
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction o && o.Card == scrap);
    }

    [Fact]
    public void Caustic_Creek_needs_an_empty_node_for_each_player()
    {
        var s = TestData.EmptyGame();
        var creek = Give(s, 0, "Caustic Creek");
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction o && o.Card == creek);
        foreach (var n in s.Players[1].AllNodes) n.IsLocked = true;
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction o && o.Card == creek);
    }

    [Fact]
    public async Task Dimensional_Rift_adds_two_warzones()
    {
        var s = TestData.EmptyGame();
        var rift = Give(s, 0, "Dimensional Rift");
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction o && o.Card == rift); // needs 1 Warzone

        ActivateWarzone(s, "Planet Earth");
        InPile(s, Def("Galactic Capital"), PileKind.WarzoneDeck);
        InPile(s, Def("Swarm Warrens"), PileKind.WarzoneDeck);
        var sink = await Run(s, Issues(rift), ScriptedController.PassForever());
        Assert.Equal(3, s.ActiveWarzones.Count);
        Assert.Contains(sink.Events, e => e is WarzonesAddedEvent { Warzones.Count: 2 }); // both at once
    }

    [Fact]
    public async Task Wormhole_swaps_the_current_warzone_for_one_of_the_top_three()
    {
        var s = TestData.EmptyGame();
        var current = ActivateWarzone(s, "Planet Earth");
        var bottom = InPile(s, Def("Cyber Moon"), PileKind.WarzoneDeck);
        var third = InPile(s, Def("Galactic Capital"), PileKind.WarzoneDeck);
        var second = InPile(s, Def("Swarm Warrens"), PileKind.WarzoneDeck);
        var top = InPile(s, Def("Training Grounds"), PileKind.WarzoneDeck);
        var wormhole = Give(s, 0, "Wormhole");
        var p0 = Issues(wormhole);
        p0.Chooser = r => r.Purpose == "Wormhole" ? new[] { r.Options.ToList().FindIndex(o => o.Card == third) } : null;

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(new[] { third }, s.ActiveWarzones);
        var options = p0.ChoicesAsked.Single(r => r.Purpose == "Wormhole").Options.Select(o => o.Card).ToList();
        Assert.Equal(new[] { top, second, third }, options);      // only the top 3, top first
        Assert.Same(bottom, s.WarzoneDeck[^1]);                    // Cyber Moon is now the top card
        Assert.Contains(current, s.WarzoneDeck.Take(3));          // the rest went to the bottom
        Assert.Contains(top, s.WarzoneDeck.Take(3));
        Assert.Contains(second, s.WarzoneDeck.Take(3));
    }

    // ------------------------------------------------------------------ deploying from piles

    [Fact]
    public async Task Barkeep_Bill_deploys_space_cowboys_from_the_retired_pile()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill; // otherwise Start-up reshuffles the Retired pile into the deck
        Place(s, "Barkeep Bill", L(s, 0));
        var cowboy = InPile(s, TestData.Brute(Race.SpaceCowboy), PileKind.RetiredUnits);
        InPile(s, TestData.Brute(Race.Blaarg), PileKind.RetiredUnits);

        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is DeployFromPileAction { Pile: PileKind.RetiredUnits });
        Assert.DoesNotContain(LegalActions.ForTurn(s, 1), a => a is DeployFromPileAction); // "you" only

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromPileAction>().First());
        var sink = await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(LocationKind.Node, cowboy.Location.Kind);
        Assert.Equal(0, cowboy.Location.Node!.Side);
        Assert.Contains(sink.Events, e => e is UnitDeployAnnouncedEvent { FromPile: PileKind.RetiredUnits });
        Assert.Equal(1, s.Players[0].DeployTurnsThisRound);
    }

    [Fact]
    public void Hills_of_Decay_lets_both_players_deploy_nullified_necrolites()
    {
        var s = TestData.EmptyGame();
        ActivateWarzone(s, "Hills of Decay");
        InPile(s, TestData.Brute(Race.Necrolite), PileKind.NullifiedUnits);
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is DeployFromPileAction { Pile: PileKind.NullifiedUnits });
        Assert.Contains(LegalActions.ForTurn(s, 1), a => a is DeployFromPileAction { Pile: PileKind.NullifiedUnits });
    }
}
