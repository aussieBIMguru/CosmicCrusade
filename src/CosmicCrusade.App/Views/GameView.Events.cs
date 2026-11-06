using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Engine events → visuals. The engine awaits each PublishAsync, so the animation for one event finishes
/// (or reaches a sensible hand-off point) before the next game step happens.
/// </summary>
public partial class GameView
{
    private CardView? _flying;

    private static readonly Color Cyan = Color.FromRgb(79, 209, 255);
    private static readonly Color Gold = Color.FromRgb(255, 201, 74);
    private static readonly Color Purple = Color.FromRgb(192, 132, 252);

    public async Task PublishAsync(GameEvent gameEvent, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _state is null) return;

        switch (gameEvent)
        {
            case GameStartedEvent:
                _hud.AddLog($"Game started – first to {State.Options.RoundsToWin} rounds.");
                break;

            case RoundStartedEvent e:
                _hud.RoundText = $"ROUND {e.Round}";
                _hud.AddLog($"— Round {e.Round} — {(e.FirstPlayer == 0 ? "you go" : "the AI goes")} first.");
                await ShowBannerAsync($"ROUND {e.Round}", e.FirstPlayer == 0 ? "You go first" : "AI goes first", Cyan, 900);
                break;

            case CardDrawnEvent e:
                await OnCardDrawnAsync(e);
                break;

            case DeckReshuffledEvent e:
                _hud.AddLog($"{BoardLayout.PileLabel(e.Deck)} reshuffled ({e.CardCount} cards).");
                UpdatePiles();
                _piles[e.Deck].Pulse();
                break;

            case TurnStartedEvent e:
                if (e.Player == 1)
                {
                    _hud.Status = "AI is thinking…";
                    _hud.Hint = "";
                }
                break;

            case PassedEvent e:
                _hud.AddLog(e.Player == 0 ? "You passed." : "AI passed.");
                if (e.Player == 1) _hud.Status = "AI passed";
                await FloatTextAsync(e.Player == 0 ? "PASS" : "AI PASSES", e.Player);
                break;

            case UnitDeployAnnouncedEvent e:
                await OnDeployAnnouncedAsync(e);
                break;

            case UnitPlayedEvent e:
                await OnUnitPlayedAsync(e);
                break;

            case DeployCancelledEvent e:
                if (_flying is not null)
                {
                    var f = _flying;
                    _flying = null;
                    await Anim.To(f, OpacityProperty, 0, 300);
                    FxCanvas.Children.Remove(f);
                }
                UpdatePiles();
                _hud.AddLog($"{e.Card.Name} was cancelled.");
                break;

            case StackPushedEvent e:
                await OnStackPushedAsync(e);
                break;

            case StackPoppedEvent e:
                await OnStackPoppedAsync(e);
                break;

            case PriorityEvent e:
                if (e.Player == 1) _hud.Status = "AI may respond…";
                break;

            case OrderIssueAnnouncedEvent e:
                OnOrderAnnounced(e);
                break;

            case OrderResolvedEvent e:
                await OnOrderResolvedAsync(e);
                break;

            case CardNullifiedOnStackEvent e:
                await OnCardNullifiedOnStackAsync(e);
                break;

            case AssembleAnnouncedEvent e:
                OnAssembleAnnounced(e);
                break;

            case AssembledEvent e:
                await OnAssembledAsync(e);
                break;

            case AbilityTriggeredEvent e:
                await OnAbilityTriggeredAsync(e);
                break;

            case ChoiceMadeEvent e:
                OnChoiceMade(e);
                break;

            case CardNullifiedEvent e:
                await OnCardNullifiedAsync(e);
                break;

            case NodeLockChangedEvent e:
                await OnNodeLockChangedAsync(e);
                break;

            case RankChangedEvent e:
                await OnRankChangedAsync(e);
                break;

            case CardMovedToStandbyEvent e:
                await OnCardMovedToStandbyAsync(e);
                break;

            case CardReturnedToHandEvent e:
                await OnCardReturnedToHandAsync(e);
                break;

            case CardRetiredFromHandEvent e:
                await OnCardRetiredFromHandAsync(e);
                break;

            case DuelEvent e:
                await OnDuelAsync(e);
                break;

            case NodesSwappedEvent e:
                await OnNodesSwappedAsync(e);
                break;

            case AbilitiesSuppressedEvent e:
                OnAbilitiesSuppressed(e);
                break;

            case NullifyPreventedEvent e:
                await OnNullifyPreventedAsync(e);
                break;

            case RoundScoredEvent e:
                await OnRoundScoredAsync(e.Result);
                break;

            case CardMovedToPileEvent e:
                await OnCardMovedToPileAsync(e);
                break;

            case WarzoneRevealedEvent e:
                await FlyBetweenPilesAsync(e.Warzone, PileKind.WarzoneDeck, PileKind.ActiveWarzone, startFaceDown: true, endFaceDown: false);
                UpdatePiles();
                _hud.AddLog($"Warzone: {e.Warzone.Name}. {e.Warzone.Definition.Text}");
                await ShowBannerAsync(e.Warzone.Name.ToUpperInvariant(), "WARZONE  ·  " + e.Warzone.Definition.Text, Purple, 1400);
                break;

            case WarzonesAddedEvent e:
            {
                // Dimensional Rift / Wormhole: every new Warzone flies in together, then one banner names them all.
                await Task.WhenAll(e.Warzones.Select(w =>
                    FlyBetweenPilesAsync(w, PileKind.WarzoneDeck, PileKind.ActiveWarzone, startFaceDown: true, endFaceDown: false)));
                UpdatePiles();
                foreach (var w in e.Warzones) _hud.AddLog($"Warzone: {w.Name}. {w.Definition.Text}");
                string names = string.Join(" + ", e.Warzones.Select(w => w.Name.ToUpperInvariant()));
                string sub = e.Warzones.Count == 1
                    ? "WARZONE  ·  " + e.Warzones[0].Definition.Text
                    : $"{State.ActiveWarzones.Count} WARZONES IN PLAY  ·  right-click the Warzone slot to read each";
                await ShowBannerAsync(names, sub, Purple, 1600);
                break;
            }

            case WarzoneReturnedEvent e:
                await FlyBetweenPilesAsync(e.Warzone, PileKind.ActiveWarzone, PileKind.WarzoneDeck, startFaceDown: false, endFaceDown: true);
                UpdatePiles();
                break;

            case RoundEndedEvent:
                await Anim.Delay(350);
                UpdatePiles();
                RefreshScores();
                break;

            case GameEndedEvent e:
                ShowGameOver(e.Outcome);
                break;

            case LogEvent e:
                _hud.AddLog(e.Message);
                break;

            case CardNullifiedFromHandEvent e:
                await OnCardNullifiedFromHandAsync(e);
                break;
        }

