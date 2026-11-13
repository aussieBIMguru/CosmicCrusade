using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Xunit;

namespace CosmicCrusade.Core.Tests;

/// <summary>
/// M3.3: the scripted handlers (Ace, Terraform, Mr Morph / Forced Mitosis, TR-1993.r) and the stack-control cards
/// (Recolonize, Commandeer, One of Us). After this every card is fully implemented.
/// </summary>
public class M33Tests
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

    private static CardInstance Give(GameState s, int player, string name) => Give(s, player, Def(name));

    private static Node U(GameState s, int player, int i) => s.Players[player].UnitNodes[i];
    private static Node L(GameState s, int player) => s.Players[player].LeaderNode!;
    private static Node S(GameState s, int player, int i) => s.Players[player].StandbyNodes[i];

    private static GameState Board()
    {
        var s = TestData.EmptyGame();
        s.Options.EmptyDeck = EmptyDeckRule.NoRefill;
        return s;
    }

    private static ScriptedController Deploys(CardInstance card, Node node) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<DeployFromHandAction>().Single(a => a.Card == card && a.Target == node));

    private static ScriptedController Issues(CardInstance card) =>
        new ScriptedController().Turn(ctx => ctx.Legal.OfType<IssueOrderAction>().First(a => a.Card == card));

    private static int IndexOf(ChoiceRequest r, Node node) => r.Options.ToList().FindIndex(o => o.Node == node);
    private static int IndexOf(ChoiceRequest r, CardInstance card) => r.Options.ToList().FindIndex(o => o.Card == card);

    // ------------------------------------------------------------------ status

    [Fact]
    public void Every_card_is_now_fully_implemented()
    {
        var notFull = TestData.Db.All.Where(c => CardStatus.Of(c) != Implementation.Full).Select(c => c.Name).ToList();
        Assert.Empty(notFull);
    }

    // ------------------------------------------------------------------ Ace, Council Tactician

    [Fact]
    public void Rearrange_rules_keep_leaders_and_locked_nodes_fixed()
    {
        var s = Board();
        var leader = Place(s, "The All-knowing", L(s, 0));
        var brute = Brute(s, Race.Blaarg, U(s, 0, 0));

        Assert.True(Rearrange.IsFixed(s, L(s, 0)));
        Assert.False(Rearrange.CanSwap(s, L(s, 0), U(s, 0, 1)));          // Leaders can't move
        Assert.True(Rearrange.CanSwap(s, U(s, 0, 0), S(s, 0, 2)));         // a Unit to any of its own Standby nodes
        Assert.False(Rearrange.CanSwap(s, U(s, 0, 0), U(s, 1, 0)));        // never across the control line
        Assert.False(Rearrange.CanSwap(s, U(s, 0, 1), U(s, 0, 2)));        // two empty nodes: nothing to switch

        U(s, 0, 1).IsLocked = true;
        Assert.False(Rearrange.CanSwap(s, U(s, 0, 0), U(s, 0, 1)));        // a Locked node keeps what it has

        // A Brute can't take an empty Leader Node; a Champion can.
        TestData.Place(s, TestData.Db.All.First(c => c.IsChampion), U(s, 1, 0));
        Brute(s, Race.Necrolite, U(s, 1, 1));
        Assert.True(Rearrange.CanSwap(s, U(s, 1, 0), L(s, 1)));
        Assert.False(Rearrange.CanSwap(s, U(s, 1, 1), L(s, 1)));
        Assert.Same(leader, L(s, 0).Card);
        Assert.Same(brute, U(s, 0, 0).Card);
    }

    [Fact]
    public async Task Ace_can_push_an_opposing_unit_into_its_standby()
    {
        var s = Board();
        var ace = Give(s, 0, "Ace, Council Tactician");
        var theirs = Brute(s, Race.Blaarg, U(s, 1, 0));

        var p0 = Deploys(ace, L(s, 0));
        int steps = 0;
        p0.Chooser = r => r.Purpose switch
        {
            "RearrangeFrom" => steps++ == 0 ? new[] { IndexOf(r, U(s, 1, 0)) } : Array.Empty<int>(),
            _ when r.Purpose.StartsWith("RearrangeTo:") => new[] { IndexOf(r, S(s, 1, 0)) },
            _ => null,
        };
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Same(S(s, 1, 0), theirs.Location.Node);
        Assert.Equal(0, RankCalculator.TotalRank(s, 1));
        Assert.Same(L(s, 0), ace.Location.Node); // it doesn't move itself
        Assert.DoesNotContain(p0.ChoicesAsked, r => r.Purpose == "RearrangeFrom" && r.Options.Any(o => o.Node == L(s, 0)));
    }

    // ------------------------------------------------------------------ Terraform

    [Fact]
    public async Task Terraform_lets_each_controller_keep_their_unit_in_standby_or_lose_it()
    {
        var s = Board();
        var terraform = Give(s, 0, "Terraform");
        var mine = Brute(s, Race.Blaarg, U(s, 0, 1));
        var theirs = Brute(s, Race.Necrolite, U(s, 1, 1));

        var p0 = Issues(terraform);
        p0.Chooser = r => r.Purpose == "Terraform" ? new[] { IndexOf(r, U(s, 0, 1)) } : null; // keep: default "Yes"
        var p1 = ScriptedController.PassForever();
        p1.Chooser = r => r.Purpose == "TerraformKeep" ? new[] { 1 } : null;               // "No": it is Nullified
        await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Same(S(s, 0, 1), mine.Location.Node);
        Assert.Equal(PileKind.NullifiedUnits, theirs.Location.Pile);
        Assert.True(U(s, 0, 1).IsLocked);
        Assert.True(U(s, 1, 1).IsLocked);
    }

    [Fact]
    public async Task Terraform_nullifies_a_leader_without_asking()
    {
        var s = Board();
        var terraform = Give(s, 0, "Terraform");
        var leader = Place(s, "The All-knowing", L(s, 1));

        var p0 = Issues(terraform);
        p0.Chooser = r => r.Purpose == "Terraform" ? new[] { IndexOf(r, L(s, 0)) } : null;
        var p1 = ScriptedController.PassForever();
        await OrderReactAssembleTests.Run(s, p0, p1);

        Assert.Equal(PileKind.NullifiedUnits, leader.Location.Pile);
        Assert.DoesNotContain(p1.ChoicesAsked, r => r.Purpose == "TerraformKeep");
        Assert.True(L(s, 0).IsLocked && L(s, 1).IsLocked);
    }

    // ------------------------------------------------------------------ Mr Morph / Forced Mitosis

    [Fact]
    public async Task Mr_morph_enters_as_a_blaarg_copy_and_turns_back_when_it_leaves()
    {
        var s = Board();
        var morph = Give(s, 0, "Mr Morph");
        var cowboy = Brute(s, Race.SpaceCowboy, U(s, 1, 0));

        await OrderReactAssembleTests.Run(s, Deploys(morph, L(s, 0)), ScriptedController.PassForever());

        Assert.True(morph.IsCopy);
        Assert.Same(cowboy.Definition, morph.Definition);
        Assert.Equal(4, RankCalculator.GetRank(s, morph));
        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, morph).ToArray());
        Assert.Contains("(as", morph.Name);
        Assert.False(Placement.CanEnterStandby(morph)); // still a Leader card underneath (ruling #36)

        s.MoveToPile(morph, PileKind.RetiredUnits);
        Assert.False(morph.IsCopy);
        Assert.Equal("Mr Morph", morph.Name);
    }

    [Fact]
    public async Task Forced_mitosis_turns_the_deploying_unit_into_a_copy()
    {
        var s = Board();
        var spider = Place(s, "Sprocket Spider", U(s, 1, 2)); // Rank 1, no abilities
        var brute = Give(s, 0, TestData.Brute(Race.Necrolite));
        Give(s, 1, "Forced Mitosis");

        await OrderReactAssembleTests.Run(s, Deploys(brute, U(s, 0, 0)), ScriptedController.PassForever().React("Forced Mitosis"));

        Assert.Same(U(s, 0, 0), brute.Location.Node);
        Assert.Same(spider.Definition, brute.Definition);
        Assert.Equal(1, RankCalculator.GetRank(s, brute));
        Assert.Equal(new[] { Race.Blaarg }, StaticLayer.Races(s, brute).ToArray());
    }

    [Fact]
    public void Forced_mitosis_needs_a_unit_in_play_to_copy()
    {
        var s = Board();
        var mitosis = Give(s, 1, "Forced Mitosis");
        var brute = Give(s, 0, TestData.Brute(Race.Necrolite));
        s.MoveToStack(brute, 0);
        var item = new StackItem(0, StackItemKind.DeployUnit, brute, "Deploy", (_, _) => Task.CompletedTask) { TargetNode = U(s, 0, 0) };
        Assert.False(ReactConditions.IsMet(s, mitosis, item));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        Assert.True(ReactConditions.IsMet(s, mitosis, item));
    }

    // ------------------------------------------------------------------ TR-1993.r

    [Fact]
    public async Task Tr_1993_re_runs_a_chosen_units_when_you_play_ability()
    {
        var s = Board();
        var neuroling = Place(s, "Neuroling", U(s, 0, 0)); // When you play: draw up to 2 Units
        var tr = Give(s, 0, "TR-1993.r");
        while (s.Players[0].HandUnitCount < 5) Give(s, 0, TestData.Brute(Race.Blaarg));
        while (s.Players[1].HandUnitCount < 5) Give(s, 1, TestData.Brute(Race.Blaarg));
        for (int i = 0; i < 3; i++) s.MoveToPile(s.CreateInstance(TestData.Brute(Race.Overmind)), PileKind.UnitDeck);

        var p0 = Deploys(tr, L(s, 0));
        p0.Chooser = r => r.Purpose switch
        {
            "Retrigger" => new[] { IndexOf(r, neuroling) },
            "Draw" => new[] { r.Options.Count - 1 },
            _ => null,
        };
        await OrderReactAssembleTests.Run(s, p0, ScriptedController.PassForever());

        Assert.Equal(6, s.Players[0].HandUnitCount); // 5 − TR + 2
        Assert.Single(s.UnitDeck);
        Assert.DoesNotContain(p0.ChoicesAsked.Where(r => r.Purpose == "Retrigger").SelectMany(r => r.Options), o => o.Card == tr);
    }

    // ------------------------------------------------------------------ Recolonize

    [Fact]
    public async Task Recolonize_sends_the_deploying_unit_to_another_legal_node()
    {
        var s = Board();
        var brute = Give(s, 0, TestData.Brute(Race.Blaarg));
        Give(s, 1, "Recolonize");

        var p1 = ScriptedController.PassForever().React("Recolonize");
        p1.Chooser = r => r.Purpose.StartsWith("Redirect:") ? new[] { IndexOf(r, U(s, 0, 2)) } : null;
        var sink = await OrderReactAssembleTests.Run(s, Deploys(brute, U(s, 0, 0)), p1);

        Assert.Same(U(s, 0, 2), brute.Location.Node);
        Assert.Contains(sink.Events, e => e is DeployRedirectedEvent d && d.From == U(s, 0, 0) && d.To == U(s, 0, 2));
        // A Brute can't go to a Leader Node, so that was never offered.
        Assert.DoesNotContain(p1.ChoicesAsked.SelectMany(r => r.Options), o => o.Node?.Kind == NodeKind.Leader);
    }

    // ------------------------------------------------------------------ One of Us / Commandeer

    [Fact]
    public async Task One_of_us_takes_a_unit_which_stays_in_play_with_its_rank_changes()
    {
        var s = Board();
        var oneOfUs = Give(s, 0, "One of Us");
        var theirs = Brute(s, Race.Overmind, U(s, 1, 1));
        theirs.PersistentModifiers.Add(new RankModifier("Test", 1));

        var sink = await OrderReactAssembleTests.Run(s, Issues(oneOfUs), ScriptedController.PassForever());

        Assert.Equal(0, theirs.Location.Node!.Side);
        Assert.Equal(5, RankCalculator.GetRank(s, theirs));
        Assert.Equal(5, RankCalculator.TotalRank(s, 0));
        Assert.Equal(0, RankCalculator.TotalRank(s, 1));
        Assert.DoesNotContain(sink.Events, e => e is UnitPlayedEvent p && p.Card == theirs); // not Played again
    }

    [Fact]
    public async Task Commandeer_takes_over_an_action_so_its_new_controller_makes_the_choices()
    {
        var s = Board();
        var oneOfUs = Give(s, 0, "One of Us");
        var mine = Brute(s, Race.Blaarg, U(s, 0, 0));
        var theirs = Brute(s, Race.Necrolite, U(s, 1, 1));
        Give(s, 1, "Commandeer");

        var sink = await OrderReactAssembleTests.Run(s, Issues(oneOfUs), ScriptedController.PassForever().React("Commandeer"));

        Assert.Contains(sink.Events, e => e is StackItemControlChangedEvent c && c.Item.Card == oneOfUs && c.NewController == 1);
        Assert.Equal(1, mine.Location.Node!.Side);   // One of Us resolved for the AI: it took OUR Unit
        Assert.Same(U(s, 1, 1), theirs.Location.Node);
        Assert.Equal(PileKind.IssuedOrders, oneOfUs.Location.Pile);
    }

    // ------------------------------------------------------------------ AI

    [Fact]
    public void Ai_rearranges_by_shoving_the_opponents_unit_into_standby()
    {
        var s = Board();
        Brute(s, Race.Blaarg, U(s, 0, 0));
        Brute(s, Race.Blaarg, U(s, 1, 0));
        var movable = Rearrange.Movable(s);
        var r = new ChoiceRequest(1, null, "test", ChoiceKind.Nodes,
            movable.Select(n => new ChoiceOption(n.Id, Node: n)).ToList(), 0, 1, "RearrangeFrom");
        var pick = AiChoices.Choose(r, s);
        Assert.Single(pick);
        Assert.Same(U(s, 0, 0), movable[pick[0]]); // the AI (player 1) moves OUR Brute out of scoring
    }

    [Fact]
    public void Ai_copies_big_for_itself_and_small_for_the_opponent()
    {
        var s = Board();
        var spider = Place(s, "Sprocket Spider", U(s, 0, 0));
        var brute = Brute(s, Race.Blaarg, U(s, 0, 1));
        var options = new[] { new ChoiceOption("a", spider), new ChoiceOption("b", brute) };
        Assert.Equal(1, AiChoices.Choose(new ChoiceRequest(1, null, "t", ChoiceKind.Cards, options, 1, 1, "CopyFor:1"), s)[0]);
        Assert.Equal(0, AiChoices.Choose(new ChoiceRequest(1, null, "t", ChoiceKind.Cards, options, 1, 1, "CopyFor:0"), s)[0]);
    }
}
