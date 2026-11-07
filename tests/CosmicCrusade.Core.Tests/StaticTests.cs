using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>M3.2: Static effects – anthems, debuffs, conditional and counted Rank, Immune, cannot be Nullified,
/// race changes, suppression and rule changes.</summary>
public class StaticTests
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

    private static CardInstance ActivateWarzone(GameState s, string name)
    {
        var wz = s.CreateInstance(Def(name));
        s.MoveToPile(wz, PileKind.ActiveWarzone);
        return wz;
    }

    private static int Rank(GameState s, CardInstance c) => RankCalculator.GetRank(s, c);

    private static Node U(GameState s, int player, int i) => s.Players[player].UnitNodes[i];
    private static Node L(GameState s, int player) => s.Players[player].LeaderNode!;
    private static Node O(GameState s, int player, int i = 0) => s.Players[player].OrderNodes[i];

    private static Task<StoppingSink> Run(GameState s, ScriptedController p0, ScriptedController p1) =>
        OrderReactAssembleTests.Run(s, p0, p1, throughCleanUp: false);

    private static ScriptedController Deploys(CardInstance card, Node node) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == card && a.Target == node));

    private static bool HasTrigger(CardDefinition c, string trigger) =>
        c.Effects.ValueKind == System.Text.Json.JsonValueKind.Array
        && c.Effects.EnumerateArray().Any(e => e.TryGetProperty("trigger", out var t) && t.GetString() == trigger);

    // ------------------------------------------------------------------ coverage

    [Fact]
    public void All_100_static_effects_are_live()
    {
        // 71 base + Warzones, plus 7 Archons (The Unnameable, Androsiar, Archon Arsenal, Unstoppable,
        // Galactic Siege, Open Source, Deify), plus 22 Spacecraft cards: one Static entry each.
        var withStatics = TestData.Db.All.Where(c => HasTrigger(c, "Static")).ToList();
        int total = withStatics.Sum(c => c.Effects.EnumerateArray()
            .Count(e => e.TryGetProperty("trigger", out var t) && t.GetString() == "Static"));
        Assert.Equal(100, total);

        var inert = withStatics.SelectMany(c => StaticLayer.InertStatics(c)).ToList();
        Assert.Empty(inert); // M3.2c: RevealHands ×3, OverrideDecision ×2, GrantAction, ReplaceRaceWords
        Assert.Equal(98, withStatics.Count);
        Assert.Equal(98, withStatics.Count(c => StaticLayer.HasLiveStatics(c)));
    }

    [Fact]
    public void As_you_issue_choose_a_race_is_live()
    {
        Assert.True(EffectSupport.IsLive(Def("Extreme Sanctions"), "OnIssue"));
        Assert.True(EffectSupport.IsLive(Def("Containment Network"), "OnIssue"));
    }

    // ------------------------------------------------------------------ anthems and debuffs

    [Fact]
    public void Blazing_Saddles_buffs_every_space_cowboy_while_it_is_in_play()
    {
        var s = TestData.EmptyGame();
        var mine = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var theirs = Brute(s, Race.SpaceCowboy, U(s, 1, 0));
        var blaarg = Brute(s, Race.Blaarg, U(s, 0, 1));
        var saddles = Place(s, "Blazing Saddles", O(s, 0));

        Assert.Equal(5, Rank(s, mine));
        Assert.Equal(5, Rank(s, theirs));
        Assert.Equal(4, Rank(s, blaarg));

        s.MoveToPile(saddles, PileKind.IssuedOrders);
        Assert.Equal(4, Rank(s, mine));
        Assert.Equal(4, Rank(s, theirs));
    }

    [Fact]
    public void Dusty_Trailblazer_buffs_other_allied_space_cowboys_only()
    {
        var s = TestData.EmptyGame();
        var dusty = Place(s, "Dusty Trailblazer", L(s, 0));
        var mine = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var theirs = Brute(s, Race.SpaceCowboy, U(s, 1, 0));

        Assert.Equal(2, Rank(s, dusty));
        Assert.Equal(5, Rank(s, mine));
        Assert.Equal(4, Rank(s, theirs));
        Assert.Equal(2 + 5, RankCalculator.TotalRank(s, 0));
    }

    [Fact]
    public void Standby_units_are_not_buffed()
    {
        var s = TestData.EmptyGame();
        Place(s, "Blazing Saddles", O(s, 0));
        var waiting = Brute(s, Race.SpaceCowboy, s.Players[0].StandbyNodes[0]);
        Assert.Equal(4, Rank(s, waiting));
    }

    [Fact]
    public void Rebel_with_a_Cause_needs_an_opposing_Necrolite()
    {
        var s = TestData.EmptyGame();
        var rebel = Place(s, "Rebel with a Cause", U(s, 0, 0));
        Assert.Equal(2, Rank(s, rebel));

        Brute(s, Race.Necrolite, U(s, 1, 2));
        Assert.Equal(4, Rank(s, rebel));
    }

    [Fact]
    public void Acid_Rain_cannot_make_a_rank_negative()
    {
        var s = TestData.EmptyGame();
        var big = Brute(s, Race.Blaarg, U(s, 0, 0));
        var small = Brute(s, Race.Blaarg, U(s, 1, 0), rank: 1);
        Place(s, "Acid Rain", O(s, 1));

        Assert.Equal(2, Rank(s, big));
        Assert.Equal(0, Rank(s, small));
    }

    [Fact]
    public void The_Creeper_lowers_its_own_side_and_has_no_general_floor()
    {
        var s = TestData.EmptyGame();
        var creeper = Place(s, "The Creeper", L(s, 0));
        var brute = Brute(s, Race.Overmind, U(s, 0, 0));
        var zero = Brute(s, Race.Overmind, U(s, 0, 1), rank: 0);

        Assert.Equal(-1, Rank(s, creeper));
        Assert.Equal(3, Rank(s, brute));
        Assert.Equal(-1, Rank(s, zero)); // spec §7.4: no floor without Acid Rain
    }

    // ------------------------------------------------------------------ counted and derived amounts

    [Fact]
    public void Boarding_Party_counts_other_allied_units_that_share_a_race()
    {
        var s = TestData.EmptyGame();
        var a = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var b = Brute(s, Race.SpaceCowboy, U(s, 0, 1));
        var c = Brute(s, Race.Blaarg, U(s, 0, 2));
        var enemy = Brute(s, Race.SpaceCowboy, U(s, 1, 0));
        Place(s, "Boarding Party", O(s, 1)); // either player's Ongoing applies to everyone

        Assert.Equal(5, Rank(s, a));
        Assert.Equal(5, Rank(s, b));
        Assert.Equal(4, Rank(s, c));
        Assert.Equal(4, Rank(s, enemy)); // enemy Space Cowboys are not allied
    }

    [Fact]
    public void Rise_of_the_Tyrant_buffs_leaders_by_their_allied_followers()
    {
        var s = TestData.EmptyGame();
        var murphy = Place(s, "Quickraw Murphy", L(s, 0));
        Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var follower = Brute(s, Race.SpaceCowboy, U(s, 0, 1));
        Brute(s, Race.SpaceCowboy, s.Players[0].StandbyNodes[2]); // errata: Standby Units are not counted
        Place(s, "Rise of the Tyrant", O(s, 0));

        Assert.Equal(3 + 2, Rank(s, murphy));
        Assert.Equal(4, Rank(s, follower));
    }

    [Fact]
    public void Cure_Failure_349_counts_nullified_necrolites()
    {
        var s = TestData.EmptyGame();
        var cure = Place(s, "Cure Failure 349", L(s, 0));
        Assert.Equal(0, Rank(s, cure));

        for (int i = 0; i < 2; i++) s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Necrolite)), PileKind.NullifiedUnits);
        s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Blaarg)), PileKind.NullifiedUnits);
        Assert.Equal(2, Rank(s, cure));
        Assert.Equal(2, RankCalculator.CurrentBaseRank(s, cure));
    }

    [Fact]
    public void Indominus_loses_rank_for_the_opponents_hand()
    {
        var s = TestData.EmptyGame();
        var indominus = Place(s, "Indominus", L(s, 0));
        for (int i = 0; i < 3; i++) Give(s, 1, TestData.Brute(Race.Blaarg));
        Assert.Equal(6 - 3, Rank(s, indominus));
    }

    [Fact]
    public void The_Resonator_adds_its_own_rank_to_allies()
    {
        var s = TestData.EmptyGame();
        var resonator = Place(s, "The Resonator", L(s, 0));
        resonator.PersistentModifiers.Add(new RankModifier("test", 2));
        var ally = Brute(s, Race.Blaarg, U(s, 0, 0));
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));

        Assert.Equal(2, Rank(s, resonator));
        Assert.Equal(6, Rank(s, ally));
        Assert.Equal(4, Rank(s, enemy));
    }

    // ------------------------------------------------------------------ base rank and races

    [Fact]
    public void Primordium_sets_base_rank_and_persistent_overrides_sit_on_top()
    {
        var s = TestData.EmptyGame();
        var brute = Brute(s, Race.Blaarg, U(s, 0, 0));
        var detained = Brute(s, Race.Blaarg, U(s, 0, 1), rank: 0); // e.g. Shot in the Dark
        Place(s, "Primordium", O(s, 0));
        Assert.Equal(1, Rank(s, brute));
        Assert.Equal(0, Rank(s, detained));
    }

    [Fact]
    public void The_Voidslime_makes_other_units_rank_1_Blaarg()
    {
        var s = TestData.EmptyGame();
        var voidslime = Place(s, "The Voidslime", L(s, 0));
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 1, 0));

        Assert.Equal(1, Rank(s, cowboy));
        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, cowboy));
        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, voidslime));
        Assert.Contains(Race.SpaceCowboy, cowboy.Races); // the printed/persistent races are untouched
    }

    [Fact]
    public void Consortium_removes_races_for_conditions_and_duels()
    {
        var s = TestData.EmptyGame();
        var rebel = Place(s, "Rebel with a Cause", U(s, 0, 0));
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 1));
        var necro = Brute(s, Race.Necrolite, U(s, 1, 1));
        Assert.Equal(4, Rank(s, rebel));
        Assert.Same(necro, Duel.Loser(s, cowboy, necro)); // Space Cowboy beats Necrolite on a tie

        Place(s, "Consortium", O(s, 1));
        Assert.Empty(StaticLayer.Races(s, necro));
        Assert.Equal(2, Rank(s, rebel));
        Assert.Equal(2, Duel.Losers(s, cowboy, necro).Count); // no weakness left: a matched Rank Nullifies both
    }

    // ------------------------------------------------------------------ Immune and cannot be Nullified

    [Fact]
    public void Vigan_ignores_statics_from_other_cards()
    {
        var s = TestData.EmptyGame();
        var vigan = Place(s, "Vigan the Untouchable", L(s, 0));
        Place(s, "Acid Rain", O(s, 1));
        Place(s, "Primordium", O(s, 1, 1));
        Assert.True(StaticLayer.IsImmune(s, vigan));
        Assert.Equal(4, Rank(s, vigan));
    }

    [Fact]
    public async Task Vigan_cannot_be_targeted_by_Tyrant_Toppler()
    {
        var s = TestData.EmptyGame();
        var vigan = Place(s, "Vigan the Untouchable", L(s, 0));
        var toppler = Give(s, 1, "Tyrant Toppler");

        await Run(s, ScriptedController.PassForever(), Deploys(toppler, U(s, 1, 0)));
        Assert.Same(vigan, L(s, 0).Card);
    }

    [Fact]
    public async Task Diplomatic_Immunity_buffs_and_protects_Visiting_Diplomats()
    {
        var s = TestData.EmptyGame();
        var diplomat = Place(s, "Visiting Diplomat", U(s, 1, 0));
        Place(s, "Diplomatic Immunity", O(s, 1));
        Assert.Equal(5, Rank(s, diplomat));

        var covert = Give(s, 0, "Covert Operative");
        var sink = await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());

        Assert.Same(diplomat, U(s, 1, 0).Card);
        Assert.Contains(sink.Events, e => e is NullifyPreventedEvent p && p.Card == diplomat && p.Source == covert);
        Assert.DoesNotContain(sink.Events, e => e is CardNullifiedEvent n && n.Card == diplomat);
    }

    [Fact]
    public async Task Force_Field_protects_your_space_cowboys()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        Place(s, "Force Field", O(s, 0));
        var covert = Give(s, 1, "Covert Operative");

        await Run(s, ScriptedController.PassForever(), Deploys(covert, U(s, 1, 0)));
        Assert.Same(cowboy, U(s, 0, 0).Card);
    }

    [Fact]
    public void No_Gods_No_Masters_needs_an_empty_leader_node()
    {
        var s = TestData.EmptyGame();
        var brute = Brute(s, Race.Synthenoid, U(s, 0, 0));
        Place(s, "No Gods, No Masters", O(s, 0));
        Assert.True(StaticLayer.PreventsNullify(s, brute, null, out _));

        L(s, 0).IsLocked = true; // errata: a Locked Node is not Empty
        Assert.False(StaticLayer.PreventsNullify(s, brute, null, out _));
        L(s, 0).IsLocked = false;

        Place(s, "Quickraw Murphy", L(s, 0));
        Assert.False(StaticLayer.PreventsNullify(s, brute, null, out _));
    }

    // ------------------------------------------------------------------ suppression and inversion

    [Fact]
    public void Suppressing_Goop_switches_off_other_statics_and_placement()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        Place(s, "Blazing Saddles", O(s, 0));
        var diplomat = Give(s, 0, "Visiting Diplomat");
        Assert.Equal(5, Rank(s, cowboy));
        Assert.Contains(Placement.LegalDeployNodes(s, 0, diplomat), n => n.Side == 1);

        Place(s, "Suppressing Goop", O(s, 1));
        Assert.Equal(4, Rank(s, cowboy));
        Assert.All(Placement.LegalDeployNodes(s, 0, diplomat), n => Assert.Equal(0, n.Side)); // "even when not in play"
    }

    [Fact]
    public void Atmosphere_Generator_suppresses_warzones()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        ActivateWarzone(s, "Planet Earth");
        Assert.Equal(5, Rank(s, cowboy));

        Place(s, "Atmosphere Generator", O(s, 0));
        Assert.Equal(4, Rank(s, cowboy));
    }

    [Fact]
    public async Task Refraction_Network_stops_when_played_abilities()
    {
        var s = TestData.EmptyGame();
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        ActivateWarzone(s, "Refraction Network");
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, U(s, 0, 0)), ScriptedController.PassForever());
        Assert.Same(enemy, U(s, 1, 0).Card);
        Assert.Same(covert, U(s, 0, 0).Card);
    }

    [Fact]
    public void Polarity_Storm_inverts_rank_changes()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        Place(s, "Blazing Saddles", O(s, 0));
        cowboy.PersistentModifiers.Add(new RankModifier("Sheriff's Badge", 2));
        Assert.Equal(7, Rank(s, cowboy));

        ActivateWarzone(s, "Polarity Storm");
        Assert.Equal(4 - 3, Rank(s, cowboy));
    }

    // ------------------------------------------------------------------ rule changes

    [Fact]
    public async Task Potency_Tank_adds_2_to_manipulate()
    {
        var s = TestData.EmptyGame();
        Place(s, "Potency Tank", L(s, 0));
        var brute = Brute(s, Race.Blaarg, U(s, 0, 0));
        var adapt = Give(s, 0, "Adapt");
        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == adapt));
        p0.Chooser = r =>
        {
            int i = r.Options.ToList().FindIndex(o => o.Card == brute);
            return i >= 0 ? new[] { i } : null; // the brute, then "+N" (its first option)
        };

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(4 + 4, Rank(s, brute));
    }

    [Fact]
    public void The_Oculus_gives_the_round_to_the_lowest_total()
    {
        var s = TestData.EmptyGame();
        Brute(s, Race.Blaarg, U(s, 0, 0));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        Brute(s, Race.Blaarg, U(s, 1, 1));
        Assert.Equal(1, Scoring.Score(s).Winner);

        ActivateWarzone(s, "The Oculus");
        Assert.Equal(0, Scoring.Score(s).Winner);
    }

    [Fact]
    public void Temporal_Stasis_stops_other_orders()
    {
        var s = TestData.EmptyGame();
        Give(s, 0, "Depose");
        Give(s, 0, "Blazing Saddles");
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction);

        Place(s, "Temporal Stasis", O(s, 1));
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is IssueOrderAction);
    }

    [Fact]
    public void Extreme_Sanctions_blocks_the_chosen_race()
    {
        var s = TestData.EmptyGame();
        var sanctions = Place(s, "Extreme Sanctions", O(s, 1));
        sanctions.Memory["race"] = Race.SpaceCowboy;
        var cowboy = Give(s, 0, TestData.Brute(Race.SpaceCowboy));
        var blaarg = Give(s, 0, TestData.Brute(Race.Blaarg));
        var saddles = Give(s, 0, "Blazing Saddles");

        var legal = LegalActions.ForTurn(s, 0);
        Assert.DoesNotContain(legal, a => a is DeployFromHandAction d && d.Card == cowboy);
        Assert.Contains(legal, a => a is DeployFromHandAction d && d.Card == blaarg);
        Assert.DoesNotContain(legal, a => a is IssueOrderAction o && o.Card == saddles);
    }

    [Fact]
    public async Task Containment_Network_remembers_the_race_chosen_as_it_is_issued()
    {
        var s = TestData.EmptyGame();
        var enemy = Brute(s, Race.Blaarg, U(s, 1, 0));
        var ally = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        var network = Give(s, 0, "Containment Network");
        var p0 = new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == network));
        p0.Chooser = r => r.Purpose == "ChooseRace"
            ? new[] { r.Options.ToList().FindIndex(o => o.Value == (int)Race.Blaarg) }
            : null;

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(Race.Blaarg, network.Memory["race"]);
        Assert.Equal(3, Rank(s, enemy));
        Assert.Equal(4, Rank(s, ally));
    }

    [Fact]
    public void Even_Odds_only_lets_equal_base_ranks_deploy_opposed()
    {
        var s = TestData.EmptyGame();
        Brute(s, Race.Blaarg, U(s, 1, 0));
        Place(s, "Even Odds", O(s, 1));
        var brute = Give(s, 0, TestData.Brute(Race.SpaceCowboy)); // Base Rank 4
        var covert = Give(s, 0, "Covert Operative");               // Base Rank 2

        var legal = LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>().ToList();
        Assert.Contains(legal, a => a.Card == brute && a.Target == U(s, 0, 0));
        Assert.DoesNotContain(legal, a => a.Card == covert && a.Target == U(s, 0, 0));
        Assert.Contains(legal, a => a.Card == covert && a.Target == U(s, 0, 1)); // unopposed: fine (errata)
    }

    [Fact]
    public void Mind_Games_forces_opposed_nodes_when_possible()
    {
        var s = TestData.EmptyGame();
        Place(s, "Mind Games", O(s, 1));
        var brute = Give(s, 0, TestData.Brute(Race.SpaceCowboy));

        // Nothing to oppose yet: any node.
        Assert.Equal(3, LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>().Count());

        Brute(s, Race.Blaarg, U(s, 1, 1));
        var legal = LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>().ToList();
        Assert.Single(legal);
        Assert.Equal(U(s, 0, 1), legal[0].Target);

        // Mind Games only binds its controller's opponents.
        Give(s, 1, TestData.Brute(Race.Blaarg));
        Assert.True(LegalActions.ForTurn(s, 1).OfType<DeployFromHandAction>().Count() > 1);
    }

    [Fact]
    public void Military_Academy_limits_deploy_turns()
    {
        var s = TestData.EmptyGame();
        ActivateWarzone(s, "Military Academy");
        Give(s, 0, TestData.Brute(Race.SpaceCowboy));
        s.Players[0].DeployTurnsThisRound = 2;
        Assert.Contains(LegalActions.ForTurn(s, 0), a => a is DeployFromHandAction);

        s.Players[0].DeployTurnsThisRound = 3;
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0), a => a is DeployFromHandAction);
    }

    [Fact]
    public void Inspect_explains_the_statics_on_a_unit()
    {
        var s = TestData.EmptyGame();
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 0, 0));
        Place(s, "Blazing Saddles", O(s, 0));
        Assert.Contains(StaticLayer.Explain(s, cowboy), l => l.StartsWith("Blazing Saddles: +1"));
    }
}