        // The All-knowing / Transparency / Oracle's Arch: show the AI's hand face up while it is revealed.
        SyncRevealedHands();

        // Static effects (anthems, Acid Rain, Indominus...) change Ranks of Units that weren't involved in the
        // event, so refresh every badge and the totals after anything that can switch a static on or off.
        if (gameEvent is OrderResolvedEvent or CardNullifiedEvent or CardMovedToPileEvent or CardMovedToStandbyEvent
            or CardReturnedToHandEvent or CardRetiredFromHandEvent or CardDrawnEvent or WarzoneRevealedEvent or WarzonesAddedEvent
            or WarzoneReturnedEvent or AbilitiesSuppressedEvent or ChoiceMadeEvent or NullifyPreventedEvent
            or NodesSwappedEvent or RankChangedEvent or CardNullifiedFromHandEvent)
            RefreshScores();
    }

    // =================================================================== draws

    private async Task OnCardDrawnAsync(CardDrawnEvent e)
    {
        UpdatePiles();
        var deckCenter = ProjectToFx(BoardLayout.PileCenter(e.FromDeck));

        if (e.Player == 0)
        {
            var v = CreateHandCard(e.Card);
            _handViews.Add(v);
            HandCanvas.Children.Add(v);
            var start = FxCanvas.TranslatePoint(deckCenter, HandCanvas);
            v.Translate.X = start.X - v.Width / 2;
            v.Translate.Y = start.Y - v.Height / 2;
            v.Scale.ScaleX = v.Scale.ScaleY = 0.45;
            LayoutHand();
        }
        else
        {
            var v = CreateOpponentHandCard(StaticLayer.IsHandRevealed(State, 1) ? e.Card : null);
            _oppHandViews.Add(v);
            OpponentHandCanvas.Children.Add(v);
            var start = FxCanvas.TranslatePoint(deckCenter, OpponentHandCanvas);
            v.Translate.X = start.X - v.Width / 2;
            v.Translate.Y = start.Y - v.Height / 2;
            LayoutOpponentHand();
        }
        await Anim.Delay(90);
    }

    // =================================================================== deploys

    private async Task OnDeployAnnouncedAsync(UnitDeployAnnouncedEvent e)
    {
        var targetRect = ProjectRect(BoardLayout.CardRectAt(BoardLayout.Center(e.Target)));
        string side = e.Target.Side == e.Player ? "" : (e.Target.Side == 0 ? "your " : "the AI's ");
        _hud.AddLog($"{(e.Player == 0 ? "You" : "AI")} deploy{(e.Player == 0 ? "" : "s")} {e.Card.Name} → {side}{e.Target.Label}");

        if (e.FromNode is not null)
        {
            // Standby advance: the card slides forward on the board.
            if (_boardCards.TryGetValue(e.Card.InstanceId, out var bv))
            {
                var to = BoardLayout.Center(e.Target);
                _ = Anim.To(bv, Canvas.LeftProperty, to.X - bv.Width / 2, 380, Anim.InOut);
                await Anim.To(bv, Canvas.TopProperty, to.Y - bv.Height / 2, 380, Anim.InOut);
            }
            return;
        }

        // From a pile (Barkeep Bill & co.): face up from the pile. Otherwise from the hand.
        var (startRect, fromBack) = e.FromPile is { } pile
            ? (ProjectRect(BoardLayout.CardRectAt(BoardLayout.PileCenter(pile))), false)
            : TakeFromHand(e.Player, e.Card);
        if (e.FromPile is not null) UpdatePiles();
        var flyer = new CardView(e.Card, faceDown: fromBack, HandCardW);

        // Place the flyer so its centre sits on the start rect, scaled to match.
        flyer.Translate.X = startRect.X + startRect.Width / 2 - flyer.Width / 2;
        flyer.Translate.Y = startRect.Y + startRect.Height / 2 - flyer.Height / 2;
        flyer.Scale.ScaleX = flyer.Scale.ScaleY = startRect.Width / flyer.Width;
        flyer.SetGlow(CardLayout.Instance.Accent(e.Card.Definition));
        FxCanvas.Children.Add(flyer);
        _flying = flyer;

        double ms = 420;
        double tx = targetRect.X + targetRect.Width / 2 - flyer.Width / 2;
        double ty = targetRect.Y + targetRect.Height / 2 - flyer.Height / 2;
        double sx = targetRect.Width / flyer.Width;
        double sy = targetRect.Height / flyer.Height;

        // Arc: rise slightly above the target, then land on UnitPlayed.
        var tasks = new List<Task>
        {
            Anim.To(flyer.Translate, TranslateTransform.XProperty, tx, ms, Anim.Out),
            Anim.To(flyer.Translate, TranslateTransform.YProperty, ty - 40, ms, Anim.Out),
            Anim.To(flyer.Scale, ScaleTransform.ScaleXProperty, sx * 1.25, ms, Anim.Out),
            Anim.To(flyer.Scale, ScaleTransform.ScaleYProperty, sy * 1.25, ms, Anim.Out),
        };

        if (flyer.FaceDown)
            tasks.Add(FlipFaceUpAsync(flyer, ms));

        await Task.WhenAll(tasks);
        _flyerTarget = (tx, ty, sx, sy);
    }

    private (double X, double Y, double Sx, double Sy) _flyerTarget;

    /// <summary>Flies a card between two piles on the board (Warzone reveal/return), flipping it if needed.</summary>
    private async Task FlyBetweenPilesAsync(CardInstance card, PileKind from, PileKind to, bool startFaceDown, bool endFaceDown)
    {
        if (!_piles.ContainsKey(from) || !_piles.ContainsKey(to)) return;

        // The destination pile is updated after landing, so hide the card at its source now.
        _piles[from].Update(State.GetPile(from));

        Rect PileCard(PileKind p)
        {
            var c = BoardLayout.PileCenter(p);
            return ProjectRect(BoardLayout.CardRectAt(new Point(c.X, c.Y - BoardLayout.SlotH / 2 + 9 + BoardLayout.CardH / 2)));
        }

        var a = PileCard(from);
        var b = PileCard(to);
        var flyer = new CardView(card, startFaceDown, HandCardW);
        flyer.SetGlow(Purple);
        flyer.Translate.X = a.X + a.Width / 2 - flyer.Width / 2;
        flyer.Translate.Y = a.Y + a.Height / 2 - flyer.Height / 2;
        flyer.Scale.ScaleX = a.Width / flyer.Width;
        flyer.Scale.ScaleY = a.Height / flyer.Height;
        FxCanvas.Children.Add(flyer);

        const double ms = 520;
        var tasks = new List<Task>
        {
            Anim.To(flyer.Translate, TranslateTransform.XProperty, b.X + b.Width / 2 - flyer.Width / 2, ms, Anim.InOut),
            Anim.To(flyer.Translate, TranslateTransform.YProperty, b.Y + b.Height / 2 - flyer.Height / 2, ms, Anim.InOut),
            Anim.To(flyer.Scale, ScaleTransform.ScaleXProperty, b.Width / flyer.Width, ms, Anim.InOut),
            Anim.To(flyer.Scale, ScaleTransform.ScaleYProperty, b.Height / flyer.Height, ms, Anim.InOut),
        };
        if (startFaceDown != endFaceDown) tasks.Add(FlipAsync(flyer, ms, endFaceDown));
        await Task.WhenAll(tasks);

        FxCanvas.Children.Remove(flyer);
        _piles[to].Update(State.GetPile(to));
        _piles[to].Pulse();
        if (to == PileKind.ActiveWarzone)
            Shockwave(new Point(BoardLayout.PileCenter(to).X, BoardLayout.PileCenter(to).Y), Purple);
    }

    private static async Task FlipAsync(CardView v, double totalMs, bool toFaceDown)
    {
        await Anim.Delay(totalMs * 0.25);
        await Anim.To(v.Flip, ScaleTransform.ScaleXProperty, 0, totalMs * 0.25, Anim.In);
        v.FaceDown = toFaceDown;
        await Anim.To(v.Flip, ScaleTransform.ScaleXProperty, 1, totalMs * 0.25, Anim.Out);
    }

    private static async Task FlipFaceUpAsync(CardView v, double totalMs)
    {
        await Anim.Delay(totalMs * 0.25);
        await Anim.To(v.Flip, ScaleTransform.ScaleXProperty, 0, totalMs * 0.25, Anim.In);
        v.FaceDown = false;
        await Anim.To(v.Flip, ScaleTransform.ScaleXProperty, 1, totalMs * 0.25, Anim.Out);
    }

    private async Task OnUnitPlayedAsync(UnitPlayedEvent e)
    {
        // Drop the flyer onto the board, then swap it for the real board card.
        if (_flying is not null && _flying.Card == e.Card)
        {
            var f = _flying;
            _flying = null;
            var (tx, ty, sx, sy) = _flyerTarget;
            await Task.WhenAll(
                Anim.To(f.Translate, TranslateTransform.YProperty, ty, 140, Anim.In),
                Anim.To(f.Scale, ScaleTransform.ScaleXProperty, sx, 140, Anim.In),
                Anim.To(f.Scale, ScaleTransform.ScaleYProperty, sy, 140, Anim.In));
            FxCanvas.Children.Remove(f);
        }

        if (!_boardCards.TryGetValue(e.Card.InstanceId, out var view))
            view = AddBoardCard(e.Card, e.Node);
        else
            PlaceOnBoard(view, e.Node);

        var accent = CardLayout.Instance.Accent(e.Card.Definition);
        Shockwave(BoardLayout.Center(e.Node), accent);
        _ = Anim.To(view.Scale, ScaleTransform.ScaleXProperty, 1, 260, Anim.Back, from: 1.18);
        _ = Anim.To(view.Scale, ScaleTransform.ScaleYProperty, 1, 260, Anim.Back, from: 1.18);
        Shake();

        RefreshScores();
        int rank = RankCalculator.GetRank(State, e.Card);
        string where = e.Node.Side == e.Player ? e.Node.Label : $"{(e.Node.Side == 0 ? "your" : "the AI's")} {e.Node.Label}";
        _hud.AddLog($"  {e.Card.Name} lands on {where} (Rank {rank}). Score {_hud.MyTotal}–{_hud.AiTotal}.");
        await Anim.Delay(220);
    }

    private CardView AddBoardCard(CardInstance card, Node node)
    {
        var v = new CardView(card, faceDown: false, BoardLayout.CardW);
        v.MouseEnter += (_, _) => ShowZoom(card);
        v.MouseLeftButtonDown += BoardCard_MouseLeftButtonDown;
        v.MouseRightButtonDown += Card_MouseRightButtonDown;
        Panel.SetZIndex(v, 10);
        PlaceOnBoard(v, node);
        BoardCanvas.Children.Add(v);
        _boardCards[card.InstanceId] = v;
        return v;
    }

    private static void PlaceOnBoard(CardView v, Node node)
    {
        var c = BoardLayout.Center(node);
        v.BeginAnimation(Canvas.LeftProperty, null);
        v.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(v, c.X - v.Width / 2);
        Canvas.SetTop(v, c.Y - v.Height / 2);
    }

    private void RefreshScores()
    {
        _hud.MyTotal = RankCalculator.TotalRank(State, 0);
        _hud.AiTotal = RankCalculator.TotalRank(State, 1);
        foreach (var v in _boardCards.Values) v.ShowRankIfInPlay(State);
        foreach (var s in _slots.Values) s.Refresh();
    }

    // =================================================================== scoring and clean-up

    private async Task OnRoundScoredAsync(RoundResult r)
    {
        _hud.MyRounds = State.Players[0].RoundsWon;
        _hud.AiRounds = State.Players[1].RoundsWon;

        // Light up the cards on the winning side.
        if (r.Winner is int w)
        {
            foreach (var node in State.Players[w].ScoringNodes)
                if (node.Card is not null && _boardCards.TryGetValue(node.Card.InstanceId, out var v))
                    v.SetGlow(Gold, pulse: true);
        }

        string title = r.Winner switch
        {
            0 => "ROUND WON!",
            1 => "ROUND LOST",
            _ => "ROUND DRAWN",
        };
        string sub = $"{r.Total0} – {r.Total1}";
        if (r.DecidedByTiebreak)
            sub += r.Winner is null
                ? $"  ·  totals and opposed nodes tied ({r.PairsWon0}–{r.PairsWon1})"
                : $"  ·  tie broken on opposed nodes {r.PairsWon0}–{r.PairsWon1}";
        if (r.WonBy is { } by) sub += $"  ·  won by {by}";

        _hud.AddLog($"Round {r.Round}: {title.ToLowerInvariant()} {sub}.");
        _hud.Status = title;
        var color = r.Winner switch { 0 => BoardSurface.PlayerColor, 1 => BoardSurface.OpponentColor, _ => Colors.Gray };
        await ShowBannerAsync(title, sub, color, 1500);
    }

    private async Task OnCardMovedToPileAsync(CardMovedToPileEvent e)
    {
        if (!_boardCards.TryGetValue(e.Card.InstanceId, out var v))
        {
            UpdatePiles();
            return;
        }
        _boardCards.Remove(e.Card.InstanceId);
        v.SetGlow(null);
        v.HideRank();

        var to = BoardLayout.PileCenter(e.Pile);
        var pile = _piles[e.Pile];
        double ms = 480;
        _ = Anim.To(v, Canvas.LeftProperty, to.X - v.Width / 2, ms, Anim.InOut);
        _ = Anim.To(v, Canvas.TopProperty, to.Y - BoardLayout.SlotH / 2 + 9 /* top card inside the pile view */, ms, Anim.InOut);
        _ = Anim.To(v, OpacityProperty, e.Pile == PileKind.NullifiedUnits ? 0.2 : 0.6, ms, Anim.In)
            .ContinueWith(_ =>
            {
                BoardCanvas.Children.Remove(v);
                UpdatePiles();
                pile.Pulse();
            }, TaskScheduler.FromCurrentSynchronizationContext());

        await Anim.Delay(110);
    }

    // =================================================================== effects

    private void Shockwave(Point boardPoint, Color color)
    {
        const double size = 320;
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 12,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new BlurEffect { Radius = 6 },
        };
        var scale = new ScaleTransform(0.25, 0.25);
        ring.RenderTransform = scale;
        Canvas.SetLeft(ring, boardPoint.X - size / 2);
        Canvas.SetTop(ring, boardPoint.Y - size / 2);
        Panel.SetZIndex(ring, 5);
        BoardCanvas.Children.Add(ring);

        _ = Anim.To(scale, ScaleTransform.ScaleXProperty, 1.25, 560, Anim.Out);
        _ = Anim.To(scale, ScaleTransform.ScaleYProperty, 1.25, 560, Anim.Out);
        _ = Anim.To(ring, OpacityProperty, 0, 560, Anim.In, from: 1)
            .ContinueWith(_ => BoardCanvas.Children.Remove(ring), TaskScheduler.FromCurrentSynchronizationContext());

        // A burst of sparks.
        var rng = new Random(boardPoint.GetHashCode() ^ Environment.TickCount);
        for (int i = 0; i < 14; i++)
        {
            var spark = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Min(255, color.R + 90), (byte)Math.Min(255, color.G + 90), (byte)Math.Min(255, color.B + 90))),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0 },
            };
            var t = new TranslateTransform();
            spark.RenderTransform = t;
            Canvas.SetLeft(spark, boardPoint.X - 4.5);
            Canvas.SetTop(spark, boardPoint.Y - 4.5);
            Panel.SetZIndex(spark, 30);
            BoardCanvas.Children.Add(spark);

            double angle = rng.NextDouble() * Math.PI * 2;
            double dist = 90 + rng.NextDouble() * 110;
            double ms = 380 + rng.NextDouble() * 260;
            _ = Anim.To(t, TranslateTransform.XProperty, Math.Cos(angle) * dist, ms, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, Math.Sin(angle) * dist, ms, Anim.Out);
            _ = Anim.To(spark, OpacityProperty, 0, ms, Anim.In, from: 1)
                .ContinueWith(_ => BoardCanvas.Children.Remove(spark), TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// <summary>Tiny camera shake when a unit lands.</summary>
    private void Shake()
    {
        var t = Viewport.RenderTransform as TranslateTransform;
        if (t is null)
        {
            t = new TranslateTransform();
            Viewport.RenderTransform = t;
        }
        var a = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(220) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(0.2)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromPercent(0.45)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(2, KeyTime.FromPercent(0.7)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        t.BeginAnimation(TranslateTransform.YProperty, a);
    }

    private async Task FloatTextAsync(string text, int player)
    {
        var anchor = ProjectToFx(new Point(760, player == 0 ? 640 : 360));
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 54,
            Foreground = new SolidColorBrush(player == 0 ? Color.FromRgb(147, 197, 253) : Color.FromRgb(252, 165, 165)),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 0 },
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var t = new TranslateTransform(anchor.X - tb.DesiredSize.Width / 2, anchor.Y - tb.DesiredSize.Height / 2);
        tb.RenderTransform = t;
        FxCanvas.Children.Add(tb);

        _ = Anim.To(t, TranslateTransform.YProperty, t.Y - 50, 700, Anim.Out);
        await Anim.To(tb, OpacityProperty, 0, 700, Anim.In, from: 1);
        FxCanvas.Children.Remove(tb);
    }

    private async Task ShowBannerAsync(string title, string subtitle, Color color, double holdMs)
    {
        BannerTitle.Text = title;
        BannerSubtitle.Text = subtitle;
        BannerGlow.Color = color;
        Banner.BorderBrush = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B));

        _ = Anim.To(BannerScale, ScaleTransform.ScaleXProperty, 1, 380, Anim.Back, from: 0.6);
        _ = Anim.To(BannerScale, ScaleTransform.ScaleYProperty, 1, 380, Anim.Back, from: 0.6);
        await Anim.To(Banner, OpacityProperty, 1, 250, Anim.Out, from: 0);
        await Anim.Delay(holdMs);
        await Anim.To(Banner, OpacityProperty, 0, 300, Anim.In);
    }

    private void ShowGameOver(GameOutcome o)
    {
        _hud.CanPass = false;
        _hud.Status = "Game over";
        GameOverTitle.Text = o.Winner switch { 0 => "VICTORY", 1 => "DEFEAT", _ => "DRAW" };
        GameOverSubtitle.Text = $"Rounds {o.RoundsWon0} – {o.RoundsWon1}   ·   {o.Reason}";
        _hud.AddLog($"Game over: {GameOverTitle.Text} ({o.RoundsWon0}–{o.RoundsWon1}).");
        GameOverPanel.Opacity = 0;
        GameOverPanel.Visibility = Visibility.Visible;
        _ = Anim.To(GameOverPanel, OpacityProperty, 1, 600, Anim.Out);
    }
}
