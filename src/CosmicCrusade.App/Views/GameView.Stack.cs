using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Milestone 2 visuals: the priority stack panel, Orders flying hand → stack → Order Node / Issued pile,
/// counter-Reacts Nullifying cards on the stack, and Assemble.
///
/// A deploying Unit keeps its existing visual (the card hovering over its target node); its stack entry is a reminder.
/// Orders and Assembles live in their stack entry while they wait.
/// </summary>
public partial class GameView
{
    private const double StackMiniW = 62;

    private static readonly Color Red = Color.FromRgb(248, 113, 113);
    private static readonly Color Green = Color.FromRgb(74, 222, 128);

    private sealed class StackEntry
    {
        public required StackItem Item { get; init; }
        public required Border Root { get; init; }
        public required CardView Mini { get; init; }
        public required TextBlock Caption { get; init; }
    }

    private readonly Dictionary<int, StackEntry> _stackEntries = new();

    // =================================================================== stack panel

    private async Task OnStackPushedAsync(StackPushedEvent e)
    {
        var item = e.Item;
        var entry = CreateStackEntry(item);
        _stackEntries[item.Id] = entry;
        StackList.Children.Insert(0, entry.Root);
        StackHost.Visibility = Visibility.Visible;
        RefreshStackHighlight();
        StackHost.UpdateLayout();

        if (item.Kind is StackItemKind.DeployUnit or StackItemKind.UnitNullifying)
        {
            Fixed(entry.Root, OpacityProperty, 0, 1, 160);
            return; // The unit itself is already hovering over its node.
        }

        // Orders and Assembles fly from the hand into their entry.
        entry.Mini.Opacity = 0;
        var (start, faceDown) = TakeFromHand(item.Controller, item.Card);
        var end = FxBounds(entry.Mini);
        await FlyAsync(item.Card, start, end, faceDown, flipToFaceUp: faceDown, ms: 420,
            glow: CardLayout.Instance.Accent(item.Card.Definition));
        entry.Mini.Opacity = 1;
    }

    private Task OnStackPoppedAsync(StackPoppedEvent e)
    {
        if (_stackEntries.Remove(e.Item.Id, out var entry))
        {
            StackList.Children.Remove(entry.Root);
        }
        if (StackList.Children.Count == 0) StackHost.Visibility = Visibility.Collapsed;
        RefreshStackHighlight();
        return Task.CompletedTask;
    }

