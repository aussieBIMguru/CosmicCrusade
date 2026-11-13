using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Engine;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Net;

namespace CosmicCrusade.App.Views;

/// <summary>
/// The turn clock and the table taps.
/// <list type="bullet">
/// <item><b>Clock</b> (<see cref="TurnClock"/>): the limit comes from the setup screen (default 2 minutes; online, the
/// host's). Shown in the side panel while someone is deciding. In the last <see cref="FuseSeconds"/> seconds a fuse
/// burns along the control line towards the PASS button. When your time runs out the clock answers for you
/// (<see cref="HumanController.AnswerOnTimeout"/>): a turn passes, a React window says "no response", a choice takes
/// its default. Online each app only enforces its own player's clock; the opponent's fuse is drawn from when their
/// app is being waited on, and their app sends the timed-out decision like any other.</item>
/// <item><b>Taps</b>: clicking bare table that wouldn't do anything else sends a small ripple and a "tink" (local
/// only, the opponent doesn't see it).</item>
/// </list>
/// </summary>
public partial class GameView
{
    /// <summary>The fuse appears when this many seconds are left.</summary>
    private const double FuseSeconds = 15;

    // Along the control line (y = 500), burning left → right, towards the PASS button.
    private const double FuseLeft = 70;
    private const double FuseRight = 1530;

    private TurnClock _myClock = new(TimeSpan.Zero);
    private TurnClock _oppClock = new(TimeSpan.Zero);
    private DispatcherTimer? _clockTimer;

    private Canvas? _fuseLayer;
    private Rectangle? _fuseRope;
    private Rectangle? _fuseCharred;
    private Rectangle? _fuseHeat;
    private Grid? _fuseEmber;
    private ScaleTransform? _fuseEmberScale;
    private bool _fuseLit;
    private int _lastTickSecond = -1;
    private DateTime _nextFuseSmoke = DateTime.MinValue;

    private int _activeTaps;

    // =================================================================== setup

    /// <summary>Called from the constructor (after InitializeComponent).</summary>
    private void InitTurnClock()
    {
        var limit = TimeSpan.FromSeconds(Math.Max(0, _options.TurnSeconds));
        _myClock = new TurnClock(limit);
        _oppClock = new TurnClock(limit);

        // My clock runs while I'm being asked something.
        _human.DecisionRequested += _ => _myClock.BeginWait();
        _human.DecisionCompleted += () => _myClock.EndWait();
        _human.ChoiceRequested += _ => _myClock.BeginWait();
        _human.ChoiceCompleted += () => _myClock.EndWait();

        // Taps on the bare table (cards, slots and piles mark the clicks they use as handled).
        BoardCanvas.MouseLeftButtonDown += BoardCanvas_Tap;

        if (!_myClock.Enabled)
        {
            TurnClockPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TurnClockPanel.Visibility = Visibility.Hidden; // keeps its space so the panel doesn't jump
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => ClockTick();
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();
        _clockTimer = timer;
    }

    /// <summary>Online: the opponent's clock runs while we wait for their decision (called from CreateOnlineGame).</summary>
    private void WatchOpponentClock(RemoteSeat remote)
    {
        remote.WaitingChanged += waiting =>
        {
            if (waiting) _oppClock.BeginWait();
            else _oppClock.EndWait();
        };
    }

    /// <summary>Engine events that start and end turns (called first thing in PublishAsync).</summary>
    private void ClockOnEvent(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case TurnStartedEvent e:
                (e.Player == Me ? _myClock : _oppClock).StartTurn();
                (e.Player == Me ? _oppClock : _myClock).EndTurn();
                break;
            case PassedEvent or RoundScoredEvent or RoundEndedEvent:
                _myClock.EndTurn();
                _oppClock.EndTurn();
                break;
            case GameEndedEvent:
                _clockTimer?.Stop();
                if (_myClock.Enabled) TurnClockPanel.Visibility = Visibility.Hidden;
                PutOutFuse(burntOut: false);
                break;
        }
    }

    // =================================================================== every frame

