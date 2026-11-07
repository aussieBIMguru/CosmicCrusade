using CosmicCrusade.Core.Effects;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>M3: the effect interpreter – "When you play" abilities, Lock/Unlock, leave-play triggers, live Orders.</summary>
public class EffectTests
{
    // ------------------------------------------------------------------ helpers

    private static CardDefinition Def(string name) => TestData.Db.All.First(c => c.Name == name);

    private static CardInstance Give(GameState s, int player, CardDefinition def)
    {
        var c = s.CreateInstance(def);
        s.MoveToHand(c, player);
        return c;
    }

    private static CardInstance Give(GameState s, int player, string name) => Give(s, player, Def(name));

    /// <summary>Fills a hand to 5 Units with vanilla Brutes so Start-up draws nothing from a hand-built deck.</summary>
    private static void FillHand(GameState s, int player)
    {
        while (s.Players[player].HandUnitCount < 5) Give(s, player, TestData.Brute(Race.Necrolite));
    }

    private static Task<StoppingSink> Run(GameState s, ScriptedController p0, ScriptedController p1, bool throughCleanUp = false) =>
        OrderReactAssembleTests.Run(s, p0, p1, throughCleanUp);

    private static ScriptedController Deploys(CardInstance card, Node node) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == card && a.Target == node));

    private static ScriptedController Issues(CardInstance card) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == card));

    /// <summary>Picks the option for this node (or card), otherwise falls back to the default.</summary>
    private static Func<ChoiceRequest, IReadOnlyList<int>?> Pick(Node? node = null, CardInstance? card = null) => r =>
    {
        int i = r.Options.ToList().FindIndex(o => (node is not null && o.Node == node) || (card is not null && o.Card == card));
        return i >= 0 ? new[] { i } : null;
    };

    // ------------------------------------------------------------------ coverage

    [Fact]
    public void Every_unscripted_when_you_play_ability_is_live()
    {
        var onPlay = TestData.Db.All.Where(c => HasTrigger(c, "OnPlay")).ToList();
        var notLive = onPlay.Where(c => !EffectSupport.IsLive(c, "OnPlay")).Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(46, onPlay.Count); // 38 + 8 Spacecraft
        Assert.Empty(notLive); // M3.3: Ace and TR-1993.r are scripted handlers now
    }

    [Fact]
    public void Leave_play_and_if_nullified_abilities_are_all_live()
    {
        Assert.All(TestData.Db.All.Where(c => HasTrigger(c, "OnLeavePlay")), c => Assert.True(EffectSupport.IsLive(c, "OnLeavePlay"), c.Name));
        Assert.All(TestData.Db.All.Where(c => HasTrigger(c, "OnSelfNullified")), c => Assert.True(EffectSupport.IsLive(c, "OnSelfNullified"), c.Name));
    }

    [Fact]
    public void Live_orders_are_the_expected_ones()
    {
        var actions = TestData.Db.All.Where(c => HasTrigger(c, "OnResolve")).ToList();
        var inert = actions.Where(c => !EffectSupport.IsLive(c, "OnResolve")).Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Empty(inert); // M3.3: One of Us and Terraform are live

        var liveReacts = TestData.Db.All.Where(c => c.OrderType == OrderType.React && EffectSupport.IsLive(c, "React"))
            .Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[]
        {
            "Already Dead", "Commandeer", "Forced Mitosis", "Gloop!", "Hijack", "Recolonize", "Respond in Kind",
            "Second Thought", "Self-Destruct", "Shadow Strike", "Spreading Sickness", "Target Practice",
        }, liveReacts);
    }

    private static bool HasTrigger(CardDefinition c, string trigger) =>
        c.Effects.ValueKind == System.Text.Json.JsonValueKind.Array
        && c.Effects.EnumerateArray().Any(e => e.TryGetProperty("trigger", out var t) && t.GetString() == trigger);

    // ------------------------------------------------------------------ when you play

    [Fact]
    public async Task Covert_Operative_may_nullify_the_opposing_unit()
    {
        var s = TestData.EmptyGame();
        var enemy = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]);
        var covert = Give(s, 0, "Covert Operative");

        var sink = await Run(s, Deploys(covert, s.Players[0].UnitNodes[0]), ScriptedController.PassForever());

        Assert.Equal(PileKind.NullifiedUnits, enemy.Location.Pile);
        Assert.Same(covert, s.Players[0].UnitNodes[0].Card);
        Assert.Contains(sink.Events, e => e is CardNullifiedEvent n && n.Card == enemy && n.Source == covert);
    }

    [Fact]
    public async Task Declining_an_optional_ability_does_nothing()
    {
        var s = TestData.EmptyGame();
        var enemy = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]);
        var covert = Give(s, 0, "Covert Operative");
        var p0 = Deploys(covert, s.Players[0].UnitNodes[0]);
        p0.Chooser = r => r.Kind == ChoiceKind.YesNo ? new[] { 1 } : null;

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Same(enemy, s.Players[1].UnitNodes[0].Card);
    }

    [Fact]
    public async Task Race_conditions_gate_the_ability()
    {
        // Chrome Domer only acts against a Necrolite.
        var s = TestData.EmptyGame();
        var blaarg = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]);
        var domer = Give(s, 0, "Chrome Domer");
        var p0 = Deploys(domer, s.Players[0].UnitNodes[0]);

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Same(blaarg, s.Players[1].UnitNodes[0].Card);
        Assert.Empty(p0.ChoicesAsked); // condition not met: not even asked
        Assert.Equal(1, RankCalculator.GetRank(s, domer));
    }

    [Fact]
    public async Task Chrome_Domer_nullifies_a_Necrolite_and_gains_2()
    {
        var s = TestData.EmptyGame();
        var necro = TestData.Place(s, TestData.Brute(Race.Necrolite), s.Players[1].UnitNodes[0]);
        var domer = Give(s, 0, "Chrome Domer");

        await Run(s, Deploys(domer, s.Players[0].UnitNodes[0]), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, necro.Location.Pile);
        Assert.Equal(3, RankCalculator.GetRank(s, domer));
    }

    [Theory]
    [InlineData("Steady Shooter", true)]
    [InlineData("Crimson Redeemer", false)]
    public async Task Play_duels_and_Crimson_Redeemer_is_protected(string name, bool shooterNullified)
    {
        var s = TestData.EmptyGame();
        var brute = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]); // Rank 4 beats Rank 2
        var shooter = Give(s, 0, name);

        var sink = await Run(s, Deploys(shooter, s.Players[0].UnitNodes[0]), ScriptedController.PassForever());

        Assert.Contains(sink.Events, e => e is DuelEvent);
        Assert.Equal(shooterNullified, shooter.Location.Pile == PileKind.NullifiedUnits);
        Assert.Same(brute, s.Players[1].UnitNodes[0].Card);
    }

    [Fact]
    public async Task Shot_in_the_Dark_sets_a_chosen_units_base_rank_to_0()
    {
        var s = TestData.EmptyGame();
        var brute = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[1]);
        var shot = Give(s, 0, "Shot in the Dark");
        var p0 = Deploys(shot, s.Players[0].UnitNodes[0]);
        p0.Chooser = Pick(card: brute);

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(0, RankCalculator.GetRank(s, brute));
    }

    [Fact]
    public async Task Roundup_Gang_locks_a_node_and_unlocks_it_when_it_leaves_play()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        var gang = Give(s, 0, "Roundup Gang");
        var target = s.Players[1].UnitNodes[1];
        var p0 = new ScriptedController { StopWhenDone = false }
            .Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == gang && a.Target == s.Players[0].UnitNodes[0]));
        p0.Chooser = Pick(node: target);

        var sink = await Run(s, p0, ScriptedController.PassForever(), throughCleanUp: true);

        int locked = sink.Events.FindIndex(e => e is NodeLockChangedEvent { Locked: true } l && l.Node == target);
        int unlocked = sink.Events.FindIndex(e => e is NodeLockChangedEvent { Locked: false } l && l.Node == target && l.Source == gang);
        int retired = sink.Events.FindIndex(e => e is CardMovedToPileEvent m && m.Card == gang);
        Assert.True(locked >= 0);
        Assert.True(retired > locked && unlocked > retired); // Clean-up retires it → "When this Unit leaves play, Unlock that Node"
    }

    [Fact]
    public void Locked_nodes_block_deploys()
    {
        var s = TestData.EmptyGame();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));
        s.Players[0].UnitNodes[0].IsLocked = true;
        Assert.DoesNotContain(LegalActions.ForTurn(s, 0).OfType<DeployFromHandAction>(), a => a.Card == brute && a.Target == s.Players[0].UnitNodes[0]);
    }

    [Fact]
    public async Task Twisted_Firestarter_takes_the_opposing_unit_with_it()
    {
        var s = TestData.EmptyGame();
        var fire = TestData.Place(s, Def("Twisted Firestarter"), s.Players[1].UnitNodes[0]);
        var covert = Give(s, 0, "Covert Operative");

        await Run(s, Deploys(covert, s.Players[0].UnitNodes[0]), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, fire.Location.Pile);
        Assert.Equal(PileKind.NullifiedUnits, covert.Location.Pile);
    }

    [Fact]
    public async Task Psyphoon_sends_other_overminds_to_standby_and_draws_that_many()
    {
        var s = TestData.EmptyGame();
        var a = TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[0].UnitNodes[0]);
        var b = TestData.Place(s, TestData.Brute(Race.Overmind), s.Players[0].UnitNodes[1]);
        var psy = Give(s, 0, "Psyphoon");
        FillHand(s, 0);
        FillHand(s, 1);
        for (int i = 0; i < 4; i++) s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Blaarg)), PileKind.UnitDeck);
        int handBefore = s.Players[0].Hand.Count;

        await Run(s, Deploys(psy, s.Players[0].LeaderNode!), ScriptedController.PassForever());

        Assert.Same(a, s.Players[0].StandbyNodes[0].Card);
        Assert.Same(b, s.Players[0].StandbyNodes[1].Card);
        Assert.Equal(handBefore - 1 + 2, s.Players[0].Hand.Count);
    }

    [Fact]
    public async Task Neuroling_draws_up_to_2_units()
    {
        var s = TestData.EmptyGame();
        var neuro = Give(s, 0, "Neuroling");
        FillHand(s, 0);
        FillHand(s, 1);
        for (int i = 0; i < 3; i++) s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Blaarg)), PileKind.UnitDeck);
        var p0 = Deploys(neuro, s.Players[0].UnitNodes[0]);
        p0.Chooser = r => r.Purpose == "Draw" ? new[] { r.Options.ToList().FindIndex(o => o.Value == 2) } : null;

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(1, s.UnitDeck.Count);
        Assert.Equal(6, s.Players[0].HandUnitCount);
    }

    [Fact]
    public async Task Arganium_switches_two_unit_nodes_and_control_follows_the_side()
    {
        var s = TestData.EmptyGame();
        var theirs = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[2]);
        var argan = Give(s, 0, "Arganium");
        var p0 = Deploys(argan, s.Players[0].LeaderNode!);
        var mine = s.Players[0].UnitNodes[2];
        var their = s.Players[1].UnitNodes[2];
        p0.Chooser = r => r.Purpose == "Swap"
            ? new[] { r.Options.ToList().FindIndex(o => o.Node == mine), r.Options.ToList().FindIndex(o => o.Node == their) }.OrderBy(i => i).ToList()
            : null;

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Same(theirs, mine.Card);
        Assert.Null(their.Card);
        Assert.Equal(4 + 3, RankCalculator.TotalRank(s, 0)); // Brute 4 + Arganium 3
    }

    // ------------------------------------------------------------------ reacts and actions

    [Fact]
    public async Task Spreading_Sickness_sends_the_deploying_unit_to_standby_and_locks_its_node()
    {
        var s = TestData.EmptyGame();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));
        Give(s, 1, "Spreading Sickness");
        var u2 = s.Players[0].UnitNodes[1];

        await Run(s, Deploys(brute, u2), ScriptedController.PassForever().React("Spreading Sickness"));

        Assert.Same(brute, s.Players[0].StandbyNodes[1].Card);
        Assert.True(u2.IsLocked);
        Assert.Null(u2.Card);
    }

    [Fact]
    public async Task Gloop_suppresses_the_units_when_you_play_ability()
    {
        var s = TestData.EmptyGame();
        var enemy = TestData.Place(s, TestData.Brute(Race.Blaarg), s.Players[1].UnitNodes[0]);
        var covert = Give(s, 0, "Covert Operative");
        Give(s, 1, "Gloop!");

        await Run(s, Deploys(covert, s.Players[0].UnitNodes[0]), ScriptedController.PassForever().React("Gloop!"));
        Assert.Same(covert, s.Players[0].UnitNodes[0].Card);
        Assert.Same(enemy, s.Players[1].UnitNodes[0].Card);
    }

    [Fact]
    public async Task Depose_nullifies_a_leader()
    {
        var s = TestData.EmptyGame();
        // Not the first Leader in the data: that's Vigan, who is Immune since M3.2a.
        var leader = TestData.Place(s, Def("Quickraw Murphy"), s.Players[1].LeaderNode!);
        var depose = Give(s, 0, "Depose");

        await Run(s, Issues(depose), ScriptedController.PassForever());
        Assert.Equal(PileKind.NullifiedUnits, leader.Location.Pile);
        Assert.Equal(PileKind.IssuedOrders, depose.Location.Pile);
    }

    [Fact]
    public async Task Sheriffs_Badge_gives_space_cowboys_2_and_others_1()
    {
        var s = TestData.EmptyGame();
        var cowboy = TestData.Place(s, TestData.Brute(Race.SpaceCowboy), s.Players[0].UnitNodes[0]);
        var badge = Give(s, 0, "Sheriff's Badge");
        var p0 = Issues(badge);
        p0.Chooser = Pick(card: cowboy);

        await Run(s, p0, ScriptedController.PassForever());
        Assert.Equal(6, RankCalculator.GetRank(s, cowboy));
    }

    [Fact]
    public async Task Rank_changes_end_when_the_unit_leaves_play()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        var cowboy = TestData.Place(s, TestData.Brute(Race.SpaceCowboy), s.Players[0].UnitNodes[0]);
        var badge = Give(s, 0, "Sheriff's Badge");
        var p0 = new ScriptedController { StopWhenDone = false }.Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == badge));
        p0.Chooser = Pick(card: cowboy);

        await Run(s, p0, ScriptedController.PassForever(), throughCleanUp: true);
        Assert.Equal(PileKind.RetiredUnits, cowboy.Location.Pile);
        Assert.Empty(cowboy.PersistentModifiers);
    }

    [Fact]
    public async Task Terraform_on_an_empty_pair_just_locks_both_nodes()
    {
        var s = TestData.EmptyGame();
        var terraform = Give(s, 0, "Terraform");
        var sink = await Run(s, Issues(terraform), ScriptedController.PassForever());
        Assert.Contains(sink.Events, e => e is OrderResolvedEvent { EffectApplied: true } r && r.Card == terraform);
        Assert.Equal(2, sink.Events.OfType<NodeLockChangedEvent>().Count(e => e.Locked));
    }
}
