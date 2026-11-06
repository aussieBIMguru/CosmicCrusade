using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Views;

/// <summary>Visuals for ability events (M3): Nullify, Lock, Rank changes, Standby moves, returns, Duels, switches.</summary>
public partial class GameView
{
    private static readonly Color LockColor = Color.FromRgb(226, 232, 240);

    private string Who(int player) => player == 0 ? "You" : "AI";

    private async Task OnAbilityTriggeredAsync(AbilityTriggeredEvent e)
    {
        string trigger = e.Trigger switch
        {
            "OnPlay" => "when played",
            "OnLeavePlay" => "leaves play",
            "OnSelfNullified" => "was Nullified",
            "React" or "OnResolve" => "resolves",
            _ => e.Trigger,
        };
        _hud.AddLog($"✦ {e.Source.Name} ({trigger}): {e.Source.Definition.Text}");
        switch (e.Source.Definition.Name)
        {
            case "Wormhole":
                OpenPortal();
                break;
            case "Dimensional Rift":
                OpenRift();
                break;
            case "High Noon":
                await HighNoonAsync();
                break;
            case "Ace, Council Tactician":
                ShowTacticalGrid();
                break;
        }
        if (_boardCards.TryGetValue(e.Source.InstanceId, out var v))
        {
            v.SetGlow(Gold, pulse: true);
            await Anim.Delay(350);
            v.SetGlow(null);
        }
    }

    private void OnChoiceMade(ChoiceMadeEvent e)
    {
        if (e.Request.Kind == ChoiceKind.YesNo && e.Request.Purpose.StartsWith("Optional"))
            _hud.AddLog($"  {Who(e.Request.Player)}: {(e.Picks.Count > 0 && e.Picks[0] == 0 ? "uses it" : "declines")}.");
        else
            _hud.AddLog($"  {Who(e.Request.Player)} chose: {e.Summary}");
        // Psychic Coercion: tendrils reach for the node the coercer picked.
        if (e.Request.Purpose == "CoerceNode" && e.Picks.Count > 0 && e.Request.Options[e.Picks[0]].Node is { } coerced)
            Tendrils(e.Request.Player, coerced);
    }

    private async Task OnCardNullifiedAsync(CardNullifiedEvent e)
    {
        string by = e.Source is null || e.Source == e.Card ? "" : $" by {e.Source.Name}";
        _hud.AddLog($"  {e.Card.Name} is Nullified{by}.");

        if (e.FromNode is not null && _boardCards.Remove(e.Card.InstanceId, out var dying))
        {
            // The death animation: flash, burn, crumble to ash, skull – or the source's own way of killing.
            var center = BoardLayout.Center(e.FromNode);
            LookAt(center);
            switch (e.Source?.Definition.Name)
            {
                case "Black Hole":
                    await BlackHoleSuckAsync(dying);
                    break;
                case "Xeno Laser" when e.Source.Location.Node is { } laserNode:
                    await LaserAsync(BoardLayout.Center(laserNode), dying);
                    _ = FloatTextAtAsync("NULLIFIED", ProjectToFx(center), Red);
                    await DeathAsync(dying);
                    break;
                case "Respond in Kind":
                    await MirrorFlashAsync(dying);
                    _ = FloatTextAtAsync("NULLIFIED", ProjectToFx(center), Red);
                    await DeathAsync(dying);
                    break;
                default:
                    _ = FloatTextAtAsync("NULLIFIED", ProjectToFx(center), Red);
                    await DeathAsync(dying);
                    break;
            }
            UpdatePiles();
            if (_piles.TryGetValue(e.To, out var pile)) pile.Pulse();
        }
        else
        {
            UpdatePiles();
            if (_piles.TryGetValue(e.To, out var pile)) pile.Pulse();
        }
        RefreshScores();
    }

    private async Task OnNodeLockChangedAsync(NodeLockChangedEvent e)
    {
        string side = e.Node.Side == 0 ? "your" : "the AI's";
        _hud.AddLog($"  {side} {e.Node.Label} is {(e.Locked ? "Locked" : "Unlocked")}{(e.Source is null ? "" : $" ({e.Source.Name})")}.");
        if (_slots.TryGetValue(e.Node, out var slot)) slot.Refresh();
        var center = BoardLayout.Center(e.Node);
        Shockwave(center, e.Locked ? LockColor : Cyan);
        Sfx.Play(SfxKind.Lock, e.Locked ? 1 : 0.6);
        _ = FloatTextAtAsync(e.Locked ? "LOCKED" : "UNLOCKED", ProjectToFx(center), e.Locked ? LockColor : Cyan);
        await Anim.Delay(320);
    }