    private void ClockTick()
    {
        bool tableBlocked = IsGameOver || _onlineEnded || OnlineWaitPanel.Visibility == Visibility.Visible;
        _myClock.SetPaused(tableBlocked);
        _oppClock.SetPaused(tableBlocked || (_online is not null && !_online.Net.OpponentConnected));

        // Whose clock to show: mine when I'm being asked; online, the opponent's while we wait for them.
        TurnClock? clock = _myClock.Waiting ? _myClock
            : _online is not null && _oppClock.Waiting ? _oppClock
            : null;

        if (clock is null)
        {
            TurnClockPanel.Visibility = Visibility.Hidden;
            PutOutFuse(burntOut: false);
            _lastTickSecond = -1;
            return;
        }

        bool mine = clock == _myClock;
        var left = clock.Remaining;
        double secs = left.TotalSeconds;

        TurnClockPanel.Visibility = Visibility.Visible;
        TurnClockLabel.Text = mine ? "YOUR CLOCK" : $"{OppName.ToUpperInvariant()}'S CLOCK";
        if (secs <= 10)
        {
            TurnClockText.Text = $"{secs:0.0}";
        }
        else
        {
            int whole = (int)Math.Ceiling(secs); // "2:00" for the whole first second
            TurnClockText.Text = $"{whole / 60}:{whole % 60:00}";
        }
        TurnClockText.Foreground = Solid(secs <= 5 ? Color.FromRgb(255, 80, 80) : secs <= FuseSeconds ? Ember : Colors.White);
        TurnClockPanel.Opacity = clock.Paused ? 0.5 : 1;

        // The fuse.
        if (secs <= FuseSeconds && !clock.Paused)
        {
            BurnFuse(secs, mine);
            int second = (int)Math.Ceiling(secs);
            if (second <= 5 && second != _lastTickSecond && second > 0)
                Sfx.Play(SfxKind.Tick, mine ? 0.9 : 0.5);
            _lastTickSecond = second;
        }
        else if (secs > FuseSeconds)
        {
            PutOutFuse(burntOut: false);
            _lastTickSecond = -1;
        }

        if (mine && _myClock.Expired) MyTimeIsUp();
    }

    private void MyTimeIsUp()
    {
        var answer = _human.AnswerOnTimeout();
        if (answer is null) return;
        PutOutFuse(burntOut: true);
        Sfx.Play(SfxKind.Gong, 0.55, minGapMs: 1500);
        _hud.AddLog(answer switch
        {
            TimeoutAnswer.Pass => "Time's up – you passed.",
            TimeoutAnswer.EndTurn => "Time's up – your turn ended.",
            TimeoutAnswer.NoResponse => "Time's up – no response.",
            _ => "Time's up – the first option was chosen for you.",
        });
        if (answer is TimeoutAnswer.Choice or TimeoutAnswer.NoResponse) _ = FloatTextAsync("TIME'S UP", Me);
    }

    // =================================================================== the fuse

