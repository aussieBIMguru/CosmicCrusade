using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.App.ViewModels;
using CosmicCrusade.Core.Ai;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Views;

/// <summary>
/// The table. The board is a 2D canvas hosted on a tilted 3D plane (Viewport2DVisual3D), so it stays clickable
/// while looking 3D. Hands, flying cards and banners live in flat 2D overlays on top.
/// This file: setup, camera, hands, input. GameView.Events.cs: engine events and animations.
/// </summary>
public partial class GameView : UserControl, IGameEventSink
{
    private const double HandCardW = 150;
    private const double OppHandCardW = 70;

    private readonly GameOptions _options;
    private readonly GameHudViewModel _hud;
    private readonly HumanController _human = new();
    private readonly CancellationTokenSource _cts = new();

    private GameState? _state;

    private readonly Dictionary<Node, NodeSlot> _slots = new();
    private readonly Dictionary<PileKind, PileView> _piles = new();
    private int _warzoneInspectIndex = -1;
    private readonly Dictionary<int, CardView> _boardCards = new();
    private readonly List<CardView> _handViews = new();
    private readonly List<CardView> _oppHandViews = new();

    private CardView? _selected;
    private CardView? _zoomed;

    public GameView(GameOptions options)
    {
        InitializeComponent();
        _options = options;
        _hud = new GameHudViewModel { PlayerName = "You", AiName = "AI", RoundsToWin = options.RoundsToWin };
        DataContext = _hud;

        _human.DecisionRequested += OnHumanDecisionRequested;
        _human.DecisionCompleted += OnHumanDecisionCompleted;
        _human.ChoiceRequested += OnHumanChoiceRequested;
        _human.ChoiceCompleted += OnHumanChoiceCompleted;

        Loaded += OnLoaded;
        Unloaded += (_, _) => _cts.Cancel();
        HandCanvas.SizeChanged += (_, _) => LayoutHand(animate: false);
        Viewport.SizeChanged += (_, _) => UpdateCamera();
        OpponentHandCanvas.SizeChanged += (_, _) => LayoutOpponentHand(animate: false);
        PreviewKeyDown += OnKeyDown;
        ZoomHost.MouseRightButtonDown += ZoomHost_MouseRightButtonDown;
        SizeChanged += (_, _) => { HideMagnifier(); CloseInspect(); };
    }

    /// <summary>Raised by the Menu button.</summary>
    public event Action? ExitRequested;

    /// <summary>Raised by Play Again (same options, new seed).</summary>
    public event Action? RestartRequested;

    private GameState State => _state ?? throw new InvalidOperationException("Game not started.");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Focus();
        UpdateCamera();
        Anim.SpeedFactor = SpeedSlider.Value;

        _state = GameEngine.CreateGame(AppPaths.Database, _options, "You", "AI");
        BuildBoard();

        var ai = new PacedController(new HeuristicAi(_options.Seed ^ 0x5EED));
        var engine = new GameEngine(_state, _human, ai, this);