    private StackEntry CreateStackEntry(StackItem item)
    {
        var mini = new CardView(item.Card, faceDown: false, StackMiniW) { Cursor = System.Windows.Input.Cursors.Help };
        mini.MouseEnter += (_, _) => ShowZoom(item.Card);
        mini.MouseRightButtonDown += Card_MouseRightButtonDown;

        string who = item.Controller == 0 ? "You" : "AI";
        string what = item.Kind switch
        {
            StackItemKind.DeployUnit => $"deploy{(item.Controller == 0 ? "" : "s")} {item.Card.Name} → {NodeText(item.TargetNode!, item.Controller)}",
            StackItemKind.Assemble => $"assemble{(item.Controller == 0 ? "" : "s")} {item.Card.Name} onto {item.AssembleTarget!.Name}",
            StackItemKind.UnitNullifying => $"Nullify {item.Card.Name}{(item.NullifySource is { } src ? $" ({src.Name})" : "")}",
            _ when item.IsReact => $"React: {item.Card.Name} → {item.RespondingTo?.Card.Name}",
            _ => item.OrderNode is null
                ? $"issue{(item.Controller == 0 ? "" : "s")} {item.Card.Name}"
                : $"issue{(item.Controller == 0 ? "" : "s")} {item.Card.Name} → {item.OrderNode.Label}",
        };
        var caption = new TextBlock
        {
            Text = $"{who} {what}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(item.Controller == 0 ? Color.FromRgb(191, 219, 254) : Color.FromRgb(254, 202, 202)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(mini);
        Grid.SetColumn(caption, 1);
        grid.Children.Add(caption);

        var side = item.Controller == 0 ? BoardSurface.PlayerColor : BoardSurface.OpponentColor;
        var root = new Border
        {
            Child = grid,
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(70, side.R, side.G, side.B)),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, side.R, side.G, side.B)),
        };
        return new StackEntry { Item = item, Root = root, Mini = mini, Caption = caption };
    }

    /// <summary>The top entry glows; the rest are dimmed a little.</summary>
    private void RefreshStackHighlight()
    {
        for (int i = 0; i < StackList.Children.Count; i++)
        {
            if (StackList.Children[i] is not Border b) continue;
            bool top = i == 0;
            b.Opacity = top ? 1 : 0.7;
            b.Effect = top ? new DropShadowEffect { Color = Purple, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 } : null;
        }
    }

    private static string NodeText(Node node, int actor) =>
        node.Side == actor ? node.Label : $"{(node.Side == 0 ? "your" : "the AI's")} {node.Label}";

    // =================================================================== orders

    private void OnOrderAnnounced(OrderIssueAnnouncedEvent e)
    {
        string who = e.Player == 0 ? "You" : "AI";
        if (e.Item.IsReact)
            _hud.AddLog($"{who} React{(e.Player == 0 ? "" : "s")} with {e.Card.Name} → {e.Item.RespondingTo?.Card.Name}!");
        else
            _hud.AddLog($"{who} issue{(e.Player == 0 ? "" : "s")} {e.Card.Name}{(e.OrderNode is null ? "" : $" → {e.OrderNode.Label}")}.");
    }

    private async Task OnOrderResolvedAsync(OrderResolvedEvent e)
    {
        var start = StackMiniRect(e.Card, out var entry);
        if (entry is not null) entry.Mini.Opacity = 0;

        if (e.OrderNode is not null)
        {
            var end = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.OrderNode)));
            await FlyAsync(e.Card, start, end, false, false, 380, CardLayout.Instance.Accent(e.Card.Definition));
            AddBoardCard(e.Card, e.OrderNode);
            Shockwave(BoardLayout.Center(e.OrderNode), CardLayout.Instance.Accent(e.Card.Definition));
            _hud.AddLog($"  {e.Card.Name} is on {e.OrderNode.Label} – {e.Note}.");
        }
        else
        {
            await FlyAsync(e.Card, start, PileCardRect(PileKind.IssuedOrders), false, false, 380, Purple);
            UpdatePiles();
            _piles[PileKind.IssuedOrders].Pulse();
            _hud.AddLog($"  {e.Card.Name} resolves{(e.EffectApplied || e.Note.Contains("Suppressed") ? "" : " (no effect yet)")}: {e.Note}.");
        }
        RefreshScores(); // Live statics (Cybernetic Enhancements) may change what's legal; ranks are unaffected for now.
    }

    private async Task OnCardNullifiedOnStackAsync(CardNullifiedOnStackEvent e)
    {
        _hud.AddLog($"  {e.By.Name} Nullifies {e.Card.Name} – it is not played!");

        Rect start;
        if (_flying is not null && _flying.Card == e.Card)
        {
            // The deploying unit hovering over its node.
            var f = _flying;
            _flying = null;
            start = FxBounds(f);
            FxCanvas.Children.Remove(f);
        }
        else if (_boardCards.TryGetValue(e.Card.InstanceId, out var boardView))
        {
            // A Standby advance: the card had slid onto its target node.
            _boardCards.Remove(e.Card.InstanceId);
            BoardCanvas.Children.Remove(boardView);
            start = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.Item.TargetNode ?? e.Item.FromNode!)));
        }
        else
        {
            start = StackMiniRect(e.Card, out var entry);
            if (entry is not null) entry.Mini.Opacity = 0;
        }

        if (_stackEntries.TryGetValue(e.Item.Id, out var victimEntry))
        {
            victimEntry.Caption.Text = "NULLIFIED – " + victimEntry.Caption.Text;
            victimEntry.Caption.Foreground = new SolidColorBrush(Red);
            victimEntry.Caption.TextDecorations = TextDecorations.Strikethrough;
            victimEntry.Mini.Opacity = 0;
        }

        _ = FloatTextAtAsync("NULLIFIED!", new Point(start.X + start.Width / 2, start.Y + start.Height / 2), Red);
        await FlyAsync(e.Card, start, PileCardRect(e.To), false, false, 480, Red);
        UpdatePiles();
        _piles[e.To].Pulse();
    }

    // =================================================================== assemble

    private void OnAssembleAnnounced(AssembleAnnouncedEvent e) =>
        _hud.AddLog($"{(e.Player == 0 ? "You" : "AI")} assemble{(e.Player == 0 ? "" : "s")} {e.Card.Name} onto {e.Target.Name}.");

    private async Task OnAssembledAsync(AssembledEvent e)
    {
        StackEntry? entry = null;
        Rect start;
        if (e.FromPile is { } fromPile)
        {
            UpdatePiles(); // Salvage for Parts: the card leaves its pile
            start = PileCardRect(fromPile);
        }
        else
        {
            start = StackMiniRect(e.Card, out entry);
        }
        if (entry is not null) entry.Mini.Opacity = 0;

        if (e.Applied && _boardCards.TryGetValue(e.Target.InstanceId, out var targetView) && e.Target.Location.Node is { } hostNode)
        {
            var end = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(hostNode))); // board cards live in the 3D view
            // Shrink into the host unit.
            var small = new Rect(end.X + end.Width * 0.3, end.Y + end.Height * 0.3, end.Width * 0.4, end.Height * 0.4);
            await FlyAsync(e.Card, start, small, false, false, 420, Green, fadeOut: true);
            Shockwave(BoardLayout.Center(hostNode), Green);
            targetView.SetGlow(Green);
            _ = Task.Delay(900).ContinueWith(_ => targetView.SetGlow(null), TaskScheduler.FromCurrentSynchronizationContext());
            RefreshScores();
            _hud.AddLog($"  {e.Target.Name} gains +{e.Amount} Rank (now {RankCalculator.GetRank(State, e.Target)}). Score {_hud.MyTotal}–{_hud.AiTotal}.");
        }
        else
        {
            await FlyAsync(e.Card, start, PileCardRect(PileKind.RetiredUnits), false, false, 380, Gold);
            _hud.AddLog($"  {e.Target.Name} has left play – the Assemble does nothing.");
        }
        UpdatePiles();
        _piles[PileKind.RetiredUnits].Pulse();
    }

    // =================================================================== helpers

    /// <summary>
    /// Removes the card's view from a hand (human: the real card; AI: a card back) and returns where it was on screen.
    /// </summary>
    private (Rect Start, bool FaceDown) TakeFromHand(int player, CardInstance card)
    {
        if (player == 0)
        {
            var handView = _handViews.FirstOrDefault(v => v.Card == card);
            if (handView is null)
                return (new Rect(FxCanvas.ActualWidth / 2, FxCanvas.ActualHeight, HandCardW, HandCardW / CardFaceFactory.Aspect), false);

            var rect = FxBounds(handView);
            HideMagnifierFor(handView);
            _handViews.Remove(handView);
            HandCanvas.Children.Remove(handView);
            if (_selected == handView) _selected = null;
            LayoutHand();
            return (rect, false);
        }

        // A revealed hand shows its faces: take that exact card (face up). Otherwise any card back.
        var back = _oppHandViews.FirstOrDefault(v => v.Card == card) ?? _oppHandViews.LastOrDefault(v => v.Card is null)
                   ?? _oppHandViews.LastOrDefault();
        if (back is null)
            return (new Rect(FxCanvas.ActualWidth / 2, -100, OppHandCardW, OppHandCardW / CardFaceFactory.Aspect), true);

        var r = FxBounds(back);
        _oppHandViews.Remove(back);
        OpponentHandCanvas.Children.Remove(back);
        LayoutOpponentHand();
        return (r, back.FaceDown);
    }

    /// <summary>Screen rect of the card's stack mini-card (or the middle of the stack panel if it has none).</summary>
    private Rect StackMiniRect(CardInstance card, out StackEntry? entry)
    {
        entry = _stackEntries.Values.FirstOrDefault(x => x.Item.Card == card);
        if (entry is not null) return FxBounds(entry.Mini);
        var host = new Rect(20, FxCanvas.ActualHeight / 2 - 40, 60, 80);
        if (StackHost.Visibility == Visibility.Visible)
            host = new Rect(StackHost.TranslatePoint(new Point(0, 0), FxCanvas), new Size(StackHost.ActualWidth, StackHost.ActualHeight));
        return new Rect(host.X + 8, host.Y + host.Height / 2 - 40, StackMiniW, StackMiniW / CardFaceFactory.Aspect);
    }

    /// <summary>Screen rect of the top card inside a pile view.</summary>
    private Rect PileCardRect(PileKind pile)
    {
        var c = BoardLayout.PileCenter(pile);
        return ProjectRect(BoardLayout.CardRectAt(new Point(c.X, c.Y - BoardLayout.SlotH / 2 + 9 + BoardLayout.CardH / 2)));
    }

    /// <summary>Flies a temporary copy of a card between two screen rects in the FX overlay.</summary>
    private async Task FlyAsync(CardInstance card, Rect from, Rect to, bool startFaceDown, bool flipToFaceUp, double ms,
        Color? glow = null, bool fadeOut = false)
    {
        var flyer = new CardView(card, startFaceDown, HandCardW);
        if (glow is not null) flyer.SetGlow(glow);
        flyer.Translate.X = from.X + from.Width / 2 - flyer.Width / 2;
        flyer.Translate.Y = from.Y + from.Height / 2 - flyer.Height / 2;
        flyer.Scale.ScaleX = from.Width / flyer.Width;
        flyer.Scale.ScaleY = from.Height / flyer.Height;
        FxCanvas.Children.Add(flyer);

        var tasks = new List<Task>
        {
            Anim.To(flyer.Translate, TranslateTransform.XProperty, to.X + to.Width / 2 - flyer.Width / 2, ms, Anim.InOut),
            Anim.To(flyer.Translate, TranslateTransform.YProperty, to.Y + to.Height / 2 - flyer.Height / 2, ms, Anim.InOut),
            Anim.To(flyer.Scale, ScaleTransform.ScaleXProperty, to.Width / flyer.Width, ms, Anim.InOut),
            Anim.To(flyer.Scale, ScaleTransform.ScaleYProperty, to.Height / flyer.Height, ms, Anim.InOut),
        };
        if (flipToFaceUp) tasks.Add(FlipFaceUpAsync(flyer, ms));
        if (fadeOut) tasks.Add(Anim.To(flyer, OpacityProperty, 0, ms, Anim.In, from: 1));
        await Task.WhenAll(tasks);
        FxCanvas.Children.Remove(flyer);
    }

    private async Task FloatTextAtAsync(string text, Point fxPoint, Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 46,
            Foreground = new SolidColorBrush(color),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 0 },
            IsHitTestVisible = false,
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var t = new TranslateTransform(fxPoint.X - tb.DesiredSize.Width / 2, fxPoint.Y - tb.DesiredSize.Height / 2);
        tb.RenderTransform = t;
        FxCanvas.Children.Add(tb);
        _ = Anim.To(t, TranslateTransform.YProperty, t.Y - 60, 900, Anim.Out);
        await Anim.To(tb, OpacityProperty, 0, 900, Anim.In, from: 1);
        FxCanvas.Children.Remove(tb);
    }
}