    private void EnsureFuse()
    {
        if (_fuseLayer is not null && BoardCanvas.Children.Contains(_fuseLayer)) return;

        _fuseLayer = new Canvas { Width = BoardLayout.Width, Height = BoardLayout.Height, Visibility = Visibility.Collapsed };
        double y = BoardLayout.ControlLineY;

        // Burnt part: a charred, smoking line.
        _fuseCharred = new Rectangle
        {
            Height = 3,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Fill = new SolidColorBrush(Color.FromArgb(200, 40, 30, 28)),
        };
        Canvas.SetLeft(_fuseCharred, FuseLeft);
        Canvas.SetTop(_fuseCharred, y - 1.5);

        // Just-burnt glow behind the ember.
        _fuseHeat = new Rectangle
        {
            Height = 5,
            Width = 110,
            RadiusX = 2.5,
            RadiusY = 2.5,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 249, 115, 22), Color.FromArgb(230, 255, 170, 60), 0),
        };
        Canvas.SetTop(_fuseHeat, y - 2.5);

        // The unburnt fuse: a twisted cord (repeating diagonal stripes).
        var cord = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(7, 7),
            SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(214, 170, 110), 0),
                new GradientStop(Color.FromRgb(232, 196, 140), 0.35),
                new GradientStop(Color.FromRgb(120, 78, 40), 0.55),
                new GradientStop(Color.FromRgb(160, 110, 62), 0.85),
                new GradientStop(Color.FromRgb(214, 170, 110), 1),
            },
        };
        cord.Freeze();
        _fuseRope = new Rectangle
        {
            Height = 9,
            RadiusX = 4.5,
            RadiusY = 4.5,
            Fill = cord,
            Stroke = new SolidColorBrush(Color.FromArgb(160, 60, 36, 18)),
            StrokeThickness = 1,
            Effect = FxSettings.HighQuality ? new DropShadowEffect { Color = Color.FromRgb(255, 170, 60), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.45 } : null,
        };
        // The cord stays put and is clipped from the left as it burns, so its twist doesn't slide along.
        _fuseRope.Width = FuseRight - FuseLeft;
        Canvas.SetLeft(_fuseRope, FuseLeft);
        Canvas.SetTop(_fuseRope, y - 4.5);

        // The ember: a hot white core in an orange flame, flickering.
        _fuseEmberScale = new ScaleTransform(1, 1, 40, 40);
        _fuseEmber = new Grid { Width = 80, Height = 80, RenderTransform = _fuseEmberScale };
        _fuseEmber.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(150, 255, 150, 40), 0),
                    new GradientStop(Color.FromArgb(60, 249, 115, 22), 0.45),
                    new GradientStop(Color.FromArgb(0, 249, 115, 22), 1),
                }),
        });
        _fuseEmber.Children.Add(new Ellipse
        {
            Width = 26,
            Height = 26,
            Fill = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Color.FromRgb(255, 236, 140), 0.3),
                    new GradientStop(Color.FromRgb(255, 140, 30), 0.65),
                    new GradientStop(Color.FromArgb(0, 255, 90, 0), 1),
                }),
            Effect = FxSettings.HighQuality ? new DropShadowEffect { Color = Color.FromRgb(255, 150, 40), BlurRadius = 22, ShadowDepth = 0 } : null,
        });
        Canvas.SetTop(_fuseEmber, y - 40);

        _fuseLayer.Children.Add(_fuseCharred);
        _fuseLayer.Children.Add(_fuseHeat);
        _fuseLayer.Children.Add(_fuseRope);
        _fuseLayer.Children.Add(_fuseEmber);
        AddFx(_fuseLayer, 0, 0, z: 38); // above the control line, below flying cards and big effects
        _fuseLit = false;
    }

    /// <summary>Draws the fuse with <paramref name="secondsLeft"/> to go (≤ <see cref="FuseSeconds"/>).</summary>
    private void BurnFuse(double secondsLeft, bool mine)
    {
        EnsureFuse();
        double progress = Math.Clamp(1 - secondsLeft / FuseSeconds, 0, 1);
        double x = FuseLeft + progress * (FuseRight - FuseLeft);
        double y = BoardLayout.ControlLineY;

        if (!_fuseLit)
        {
            _fuseLit = true;
            _fuseLayer!.BeginAnimation(OpacityProperty, null);
            _fuseLayer.Opacity = 1;
            _fuseLayer.Visibility = Visibility.Visible;
            SparkBurst(new Point(x, y), Ember, 10, 40, 4);
            Sfx.Play(SfxKind.FuseHiss, mine ? 0.5 : 0.3);
        }

        _fuseCharred!.Width = Math.Max(0, x - FuseLeft);
        _fuseRope!.Clip = new RectangleGeometry(new Rect(x - FuseLeft, -20, Math.Max(0, FuseRight - x) + 20, 49));
        _fuseHeat!.Width = Math.Min(110, Math.Max(0, x - FuseLeft));
        Canvas.SetLeft(_fuseHeat, x - _fuseHeat.Width);
        Canvas.SetLeft(_fuseEmber!, x - 40);

        // Flicker, and faster/brighter in the last five seconds.
        double urgency = secondsLeft <= 5 ? 1.35 : 1;
        double s = urgency * Rand(0.8, 1.2);
        _fuseEmberScale!.ScaleX = s;
        _fuseEmberScale.ScaleY = s * Rand(0.85, 1.15);

        // Sparks spitting off the ember.
        int sparks = _fxRng.NextDouble() < (FxSettings.HighQuality ? 0.8 : 0.3) ? (secondsLeft <= 5 ? 2 : 1) : 0;
        for (int i = 0; i < sparks; i++) FuseSpark(x, y);

        // A wisp of smoke now and then, and the crackle under it.
        if (DateTime.UtcNow >= _nextFuseSmoke)
        {
            _nextFuseSmoke = DateTime.UtcNow.AddMilliseconds(FxSettings.HighQuality ? 260 : 700);
            FuseSmoke(x - 6, y);
            Sfx.Play(SfxKind.FuseHiss, mine ? 0.35 : 0.2, minGapMs: 760);
        }
    }

    private void FuseSpark(double x, double y)
    {
        double size = Rand(2.5, 4.5);
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = Solid(Color.FromRgb(255, (byte)Rand(190, 250), 120)),
        };
        var t = new TranslateTransform();
        dot.RenderTransform = t;
        AddFx(dot, x - size / 2, y - size / 2, 39);
        double a = Rand(0, Math.PI * 2), d = Rand(14, 46), ms = Rand(260, 520);
        // Mostly backwards and outwards, like a real fuse spitting.
        double dx = Math.Cos(a) * d - Rand(0, 18), dy = Math.Sin(a) * d;
        _ = Anim.To(t, TranslateTransform.XProperty, dx, ms, Anim.Out);
        _ = Anim.To(t, TranslateTransform.YProperty, dy, ms, Anim.Out);
        _ = FadeAndRemoveAsync(dot, ms);
    }

    private void FuseSmoke(double x, double y)
    {
        double size = Rand(14, 22);
        var puff = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new RadialGradientBrush(Color.FromArgb(90, 150, 140, 135), Color.FromArgb(0, 120, 110, 105)),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var scale = new ScaleTransform(0.6, 0.6);
        var move = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);
        puff.RenderTransform = group;
        AddFx(puff, x - size / 2, y - size / 2, 37);
        double ms = Rand(900, 1400);
        _ = Anim.To(scale, ScaleTransform.ScaleXProperty, Rand(1.8, 2.6), ms, Anim.Out);
        _ = Anim.To(scale, ScaleTransform.ScaleYProperty, Rand(1.8, 2.6), ms, Anim.Out);
        _ = Anim.To(move, TranslateTransform.XProperty, -Rand(10, 30), ms, Anim.Out);
        _ = Anim.To(move, TranslateTransform.YProperty, Rand(-26, 26), ms, Anim.Out);
        _ = FadeAndRemoveAsync(puff, ms);
    }

    /// <summary>
    /// Hides the fuse: snuffed out with a puff when the player answered in time, or a bang at the end when it burnt out.
    /// </summary>
    private void PutOutFuse(bool burntOut)
    {
        if (!_fuseLit || _fuseLayer is null) return;
        _fuseLit = false;
        double x = Canvas.GetLeft(_fuseEmber!) + 40, y = BoardLayout.ControlLineY;
        if (burntOut)
        {
            SparkBurst(new Point(FuseRight, y), Ember, 18, 90, 6);
            SparkBurst(new Point(FuseRight, y), Colors.White, 8, 50, 4);
        }
        else
        {
            for (int i = 0; i < FxSettings.Count(3); i++) FuseSmoke(x + Rand(-8, 8), y);
        }
        var layer = _fuseLayer;
        var fade = new DoubleAnimation(0, Anim.Ms(burntOut ? 450 : 220));
        fade.Completed += (_, _) =>
        {
            if (!_fuseLit) layer.Visibility = Visibility.Collapsed;
        };
        layer.BeginAnimation(OpacityProperty, fade);
    }

    // =================================================================== table taps

    /// <summary>
    /// A click on the board nobody used: a small ripple where it landed (blue on your side, red on the opponent's)
    /// and a soft "tink". Cards and piles are left alone, even when clicking them does nothing right now.
    /// </summary>
    private void BoardCanvas_Tap(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || _state is null || _activeTaps >= 6) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != BoardCanvas; d = ParentOf(d))
            if (d is CardView or PileView) return;

        var p = e.GetPosition(BoardCanvas);
        if (p.X < 0 || p.Y < 0 || p.X > BoardLayout.Width || p.Y > BoardLayout.Height) return;
        _ = TapRippleAsync(p);
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private async Task TapRippleAsync(Point p)
    {
        _activeTaps++;
        try
        {
            var color = p.Y >= BoardLayout.ControlLineY ? PlayerBolt : OpponentBolt;
            Sfx.Play(SfxKind.TableTap, 0.45, minGapMs: 60);

            // The grid lights up a little around the tap.
            var glow = new Ellipse
            {
                Width = 240,
                Height = 240,
                Fill = new RadialGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
            };
            AddFx(glow, p.X - 120, p.Y - 120, 36);
            _ = FadeAndRemoveAsync(glow, 520);

            // A bright point...
            var core = new Ellipse
            {
                Width = 26,
                Height = 26,
                Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, color.R, color.G, color.B)),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.4, 0.4),
            };
            AddFx(core, p.X - 13, p.Y - 13, 41);
            var cs = (ScaleTransform)core.RenderTransform;
            _ = Anim.To(cs, ScaleTransform.ScaleXProperty, 1.3, 240, Anim.Out);
            _ = Anim.To(cs, ScaleTransform.ScaleYProperty, 1.3, 240, Anim.Out);
            _ = FadeAndRemoveAsync(core, 280);

            // ...two rings rolling out...
            var first = Ring(p, color, 2.5, 4.2, 520, 0);
            var second = Ring(p, color, 1.5, 2.8, 460, 90);

            // ...and a few motes.
            SparkBurst(p, color, 6, 34, 4);

            await Task.WhenAll(first, second);
        }
        finally
        {
            _activeTaps--;
        }
    }

    private async Task Ring(Point p, Color color, double thickness, double grow, double ms, double delayMs)
    {
        var ring = new Ellipse
        {
            Width = 24,
            Height = 24,
            Stroke = Solid(Lighten(color, 40), 230),
            StrokeThickness = thickness,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.3, 0.3),
            Effect = FxSettings.HighQuality ? new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0 } : null,
        };
        AddFx(ring, p.X - 12, p.Y - 12, 40);
        var s = (ScaleTransform)ring.RenderTransform;
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, grow, ms, Anim.Out, delayMs: delayMs);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, grow, ms, Anim.Out, delayMs: delayMs);
        await FadeAndRemoveAsync(ring, ms, delayMs);
    }
}