        try
        {
            await engine.RunAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Left the table.
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Engine error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =================================================================== board construction

    private void BuildBoard()
    {
        BoardCanvas.Children.Clear();
        BoardSurface.DrawBackground(BoardCanvas);

        foreach (var node in State.AllNodes)
        {
            var color = node.Side == 0 ? BoardSurface.PlayerColor : BoardSurface.OpponentColor;
            var slot = new NodeSlot(node, color);
            var c = BoardLayout.Center(node);
            Canvas.SetLeft(slot, c.X - slot.Width / 2);
            Canvas.SetTop(slot, c.Y - slot.Height / 2);
            slot.MouseLeftButtonDown += Slot_MouseLeftButtonDown;
            BoardCanvas.Children.Add(slot);
            _slots[node] = slot;
        }

        foreach (PileKind pile in Enum.GetValues<PileKind>())
        {
            bool warzonePile = pile is PileKind.WarzoneDeck or PileKind.ActiveWarzone;
            if (warzonePile && !State.Options.Warzones) continue;

            var view = new PileView(pile);
            var c = BoardLayout.PileCenter(pile);
            Canvas.SetLeft(view, c.X - view.Width / 2);
            Canvas.SetTop(view, c.Y - BoardLayout.SlotH / 2);
            var kind = pile;
            view.MouseEnter += (_, _) =>
            {
                // Inspect the top card of face-up piles (Active Warzone, Retired, Nullified, Issued).
                if (kind is PileKind.UnitDeck or PileKind.OrderDeck or PileKind.WarzoneDeck) return;
                var list = State.GetPile(kind);
                if (list.Count > 0) ShowZoom(list[^1]);
            };
            view.MouseLeftButtonDown += Pile_MouseLeftButtonDown;
            view.MouseRightButtonDown += (_, e) =>
            {
                // Right-click a face-up pile to enlarge its top card. Several Active Warzones: each right-click
                // shows the next one.
                if (kind is PileKind.UnitDeck or PileKind.OrderDeck or PileKind.WarzoneDeck) return;
                var list = State.GetPile(kind);
                if (list.Count == 0) return;
                e.Handled = true;
                if (kind == PileKind.ActiveWarzone && list.Count > 1)
                {
                    _warzoneInspectIndex = (_warzoneInspectIndex + 1) % list.Count;
                    OpenInspect(list[list.Count - 1 - _warzoneInspectIndex]);
                    return;
                }
                _warzoneInspectIndex = 0;
                OpenInspect(list[^1]);
            };
            BoardCanvas.Children.Add(view);
            _piles[pile] = view;
        }

        UpdatePiles();
    }

    private void UpdatePiles()
    {
        bool virtualDecks = State.Options.DeckMode == DeckMode.RandomDraw;
        foreach (var (kind, view) in _piles)
        {
            bool isDeck = kind is PileKind.UnitDeck or PileKind.OrderDeck;
            view.Update(State.GetPile(kind), virtualDecks && isDeck ? (int?)int.MaxValue : null);
        }
    }

    // =================================================================== camera

    // Screen space kept clear for the hands (px). The board is fitted between them.
    private const double TopReserve = 46;
    private const double BottomReserve = 128;
    private const double SideMargin = 18;

    /// <summary>
    /// Fits the tilted board into the free area of the viewport, including the near corners that perspective
    /// widens. Zoom is a multiplier on the fitted distance (1 = exactly fits).
    /// </summary>
    private void UpdateCamera()
    {
        if (Camera is null || TiltSlider is null || ZoomSlider is null || Viewport is null) return;

        double w = Viewport.ActualWidth, h = Viewport.ActualHeight;
        if (w < 50 || h < 50) { w = 1260; h = 900; }

        double tilt = TiltSlider.Value * Math.PI / 180.0;
        double tanHalf = Math.Tan(Camera.FieldOfView * Math.PI / 360.0);
        var fit = new CameraFit(tilt, tanHalf, w, h);

        // Smallest distance at which the whole board fits (binary search; the target is re-centred each step).
        double lo = 2, hi = 400;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (fit.Fits(mid, fit.CentredTargetY(mid))) hi = mid; else lo = mid;
        }

        double distance = hi * ZoomSlider.Value;
        double ty = fit.CentredTargetY(distance);
        var target = new Point3D(0, ty, 0);
        var position = new Point3D(0, ty - distance * Math.Sin(tilt), distance * Math.Cos(tilt));
        Camera.Position = position;
        Camera.LookDirection = target - position;
        Camera.UpDirection = new Vector3D(0, 1, 0);
    }

