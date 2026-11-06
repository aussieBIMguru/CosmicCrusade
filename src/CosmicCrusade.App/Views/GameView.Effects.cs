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
    }

    private async Task OnCardNullifiedAsync(CardNullifiedEvent e)
    {
        string by = e.Source is null || e.Source == e.Card ? "" : $" by {e.Source.Name}";
        _hud.AddLog($"  {e.Card.Name} is Nullified{by}.");

        if (e.FromNode is not null && _boardCards.ContainsKey(e.Card.InstanceId))
        {
            var center = BoardLayout.Center(e.FromNode);
            Shockwave(center, Red);
            _ = FloatTextAtAsync("NULLIFIED", ProjectToFx(center), Red);
            await OnCardMovedToPileAsync(new CardMovedToPileEvent(e.Card, e.FromNode, e.To));
            await Anim.Delay(300);
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
            _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 1, 300, Anim.Back, from: 1.15);
            _ = Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 1, 300, Anim.Back, from: 1.15);
            _ = FloatTextAtAsync(e.Description.Split(' ')[0] == "Base" ? $"RANK {rank}" : e.Description.Split(' ')[0],
                ProjectToFx(BoardLayout.Center(node)), color);
            await Anim.Delay(420);
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
            await FlyAsync(e.Card, PileCardRect(pile), to, false, false, 460, Purple);
            AddBoardCard(e.Card, e.To);
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

    private async Task OnDuelAsync(DuelEvent e)
    {
        string result = e.Losers.Count == 2 ? "matched Ranks: both are Nullified"
            : e.Loser is not null ? $"{e.Loser.Name} loses{(e.Protected ? " (the other is protected)" : "")}"
            : e.Protected ? "the loser is protected" : "nothing happens";
        _hud.AddLog($"  DUEL: {e.A.Name} ({RankCalculator.GetRank(State, e.A)}) vs {e.B.Name} ({RankCalculator.GetRank(State, e.B)}) – {result}.");
        CardView? va = _boardCards.GetValueOrDefault(e.A.InstanceId);
        CardView? vb = _boardCards.GetValueOrDefault(e.B.InstanceId);
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
        Shockwave(BoardLayout.Center(node), Cyan);
        _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 1, 300, Anim.Back, from: 1.12);
        await Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 1, 300, Anim.Back, from: 1.12);
        v.SetGlow(null);
    }

    private void OnAbilitiesSuppressed(AbilitiesSuppressedEvent e)
    {
        _hud.AddLog($"  {e.Card.Name}'s abilities are Suppressed{(e.Source is null ? "" : $" ({e.Source.Name})")}.");
        if (_flying is not null && _flying.Card == e.Card) _flying.SetGlow(Colors.Gray);
    }
}