    private async Task OnRankChangedAsync(RankChangedEvent e)
    {
        RefreshScores();
        int rank = RankCalculator.GetRank(State, e.Card);
        _hud.AddLog($"  {e.Card.Name}: {e.Description} → Rank {rank}. Score {_hud.MyTotal}–{_hud.AiTotal}.");
        if (_boardCards.TryGetValue(e.Card.InstanceId, out var v) && e.Card.Location.Node is { } node)
        {
            bool up = e.Description.StartsWith("+");
            var color = e.Description.StartsWith("Base") ? Purple : up ? Green : Red;
            v.SetGlow(color);
            var fx = e.Kind switch
            {
                RankChangeKind.Manipulate => SlimeSplatAsync(BoardLayout.Center(node), v),
                RankChangeKind.Assembled => CogSpinAsync(BoardLayout.Center(node), 700),
                _ => Task.CompletedTask,
            };
            _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 1, 300, Anim.Back, from: 1.15);
            _ = Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 1, 300, Anim.Back, from: 1.15);
            _ = FloatTextAtAsync(e.Description.Split(' ')[0] == "Base" ? $"RANK {rank}" : e.Description.Split(' ')[0],
                ProjectToFx(BoardLayout.Center(node)), color);
            await Task.WhenAll(fx, Anim.Delay(420));
            v.SetGlow(null);
        }
    }

    private async Task OnCardMovedToStandbyAsync(CardMovedToStandbyEvent e)
    {
        var to = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.To)));
        _hud.AddLog($"  {e.Card.Name} goes to {(e.To.Side == 0 ? "your" : "the AI's")} Standby {e.To.Label}.");

        if (e.FromStack)
        {
            Rect start;
            if (_flying is not null && _flying.Card == e.Card)
            {
                start = FxBounds(_flying);
                FxCanvas.Children.Remove(_flying);
                _flying = null;
            }
            else start = StackMiniRect(e.Card, out _);
            await FlyAsync(e.Card, start, to, false, false, 420, Purple);
            AddBoardCard(e.Card, e.To);
        }
        else if (e.FromNode is not null && _boardCards.TryGetValue(e.Card.InstanceId, out var v))
        {
            v.HideRank();
            var c = BoardLayout.Center(e.To);
            _ = Anim.To(v, Canvas.LeftProperty, c.X - v.Width / 2, 420, Anim.InOut);
            await Anim.To(v, Canvas.TopProperty, c.Y - v.Height / 2, 420, Anim.InOut);
        }
        else if (e.FromPile is { } pile)
        {
            UpdatePiles();
            await FlyAsync(e.Card, PileCardRect(pile), to, false, false, 460, pile == PileKind.NullifiedUnits ? DecayGreen : Purple);
            var risen = AddBoardCard(e.Card, e.To);
            // Back from the dead (Shallow Grave, Grim Angler, Reaper of Rot).
            if (pile == PileKind.NullifiedUnits) await DecayAsync(risen, "RISEN");
        }
        RefreshScores();
    }

    private async Task OnCardReturnedToHandAsync(CardReturnedToHandEvent e)
    {
        _hud.AddLog($"  {e.Card.Name} returns to {(e.Player == 0 ? "your" : "the AI's")} hand.");
        Rect start;
        if (e.FromNode is not null && _boardCards.Remove(e.Card.InstanceId, out var v))
        {
            BoardCanvas.Children.Remove(v);
            start = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.FromNode)));
        }
        else
        {
            start = PileCardRect(e.FromPile ?? PileKind.RetiredUnits);
        }
        UpdatePiles();
        RefreshScores();

        if (e.Player == 0)
        {
            await FlyAsync(e.Card, start, new Rect(FxCanvas.ActualWidth / 2 - HandCardW / 2, FxCanvas.ActualHeight - 120,
                HandCardW, HandCardW / CardFaceFactory.Aspect), false, false, 420, Cyan);
            var hv = CreateHandCard(e.Card);
            _handViews.Add(hv);
            HandCanvas.Children.Add(hv);
            LayoutHand(animate: false);
        }
        else
        {
            await FlyAsync(e.Card, start, new Rect(FxCanvas.ActualWidth / 2 - OppHandCardW / 2, -40, OppHandCardW,
                OppHandCardW / CardFaceFactory.Aspect), false, false, 420, Red);
            var back = CreateOpponentHandCard();
            _oppHandViews.Add(back);
            OpponentHandCanvas.Children.Add(back);
            LayoutOpponentHand(animate: false);
        }
    }

    private async Task OnCardRetiredFromHandAsync(CardRetiredFromHandEvent e)
    {
        _hud.AddLog($"  {Who(e.Player)} Retire{(e.Player == 0 ? "" : "s")} {e.Card.Name} from hand.");
        var (start, faceDown) = TakeFromHand(e.Player, e.Card);
        var pile = e.Card.Definition.IsOrder ? PileKind.IssuedOrders : PileKind.RetiredUnits;
        await FlyAsync(e.Card, start, PileCardRect(pile), faceDown, flipToFaceUp: faceDown, ms: 380, glow: Gold);
        UpdatePiles();
        _piles[pile].Pulse();
    }

    /// <summary>Abominations' cost: a card Nullified straight from a hand flies to its pile with a red glow.</summary>
    private async Task OnCardNullifiedFromHandAsync(CardNullifiedFromHandEvent e)
    {
        _hud.AddLog($"  {Who(e.Player)} Nullif{(e.Player == 0 ? "y" : "ies")} {e.Card.Name} from hand{(e.Source is { } s ? $" ({s.Name})" : "")}.");
        var (start, faceDown) = TakeFromHand(e.Player, e.Card);
        await FlyAsync(e.Card, start, PileCardRect(e.To), faceDown, flipToFaceUp: faceDown, ms: 380, glow: Red);
        UpdatePiles();
        _piles[e.To].Pulse();
    }

    private async Task OnDuelAsync(DuelEvent e)
    {
        string result = e.Losers.Count == 2 ? "matched Ranks: both are Nullified"
            : e.Loser is not null ? $"{e.Loser.Name} loses{(e.Protected ? " (the other is protected)" : "")}"
            : e.Protected ? "the loser is protected" : "nothing happens";
        _hud.AddLog($"  DUEL: {e.A.Name} ({RankCalculator.GetRank(State, e.A)}) vs {e.B.Name} ({RankCalculator.GetRank(State, e.B)}) – {result}.");
        CardView? va = _boardCards.GetValueOrDefault(e.A.InstanceId);
        CardView? vb = _boardCards.GetValueOrDefault(e.B.InstanceId);

        // Effects pass 3: a tie decided by weakness shows the race cycle; an AI win may earn a taunt.
        if (WeaknessDecider(e) is { } decided) await RaceCycleAsync(decided.Winner, decided.Loser);
        if (e.Losers.Count > 0 && e.Losers.All(l => l.Location.Node?.Side == 0)) MaybeTaunt(DuelTaunts, 0.6);

        if (va is not null && vb is not null && e.A.Location.Node is { } nodeA && e.B.Location.Node is { } nodeB)
        {
            // Blasters at dawn: shots fly back and forth, then the loser(s) get smoked out (the Nullify follows).
            var mid = new Point((BoardLayout.Center(nodeA).X + BoardLayout.Center(nodeB).X) / 2,
                (BoardLayout.Center(nodeA).Y + BoardLayout.Center(nodeB).Y) / 2);
            LookAt(mid);
            // Effects pass 3: the Duel that decides the game plays in slow motion with the camera leaning in.
            bool slowMo = IsDecidingDuel(e);
            double speed = Anim.SpeedFactor;
            if (slowMo)
            {
                await SlowMoBeginAsync(mid);
                Anim.SpeedFactor = speed * 2.2;
            }
            try
            {
                _ = FloatTextAtAsync("DUEL!", ProjectToFx(mid), Gold);
                var losers = e.Losers.Select(l => _boardCards.GetValueOrDefault(l.InstanceId)).OfType<CardView>().ToList();
                var winners = new[] { va, vb }.Where(x => !losers.Contains(x) && e.Losers.Count > 0).ToList();
                await BlasterDuelAsync(va, vb, nodeA.Side, nodeB.Side, losers, winners);
            }
            finally
            {
                if (slowMo)
                {
                    Anim.SpeedFactor = speed;
                    await SlowMoEndAsync();
                }
            }
            return;
        }
        va?.SetGlow(Gold, pulse: true);
        vb?.SetGlow(Gold, pulse: true);
        if (e.A.Location.Node is { } na && e.B.Location.Node is { } nb)
        {
            var ca = BoardLayout.Center(na);
            var cb = BoardLayout.Center(nb);
            _ = FloatTextAtAsync("DUEL!", ProjectToFx(new Point((ca.X + cb.X) / 2, (ca.Y + cb.Y) / 2)), Gold);
        }
        await Anim.Delay(700);
        va?.SetGlow(null);
        vb?.SetGlow(null);
    }

    private async Task OnNodesSwappedAsync(NodesSwappedEvent e)
    {
        _hud.AddLog($"  Switched {(e.A.Side == 0 ? "your" : "the AI's")} {e.A.Label} and {(e.B.Side == 0 ? "your" : "the AI's")} {e.B.Label}.");
        if (e.A.Side != e.B.Side) TractorBeam(BoardLayout.Center(e.A), BoardLayout.Center(e.B), 520); // Arganium
        var tasks = new List<Task>();
        foreach (var (card, node) in new[] { (e.CardNowOnA, e.A), (e.CardNowOnB, e.B) })
        {
            if (card is null || !_boardCards.TryGetValue(card.InstanceId, out var v)) continue;
            var c = BoardLayout.Center(node);
            tasks.Add(Anim.To(v, Canvas.LeftProperty, c.X - v.Width / 2, 520, Anim.InOut));
            tasks.Add(Anim.To(v, Canvas.TopProperty, c.Y - v.Height / 2, 520, Anim.InOut));
        }
        await Task.WhenAll(tasks);
        RefreshScores();
    }

    /// <summary>Immune / "cannot be Nullified": a cyan shield pulse on the Unit and a log line.</summary>
    private async Task OnNullifyPreventedAsync(NullifyPreventedEvent e)
    {
        _hud.AddLog($"  {e.Card.Name} is not Nullified – {e.Reason}.");
        if (!_boardCards.TryGetValue(e.Card.InstanceId, out var v) || e.Card.Location.Node is not { } node) return;
        v.SetGlow(Cyan);
        ShieldBubble(BoardLayout.Center(node));
        _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 1, 300, Anim.Back, from: 1.12);
        await Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 1, 300, Anim.Back, from: 1.12);
        v.SetGlow(null);
    }

    private void OnAbilitiesSuppressed(AbilitiesSuppressedEvent e)
    {
        _hud.AddLog($"  {e.Card.Name}'s abilities are Suppressed{(e.Source is null ? "" : $" ({e.Source.Name})")}.");
        if (_flying is not null && _flying.Card == e.Card) _flying.SetGlow(Colors.Gray);
    }

    // =================================================================== M3.3

    /// <summary>Mr Morph / Forced Mitosis: the card flips and comes back as the copied card.</summary>
    private async Task OnCardCopiedAsync(CardCopiedEvent e)
    {
        if (!_boardCards.TryGetValue(e.Card.InstanceId, out var v) || e.Card.Location.Node is not { } node)
        {
            v?.RefreshFace();
            return;
        }
        v.SetGlow(Purple, pulse: true);
        await LiquidMetalAsync(v); // liquid chrome re-forms it as the copy
        Shockwave(BoardLayout.Center(node), Purple);
        _ = FloatTextAtAsync($"COPY: {e.CopiedFrom.Name.ToUpperInvariant()}", ProjectToFx(BoardLayout.Center(node)), Purple);
        await Anim.Delay(420);
        v.SetGlow(null);
    }

    /// <summary>Recolonize: the hovering Unit (or the Standby Unit sliding forward) heads for its new node.</summary>
    private async Task OnDeployRedirectedAsync(DeployRedirectedEvent e)
    {
        string where = e.To.Side == e.Item.Controller ? e.To.Label : $"{(e.To.Side == 0 ? "your" : "the AI's")} {e.To.Label}";
        _hud.AddLog($"  {e.Source?.Name ?? "Redirect"}: {e.Item.Card.Name} is Deployed to {where} instead.");
        RebuildStackEntry(e.Item);

        if (_flying is not null && _flying.Card == e.Item.Card)
        {
            var f = _flying;
            var target = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.To)));
            var here = Mid(FxBounds(f));
            ThrusterBurst(here, here - Mid(target)); // the burst fires away from where it's going
            double tx = target.X + target.Width / 2 - f.Width / 2;
            double ty = target.Y + target.Height / 2 - f.Height / 2;
            double sx = target.Width / f.Width;
            double sy = target.Height / f.Height;
            await Task.WhenAll(
                Anim.To(f.Translate, TranslateTransform.XProperty, tx, 380, Anim.InOut),
                Anim.To(f.Translate, TranslateTransform.YProperty, ty - 40, 380, Anim.InOut),
                Anim.To(f.Scale, ScaleTransform.ScaleXProperty, sx * 1.25, 380, Anim.InOut),
                Anim.To(f.Scale, ScaleTransform.ScaleYProperty, sy * 1.25, 380, Anim.InOut));
            _flyerTarget = (tx, ty, sx, sy);
        }
        else if (_boardCards.TryGetValue(e.Item.Card.InstanceId, out var v))
        {
            var c = BoardLayout.Center(e.To);
            _ = Anim.To(v, Canvas.LeftProperty, c.X - v.Width / 2, 380, Anim.InOut);
            await Anim.To(v, Canvas.TopProperty, c.Y - v.Height / 2, 380, Anim.InOut);
        }
        Shockwave(BoardLayout.Center(e.To), Cyan);
    }

    /// <summary>Commandeer: the Order changes hands on the stack (or a commandeered React gets a new target).</summary>
    private async Task OnStackItemControlChangedAsync(StackItemControlChangedEvent e)
    {
        if (e.OldController != e.NewController)
        {
            await ClawAsync(e.Item, e.NewController);
            _hud.AddLog($"  {Who(e.NewController)} take{(e.NewController == 0 ? "" : "s")} control of {e.Item.Card.Name}{(e.Source is null ? "" : $" ({e.Source.Name})")}!");
            await ShowBannerAsync("COMMANDEERED", $"{e.Item.Card.Name.ToUpperInvariant()}  ·  NOW {(e.NewController == 0 ? "YOURS" : "THE AI'S")}", Purple, 900);
        }
        RebuildStackEntry(e.Item);
    }

    /// <summary>One of Us: the Unit crosses the control line to its new controller's node.</summary>
    private async Task OnUnitControlGainedAsync(UnitControlGainedEvent e)
    {
        string to = e.To.Side == 0 ? "your" : "the AI's";
        _hud.AddLog($"  {Who(e.NewController)} take{(e.NewController == 0 ? "" : "s")} control of {e.Card.Name}: it moves to {to} {e.To.Label}.");
        if (_boardCards.TryGetValue(e.Card.InstanceId, out var v))
        {
            v.SetGlow(Purple, pulse: true);
            var c = BoardLayout.Center(e.To);
            TractorBeam(c, BoardLayout.Center(e.From), 560);
            _ = Anim.To(v, Canvas.LeftProperty, c.X - v.Width / 2, 560, Anim.InOut);
            await Anim.To(v, Canvas.TopProperty, c.Y - v.Height / 2, 560, Anim.InOut);
            Shockwave(c, Purple);
            _ = FloatTextAtAsync("CONTROL!", ProjectToFx(c), Purple);
            await Anim.Delay(300);
            v.SetGlow(null);
        }
        RefreshScores();
    }

    // =================================================================== effects pass

    /// <summary>"That Unit becomes a Necrolite" (Already Dead, Abominations): the Unit rots before your eyes.</summary>
    private async Task OnRacesChangedAsync(RacesChangedEvent e)
    {
        if (!e.Races.Contains(Race.Necrolite)) return;
        if (_boardCards.TryGetValue(e.Card.InstanceId, out var v) && e.Card.Location.Node is not null)
            await DecayAsync(v, "NECROLITE");
    }
}