    /// <summary>Pinhole projection matching WPF's PerspectiveCamera (horizontal field of view).</summary>
    private readonly record struct CameraFit(double Tilt, double TanHalf, double W, double H)
    {
        private static readonly (double X, double Y)[] Corners = { (-8, -5), (8, -5), (8, 5), (-8, 5) };

        public Point Project(double x, double y, double distance, double targetY)
        {
            double sin = Math.Sin(Tilt), cos = Math.Cos(Tilt);
            // camera at (0, ty - d·sin, d·cos); forward (0, sin, -cos); right (1,0,0); up (0, cos, sin)
            double vy = y - (targetY - distance * sin);
            double vz = 0 - distance * cos;
            double depth = vy * sin - vz * cos;
            double up = vy * cos + vz * sin;
            if (depth < 0.05) return new Point(double.NaN, double.NaN); // behind the camera
            double sx = W / 2 + x / (depth * TanHalf) * W / 2;
            double sy = H / 2 - up / (depth * TanHalf) * W / 2;
            return new Point(sx, sy);
        }

        public bool Fits(double distance, double targetY)
        {
            foreach (var (x, y) in Corners)
            {
                var p = Project(x, y, distance, targetY);
                if (double.IsNaN(p.X)) return false;
                if (p.X < SideMargin || p.X > W - SideMargin) return false;
                if (p.Y < TopReserve || p.Y > H - BottomReserve) return false;
            }
            return true;
        }

        /// <summary>Target height that centres the board vertically in the free band.</summary>
        public double CentredTargetY(double distance)
        {
            double ty = 0;
            double wanted = (TopReserve + H - BottomReserve) / 2;
            double worldPerPx = 2 * distance * TanHalf / W;
            for (int i = 0; i < 8; i++)
            {
                double far = Project(0, 5, distance, ty).Y;
                double near = Project(0, -5, distance, ty).Y;
                if (double.IsNaN(far) || double.IsNaN(near)) break;
                ty -= ((far + near) / 2 - wanted) * worldPerPx;
            }
            return ty;
        }
    }

