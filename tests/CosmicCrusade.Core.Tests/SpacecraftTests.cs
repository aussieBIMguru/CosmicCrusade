using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>Spacecraft module (design/spacecraft.md): boarding, elevation, Duels, scoring, Clean-up and the cards.</summary>
public class SpacecraftTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static GameState Board()
    {
        var s = TestData.EmptyGame();
        s.Options.Spacecraft = true;
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        s.Options.HandUnitTarget = 0; // no Start-up draws: tests put cards where they want them
        s.Options.HandOrderTarget = 0;
        s.Round = 1;              // Start-up makes this round 2 ...
        s.LastRoundWinner = 0;    // ... and player 0 goes first
        return s;
    }

    private static CardInstance Hand(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance Hand(GameState s, int player, string name) => Hand(s, player, Def(name));

    private static CardInstance Place(GameState s, string name, Node node, int? rank = null) => TestData.Place(s, Def(name), node, rank);

    private static CardInstance Brute(GameState s, Race race, Node node) => TestData.Place(s, TestData.Brute(race), node);

    private static int IndexOf(ChoiceRequest r, CardInstance card) => r.Options.ToList().FindIndex(o => o.Card == card);

    // ------------------------------------------------------------------ data and module

    [Fact]
    public void Every_spacecraft_card_is_fully_implemented()
    {
        var notFull = TestData.Db.All.Where(c => c.IsSpacecraftSet && CardStatus.Of(c) != Implementation.Full)
            .Select(c => c.Name).ToList();
        Assert.Empty(notFull);
    }

    [Fact]
    public void Module_is_off_by_default_and_adds_ships_and_orders_when_on()
    {
        var off = TestData.NewGame(1);
        Assert.False(off.Options.Spacecraft);
        Assert.DoesNotContain(off.UnitDeck, c => c.Definition.IsSpacecraftSet);
        Assert.DoesNotContain(off.OrderDeck, c => c.Definition.IsSpacecraftSet);

        var on = TestData.NewGame(1, o => o.Spacecraft = true);
        Assert.Equal(186, on.UnitDeck.Count);   // 150 + 6 Leader ships + 6×2 Champion ships + 18 ships
        Assert.Equal(92, on.OrderDeck.Count);   // 80 + 12 spacecraft Orders
        Assert.DoesNotContain(on.UnitDeck, c => c.Definition.IsToken);
        Assert.Empty(on.WarzoneDeck);           // spacecraft Warzones need Warzones too

        var both = TestData.NewGame(1, o => { o.Spacecraft = true; o.Warzones = true; });
        Assert.Equal(40, both.WarzoneDeck.Count); // 35 + 5
    }

    // ------------------------------------------------------------------ boarding (S1–S3, S11)

    [Fact]
    public void A_unit_can_be_deployed_onto_your_own_uncrewed_spacecraft()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        var ship = Place(s, "Blazing Saddle", node);
        var brute = Hand(s, 0, TestData.Brute(Race.Blaarg));

        var deploys = LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>().Where(d => d.Card == brute).ToList();
        Assert.Contains(deploys, d => d.Target == node);

        // Not onto the opponent's spacecraft.
        var theirs = Place(s, "Blazing Saddle", s.Players[1].UnitNodes[1]);
        Assert.DoesNotContain(deploys, d => d.Target == theirs.Location.Node);
        Assert.Same(ship, node.Card);
    }

    [Fact]
    public async Task Deploying_onto_a_spacecraft_makes_the_unit_its_crew_and_both_score()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        var ship = Place(s, "Exo-Frame", node);            // Rank 2
        var brute = Hand(s, 0, TestData.Brute(Race.Blaarg)); // Rank 4

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == brute && d.Target == node));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(ship, node.Card);
        Assert.Same(brute, node.Crew);
        Assert.Same(ship, Spacecraft.ShipOf(brute));
        Assert.Equal(6, RankCalculator.TotalRank(s, 0));
        Assert.Contains(brute, s.Players[0].UnitsInPlay);
    }

    [Fact]
    public async Task A_spacecraft_can_be_laid_under_your_unit()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[1];
        var brute = Brute(s, Race.Overmind, node);
        var ship = Hand(s, 0, "Exo-Frame");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == ship && d.Target == node));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(ship, node.Card);
        Assert.Same(brute, node.Crew);
    }

    [Fact]
    public void Crew_class_follows_the_node_and_some_spacecraft_cannot_be_crewed()
    {
        var s = Board();
        var leaderNode = s.Players[0].LeaderNode!;
        var dread = Place(s, "Dreadnought", leaderNode);
        var minion = s.CreateInstance(Def("Lone Wolf"));
        var leader = s.CreateInstance(Def("Lance Corporal"));
        Assert.False(Spacecraft.CanCrew(s, minion, dread, leaderNode)); // only a Leader or Champion on the Leader Node
        Assert.True(Spacecraft.CanCrew(s, leader, dread, leaderNode));

        var remnant = Place(s, "Remnant AI", s.Players[0].UnitNodes[0]);
        Assert.False(Spacecraft.CanCrew(s, minion, remnant, remnant.Location.Node!));
        var inHand = Hand(s, 0, "Remnant AI");
        var lone = Brute(s, Race.Blaarg, s.Players[0].UnitNodes[1]);
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>(), d => d.Card == inHand && d.Target == lone.Location.Node);
    }

    [Fact]
    public void Viscous_Fog_stops_boarding()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        Place(s, "Blazing Saddle", node);
        var brute = Hand(s, 0, TestData.Brute(Race.Blaarg));
        Assert.Contains(LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>(), d => d.Card == brute && d.Target == node);

        Place(s, "Viscous Fog", s.Players[1].OrderNodes[0]);
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>(), d => d.Card == brute && d.Target == node);
    }

    // ------------------------------------------------------------------ elevation (S4–S8, S19)

    [Fact]
    public async Task From_the_ground_an_opposing_unit_effect_hits_the_spacecraft_and_its_crew_drops()
    {
        var s = Board();
        var theirNode = s.Players[1].UnitNodes[1];
        var ship = Place(s, "Exo-Frame", theirNode);
        var crew = Brute(s, Race.Blaarg, theirNode);
        var op = Hand(s, 0, "Covert Operative");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == op && d.Target == s.Players[0].UnitNodes[1]));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(PileKind.NullifiedUnits, ship.Location.Pile);
        Assert.Same(crew, theirNode.Card); // dropped to the ground, still in play
        Assert.Null(theirNode.Crew);
    }

    [Fact]
    public async Task From_the_air_the_controller_chooses_the_spacecraft_or_its_crew()
    {
        var s = Board();
        var myNode = s.Players[0].UnitNodes[0];
        var theirNode = s.Players[1].UnitNodes[0];
        Place(s, "Blazing Saddle", myNode);
        var ship = Place(s, "Exo-Frame", theirNode);
        var crew = Brute(s, Race.Blaarg, theirNode);
        var op = Hand(s, 0, "Covert Operative");

        var p0 = new ScriptedController
        {
            Chooser = r => r.Kind == ChoiceKind.Cards && IndexOf(r, crew) >= 0 ? new[] { IndexOf(r, crew) } : null,
        }.Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == op && d.Target == myNode));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(op, myNode.Crew);                       // the Operative boarded our spacecraft...
        Assert.Contains(p0.ChoicesAsked, r => IndexOf(r, ship) >= 0 && IndexOf(r, crew) >= 0); // ...and was offered both
        Assert.Equal(PileKind.NullifiedUnits, crew.Location.Pile);
        Assert.Same(ship, theirNode.Card);
        Assert.Null(theirNode.Crew);
    }

    [Fact]
    public async Task Gravity_Well_lets_the_ground_reach_the_crew()
    {
        var s = Board();
        var gw = s.CreateInstance(Def("Gravity Well"));
        s.MoveToPile(gw, PileKind.ActiveWarzone);
        var theirNode = s.Players[1].UnitNodes[2];
        var ship = Place(s, "Exo-Frame", theirNode);
        var crew = Brute(s, Race.Blaarg, theirNode);
        var op = Hand(s, 0, "Covert Operative");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == op && d.Target == s.Players[0].UnitNodes[2]));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Contains(p0.ChoicesAsked, r => IndexOf(r, ship) >= 0 && IndexOf(r, crew) >= 0);
    }

    [Fact]
    public async Task A_duel_against_a_crewed_node_compares_the_spacecraft_alone()
    {
        var s = Board();
        var theirNode = s.Players[1].UnitNodes[0];
        var ship = Place(s, "Blazing Saddle", theirNode);  // Rank 1 (its crew isn't a Space Cowboy)
        var crew = Brute(s, Race.Overmind, theirNode);    // Rank 4: doesn't help in the Duel
        var shooter = Hand(s, 0, "Steady Shooter");       // Rank 2, Duels on play

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == shooter && d.Target == s.Players[0].UnitNodes[0]));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(PileKind.NullifiedUnits, ship.Location.Pile);
        Assert.Same(crew, theirNode.Card);
        Assert.Equal(LocationKind.Node, shooter.Location.Kind);
    }

    [Fact]
    public void A_crewed_spacecraft_counts_its_crews_races_in_a_duel_tie()
    {
        var s = Board();
        var a = s.Players[0].UnitNodes[0];
        var b = s.Players[1].UnitNodes[0];
        var ship = Place(s, "Exo-Frame", a, rank: 3);       // Synthenoid
        Place(s, "Lone Wolf", a);                          // Space Cowboy crew
        var necro = Brute(s, Race.Necrolite, b);
        necro.BaseRankOverride = 3;
        // Space Cowboy beats Necrolite: the crewed spacecraft wins the tie (S7).
        Assert.Same(necro, Duel.Loser(s, ship, necro));
    }

    // ------------------------------------------------------------------ scoring (S23)

    [Fact]
    public void Opposed_pairs_compare_spacecraft_plus_crew()
    {
        var s = Board();
        var mine = s.Players[0].UnitNodes[0];
        Place(s, "Exo-Frame", mine);           // 2
        Brute(s, Race.Blaarg, mine);           // + 4
        Brute(s, Race.Overmind, s.Players[1].UnitNodes[0]); // 4
        Brute(s, Race.Overmind, s.Players[1].UnitNodes[1]); // 4 (unopposed)

        var r = Scoring.Score(s);
        Assert.Equal(6, r.Total0);
        Assert.Equal(8, r.Total1);
        Assert.Equal(6, r.Pairs[0].Rank0);
        Assert.Equal(0, r.Pairs[0].Winner);
    }

    [Fact]
    public void Dreadnought_only_counts_with_a_leader_or_champion_aboard()
    {
        var s = Board();
        var l = s.Players[0].LeaderNode!;
        Place(s, "Dreadnought", l);
        Assert.Equal(0, RankCalculator.TotalRank(s, 0));
        Place(s, "Lance Corporal", l, rank: 1);
        Assert.Equal(5, RankCalculator.TotalRank(s, 0));
    }

    // ------------------------------------------------------------------ Clean-up (S9, S12, S13)

    [Fact]
    public async Task Clean_up_retires_the_spacecraft_while_a_lone_wolf_crew_goes_to_standby()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        var ship = Place(s, "Exo-Frame", node);
        var wolf = Place(s, "Lone Wolf", node);

        await OrderReactAssembleTests.Run(s, ScriptedController.PassForever(), ScriptedController.PassForever(), throughCleanUp: true);

        Assert.Equal(PileKind.RetiredUnits, ship.Location.Pile);
        Assert.Equal(NodeKind.Standby, wolf.Location.Node?.Kind);
    }

    [Fact]
    public void A_token_vanishes_when_it_leaves_play()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        var brute = Brute(s, Race.Blaarg, node);
        var fighter = s.CreateInstance(TestData.Db["0ST1"]);
        s.MoveToNode(fighter, node);
        Assert.Same(brute, node.Crew);

        s.MoveToPile(fighter, PileKind.RetiredUnits);
        Assert.Equal(LocationKind.None, fighter.Location.Kind);
        Assert.DoesNotContain(fighter, s.RetiredUnits);
        Assert.Same(brute, node.Card); // the crew dropped to the ground
    }

    // ------------------------------------------------------------------ cards

    [Fact]
    public async Task Mothership_puts_each_ground_unit_into_a_fighter()
    {
        var s = Board();
        var b1 = Brute(s, Race.Blaarg, s.Players[0].UnitNodes[0]);
        var b2 = Brute(s, Race.Overmind, s.Players[0].UnitNodes[1]);
        var mother = Hand(s, 0, "Mothership");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(d => d.Card == mother && d.Target == s.Players[0].UnitNodes[2]));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.True(s.Players[0].UnitNodes[0].Card!.Definition.IsToken);
        Assert.Same(b1, s.Players[0].UnitNodes[0].Crew);
        Assert.Same(b2, s.Players[0].UnitNodes[1].Crew);
        Assert.Equal(4 + 1 + 4 + 1 + 1, RankCalculator.TotalRank(s, 0));
    }

    [Fact]
    public async Task Beam_Up_moves_a_ground_unit_onto_your_spacecraft()
    {
        var s = Board();
        var shipNode = s.Players[0].UnitNodes[0];
        Place(s, "Blazing Saddle", shipNode);
        var brute = Brute(s, Race.Blaarg, s.Players[0].UnitNodes[1]);
        var beam = Hand(s, 0, "Beam Up");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(o => o.Card == beam));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(brute, shipNode.Crew);
        Assert.Null(s.Players[0].UnitNodes[1].Card);
    }

    [Fact]
    public async Task Chrome_Steed_crews_itself_with_a_retired_unit()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        var retired = s.CreateInstance(TestData.Brute(Race.Blaarg));
        s.MoveToPile(retired, PileKind.RetiredUnits);
        var steed = Hand(s, 0, "Chrome Steed");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == steed && d.Target == node));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(steed, node.Card);
        Assert.Same(retired, node.Crew);                 // on the node, aboard the spacecraft
        Assert.DoesNotContain(retired, s.RetiredUnits);
        Assert.Equal(1 + 4, RankCalculator.TotalRank(s, 0)); // the spacecraft keeps its own Rank 1
        Assert.Equal(1, RankCalculator.GetRank(s, steed));
        Assert.Contains(sink.Events, ev => ev is CrewBoardedEvent b && b.Crew == retired && b.FromPile == PileKind.RetiredUnits);
    }

    [Fact]
    public async Task Sarcophagar_raises_a_nullified_leader_as_a_necrolite_crew()
    {
        var s = Board();
        var node = s.Players[0].LeaderNode!;
        var dead = s.CreateInstance(Def("Lance Corporal"));
        s.MoveToPile(dead, PileKind.NullifiedUnits);
        var sarc = Hand(s, 0, "Sarcophagar");

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == sarc && d.Target == node));
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(dead, node.Crew);
        Assert.Contains(Race.Necrolite, dead.Races);
    }

    [Fact]
    public void Hull_Rot_and_Drone_Hub_read_whether_spacecraft_are_crewed()
    {
        var s = Board();
        var crewedNode = s.Players[1].UnitNodes[0];
        var crewed = Place(s, "Exo-Frame", crewedNode);
        Brute(s, Race.Blaarg, crewedNode);
        var empty = Place(s, "Exo-Frame", s.Players[1].UnitNodes[1]);
        Place(s, "Hull Rot", s.Players[0].OrderNodes[0]);
        Assert.Equal(1, RankCalculator.GetRank(s, crewed));
        Assert.Equal(0, RankCalculator.GetRank(s, empty));

        Place(s, "Drone Hub", s.Players[1].LeaderNode!);   // Champion: uncrewed spacecraft you control +2 (itself too)
        Assert.Equal(2, RankCalculator.GetRank(s, empty));
        Assert.Equal(1, RankCalculator.GetRank(s, crewed));
    }

    [Fact]
    public async Task Self_Destruct_takes_the_opposing_spacecraft_and_its_crew_down_too()
    {
        var s = Board();
        var mine = Place(s, "Blazing Saddle", s.Players[0].UnitNodes[0]);
        var theirNode = s.Players[1].UnitNodes[0];
        var theirs = Place(s, "Exo-Frame", theirNode);
        var crew = Brute(s, Race.Overmind, theirNode);
        Hand(s, 0, "Self-Destruct");
        var engulf = Hand(s, 1, "Engulf");

        var p0 = ScriptedController.PassForever().React("Self-Destruct");
        var p1 = new ScriptedController
        {
            Chooser = r => r.Purpose == "Nullify" && IndexOf(r, mine) >= 0 ? new[] { IndexOf(r, mine) } : null,
        }.Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().Single(o => o.Card == engulf));
        await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Equal(PileKind.NullifiedUnits, mine.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, theirs.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, crew.Location.Pile);
    }

    [Fact]
    public async Task Hijack_steals_a_spacecraft_as_it_is_deployed()
    {
        var s = Board();
        Hand(s, 0, "Hijack");
        var ship = Hand(s, 1, "Blazing Saddle");

        var p0 = ScriptedController.PassForever().React("Hijack");
        var p1 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().First(d => d.Card == ship && d.Target.Side == 1));
        await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Equal(0, ship.Location.Node?.Side);
    }

    [Fact]
    public void Ace_moves_a_spacecraft_and_its_crew_together()
    {
        var s = Board();
        var from = s.Players[0].UnitNodes[0];
        var to = s.Players[0].UnitNodes[2];
        var ship = Place(s, "Exo-Frame", from);
        var crew = Brute(s, Race.Blaarg, from);
        Assert.True(Rearrange.CanSwap(s, from, to));
        Rearrange.WithSwap(s, from, to, () =>
        {
            Assert.Same(ship, to.Card);
            Assert.Same(crew, to.Crew);
            return 0;
        });
        Assert.Same(ship, from.Card);
        Assert.Same(crew, from.Crew);
    }

    // ------------------------------------------------------------------ cloning and whole games

    [Fact]
    public void Clone_copies_crews()
    {
        var s = Board();
        var node = s.Players[0].UnitNodes[0];
        Place(s, "Exo-Frame", node);
        var crew = Brute(s, Race.Blaarg, node);
        var c = s.Clone(5);
        var n = c.CorrespondingNode(node);
        Assert.NotNull(n.Crew);
        Assert.Equal(crew.InstanceId, n.Crew!.InstanceId);
        Assert.Same(n, n.Crew.Location.Node);
        Assert.Equal(RankCalculator.TotalRank(s, 0), RankCalculator.TotalRank(c, 0));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public async Task Heuristic_games_with_spacecraft_finish_and_keep_every_card(int seed)
    {
        var s = TestData.NewGame(seed, o => { o.Spacecraft = true; o.Warzones = true; });
        var outcome = await new GameEngine(s, new HeuristicAi(seed), new HeuristicAi(seed + 1)).RunAsync();
        Assert.True(outcome.RoundsPlayed > 0);
        var real = s.AllCards.Where(c => !c.PrintedDefinition.IsToken).ToList();
        Assert.Equal(186, real.Count(c => c.PrintedDefinition.IsUnit));
        Assert.Equal(104, real.Count(c => c.PrintedDefinition.IsOrder)); // 80 + 12 Warzones Orders + 12 spacecraft Orders
        Assert.All(s.AllNodes, n => Assert.True(n.Crew is null || (n.Card is { } ship && ship.Definition.IsSpacecraft)));
    }

    [Fact]
    public async Task Monte_carlo_plays_with_spacecraft()
    {
        var s = TestData.NewGame(41, o => o.Spacecraft = true);
        var mc = new MonteCarloAi(3, new MonteCarloSettings { TimeBudgetMs = 0, MaxRollouts = 30, MinRolloutsPerAction = 1, Background = false });
        var outcome = await new GameEngine(s, mc, new HeuristicAi(4)).RunAsync();
        Assert.True(outcome.RoundsPlayed > 0);
    }

    // ------------------------------------------------------------------ 8 Nov 2026: Salvage Tug report, ruling #57

    [Fact]
    public async Task Salvage_Tug_places_a_retired_spacecraft_into_Standby()
    {
        var s = Board();
        var wreck = s.CreateInstance(Def("Blazing Saddle"));
        s.MoveToPile(wreck, PileKind.RetiredUnits);
        var tug = Hand(s, 0, "Salvage Tug");
        var node = s.Players[0].UnitNodes[1];

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == tug && d.Target == node));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(tug, node.Card);
        Assert.DoesNotContain(wreck, s.RetiredUnits);
        Assert.Contains(s.Players[0].StandbyNodes, n => n.Card == wreck);
        Assert.Contains(sink.Events, ev => ev is CardMovedToStandbyEvent m && m.Card == wreck && m.FromPile == PileKind.RetiredUnits);
    }

    [Fact]
    public async Task Salvage_Tug_skips_Leader_spacecraft_and_says_when_there_is_nothing_to_salvage()
    {
        var s = Board();
        var dread = s.CreateInstance(Def("Dreadnought")); // a Leader spacecraft: never into Standby (ruling #36)
        s.MoveToPile(dread, PileKind.RetiredUnits);
        var tug = Hand(s, 0, "Salvage Tug");
        var node = s.Players[0].UnitNodes[0];

        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(d => d.Card == tug && d.Target == node));
        var sink = await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Contains(dread, s.RetiredUnits);
        Assert.Contains(sink.Events, ev => ev is LogEvent l && l.Message.Contains("nothing it can place in Standby"));
    }

    [Fact]
    public void Ruling57_a_Champion_in_the_Leader_slot_cant_go_to_Standby_but_elsewhere_it_can()
    {
        var s = Board();
        var champDef = TestData.Db.All.First(c => c.UnitClass == UnitClass.Champion && !c.IsSpacecraft);
        var onUnitNode = TestData.Place(s, champDef, s.Players[0].UnitNodes[0]);
        var inLeaderSlot = TestData.Place(s, champDef, s.Players[1].LeaderNode!);
        var shipInLeaderSlot = Place(s, "Drone Hub", s.Players[0].LeaderNode!); // a Champion spacecraft

        Assert.True(Placement.CanEnterStandby(onUnitNode));
        Assert.False(Placement.CanEnterStandby(inLeaderSlot));
        Assert.False(Placement.CanEnterStandby(shipInLeaderSlot));
        Assert.True(Rearrange.Fits(onUnitNode, s.Players[0].StandbyNodes[0]));     // Ace may still send it to Standby
        Assert.False(Rearrange.Fits(inLeaderSlot, s.Players[1].StandbyNodes[0]));  // ... but not from the Leader slot
        Assert.Contains("Leader slot", Placement.StandbyRefusal(inLeaderSlot));
    }
}
