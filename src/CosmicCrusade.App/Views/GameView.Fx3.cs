using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;
using Path = System.Windows.Shapes.Path;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Effects pass 3 (backlog E, B, C, D): the effects settings, the board-reading aids (pair chevrons, rolling totals,
/// tug-of-war, weakness cycle, dormant Standby cards), atmosphere (lighting lean, idle life, trails, the AI's eye and
/// taunts, riffle shuffle, fanned deal) and the big moments (victory lift-off, defeat crack, the deciding Duel).
/// The Warzone ambients (backlog A) are in GameView.Fx3Ambient.cs.
/// </summary>
public partial class GameView
{
    // =================================================================== E. settings

    private bool _syncingFxSettings;

    /// <summary>Puts the saved settings into the side panel and listens for changes (called from the constructor).</summary>
    private void InitFxSettings()
    {
        _syncingFxSettings = true;
        ReduceFxCheck.IsChecked = FxSettings.ReduceEffects;
        FxQualityBox.SelectedIndex = FxSettings.HighQuality ? 1 : 0;
        _syncingFxSettings = false;
        FxSettings.Changed += OnFxSettingsChanged;
        Unloaded += (_, _) =>
        {
            FxSettings.Changed -= OnFxSettingsChanged;
            _aiEyeTimer?.Stop();
            foreach (var t in _rolls.Values) t.Timer.Stop();
        };
    }

    /// <summary>Side-panel "Reduce effects" check box and "Effects" quality box.</summary>
    private void FxSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingFxSettings || ReduceFxCheck is null || FxQualityBox is null) return; // still loading the XAML
        FxSettings.ReduceEffects = ReduceFxCheck.IsChecked == true;
        if (FxQualityBox.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<FxQuality>(tag, out var q))
            FxSettings.Quality = q;
    }

    /// <summary>Restarts everything that was built for the old settings: ambients, idle motion and sigils.</summary>
    private void OnFxSettingsChanged()
    {
        if (_state is null) return;
        StopAllAmbient();
        foreach (var stop in _sigils.Values) stop();
        _sigils.Clear();
        foreach (var v in _boardCards.Values) v.SetIdleBob(false);
        SyncAmbient();
    }

    // =================================================================== B. reading the board: pair chevrons

    private sealed record PairMarker(Node Mine, FrameworkElement Element, Path Arrow, RotateTransform Turn, ScaleTransform Squash);

    private readonly List<PairMarker> _pairMarkers = new();
    private Ellipse? _leanBlue;
    private Ellipse? _leanRed;

    /// <summary>Called at the end of BuildBoard: the pair chevrons on the control line and the lighting-lean glows.</summary>
    private void BuildBoardReadout()
    {
        // C. Lighting: two broad glows that lean towards whoever is ahead.
        _leanBlue = new Ellipse
        {
            Width = BoardLayout.Width,
            Height = 620,
            Fill = new RadialGradientBrush(Color.FromArgb(110, PlayerBolt.R, PlayerBolt.G, PlayerBolt.B), Color.FromArgb(0, PlayerBolt.R, PlayerBolt.G, PlayerBolt.B)),
            Opacity = 0.2,
        };
        _leanRed = new Ellipse
        {
            Width = BoardLayout.Width,
            Height = 620,
            Fill = new RadialGradientBrush(Color.FromArgb(110, OpponentBolt.R, OpponentBolt.G, OpponentBolt.B), Color.FromArgb(0, OpponentBolt.R, OpponentBolt.G, OpponentBolt.B)),
            Opacity = 0.2,
        };
        // Kept inside the board: anything past the edges would change the plane's fit (see GameView.xaml).
        AddFx(_leanBlue, 0, BoardLayout.Height - 620, 1);
        AddFx(_leanRed, 0, 0, 1);

        // B. One chevron per opposed pair, on the control line.
        var p0 = State.Players[0];
        var p1 = State.Players[1];
        var mine = p0.UnitNodes.ToList();
        if (p0.LeaderNode is not null && p1.LeaderNode is not null) mine.Add(p0.LeaderNode);
        foreach (var node in mine)
        {
            var arrow = new Path
            {
                Data = Geometry.Parse("M 0,0 L 22,13 L 44,0 L 44,11 L 22,25 L 0,11 Z"),
                Fill = Solid(Colors.Gray, 160),
                Stroke = Solid(Colors.Black, 140),
                StrokeThickness = 1.5,
            };
            var host = new Grid { Width = 44, Height = 25, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.5 };
            host.Children.Add(arrow);
            var turn = new RotateTransform(0);
            var squash = new ScaleTransform(1, 0.35);
            var g = new TransformGroup();
            g.Children.Add(squash);
            g.Children.Add(turn);
            host.RenderTransform = g;
            host.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0 };
            var c = BoardLayout.Center(node);
            AddFx(host, c.X - 22, BoardLayout.ControlLineY - 12.5, 7);
            _pairMarkers.Add(new PairMarker(node, host, arrow, turn, squash));
        }
    }

    /// <summary>
    /// After every score refresh: the chevrons, the rolling totals, the tug-of-war knot and the lighting lean.
    /// The Oculus inverts both the round and the pair tiebreak (ruling #39), so the chevrons and the knot point at
    /// whoever is actually winning.
    /// </summary>
    private void UpdateBoardReadout()
    {
        if (_state is null) return;
        RollTotal(MyTotalText, _hud.MyTotal);
        RollTotal(AiTotalText, _hud.AiTotal);

        var result = Scoring.Score(State);
        bool inverted = StaticLayer.RoundWinnerInverted(State);
        bool tied = result.Total0 == result.Total1;
        bool anyUnits = State.Players.Any(p => p.ScoringNodes.Any(n => n.Card is not null));

        foreach (var m in _pairMarkers)
        {
            var pair = result.Pairs.FirstOrDefault(p => p.Label == m.Mine.Label);
            int? w = pair?.Winner;
            if (w is int pw && inverted) w = GameState.Other(pw);
            var color = w switch { 0 => PlayerBolt, 1 => OpponentBolt, _ => Colors.Gray };
            m.Arrow.Fill = Solid(color, 230);
            m.Element.Effect = w is null
                ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0 }
                : new DropShadowEffect { Color = color, BlurRadius = tied ? 22 : 12, ShadowDepth = 0 };
            _ = Anim.To(m.Turn, RotateTransform.AngleProperty, w == 1 ? 180 : 0, 300, Anim.InOut);
            _ = Anim.To(m.Squash, ScaleTransform.ScaleYProperty, w is null ? 0.35 : 1, 300, Anim.InOut);
            // The pairs only decide a tied round: then they light up.
            double opacity = !anyUnits ? 0 : tied ? 1 : 0.5;
            _ = Anim.To(m.Element, OpacityProperty, opacity, 300, null);
        }

        // Who is winning the round, and by how much: -1 (AI) … 1 (you).
        double lean = 0;
        if (anyUnits && _roundLive)
        {
            int d = result.Total0 - result.Total1;
            if (inverted) d = -d;
            lean = d != 0 ? Math.Tanh(d / 5.0) : result.Winner switch { 0 => 0.12, 1 => -0.12, _ => 0 };
        }
        UpdateTug(lean);
        if (_leanBlue is not null && _leanRed is not null)
        {
            _ = Anim.To(_leanBlue, OpacityProperty, 0.2 + 0.6 * Math.Max(0, lean), 900, Anim.InOut);
            _ = Anim.To(_leanRed, OpacityProperty, 0.2 + 0.6 * Math.Max(0, -lean), 900, Anim.InOut);
        }
    }

    private void UpdateTug(double lean)
    {
        double w = TugTrack.ActualWidth;
        if (w <= 0) return;
        double f = 0.5 + 0.47 * lean;
        _ = Anim.To(TugBlue, WidthProperty, Math.Max(0, w * f), 500, Anim.InOut);
        _ = Anim.To(TugKnotShift, TranslateTransform.XProperty, w * f - TugKnot.Width / 2, 500, Anim.InOut);
        TugGlow.Color = lean > 0.05 ? PlayerBolt : lean < -0.05 ? OpponentBolt : Gold;
    }

    // =================================================================== B. rolling totals

    private sealed class Roll
    {
        public int Shown;
        public int Target;
        public DispatcherTimer Timer = null!;
    }

    private readonly Dictionary<TextBlock, Roll> _rolls = new();

    /// <summary>The total counts up or down to its new value one step at a time, each digit dropping in.</summary>
    private void RollTotal(TextBlock tb, int target)
    {
        if (!_rolls.TryGetValue(tb, out var roll))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            roll = new Roll { Shown = target, Target = target, Timer = timer };
            _rolls[tb] = roll;
            var r = roll;
            timer.Tick += (_, _) => RollStep(tb, r);
            tb.Text = target.ToString();
            return;
        }
        roll.Target = target;
        if (roll.Shown != target && !roll.Timer.IsEnabled)
        {
            roll.Timer.Interval = TimeSpan.FromMilliseconds(Math.Max(25, 70 * Anim.SpeedFactor));
            roll.Timer.Start();
        }
    }

    private void RollStep(TextBlock tb, Roll roll)
    {
        int diff = roll.Target - roll.Shown;
        if (diff == 0)
        {
            roll.Timer.Stop();
            tb.ClearValue(TextBlock.ForegroundProperty);
            return;
        }
        int step = Math.Sign(diff) * Math.Max(1, Math.Abs(diff) / 6);
        roll.Shown += step;
        tb.Text = roll.Shown.ToString();
        tb.Foreground = Solid(step > 0 ? Green : Red);
        var (_, slide) = TotalTransforms(tb);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(step > 0 ? 14 : -14, 0, TimeSpan.FromMilliseconds(80)));
        tb.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(80)));
        if (roll.Shown == roll.Target) Sfx.Play(SfxKind.Tick, 0.35, minGapMs: 60);
    }

    /// <summary>The score text's transforms: a scale (heartbeat) and a slide (rolling digits).</summary>
    private static (ScaleTransform Scale, TranslateTransform Slide) TotalTransforms(TextBlock tb)
    {
        if (tb.RenderTransform is TransformGroup g0 && g0.Children.Count == 2
            && g0.Children[0] is ScaleTransform s && g0.Children[1] is TranslateTransform t)
            return (s, t);
        tb.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        var g = new TransformGroup();
        g.Children.Add(scale);
        g.Children.Add(slide);
        tb.RenderTransform = g;
        return (scale, slide);
    }

    // =================================================================== B. a tie decided by weakness: the race cycle

    private static readonly (Race Race, string Short, Color Color)[] WeaknessCycle =
    {
        (Race.SpaceCowboy, "SC", Color.FromRgb(248, 70, 70)),
        (Race.Necrolite, "NE", Color.FromRgb(34, 197, 94)),
        (Race.Overmind, "OV", Color.FromRgb(74, 125, 255)),
        (Race.Blaarg, "BL", Color.FromRgb(202, 202, 40)),
        (Race.Synthenoid, "SY", Color.FromRgb(170, 170, 180)),
    };

    /// <summary>The races that decided a tied Duel (winner beats loser), or null if the Ranks differed / nothing decided it.</summary>
    private (Race Winner, Race Loser)? WeaknessDecider(DuelEvent e)
    {
        if (e.Loser is not { } loser) return null;
        var winner = loser == e.A ? e.B : e.A;
        if (RankCalculator.GetRank(State, e.A) != RankCalculator.GetRank(State, e.B)) return null;
        foreach (var w in StaticLayer.Races(State, winner))
            foreach (var l in StaticLayer.Races(State, loser))
                if (State.Database.Beats(w, l)) return (w, l);
        return null;
    }

    /// <summary>The five-race cycle appears over the table; the arrow from the winning race to the losing one lights up.</summary>
    private async Task RaceCycleAsync(Race winner, Race loser)
    {
        Sfx.Play(SfxKind.Cycle, 0.8);
        const double size = 300, r = 105, node = 54;
        var host = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        host.Children.Add(new Ellipse { Width = size, Height = size, Fill = new RadialGradientBrush(Color.FromArgb(220, 7, 9, 28), Color.FromArgb(0, 7, 9, 28)) });
        Point At(int i)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
            return new Point(size / 2 + Math.Cos(a) * r, size / 2 + Math.Sin(a) * r);
        }

        int wi = Array.FindIndex(WeaknessCycle, c => c.Race == winner);
        Path? lit = null;
        for (int i = 0; i < 5; i++)
        {
            var a = At(i);
            var b = At((i + 1) % 5);
            var dir = b - a;
            dir.Normalize();
            var from = a + dir * (node / 2 + 4);
            var to = b - dir * (node / 2 + 6);
            var normal = new Vector(-dir.Y, dir.X);
            var head1 = to - dir * 12 + normal * 7;
            var head2 = to - dir * 12 - normal * 7;
            bool isWin = i == wi && WeaknessCycle[(i + 1) % 5].Race == loser;
            var arrow = new Path
            {
                Data = Geometry.Parse(FormattableString.Invariant(
                    $"M {from.X},{from.Y} L {to.X},{to.Y} M {head1.X},{head1.Y} L {to.X},{to.Y} L {head2.X},{head2.Y}")),
                Stroke = Solid(isWin ? Gold : Colors.Gray, isWin ? (byte)255 : (byte)120),
                StrokeThickness = isWin ? 6 : 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            if (isWin)
            {
                arrow.Effect = new DropShadowEffect { Color = Gold, BlurRadius = 18, ShadowDepth = 0 };
                lit = arrow;
            }
            host.Children.Add(arrow);
        }
        for (int i = 0; i < 5; i++)
        {
            var (race, label, color) = WeaknessCycle[i];
            var p = At(i);
            bool involved = race == winner || race == loser;
            var disc = new Grid { Width = node, Height = node };
            disc.Children.Add(new Ellipse
            {
                Fill = Solid(color, involved ? (byte)240 : (byte)110),
                Stroke = Solid(race == winner ? Gold : race == loser ? Red : Colors.White, involved ? (byte)255 : (byte)90),
                StrokeThickness = involved ? 4 : 2,
                Effect = involved ? new DropShadowEffect { Color = race == winner ? Gold : Red, BlurRadius = 20, ShadowDepth = 0 } : null,
            });
            disc.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Haettenschweiler, Impact"),
                FontSize = 24,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Canvas.SetLeft(disc, p.X - node / 2);
            Canvas.SetTop(disc, p.Y - node / 2);
            host.Children.Add(disc);
        }
        var caption = new TextBlock
        {
            Text = "TIE · WEAKNESS DECIDES",
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 20,
            Foreground = Solid(Gold),
            Width = size,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetTop(caption, size / 2 - 12);
        host.Children.Add(caption);

        var s = new ScaleTransform(0.6, 0.6);
        host.RenderTransform = s;
        host.Opacity = 0;
        AddScreenFx(host, FxCanvas.ActualWidth / 2 - size / 2, FxCanvas.ActualHeight * 0.42 - size / 2, 250);
        _ = Anim.To(host, OpacityProperty, 1, 200, Anim.Out);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 320, Anim.Back);
        await Anim.To(s, ScaleTransform.ScaleYProperty, 1, 320, Anim.Back);
        if (lit is not null)
            lit.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, Anim.Ms(160)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
        await Anim.Delay(1100);
        await FadeAndRemoveScreenAsync(host, 300);
    }

    // =================================================================== B/C. per-card state: Standby, idle bob, frost, sigils

    private readonly Dictionary<int, Action> _sigils = new();

    /// <summary>
    /// Called at the end of SyncAmbient (after every event): Standby Units look dormant, Units in play bob gently, Blaarg
    /// Units frost over under Cryogenic Chambers, Ongoing Orders carry a pulsing sigil, Military Academy's chevrons
    /// count the Deployment Turns left.
    /// </summary>
    private void SyncIdle()
    {
        if (_state is null) return;
        bool cryo = State.ActiveWarzones.Any(w => w.Definition.Name == "Cryogenic Chambers");
        foreach (var v in _boardCards.Values)
        {
            if (v.Card is not { } c) continue;
            var node = c.Location.Node;
            bool standby = node?.Kind == NodeKind.Standby;
            bool inPlay = node is { IsScoring: true };
            v.SetDormant(standby);
            v.SetIdleBob(FxSettings.HighQuality && inPlay, c.InstanceId * 0.37);
            v.SetFrost(cryo && inPlay && StaticLayer.Races(State, c).Contains(Race.Blaarg));
        }

        var ongoing = State.Players.SelectMany(p => p.OrderNodes)
            .Where(n => n.Card is { } oc && oc.Definition.OrderType == OrderType.Ongoing)
            .ToDictionary(n => n.Card!.InstanceId, n => n);
        foreach (var id in _sigils.Keys.Where(id => !ongoing.ContainsKey(id)).ToList())
        {
            _sigils[id]();
            _sigils.Remove(id);
        }
        foreach (var (id, node) in ongoing)
            if (!_sigils.ContainsKey(id)) _sigils[id] = StartSigil(node);

        UpdateAcademyChevrons();
    }

    /// <summary>A faint rune circle under an Ongoing Order that breathes (and turns, at High quality).</summary>
    private Action StartSigil(Node node)
    {
        var accent = node.Card is { } card ? CardLayout.Instance.Accent(card.Definition) : Purple;
        const double d = 240;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // Rune ticks round the rim.
            for (int i = 0; i < 24; i++)
            {
                double a = i * Math.PI * 2 / 24;
                double r0 = d / 2 - (i % 3 == 0 ? 18 : 10);
                ctx.BeginFigure(new Point(d / 2 + Math.Cos(a) * r0, d / 2 + Math.Sin(a) * r0), false, false);
                ctx.LineTo(new Point(d / 2 + Math.Cos(a) * (d / 2 - 3), d / 2 + Math.Sin(a) * (d / 2 - 3)), true, false);
            }
            // A hexagram inside.
            foreach (double off in new[] { 0.0, Math.PI / 3 })
            {
                for (int k = 0; k <= 3; k++)
                {
                    double a = off - Math.PI / 2 + k * Math.PI * 2 / 3;
                    var p = new Point(d / 2 + Math.Cos(a) * d * 0.36, d / 2 + Math.Sin(a) * d * 0.36);
                    if (k == 0) ctx.BeginFigure(p, false, false);
                    else ctx.LineTo(p, true, false);
                }
            }
        }
        geo.Freeze();
        var sigil = new Grid { Width = d, Height = d, RenderTransformOrigin = new Point(0.5, 0.5) };
        sigil.Children.Add(new Ellipse { Stroke = Solid(accent, 200), StrokeThickness = 3 });
        sigil.Children.Add(new Ellipse { Margin = new Thickness(26), Stroke = Solid(accent, 140), StrokeThickness = 1.5 });
        sigil.Children.Add(new Path { Data = geo, Stroke = Solid(accent, 170), StrokeThickness = 2 });
        sigil.Effect = Glow(accent, 18);
        var spin = new RotateTransform();
        sigil.RenderTransform = spin;
        var c = BoardLayout.Center(node);
        AddFx(sigil, c.X - d / 2, c.Y - d / 2, 9);
        sigil.Opacity = 0;
        sigil.BeginAnimation(OpacityProperty, Loop(0.2, 0.6, 2.4, reverse: true));
        if (FxSettings.HighQuality) spin.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 40));
        return StopWith(new UIElement[] { sigil });
    }

    // =================================================================== C. race-coloured trails

    /// <summary>Glowing motes in the card's race colour stream off a card flying across the screen (High quality only).</summary>
    private void Trail(CardView flyer, Color color, double maxMs = 2500)
    {
        if (!FxSettings.HighQuality) return;
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
        timer.Tick += (_, _) =>
        {
            if (flyer.Parent is null || (DateTime.UtcNow - started).TotalMilliseconds > maxMs)
            {
                timer.Stop();
                return;
            }
            var r = FxBounds(flyer);
            double size = Math.Clamp(r.Width * 0.35, 12, 46);
            var mote = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new RadialGradientBrush(Color.FromArgb(200, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
                Opacity = 0.8,
            };
            var m = Mid(r);
            AddScreenFx(mote, m.X - size / 2 + Rand(-6, 6), m.Y - size / 2 + Rand(-6, 6), -1);
            _ = FadeAndRemoveScreenAsync(mote, 380);
        };
        timer.Start();
    }

    private static Color RaceColor(CardInstance card) => CardLayout.Instance.Accent(card.Definition);

    // =================================================================== C. the AI's eye and taunts

    private FrameworkElement? _aiEye;
    private TranslateTransform? _aiPupil;
    private ScaleTransform? _aiLid;
    private ScaleTransform? _aiEyePulse;
    private DispatcherTimer? _aiEyeTimer;
    private bool _aiThinking;
    private DateTime _lastTaunt = DateTime.MinValue;

    /// <summary>A small holographic eye floats over the AI's hand (called once the view is loaded).</summary>
    private void BuildAiEye()
    {
        var (eye, pupil, lid) = BuildEye(70, 34, Color.FromRgb(190, 18, 60), Color.FromRgb(251, 113, 133));
        var host = new Grid { Width = 70, Height = 34, RenderTransformOrigin = new Point(0.5, 0.5) };
        host.Children.Add(eye);
        // Scan lines: it's a hologram.
        var lines = new StreamGeometry();
        using (var ctx = lines.Open())
            for (double y = 1; y < 34; y += 3)
            {
                ctx.BeginFigure(new Point(0, y), false, false);
                ctx.LineTo(new Point(70, y), true, false);
            }
        lines.Freeze();
        host.Children.Add(new Path { Data = lines, Stroke = Solid(Colors.Black, 60), StrokeThickness = 1 });
        var pulse = new ScaleTransform(1, 1);
        host.RenderTransform = pulse;
        host.Opacity = 0.45;
        host.ToolTip = "The AI";
        AddScreenFx(host, 0, 0, 190);
        _aiEye = host;
        _aiPupil = pupil;
        _aiLid = lid;
        _aiEyePulse = pulse;
        PlaceAiEye();
        FxCanvas.SizeChanged += (_, _) => PlaceAiEye();

        _aiEyeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _aiEyeTimer.Tick += (_, _) =>
        {
            if (_aiPupil is null || _aiLid is null) return;
            if (_aiThinking)
            {
                // Darting about while it thinks.
                _ = Anim.To(_aiPupil, TranslateTransform.XProperty, Rand(-14, 14), 160, Anim.Out);
                _ = Anim.To(_aiPupil, TranslateTransform.YProperty, Rand(-3, 3), 160, Anim.Out);
            }
            else if (_fxRng.NextDouble() < 0.04)
            {
                Blink(_aiLid);
            }
        };
        _aiEyeTimer.Start();
    }

    private void PlaceAiEye()
    {
        if (_aiEye is null) return;
        // Just below the AI's hand, centred.
        Canvas.SetLeft(_aiEye, FxCanvas.ActualWidth / 2 - _aiEye.Width / 2);
        Canvas.SetTop(_aiEye, 26);
    }

    /// <summary>The AI's pacing controller reports when it starts and stops "thinking".</summary>
    private void SetAiThinking(bool thinking)
    {
        _aiThinking = thinking;
        if (_aiEye is null || _aiEyePulse is null || _aiPupil is null) return;
        if (thinking)
        {
            _ = Anim.To(_aiEye, OpacityProperty, 1, 200, Anim.Out);
            var beat = new DoubleAnimation(1, 1.18, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            };
            _aiEyePulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            _aiEyePulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
            _aiEye.Effect = new DropShadowEffect { Color = Color.FromRgb(251, 113, 133), BlurRadius = 24, ShadowDepth = 0 };
        }
        else
        {
            _ = Anim.To(_aiEye, OpacityProperty, 0.45, 400, Anim.In);
            _ = Anim.To(_aiEyePulse, ScaleTransform.ScaleXProperty, 1, 200, Anim.Out);
            _ = Anim.To(_aiEyePulse, ScaleTransform.ScaleYProperty, 1, 200, Anim.Out);
            _ = Anim.To(_aiPupil, TranslateTransform.XProperty, 0, 300, Anim.Out);
            _ = Anim.To(_aiPupil, TranslateTransform.YProperty, 0, 300, Anim.Out);
            _aiEye.Effect = null;
        }
    }

    private static readonly string[] DuelTaunts =
    {
        "Outgunned.", "Calculated.", "Was that your best Unit?", "Another one for the Nullified pile.", "Predictable.",
        "My circuits barely warmed up.",
    };

    private static readonly string[] PassTaunts =
    {
        "Passing already?", "Running out of ideas?", "Bold strategy.", "I'll take that.", "Thinking it over? I already have.",
    };

    private static readonly string[] RoundTaunts = { "As foreseen.", "One step closer.", "The odds were always mine." };

    /// <summary>Now and then (never more than every 15 s) the AI says something smug in a hologram bubble.</summary>
    private void MaybeTaunt(string[] lines, double chance)
    {
        if (_aiEye is null || _fxRng.NextDouble() > chance || (DateTime.UtcNow - _lastTaunt).TotalSeconds < 15) return;
        _lastTaunt = DateTime.UtcNow;
        string line = lines[_fxRng.Next(lines.Length)];
        _hud.AddLog($"AI: “{line}”");
        Sfx.Play(SfxKind.Taunt, 0.6);
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 40, 6, 20)),
            BorderBrush = Solid(Color.FromRgb(251, 113, 133), 220),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 5, 12, 6),
            Child = new TextBlock { Text = line, FontSize = 15, FontStyle = FontStyles.Italic, Foreground = Solid(Color.FromRgb(254, 205, 211)) },
            Effect = new DropShadowEffect { Color = Color.FromRgb(251, 113, 133), BlurRadius = 16, ShadowDepth = 0 },
            RenderTransformOrigin = new Point(0, 0.5),
        };
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var s = new ScaleTransform(0.4, 0.4);
        bubble.RenderTransform = s;
        AddScreenFx(bubble, Canvas.GetLeft(_aiEye) + _aiEye.Width + 10, Canvas.GetTop(_aiEye) + 2, 195);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 260, Anim.Back);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 260, Anim.Back);
        _ = FadeAndRemoveScreenAsync(bubble, 400, 2400);
        if (_aiLid is not null) Blink(_aiLid);
    }

    // =================================================================== C. riffle shuffle and the fanned deal

    /// <summary>The deck splits into two packets that riffle back together (board space, at the deck).</summary>
    private async Task RiffleAsync(PileKind deck)
    {
        if (!_piles.ContainsKey(deck)) return;
        Sfx.Play(SfxKind.Riffle, 0.8);
        var c = BoardLayout.PileCenter(deck);
        double w = BoardLayout.CardW * 0.8, h = BoardLayout.CardH * 0.8;
        int n = FxSettings.Count(8);
        var cards = new List<(Image Img, TranslateTransform T, RotateTransform R)>();
        for (int i = 0; i < n * 2; i++)
        {
            bool left = i % 2 == 0;
            var img = new Image { Source = CardFaceFactory.GetBack(), Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 0.5) };
            var t = new TranslateTransform();
            var rot = new RotateTransform();
            var g = new TransformGroup();
            g.Children.Add(rot);
            g.Children.Add(t);
            img.RenderTransform = g;
            AddFx(img, c.X - w / 2, c.Y - h / 2 - 10, 20 + i);
            cards.Add((img, t, rot));
            // Split: each packet slides out and bends.
            _ = Anim.To(t, TranslateTransform.XProperty, left ? -78 : 78, 220, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, -(i / 2) * 2.5, 220, Anim.Out);
            _ = Anim.To(rot, RotateTransform.AngleProperty, left ? -14 : 14, 220, Anim.Out);
        }
        await Anim.Delay(260);
        // Riffle: alternate cards from each side drop onto the centre stack.
        for (int i = 0; i < cards.Count; i++)
        {
            var (_, t, rot) = cards[i];
            _ = Anim.To(t, TranslateTransform.XProperty, 0, 90, Anim.In);
            _ = Anim.To(t, TranslateTransform.YProperty, -i * 1.2, 90, Anim.In);
            _ = Anim.To(rot, RotateTransform.AngleProperty, Rand(-2, 2), 90, Anim.In);
            await Anim.Delay(30);
        }
        await Anim.Delay(140);
        // Square up and hand back to the real pile.
        foreach (var (img, t, rot) in cards)
        {
            _ = Anim.To(t, TranslateTransform.YProperty, 0, 120, Anim.Out);
            _ = Anim.To(rot, RotateTransform.AngleProperty, 0, 120, Anim.Out);
            _ = FadeAndRemoveAsync(img, 160, 120);
        }
        await Anim.Delay(200);
    }

    private bool _dealing;

    /// <summary>Round start: the Unit and Order decks fan out in an arc, then snap shut; the Start-up draws twirl in.</summary>
    private async Task DealFanAsync()
    {
        _dealing = true;
        var tasks = new List<Task>();
        foreach (var deck in new[] { PileKind.UnitDeck, PileKind.OrderDeck })
        {
            if (!_piles.ContainsKey(deck) || (State.GetPile(deck).Count == 0 && State.Options.DeckMode != DeckMode.RandomDraw)) continue;
            var c = BoardLayout.PileCenter(deck);
            double w = BoardLayout.CardW * 0.8, h = BoardLayout.CardH * 0.8;
            int n = FxSettings.Count(9);
            for (int i = 0; i < n; i++)
            {
                double f = n == 1 ? 0 : i / (double)(n - 1) - 0.5;
                var img = new Image { Source = CardFaceFactory.GetBack(), Width = w, Height = h };
                var rot = new RotateTransform(0, w / 2, h * 1.1);
                img.RenderTransform = rot;
                AddFx(img, c.X - w / 2, c.Y - h / 2 - 10, 20 + i);
                var a = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(700) };
                a.KeyFrames.Add(new EasingDoubleKeyFrame(f * 80, KeyTime.FromPercent(0.45), Anim.Out));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(f * 80, KeyTime.FromPercent(0.6)));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), Anim.In));
                var done = new TaskCompletionSource();
                a.Completed += (_, _) =>
                {
                    RemoveFx(img);
                    done.TrySetResult();
                };
                rot.BeginAnimation(RotateTransform.AngleProperty, a);
                tasks.Add(done.Task);
            }
        }
        if (tasks.Count == 0) return;
        Sfx.Play(SfxKind.Riffle, 0.5);
        await Task.WhenAll(tasks);
        Sfx.Play(SfxKind.Deal, 0.7);
    }

    // =================================================================== D. big moments

    private bool _finaleShown;

    /// <summary>The round that wins or loses the game (called from OnRoundScoredAsync, before the banner).</summary>
    private async Task FinaleAsync(RoundResult r)
    {
        int need = State.Options.RoundsToWin;
        if (r.Winner == 0 && State.Players[0].RoundsWon >= need) await LiftOffAsync();
        else if (r.Winner == 1 && State.Players[1].RoundsWon >= need) await DefeatCrackAsync();
    }

    /// <summary>Game over: shows the defeat moment if the game ended some other way (e.g. "end after round").</summary>
    private async Task GameOverMomentAsync(GameOutcome o)
    {
        if (_finaleShown) return;
        if (o.Winner == 1) await DefeatCrackAsync();
    }

    /// <summary>Victory: your Units lift off the board, spinning up into the fireworks.</summary>
    private async Task LiftOffAsync()
    {
        _finaleShown = true;
        Sfx.Play(SfxKind.LiftOff);
        var tasks = new List<Task>();
        int i = 0;
        foreach (var node in State.Players[0].ScoringNodes)
        {
            if (node.Card is not { } card || !_boardCards.TryGetValue(card.InstanceId, out var v)) continue;
            var rect = ProjectRect(BoardRectOf(v));
            // The board card is replaced by a screen copy (Clean-up then finds no view and just updates the pile).
            _boardCards.Remove(card.InstanceId);
            BoardCanvas.Children.Remove(v);
            var flyer = new CardView(card, faceDown: false, HandCardW);
            flyer.Translate.X = rect.X + rect.Width / 2 - flyer.Width / 2;
            flyer.Translate.Y = rect.Y + rect.Height / 2 - flyer.Height / 2;
            flyer.Scale.ScaleX = rect.Width / flyer.Width;
            flyer.Scale.ScaleY = rect.Height / flyer.Height;
            flyer.Rotate.CenterX = flyer.Width / 2;
            flyer.Rotate.CenterY = flyer.Height / 2;
            flyer.SetGlow(Gold, pulse: true);
            FxCanvas.Children.Add(flyer);
            Trail(flyer, RaceColor(card), 3000);
            double delay = i++ * 140, ms = 1800;
            double spin = FxSettings.ReduceEffects ? 180 : 720 * (i % 2 == 0 ? 1 : -1);
            var up = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms), BeginTime = TimeSpan.FromMilliseconds(delay * Anim.SpeedFactor) };
            up.KeyFrames.Add(new EasingDoubleKeyFrame(flyer.Translate.Y - 30, KeyTime.FromPercent(0.25), Anim.Out));
            up.KeyFrames.Add(new EasingDoubleKeyFrame(-flyer.Height * 1.5, KeyTime.FromPercent(1), Anim.In));
            flyer.Translate.BeginAnimation(TranslateTransform.YProperty, up);
            _ = Anim.To(flyer.Scale, ScaleTransform.ScaleXProperty, flyer.Scale.ScaleX * 1.35, ms, Anim.Out, delayMs: delay);
            _ = Anim.To(flyer.Scale, ScaleTransform.ScaleYProperty, flyer.Scale.ScaleY * 1.35, ms, Anim.Out, delayMs: delay);
            var f = flyer;
            tasks.Add(Anim.To(flyer.Rotate, RotateTransform.AngleProperty, spin, ms, Anim.InOut, delayMs: delay)
                .ContinueWith(_ => FxCanvas.Children.Remove(f), TaskScheduler.FromCurrentSynchronizationContext()));
        }
        Fireworks(5, Gold);
        if (tasks.Count > 0) await Task.WhenAll(tasks);
    }

    /// <summary>Defeat: the screen cracks from an impact point, then the colour drains out of the table.</summary>
    private async Task DefeatCrackAsync()
    {
        _finaleShown = true;
        double W = FxCanvas.ActualWidth, H = FxCanvas.ActualHeight;
        if (W < 10 || H < 10) return;
        var impact = new Point(W * Rand(0.4, 0.6), H * Rand(0.35, 0.55));
        Sfx.Play(SfxKind.Crack);
        Shake(strength: 3);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            int spokes = 13;
            var ends = new List<List<Point>>();
            for (int i = 0; i < spokes; i++)
            {
                double a = i * Math.PI * 2 / spokes + Rand(-0.15, 0.15);
                var pts = new List<Point> { impact };
                double r = 0, len = Math.Max(W, H) * Rand(0.35, 0.8);
                while (r < len)
                {
                    r += Rand(40, 110);
                    double wob = a + Rand(-0.12, 0.12);
                    pts.Add(new Point(impact.X + Math.Cos(wob) * r, impact.Y + Math.Sin(wob) * r));
                }
                ctx.BeginFigure(pts[0], false, false);
                foreach (var p in pts.Skip(1)) ctx.LineTo(p, true, true);
                ends.Add(pts);
            }
            // Broken rings joining neighbouring spokes.
            foreach (int ring in new[] { 2, 4 })
                for (int i = 0; i < spokes; i++)
                {
                    if (_fxRng.NextDouble() < 0.3) continue;
                    var a = ends[i];
                    var b = ends[(i + 1) % spokes];
                    if (a.Count <= ring || b.Count <= ring) continue;
                    ctx.BeginFigure(a[ring], false, false);
                    ctx.LineTo(b[ring], true, true);
                }
        }
        geo.Freeze();
        var crack = new Path
        {
            Data = geo,
            Stroke = Solid(Colors.White, 230),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Bevel,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.9 },
            StrokeDashArray = new DoubleCollection { 2000 },
            StrokeDashOffset = 2000,
        };
        AddScreenFx(crack, 0, 0, 160);
        crack.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(2000, 0, Anim.Ms(260)) { EasingFunction = Anim.Out });
        var glint = new Ellipse { Width = 160, Height = 160, Fill = new RadialGradientBrush(Color.FromArgb((byte)(220 * FxSettings.Intensity), 255, 255, 255), Color.FromArgb(0, 255, 255, 255)) };
        AddScreenFx(glint, impact.X - 80, impact.Y - 80, 161);
        _ = FadeAndRemoveScreenAsync(glint, 500);

        // Drain: a cold grey wash settles over the table (a true greyscale would need a pixel shader).
        var wash = new Rectangle
        {
            Width = W,
            Height = H,
            Fill = new RadialGradientBrush(Color.FromArgb(150, 60, 62, 70), Color.FromArgb(225, 22, 22, 26)),
            Opacity = 0,
        };
        AddScreenFx(wash, 0, 0, 150);
        await Anim.To(wash, OpacityProperty, 1, 1600, Anim.InOut);
    }

    // =================================================================== D. the deciding Duel

    /// <summary>
    /// A Duel "decides the game" when someone is one round from victory and the Duel's losers change who is winning
    /// the round right now.
    /// </summary>
    private bool IsDecidingDuel(DuelEvent e)
    {
        if (e.Losers.Count == 0) return false;
        int need = State.Options.RoundsToWin;
        if (!State.Players.Any(p => p.RoundsWon == need - 1)) return false;
        var before = Scoring.Score(State);
        int t0 = before.Total0, t1 = before.Total1;
        foreach (var l in e.Losers)
        {
            if (l.Location.Node is not { IsScoring: true } n) continue;
            int rank = RankCalculator.GetRank(State, l);
            if (n.Side == 0) t0 -= rank; else t1 -= rank;
        }
        int? after = t0 == t1 ? null : t0 > t1 ? 0 : 1;
        if (after is int a && StaticLayer.RoundWinnerInverted(State)) after = GameState.Other(a);
        return after != before.Winner;
    }

    private readonly List<UIElement> _letterbox = new();

    /// <summary>Slow motion: cinematic bars slide in and the camera leans in on the Duel.</summary>
    private async Task SlowMoBeginAsync(Point boardMid)
    {
        Sfx.Play(SfxKind.SlowMo);
        double W = FxCanvas.ActualWidth, H = FxCanvas.ActualHeight;
        foreach (bool top in new[] { true, false })
        {
            var bar = new Rectangle { Width = W, Height = H * 0.09, Fill = Brushes.Black };
            var t = new TranslateTransform(0, top ? -H * 0.09 : H * 0.09);
            bar.RenderTransform = t;
            AddScreenFx(bar, 0, top ? 0 : H - H * 0.09, 240);
            _ = Anim.To(t, TranslateTransform.YProperty, 0, 400, Anim.Out);
            _letterbox.Add(bar);
        }
        EnsureCamera();
        var fx = ProjectToFx(boardMid);
        var vp = FxCanvas.TranslatePoint(fx, Viewport);
        _camScale!.CenterX = vp.X;
        _camScale.CenterY = vp.Y;
        double zoom = 1 + 0.18 * FxSettings.Intensity;
        _ = Anim.To(_camScale, ScaleTransform.ScaleXProperty, zoom, 600, Anim.InOut);
        _ = FloatTextAtAsync("DECIDING DUEL", new Point(W / 2, H * 0.16), Gold);
        await Anim.To(_camScale, ScaleTransform.ScaleYProperty, zoom, 600, Anim.InOut);
    }

    private async Task SlowMoEndAsync()
    {
        foreach (var bar in _letterbox) _ = FadeAndRemoveScreenAsync(bar, 400);
        _letterbox.Clear();
        if (_camScale is null) return;
        _ = Anim.To(_camScale, ScaleTransform.ScaleXProperty, 1, 500, Anim.InOut);
        await Anim.To(_camScale, ScaleTransform.ScaleYProperty, 1, 500, Anim.InOut);
    }
}