    private void Camera_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateCamera();

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedSlider is not null) Anim.SpeedFactor = SpeedSlider.Value;
    }

    private void PlayArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value - e.Delta / 2400.0, ZoomSlider.Minimum, ZoomSlider.Maximum);
    }

    /// <summary>Board-canvas point → point in the FX overlay, through the 3D projection.</summary>
    private Point ProjectToFx(Point boardPoint)
    {
        var p3 = new Point3D(
            -8 + 16 * boardPoint.X / BoardLayout.Width,
            5 - 10 * boardPoint.Y / BoardLayout.Height,
            0);
        try
        {
            var transform = BoardVisual3D.TransformToAncestor(Viewport);
            if (transform is not null && transform.TryTransform(p3, out var p2))
                return Viewport.TranslatePoint(p2, FxCanvas);
        }
        catch (InvalidOperationException)
        {
            // Not connected yet; fall through.
        }
        return new Point(FxCanvas.ActualWidth / 2, FxCanvas.ActualHeight / 2);
    }

    /// <summary>Screen-space bounding box of a board rectangle.</summary>
    private Rect ProjectRect(Rect boardRect)
    {
        var a = ProjectToFx(boardRect.TopLeft);
        var b = ProjectToFx(boardRect.TopRight);
        var c = ProjectToFx(boardRect.BottomLeft);
        var d = ProjectToFx(boardRect.BottomRight);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>Screen-space rect (in the FX overlay) of any element, including its render transform.</summary>
    private Rect FxBounds(FrameworkElement element)
    {
        try
        {
            return element.TransformToVisual(FxCanvas).TransformBounds(new Rect(0, 0, element.Width, element.Height));
        }
        catch (InvalidOperationException)
        {
            return new Rect(FxCanvas.ActualWidth / 2, FxCanvas.ActualHeight, element.Width, element.Height);
        }
    }

    // =================================================================== hands

    private CardView CreateHandCard(CardInstance card)
    {
        var v = new CardView(card, faceDown: false, HandCardW);
        v.Rotate.CenterX = v.Width / 2;
        v.Rotate.CenterY = v.Height * 1.4;
        v.Cursor = Cursors.Hand;
        v.MouseEnter += HandCard_MouseEnter;
        v.MouseLeave += HandCard_MouseLeave;
        v.MouseLeftButtonDown += HandCard_MouseLeftButtonDown;
        v.MouseRightButtonDown += Card_MouseRightButtonDown;
        if (card.Definition.OrderType == OrderType.React)
            v.ToolTip = "React: glows when it can answer what's happening. Click it then to respond.";
        return v;
    }

    private CardView CreateOpponentHandCard()
    {
        var v = new CardView(null, faceDown: true, OppHandCardW);
        v.Rotate.CenterX = v.Width / 2;
        v.Rotate.CenterY = -v.Height * 0.6;
        return v;
    }

    private sealed record HandSpot(double X, double Y, double Angle);

    private HandSpot HomeOf(CardView v) => (HandSpot)v.Tag;

    private void LayoutHand(bool animate = true)
    {
        double width = HandCanvas.ActualWidth;
        int n = _handViews.Count;
        if (width <= 0 || n == 0) return;

        double spacing = Math.Min(HandCardW * 0.86, (width - 220) / Math.Max(1, n));
        double total = spacing * (n - 1) + HandCardW;
        double startX = (width - total) / 2;

        for (int i = 0; i < n; i++)
        {
            var v = _handViews[i];
            double offset = i - (n - 1) / 2.0;
            var spot = new HandSpot(startX + i * spacing, 20 + offset * offset * 2.5, offset * 3.2);
            v.Tag = spot;
            Panel.SetZIndex(v, i);
            if (v == _selected) continue; // stays lifted
            MoveToSpot(v, spot, lift: 0, scale: 1, animate);
        }
    }

    private static void MoveToSpot(CardView v, HandSpot spot, double lift, double scale, bool animate)
    {
        if (animate)
        {
            _ = Anim.To(v.Translate, TranslateTransform.XProperty, spot.X, 260, Anim.Out);
            _ = Anim.To(v.Translate, TranslateTransform.YProperty, spot.Y - lift, 260, Anim.Out);
            _ = Anim.To(v.Rotate, RotateTransform.AngleProperty, lift > 0 ? 0 : spot.Angle, 260, Anim.Out);
            _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, scale, 200, Anim.Out);
            _ = Anim.To(v.Scale, ScaleTransform.ScaleYProperty, scale, 200, Anim.Out);
        }
        else
        {
            v.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            v.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            v.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            v.Translate.X = spot.X;
            v.Translate.Y = spot.Y - lift;
            v.Rotate.Angle = lift > 0 ? 0 : spot.Angle;
        }
    }

    private void LayoutOpponentHand(bool animate = true)
    {
        double width = OpponentHandCanvas.ActualWidth;
        int n = _oppHandViews.Count;
        if (width <= 0 || n == 0) return;

        double spacing = OppHandCardW * 0.6;
        double total = spacing * (n - 1) + OppHandCardW;
        double startX = (width - total) / 2;
        for (int i = 0; i < n; i++)
        {
            var v = _oppHandViews[i];
            double offset = i - (n - 1) / 2.0;
            var spot = new HandSpot(startX + i * spacing, -50 - offset * offset * 1.5, -offset * 4);
            v.Tag = spot;
            Panel.SetZIndex(v, i);
            MoveToSpot(v, spot, 0, 1, animate);
        }
    }

    private void HandCard_MouseEnter(object sender, MouseEventArgs e)
    {
        var v = (CardView)sender;
        ShowZoom(v.Card);
        ScheduleMagnify(v);
        if (v == _selected || v.Tag is not HandSpot spot) return;
        Panel.SetZIndex(v, 100);
        MoveToSpot(v, spot, lift: 88, scale: 1.1, animate: true); // just enough to reveal the whole card, so it stays under the cursor
    }

    private void HandCard_MouseLeave(object sender, MouseEventArgs e)
    {
        var v = (CardView)sender;
        UnscheduleMagnify(v);
        if (v == _selected || v.Tag is not HandSpot spot) return;
        Panel.SetZIndex(v, _handViews.IndexOf(v));
        MoveToSpot(v, spot, lift: 0, scale: 1, animate: true);
    }

    // =================================================================== zoom panel

    private void ShowZoom(CardInstance? card)
    {
        if (card is null) return;
        if (_zoomed?.Card == card) return;
        _zoomed = new CardView(card, faceDown: false, 238, vector: true);
        _zoomed.ShowRankIfInPlay(State);
        ZoomHost.Child = _zoomed;
    }

    // =================================================================== input

    private const string TurnHint = "Click a glowing card in your hand, then its target. Right-click any card to enlarge it. Space = pass.";

    private void OnHumanDecisionRequested(DecisionContext ctx)
    {
        _hud.CanPass = true;
        _hud.IsMyTurn = true;

        if (ctx.IsResponse)
        {
            var top = ctx.StackTopFirst.FirstOrDefault();
            _hud.Status = "Respond?";
            _hud.PassText = "OK – NO RESPONSE";
            _hud.Hint = top is null ? "" :
                $"{DescribeForPrompt(top)} Click a glowing React to respond, or OK (Space) to let it happen.";
            foreach (var v in _handViews)
                v.SetGlow(ctx.Legal.OfType<ReactAction>().Any(a => a.Card == v.Card) ? (Color?)Purple : null,
                    pulse: ctx.Legal.OfType<ReactAction>().Any(a => a.Card == v.Card));
            return;
        }

        _hud.Status = "Your turn";
        _hud.PassText = "PASS";
        _hud.Hint = ctx.OpponentJustPassed ? "The AI passed – if you pass too, the round is scored." : TurnHint;

        var playable = PlayableCards(ctx);
        foreach (var v in _handViews)
            v.SetGlow(playable.Contains(v.Card!) ? (Color?)Cyan : null);
        foreach (var node in ctx.Legal.OfType<AdvanceFromStandbyAction>().Select(a => a.From))
            if (node.Card is not null && _boardCards.TryGetValue(node.Card.InstanceId, out var bv))
                bv.SetGlow(Cyan);
        GlowDeployablePiles(ctx);
    }

    /// <summary>Barkeep Bill & co.: the Retired / Nullified pile glows when a Unit can be Deployed from it.</summary>
    private void GlowDeployablePiles(DecisionContext ctx)
    {
        foreach (var a in ctx.Legal.OfType<DeployFromPileAction>())
            if (_piles.TryGetValue(a.Pile, out var pv)) pv.SetTarget(true);
        if (ctx.Legal.OfType<DeployFromPileAction>().Any())
            _hud.Hint += " A glowing pile: click it to Deploy a Unit from it.";
    }

    private static string DescribeForPrompt(StackItem item)
    {
        string who = item.Controller == 0 ? "You are" : "The AI is";
        return item.Kind switch
        {
            StackItemKind.DeployUnit => $"{who} deploying {item.Card.Name} to {NodeText(item.TargetNode!, 0)}.",
            StackItemKind.Assemble => $"{who} assembling {item.Card.Name} onto {item.AssembleTarget!.Name}.",
            StackItemKind.UnitNullifying => $"{item.Card.Name} is about to be Nullified{(item.NullifySource is { } src ? $" by {src.Name}" : "")}.",
            _ when item.IsReact => $"{who} reacting with {item.Card.Name} (against {item.RespondingTo?.Card.Name}).",
            _ => $"{who} issuing {item.Card.Name}.",
        };
    }

    private void OnHumanDecisionCompleted()
    {
        _hud.CanPass = false;
        _hud.IsMyTurn = false;
        _hud.Hint = "";
        _hud.PassText = "PASS";
        ClearSelection();
        foreach (var p in _piles.Values) if (p.IsTarget) p.SetTarget(false);
        foreach (var v in _handViews) v.SetGlow(null);
        foreach (var v in _boardCards.Values) v.SetGlow(null);
    }

    /// <summary>Hand cards with at least one legal turn action (deploy, issue, assemble).</summary>
    private static HashSet<CardInstance> PlayableCards(DecisionContext ctx)
    {
        var set = new HashSet<CardInstance>();
        foreach (var a in ctx.Legal)
        {
            switch (a)
            {
                case DeployFromHandAction d: set.Add(d.Card); break;
                case IssueOrderAction o: set.Add(o.Card); break;
                case AssembleAction x: set.Add(x.Card); break;
            }
        }
        return set;
    }

    // ------------------------------------------------------------------ selection state

    private Node? _selectedStandby;
    private IssueOrderAction? _selectedIssue;                       // an Action Order waiting for confirmation
    private readonly Dictionary<CardInstance, AssembleAction> _assembleTargets = new();

    private void HandCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var v = (CardView)sender;
        if (TryChoiceClick(v.Card, null)) return;
        var ctx = _human.Current;
        if (ctx is null || v.Card is null) return;

        // Priority window: clicking a glowing React issues it against the topmost item it can answer.
        if (ctx.IsResponse)
        {
            var react = ctx.Legal.OfType<ReactAction>().FirstOrDefault(a => a.Card == v.Card);
            if (react is not null) _human.Submit(react);
            else _hud.Hint = "That card can't respond to this. Click a glowing React, or OK.";
            return;
        }

        if (_selected == v)
        {
            ClearSelection();
            _hud.Hint = TurnHint;
            return;
        }

        var card = v.Card;
        var deployTargets = ctx.Legal.OfType<DeployFromHandAction>().Where(a => a.Card == card).Select(a => a.Target).ToList();
        var ongoingTargets = ctx.Legal.OfType<IssueOrderAction>().Where(a => a.Card == card && a.OrderNode is not null).Select(a => a.OrderNode!).ToList();
        var issueAction = ctx.Legal.OfType<IssueOrderAction>().FirstOrDefault(a => a.Card == card && a.OrderNode is null);
        var assembles = ctx.Legal.OfType<AssembleAction>().Where(a => a.Card == card).ToList();

        if (deployTargets.Count == 0 && ongoingTargets.Count == 0 && issueAction is null && assembles.Count == 0)
        {
            _hud.Hint = card.Definition.OrderType switch
            {
                OrderType.React => "Reacts are issued in response to something – they glow when you can use them.",
                OrderType.Ongoing => "No Empty Order Node for that Ongoing Order.",
                _ => "No legal target for that card right now.",
            };
            return;
        }

        ClearSelection();
        _selected = v;
        v.SetGlow(Gold, pulse: true);
        if (v.Tag is HandSpot spot)
        {
            Panel.SetZIndex(v, 100);
            MoveToSpot(v, spot, lift: 95, scale: 1.05, animate: true);
        }

        foreach (var node in deployTargets.Concat(ongoingTargets)) _slots[node].SetTarget(true);
        foreach (var a in assembles)
        {
            _assembleTargets[a.Target] = a;
            if (_boardCards.TryGetValue(a.Target.InstanceId, out var bv)) bv.SetGlow(Green, pulse: true);
        }
        if (issueAction is not null)
        {
            _selectedIssue = issueAction;
            _piles[PileKind.IssuedOrders].SetTarget(true);
        }

        var parts = new List<string>();
        if (deployTargets.Count > 0)
            parts.Add(deployTargets.Any(n => n.Side != 0)
                ? "a glowing node (it may also go on the AI's side, where it scores for the AI)"
                : "a glowing node to deploy");
        if (assembles.Count > 0) parts.Add("a green unit to Assemble onto (+" + AssembleRules.AssembledRank(card) + " Rank)");
        if (ongoingTargets.Count > 0) parts.Add("a glowing Order Node");
        if (issueAction is not null) parts.Add("the ISSUED pile (or press Enter) to issue it");
        _hud.Hint = "Choose " + string.Join(", or ", parts) + ". Right-click cancels.";
    }

    private void ClearSelection()
    {
        foreach (var s in _slots.Values) if (s.IsTarget) s.SetTarget(false);
        foreach (var p in _piles.Values) if (p.IsTarget) p.SetTarget(false);
        foreach (var t in _assembleTargets.Keys)
            if (_boardCards.TryGetValue(t.InstanceId, out var bv)) bv.SetGlow(null);
        _assembleTargets.Clear();
        _selectedIssue = null;
        if (_human.Current is { IsResponse: false } turn)
            foreach (var a in turn.Legal.OfType<DeployFromPileAction>())
                if (_piles.TryGetValue(a.Pile, out var pv)) pv.SetTarget(true);

        if (_selected is not null)
        {
            var v = _selected;
            _selected = null;
            v.SetGlow(_human.Current is { IsResponse: false } ctx && v.Card is not null && PlayableCards(ctx).Contains(v.Card)
                ? (Color?)Cyan : null);
            if (v.Tag is HandSpot spot && _handViews.Contains(v))
            {
                Panel.SetZIndex(v, _handViews.IndexOf(v));
                MoveToSpot(v, spot, 0, 1, animate: true);
            }
        }

        // Standby cards keep their "can advance" glow while it is still our turn.
        if (_selectedStandby is not null && _human.Current is { IsResponse: false } c2)
            foreach (var node in c2.Legal.OfType<AdvanceFromStandbyAction>().Select(a => a.From))
                if (node.Card is not null && _boardCards.TryGetValue(node.Card.InstanceId, out var bv))
                    bv.SetGlow(Cyan);
        _selectedStandby = null;
    }

    private void BoardCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var v = (CardView)sender;
        if (TryChoiceClick(v.Card, null))
        {
            e.Handled = true;
            return;
        }
        var ctx = _human.Current;
        if (ctx is null || ctx.IsResponse || v.Card is null) return;

        // Assemble the selected hand card onto this unit.
        if (_assembleTargets.TryGetValue(v.Card, out var assemble))
        {
            e.Handled = true;
            _human.Submit(assemble);
            return;
        }

        var advance = ctx.Legal.OfType<AdvanceFromStandbyAction>().FirstOrDefault(a => a.From.Card == v.Card);
        if (advance is null) return;
        e.Handled = true;
        ClearSelection();
        _selectedStandby = advance.From;
        v.SetGlow(Gold, pulse: true);
        _slots[advance.To].SetTarget(true);
        _hud.Hint = "Advance into the glowing node.";
    }

    private void Slot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slot = (NodeSlot)sender;
        if (TryChoiceClick(null, slot.Node))
        {
            e.Handled = true;
            return;
        }
        var ctx = _human.Current;
        if (ctx is null || ctx.IsResponse || !slot.IsTarget) return;
        e.Handled = true;

        GameAction? action = null;
        if (_selected?.Card is { } card)
        {
            action = card.Definition.IsOrder
                ? ctx.Legal.OfType<IssueOrderAction>().FirstOrDefault(a => a.Card == card && a.OrderNode == slot.Node)
                : ctx.Legal.OfType<DeployFromHandAction>().FirstOrDefault(a => a.Card == card && a.Target == slot.Node);
        }
        else if (_selectedStandby is not null)
        {
            action = ctx.Legal.OfType<AdvanceFromStandbyAction>().FirstOrDefault(a => a.From == _selectedStandby && a.To == slot.Node);
        }

        if (action is not null) _human.Submit(action);
    }

    private void Pile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not PileView { IsTarget: true } pv) return;
        e.Handled = true;
        if (_selectedIssue is null && _human.Current is { IsResponse: false } ctx
            && ctx.Legal.OfType<DeployFromPileAction>().FirstOrDefault(a => a.Pile == pv.Pile) is { } fromPile)
        {
            ClearSelection();
            _human.Submit(fromPile);
            return;
        }
        TryIssueSelected();
    }

    private void TryIssueSelected()
    {
        if (_selectedIssue is { } issue && _human.Current is { IsResponse: false } ctx && ctx.Legal.Contains(issue))
            _human.Submit(issue);
    }

    private void PlayArea_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selected is not null || _selectedStandby is not null)
        {
            ClearSelection();
            if (_human.Current is { IsResponse: false }) _hud.Hint = TurnHint;
        }
    }

    private void PauseReacts_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return; // can fire during InitializeComponent
        _human.PauseForReacts = cb.IsChecked == true;
        // Turning it off while being asked answers "no response" right away.
        if (!_human.PauseForReacts && _human.Current is { IsResponse: true } ctx)
            _human.Submit(ctx.Legal.OfType<PassAction>().First());
    }

    private void Pass_Click(object sender, RoutedEventArgs e) => TryPass();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            TryPass();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ActiveChoice is not null)
        {
            ConfirmChoice();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _selectedIssue is not null)
        {
            TryIssueSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (IsInspectOpen) CloseInspect(); // first Esc closes the enlarged card, the next clears the selection
            else ClearSelection();
            e.Handled = true;
        }
    }

    private void TryPass()
    {
        var ctx = _human.Current;
        if (ctx is null) return;
        var pass = ctx.Legal.OfType<PassAction>().FirstOrDefault();
        if (pass is not null) _human.Submit(pass);
    }

    private void PlayAgain_Click(object sender, RoutedEventArgs e) => RestartRequested?.Invoke();

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        ExitRequested?.Invoke();
    }
}

internal static class CardViewExtensions
{
    /// <summary>Shows the live rank badge when the card is on a scoring node.</summary>
    public static void ShowRankIfInPlay(this CardView view, GameState state)
    {
        var card = view.Card;
        if (card is null || card.Location.Kind != LocationKind.Node || card.Location.Node?.IsScoring != true)
        {
            view.HideRank();
            return;
        }
        view.ShowRank(RankCalculator.GetRank(state, card), card.Definition.BaseRank ?? 0);
    }
}
