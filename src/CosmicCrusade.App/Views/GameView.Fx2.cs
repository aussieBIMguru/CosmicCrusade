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
/// Second effects batch: the ambient layer (active Warzones and environmental Ongoings), Warzone moments (Black Hole,
/// Wormhole, Dimensional Rift), combat (Shadow Strike, Target Practice, Second Thought, High Noon, Xeno Laser, Respond
/// in Kind), control (Commandeer, tractor beams, Psychic Coercion, Shoot First, Recolonize), race landings, Leader
/// arrivals, copies, Ace's grid, instant wins, the Clean-up sweep and the close-round heartbeat.
/// </summary>
public partial class GameView
{
    private static readonly Color Chrome = Color.FromRgb(226, 232, 240);
    private static readonly Color Sunset = Color.FromRgb(251, 146, 60);
    private static readonly Color Dust = Color.FromRgb(180, 140, 96);

    // =================================================================== screen-space plumbing (FxCanvas)

    private T AddScreenFx<T>(T element, double x, double y, int z = 200) where T : UIElement
    {
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        Panel.SetZIndex(element, z);
        FxCanvas.Children.Add(element);
        return element;
    }

    private async Task FadeAndRemoveScreenAsync(UIElement element, double ms, double delayMs = 0)
    {
        await Anim.To(element, OpacityProperty, 0, ms, Anim.In, delayMs: delayMs);
        FxCanvas.Children.Remove(element);
    }

    private static Point Mid(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    // =================================================================== ambient layer

    /// <summary>What's currently showing for each active Warzone / Ongoing, by card instance: how to stop it.</summary>
    private readonly Dictionary<int, Action> _ambient = new();

    /// <summary>
    /// Called after every event: starts the ambient effect of every Warzone or Ongoing that just became active, stops
    /// the ones that left, and refreshes the per-card shield (can't be Nullified / Immune) and goo (Suppressed) overlays.
    /// </summary>
    private void SyncAmbient()
    {
        if (_state is null) return;
        var active = State.ActiveWarzones
            .Concat(State.Players.SelectMany(p => p.OrderNodes).Select(n => n.Card).OfType<CardInstance>())
            .ToList();
        var ids = active.Select(c => c.InstanceId).ToHashSet();
        foreach (var id in _ambient.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _ambient[id]();
            _ambient.Remove(id);
        }
        int warzoneIndex = 0;
        foreach (var card in active)
        {
            bool isWarzone = card.Definition.Kind == CardKind.Warzone;
            if (!_ambient.ContainsKey(card.InstanceId) && StartAmbient(card, isWarzone ? warzoneIndex : -1) is { } stop)
                _ambient[card.InstanceId] = stop;
            if (isWarzone) warzoneIndex++;
        }

        foreach (var v in _boardCards.Values)
        {
            if (v.Card is { } c && c.Location.Node is { IsScoring: true })
            {
                v.SetShield(StaticLayer.IsImmune(State, c) || StaticLayer.PreventsNullify(State, c, null, out _));
                v.SetGoo(StaticLayer.IsSuppressed(State, c));
            }
            else
            {
                v.SetShield(false);
                v.SetGoo(false);
            }
        }
        UpdateTension();
        SyncIdle(); // effects pass 3: Standby dormancy, idle bob, frost, Ongoing sigils, Academy chevrons
    }

    private void StopAllAmbient()
    {
        foreach (var stop in _ambient.Values) stop();
        _ambient.Clear();
    }

    /// <summary>Starts a card's ambient effect; returns how to stop it (null = it has none).</summary>
    private Action? StartAmbient(CardInstance card, int warzoneIndex) => card.Definition.Name switch
    {
        "Black Hole" => AmbientBlackHole(),
        "Uncharted Space" => AmbientNebula(),
        "Atmosphere Generator" => AmbientDome(),
        "Acid Rain" => AmbientAcidRain(),
        "Stagnation" => AmbientFog(),
        "Polarity Storm" => AmbientLightning(),
        _ when StartAmbient3(card) is { } pass3 => pass3, // effects pass 3: every other Warzone
        _ when warzoneIndex >= 0 => AmbientWarzoneAura(card, warzoneIndex),
        _ => null,
    };

    /// <summary>Fades out and removes a set of elements, stops their looping animations, and stops timers.</summary>
    private Action StopWith(IEnumerable<UIElement> elements, params DispatcherTimer[] timers) => () =>
    {
        foreach (var t in timers) t.Stop();
        foreach (var e in elements.ToList())
            _ = Anim.To(e, OpacityProperty, 0, 500, Anim.In).ContinueWith(_ =>
            {
                BoardCanvas.Children.Remove(e);
                StopAnimationsDeep(e);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    };

    /// <summary>
    /// Stops every animation on an element, its transforms and its children. A Forever animation does NOT die with its
    /// element: WPF keeps ticking it after the element is removed, so long games piled them up and slowed down.
    /// </summary>
    private static void StopAnimationsDeep(DependencyObject root)
    {
        if (root is UIElement el)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            StopTransformAnimations(el.RenderTransform);
            if (el is Shape shape) shape.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        }
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++) StopAnimationsDeep(VisualTreeHelper.GetChild(root, i));
    }

    private static void StopTransformAnimations(Transform? t)
    {
        if (t is null || t.IsFrozen) return;
        switch (t)
        {
            case TransformGroup g:
                foreach (var c in g.Children) StopTransformAnimations(c);
                break;
            case TranslateTransform tt:
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                break;
            case ScaleTransform s:
                s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                break;
            case RotateTransform r:
                r.BeginAnimation(RotateTransform.AngleProperty, null);
                break;
            case SkewTransform k:
                k.BeginAnimation(SkewTransform.AngleXProperty, null);
                k.BeginAnimation(SkewTransform.AngleYProperty, null);
                break;
        }
    }

    private static DoubleAnimation Loop(double from, double to, double seconds, bool reverse = false, double beginSeconds = 0) => new(from, to,
        TimeSpan.FromSeconds(seconds))
    {
        RepeatBehavior = RepeatBehavior.Forever,
        AutoReverse = reverse,
        BeginTime = TimeSpan.FromSeconds(beginSeconds),
        EasingFunction = reverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null,
    };

    /// <summary>Any Warzone without its own effect: a breathing glow round the Warzone slot in its race colour.</summary>
    private Action AmbientWarzoneAura(CardInstance card, int index)
    {
        var accent = CardLayout.Instance.Accent(card.Definition);
        var c = BoardLayout.PileCenter(PileKind.ActiveWarzone);
        double pad = 16 + index * 12;
        var ring = new Rectangle
        {
            Width = BoardLayout.SlotW + pad * 2,
            Height = BoardLayout.SlotH + pad * 2,
            RadiusX = 18,
            RadiusY = 18,
            Stroke = Solid(accent, 200),
            StrokeThickness = 4,
            Effect = new DropShadowEffect { Color = accent, BlurRadius = 22, ShadowDepth = 0 },
        };
        AddFx(ring, c.X - ring.Width / 2, c.Y - ring.Height / 2, 3);
        ring.BeginAnimation(OpacityProperty, Loop(0.25, 0.9, 1.8, reverse: true, beginSeconds: index * 0.4));
        return StopWith(new[] { ring });
    }

    /// <summary>Black Hole: a slowly turning vortex behind the Warzone slot.</summary>
    private Action AmbientBlackHole()
    {
        Sfx.Play(SfxKind.Vortex, 0.6);
        var c = BoardLayout.PileCenter(PileKind.ActiveWarzone);
        const double size = 380;
        var host = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        var spin = new RotateTransform(0);
        host.RenderTransform = spin;

        // Spiral arms.
        for (int arm = 0; arm < 4; arm++)
        {
            var pts = new PointCollection();
            for (int k = 0; k <= 40; k++)
            {
                double f = k / 40.0;
                double r = 30 + f * (size / 2 - 30);
                double a = arm * Math.PI / 2 + f * Math.PI * 1.4;
                pts.Add(new Point(size / 2 + Math.Cos(a) * r, size / 2 + Math.Sin(a) * r));
            }
            host.Children.Add(new Polyline
            {
                Points = pts,
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                Stroke = new LinearGradientBrush(Color.FromArgb(220, 192, 132, 252), Color.FromArgb(0, 60, 20, 120), 0),
                Effect = new BlurEffect { Radius = 6 },
            });
        }
        // Accretion ring and the dark core.
        var ringEl = new Ellipse
        {
            Width = 120,
            Height = 120,
            Stroke = Solid(Ember, 220),
            StrokeThickness = 5,
            Effect = new DropShadowEffect { Color = Ember, BlurRadius = 24, ShadowDepth = 0 },
        };
        Canvas.SetLeft(ringEl, size / 2 - 60);
        Canvas.SetTop(ringEl, size / 2 - 60);
        host.Children.Add(ringEl);
        var core = new Ellipse
        {
            Width = 104,
            Height = 104,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Colors.Black, 0),
                new(Colors.Black, 0.7),
                new(Color.FromArgb(0, 0, 0, 0), 1),
            }),
        };
        Canvas.SetLeft(core, size / 2 - 52);
        Canvas.SetTop(core, size / 2 - 52);
        host.Children.Add(core);

        AddFx(host, c.X - size / 2, c.Y - size / 2, 1);
        host.Opacity = 0;
        _ = Anim.To(host, OpacityProperty, 0.85, 900, Anim.Out);
        spin.BeginAnimation(RotateTransform.AngleProperty, Loop(0, -360, 7));
        ringEl.BeginAnimation(OpacityProperty, Loop(0.5, 1, 1.2, reverse: true));
        return StopWith(new UIElement[] { host });
    }

    /// <summary>Uncharted Space: soft nebulae drift across the table.</summary>
    private Action AmbientNebula()
    {
        var colors = new[] { Color.FromRgb(147, 51, 234), Color.FromRgb(56, 189, 248), Color.FromRgb(236, 72, 153), Color.FromRgb(34, 197, 94) };
        var els = new List<UIElement>();
        for (int i = 0; i < FxSettings.Count(5); i++)
        {
            double w = Rand(420, 680), h = w * Rand(0.5, 0.8);
            var col = colors[i % colors.Length];
            var cloud = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = new RadialGradientBrush(Color.FromArgb(70, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B)),
                Effect = new BlurEffect { Radius = 30 },
            };
            var t = new TranslateTransform();
            cloud.RenderTransform = t;
            AddFx(cloud, Rand(-200, BoardLayout.Width - 200), Rand(-100, BoardLayout.Height - 300), 1);
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(80, 220), Rand(80, 220), Rand(16, 28), reverse: true));
            t.BeginAnimation(TranslateTransform.YProperty, Loop(-Rand(30, 80), Rand(30, 80), Rand(12, 20), reverse: true));
            cloud.Opacity = 0;
            _ = Anim.To(cloud, OpacityProperty, 1, 1500, Anim.Out);
            els.Add(cloud);
        }
        return StopWith(els);
    }

    /// <summary>Atmosphere Generator: a faint dome over the board with heat shimmer rising through it.</summary>
    private Action AmbientDome()
    {
        var dome = new Ellipse
        {
            Width = BoardLayout.Width + 200,
            Height = BoardLayout.Height + 160,
            Stroke = Solid(Cyan, 70),
            StrokeThickness = 6,
            Fill = new RadialGradientBrush(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), Color.FromArgb(30, Cyan.R, Cyan.G, Cyan.B)),
            Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 30, ShadowDepth = 0 },
        };
        AddFx(dome, -100, -80, 33);
        var shimmer = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = 140,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 255, 255, 255), 0),
                new(Color.FromArgb(22, 255, 250, 230), 0.5),
                new(Color.FromArgb(0, 255, 255, 255), 1),
            }, 90),
        };
        var t = new TranslateTransform();
        shimmer.RenderTransform = t;
        AddFx(shimmer, 0, 0, 33);
        t.BeginAnimation(TranslateTransform.YProperty, Loop(BoardLayout.Height, -140, 4.5));
        dome.Opacity = 0;
        _ = Anim.To(dome, OpacityProperty, 1, 1000, Anim.Out);
        return StopWith(new UIElement[] { dome, shimmer });
    }

    /// <summary>Acid Rain: green drizzle falling diagonally over the board.</summary>
    private Action AmbientAcidRain()
    {
        var els = new List<UIElement>();
        for (int i = 0; i < FxSettings.Count(70); i++)
        {
            var drop = new Line
            {
                X1 = 0, Y1 = 0, X2 = 12, Y2 = 38,
                Stroke = Solid(Color.FromRgb(163, 230, 53), (byte)Rand(70, 140)),
                StrokeThickness = Rand(1.5, 2.5),
                StrokeEndLineCap = PenLineCap.Round,
            };
            var t = new TranslateTransform();
            drop.RenderTransform = t;
            double x0 = Rand(-300, BoardLayout.Width);
            AddFx(drop, x0, 0, 36);
            double secs = Rand(0.9, 1.5), begin = Rand(0, 1.5);
            t.BeginAnimation(TranslateTransform.YProperty, Loop(-80, BoardLayout.Height + 40, secs, beginSeconds: begin));
            t.BeginAnimation(TranslateTransform.XProperty, Loop(0, 330, secs, beginSeconds: begin));
            els.Add(drop);
        }
        return StopWith(els);
    }

    /// <summary>Stagnation: murky fog rolls in, and every Unit's race reads "Blaarg" for a moment.</summary>
    private Action AmbientFog()
    {
        var els = new List<UIElement>();
        for (int i = 0; i < FxSettings.Count(7); i++)
        {
            double w = Rand(500, 800);
            var fog = new Ellipse
            {
                Width = w,
                Height = w * 0.45,
                Fill = new RadialGradientBrush(Color.FromArgb(80, 110, 140, 70), Color.FromArgb(0, 80, 110, 50)),
                Effect = new BlurEffect { Radius = 35 },
            };
            var t = new TranslateTransform();
            fog.RenderTransform = t;
            AddFx(fog, Rand(-300, BoardLayout.Width - 300), Rand(0, BoardLayout.Height - 200), 34);
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(100, 200), Rand(100, 200), Rand(10, 18), reverse: true));
            fog.Opacity = 0;
            _ = Anim.To(fog, OpacityProperty, 1, 1500, Anim.Out);
            els.Add(fog);
        }
        Sfx.Play(SfxKind.Squelch, 0.7);
        foreach (var v in _boardCards.Values) GlowBriefly(v, SlimeLight, 1200);
        _ = FloatTextAtAsync("ALL BECOME BLAARG", ProjectToFx(new Point(BoardLayout.Width / 2 - 100, BoardLayout.ControlLineY)), SlimeLight);
        return StopWith(els);
    }

    /// <summary>Polarity Storm: Rank badges flip, then lightning crackles between Units every few seconds.</summary>
    private Action AmbientLightning()
    {
        Sfx.Play(SfxKind.Thunder, 0.7);
        foreach (var v in _boardCards.Values) v.FlipRankBadge();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(FxSettings.ReduceEffects ? 7 : 2.8) };
        timer.Tick += (_, _) => LightningBolt();
        timer.Start();
        LightningBolt();
        return StopWith(Array.Empty<UIElement>(), timer);
    }

    private void LightningBolt()
    {
        if (_state is null) return;
        var views = _boardCards.Values.Where(v => v.Card?.Location.Node is { IsScoring: true }).ToList();
        Point a, b;
        if (views.Count >= 2)
        {
            int i = _fxRng.Next(views.Count), j = (i + 1 + _fxRng.Next(views.Count - 1)) % views.Count;
            a = BoardCenterOf(views[i]);
            b = BoardCenterOf(views[j]);
        }
        else
        {
            a = new Point(Rand(300, 1100), Rand(100, 400));
            b = new Point(a.X + Rand(-300, 300), a.Y + Rand(250, 500));
        }

        var pts = new PointCollection { a };
        int segments = 10;
        for (int k = 1; k < segments; k++)
        {
            double f = k / (double)segments;
            double nx = -(b.Y - a.Y), ny = b.X - a.X;
            double len = Math.Max(1, Math.Sqrt(nx * nx + ny * ny));
            double off = Rand(-40, 40);
            pts.Add(new Point(a.X + (b.X - a.X) * f + nx / len * off, a.Y + (b.Y - a.Y) * f + ny / len * off));
        }
        pts.Add(b);
        var bolt = new Polyline
        {
            Points = pts,
            Stroke = Brushes.White,
            StrokeThickness = FxSettings.ReduceEffects ? 2 : 3.5,
            StrokeLineJoin = PenLineJoin.Bevel,
            Effect = new DropShadowEffect { Color = Color.FromRgb(147, 197, 253), BlurRadius = 20, ShadowDepth = 0 },
        };
        AddFx(bolt, 0, 0, 37);
        SparkBurst(b, Color.FromRgb(147, 197, 253), 6, 30, 5);
        Sfx.Play(SfxKind.Thunder, 0.3, minGapMs: 1000);
        var flicker = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(320) };
        foreach (var (v, at) in FxSettings.ReduceEffects
                     ? new[] { (0.45, 0.0), (0.45, 0.4), (0.0, 1.0) } // no strobe when reduced
                     : new[] { (1.0, 0.0), (0.2, 0.25), (1.0, 0.4), (0.0, 1.0) })
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromPercent(at)));
        flicker.Completed += (_, _) => RemoveFx(bolt);
        bolt.BeginAnimation(OpacityProperty, flicker);
    }

    // =================================================================== Warzone moments

    /// <summary>Black Hole Nullifies a Unit: it is stretched, spun and sucked into the vortex.</summary>
    private async Task BlackHoleSuckAsync(CardView v)
    {
        Sfx.Play(SfxKind.Vortex);
        v.HideRank();
        v.SetGlow(Purple);
        var target = BoardLayout.PileCenter(PileKind.ActiveWarzone);
        var start = BoardCenterOf(v);
        const double ms = 900;

        // Light streaks along the way.
        for (int i = 1; i <= 5; i++)
        {
            double f = i / 6.0;
            var streak = new Ellipse
            {
                Width = 70,
                Height = 10,
                Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 192, 132, 252)),
                RenderTransform = new RotateTransform(Math.Atan2(target.Y - start.Y, target.X - start.X) * 180 / Math.PI, 35, 5),
                Opacity = 0,
            };
            AddFx(streak, start.X + (target.X - start.X) * f - 35, start.Y + (target.Y - start.Y) * f - 5, 44);
            _ = Anim.To(streak, OpacityProperty, 0.9, 120, null, delayMs: ms * f * 0.8)
                .ContinueWith(_ => FadeAndRemoveAsync(streak, 300), TaskScheduler.FromCurrentSynchronizationContext());
        }

        Panel.SetZIndex(v, 45);
        var sx = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.45, KeyTime.FromPercent(0.4), new SineEase()));
        sx.KeyFrames.Add(new EasingDoubleKeyFrame(0.02, KeyTime.FromPercent(1), Anim.In));
        var sy = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
        sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.7, KeyTime.FromPercent(0.4), new SineEase()));
        sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.02, KeyTime.FromPercent(1), Anim.In));
        v.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        v.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        _ = Anim.To(v.Rotate, RotateTransform.AngleProperty, 720, ms, Anim.In);
        _ = Anim.To(v, Canvas.LeftProperty, target.X - v.Width / 2, ms, Anim.In);
        await Anim.To(v, Canvas.TopProperty, target.Y - v.Height / 2, ms, Anim.In);

        RemoveFx(v);
        var flash = new Ellipse { Width = 140, Height = 140, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 192, 132, 252)),
            Opacity = FxSettings.Flash(1) };
        AddFx(flash, target.X - 70, target.Y - 70, 46);
        _ = FadeAndRemoveAsync(flash, 400);
        _ = FloatTextAtAsync("CONSUMED", ProjectToFx(target), Purple);
    }

    private FrameworkElement? _portal;
    private FrameworkElement? _rift;

    /// <summary>Wormhole: a portal opens over the Warzone slot while the issuer picks; it closes once the new one is in.</summary>
    private void OpenPortal()
    {
        if (_portal is not null) return;
        Sfx.Play(SfxKind.Portal);
        var c = BoardLayout.PileCenter(PileKind.ActiveWarzone);
        const double size = 300;
        var host = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        var swirl = new Ellipse
        {
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(230, 20, 0, 40), 0),
                new(Color.FromArgb(160, 147, 51, 234), 0.75),
                new(Color.FromArgb(0, 147, 51, 234), 1),
            }),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var ring = new Ellipse
        {
            Stroke = new LinearGradientBrush(Color.FromRgb(216, 180, 254), Color.FromRgb(56, 189, 248), 45),
            StrokeThickness = 14,
            StrokeDashArray = new DoubleCollection { 3, 1.5 },
            Effect = new DropShadowEffect { Color = Purple, BlurRadius = 30, ShadowDepth = 0 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        host.Children.Add(swirl);
        host.Children.Add(ring);
        var scale = new ScaleTransform(0.05, 0.05);
        host.RenderTransform = scale;
        var spin = new RotateTransform();
        ring.RenderTransform = spin;
        spin.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 2.2));
        AddFx(host, c.X - size / 2, c.Y - size / 2, 42);
        _ = Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 500, Anim.Back);
        _ = Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 500, Anim.Back);
        _portal = host;
    }

    private async Task ClosePortalAsync()
    {
        if (_portal is not { } p) return;
        _portal = null;
        if (p.RenderTransform is ScaleTransform s)
        {
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 0.02, 380, Anim.In);
            await Anim.To(s, ScaleTransform.ScaleYProperty, 0.02, 380, Anim.In);
        }
        RemoveFx(p);
    }

    /// <summary>Dimensional Rift: a jagged tear rips open beside the Warzones; the new ones fall out of it.</summary>
    private void OpenRift()
    {
        if (_rift is not null) return;
        Sfx.Play(SfxKind.Rift);
        var c = BoardLayout.PileCenter(PileKind.ActiveWarzone);
        var pts = new PointCollection();
        for (int k = 0; k <= 12; k++)
        {
            double f = k / 12.0;
            pts.Add(new Point(c.X - 260 + f * 520 + Rand(-10, 10), c.Y - 60 + f * 150 + (k % 2 == 0 ? -18 : 18)));
        }
        var host = new Canvas();
        var glow = new Polyline { Points = pts, Stroke = Solid(Color.FromRgb(216, 180, 254)), StrokeThickness = 0, StrokeLineJoin = PenLineJoin.Miter,
            Effect = new DropShadowEffect { Color = Purple, BlurRadius = 34, ShadowDepth = 0 } };
        var core = new Polyline { Points = pts, Stroke = Brushes.White, StrokeThickness = 0, StrokeLineJoin = PenLineJoin.Miter };
        host.Children.Add(glow);
        host.Children.Add(core);
        AddFx(host, 0, 0, 43);
        glow.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(0, 22, Anim.Ms(420)) { EasingFunction = Anim.Out });
        core.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(0, 6, Anim.Ms(420)) { EasingFunction = Anim.Out });
        foreach (var p in pts.Where((_, i) => i % 3 == 0)) SparkBurst(p, Purple, 5, 40, 5);
        _rift = host;
    }

    private async Task CloseRiftAsync()
    {
        if (_rift is not Canvas host) return;
        _rift = null;
        foreach (var line in host.Children.OfType<Polyline>())
            line.BeginAnimation(Shape.StrokeThicknessProperty, new DoubleAnimation(0, Anim.Ms(380)) { EasingFunction = Anim.In });
        await Anim.Delay(400);
        RemoveFx(host);
    }

    // =================================================================== combat

    /// <summary>Shadow Strike: a dark crescent slash across the deploying card.</summary>
    private async Task SlashAsync(Rect r)
    {
        Sfx.Play(SfxKind.Slash);
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(r.Left - 30, r.Bottom + 10), IsClosed = true };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(r.Left + r.Width * 0.3, r.Top + r.Height * 0.3), new Point(r.Right + 30, r.Top - 10), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(r.Left + r.Width * 0.45, r.Top + r.Height * 0.45), new Point(r.Left - 30, r.Bottom + 10), true));
        geo.Figures.Add(fig);
        var slash = new Path
        {
            Data = geo,
            Fill = new LinearGradientBrush(Color.FromRgb(20, 0, 40), Color.FromRgb(192, 132, 252), 0),
            Effect = new DropShadowEffect { Color = Color.FromRgb(88, 28, 135), BlurRadius = 24, ShadowDepth = 0 },
            Opacity = 0,
        };
        AddScreenFx(slash, 0, 0);
        await Anim.To(slash, OpacityProperty, 1, 90, Anim.Out);
        _ = FadeAndRemoveScreenAsync(slash, 350, 120);
    }

    /// <summary>Target Practice: crosshairs lock on (two beeps), then one shot.</summary>
    private async Task CrosshairAsync(Rect r)
    {
        Sfx.Play(SfxKind.LockOn);
        var c = Mid(r);
        const double size = 160;
        var host = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        var red = Solid(Red);
        host.Children.Add(new Ellipse { Width = size, Height = size, Stroke = red, StrokeThickness = 3 });
        var inner = new Ellipse { Width = 50, Height = 50, Stroke = red, StrokeThickness = 2 };
        Canvas.SetLeft(inner, 55);
        Canvas.SetTop(inner, 55);
        host.Children.Add(inner);
        foreach (var (x1, y1, x2, y2) in new[] { (0.0, 80.0, 60.0, 80.0), (100.0, 80.0, 160.0, 80.0), (80.0, 0.0, 80.0, 60.0), (80.0, 100.0, 80.0, 160.0) })
            host.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = red, StrokeThickness = 3 });
        host.Effect = new DropShadowEffect { Color = Red, BlurRadius = 12, ShadowDepth = 0 };
        var s = new ScaleTransform(2.4, 2.4);
        var rot = new RotateTransform(90);
        var g = new TransformGroup();
        g.Children.Add(rot);
        g.Children.Add(s);
        host.RenderTransform = g;
        AddScreenFx(host, c.X - size / 2, c.Y - size / 2);
        _ = Anim.To(rot, RotateTransform.AngleProperty, 0, 420, Anim.Out);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 420, Anim.Out);
        await Anim.To(s, ScaleTransform.ScaleYProperty, 1, 420, Anim.Out);
        await Anim.Delay(120);
        Sfx.Play(SfxKind.Blaster);
        var flash = new Ellipse { Width = 120, Height = 120, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 255, 80, 80)),
            Opacity = FxSettings.Flash(1) };
        AddScreenFx(flash, c.X - 60, c.Y - 60);
        _ = FadeAndRemoveScreenAsync(flash, 220);
        _ = FadeAndRemoveScreenAsync(host, 250);
    }

    /// <summary>The card bursts into glass shards that tumble away (screen space).</summary>
    private async Task ShatterAsync(Rect r, Color tint)
    {
        Sfx.Play(SfxKind.Shatter);
        for (int i = 0; i < 22; i++)
        {
            double cx = r.X + Rand(0, r.Width), cy = r.Y + Rand(0, r.Height), s = Rand(10, 26);
            var shard = new Polygon
            {
                Points = new PointCollection { new(0, 0), new(s, Rand(0, s * 0.4)), new(Rand(0, s * 0.6), s) },
                Fill = new LinearGradientBrush(Color.FromArgb(230, 255, 255, 255), Color.FromArgb(200, tint.R, tint.G, tint.B), 45),
                Stroke = Solid(Colors.White, 200),
                StrokeThickness = 0.8,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var t = new TranslateTransform();
            var rot = new RotateTransform();
            var g = new TransformGroup();
            g.Children.Add(rot);
            g.Children.Add(t);
            shard.RenderTransform = g;
            AddScreenFx(shard, cx, cy);
            double ms = Rand(600, 950);
            double dx = (cx - Mid(r).X) * Rand(1.5, 3), up = -Rand(40, 120);
            var fall = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(up, KeyTime.FromPercent(0.3), Anim.Out));
            fall.KeyFrames.Add(new EasingDoubleKeyFrame(up + Rand(220, 340), KeyTime.FromPercent(1), Anim.In));
            t.BeginAnimation(TranslateTransform.YProperty, fall);
            _ = Anim.To(t, TranslateTransform.XProperty, dx, ms, Anim.Out);
            _ = Anim.To(rot, RotateTransform.AngleProperty, Rand(-540, 540), ms, null);
            _ = FadeAndRemoveScreenAsync(shard, ms * 0.5, ms * 0.5);
        }
        await Anim.Delay(420);
    }

    /// <summary>Second Thought: the card is rewound – cyan rings spin backwards round it.</summary>
    private async Task RewindAsync(Rect r)
    {
        Sfx.Play(SfxKind.Rewind);
        var c = Mid(r);
        for (int i = 0; i < 3; i++)
        {
            double d = r.Height * (0.7 + i * 0.25);
            var arc = new Ellipse
            {
                Width = d,
                Height = d,
                Stroke = Solid(Cyan),
                StrokeThickness = 4,
                StrokeDashArray = new DoubleCollection { 8, 4 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 14, ShadowDepth = 0 },
            };
            var rot = new RotateTransform();
            arc.RenderTransform = rot;
            AddScreenFx(arc, c.X - d / 2, c.Y - d / 2);
            _ = Anim.To(rot, RotateTransform.AngleProperty, -540, 700, Anim.InOut);
            _ = FadeAndRemoveScreenAsync(arc, 300, 450);
        }
        _ = FloatTextAtAsync("⟲ RECALLED", c, Cyan);
        await Anim.Delay(500);
    }

    private Rectangle? _sunsetLayer;

    /// <summary>High Noon: the light turns to sunset, the wind picks up and a tumbleweed rolls across the board.</summary>
    private async Task HighNoonAsync()
    {
        Sfx.Play(SfxKind.Wind);
        if (_sunsetLayer is null)
        {
            _sunsetLayer = new Rectangle
            {
                Width = FxCanvas.ActualWidth,
                Height = FxCanvas.ActualHeight,
                Fill = new LinearGradientBrush(Color.FromArgb(90, Sunset.R, Sunset.G, Sunset.B), Color.FromArgb(140, 127, 29, 29), 90),
                Opacity = 0,
            };
            AddScreenFx(_sunsetLayer, 0, 0, 5);
            _ = Anim.To(_sunsetLayer, OpacityProperty, 1, 700, Anim.Out);
        }

        // The tumbleweed: a scribbled ball rolling and bouncing along the control line.
        var weed = new Path { Data = TumbleweedGeometry(46), Stroke = Solid(Color.FromRgb(146, 104, 60)), StrokeThickness = 2.2, Width = 92, Height = 92,
            RenderTransformOrigin = new Point(0.5, 0.5) };
        var rot = new RotateTransform();
        var t = new TranslateTransform(-120, 0);
        var g = new TransformGroup();
        g.Children.Add(rot);
        g.Children.Add(t);
        weed.RenderTransform = g;
        AddFx(weed, 0, BoardLayout.ControlLineY - 70, 38);
        const double ms = 2300;
        _ = Anim.To(t, TranslateTransform.XProperty, BoardLayout.Width + 120, ms, null);
        _ = Anim.To(rot, RotateTransform.AngleProperty, 1080, ms, null);
        var bounce = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
        for (int i = 0; i <= 8; i++)
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(i % 2 == 0 ? 0 : -Rand(25, 55), KeyTime.FromPercent(i / 8.0), new SineEase()));
        t.BeginAnimation(TranslateTransform.YProperty, bounce);
        _ = FadeAndRemoveAsync(weed, 200, ms);
        _ = FloatTextAtAsync("HIGH NOON", ProjectToFx(new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY)), Sunset);
        await Anim.Delay(1300);
    }

    private void EndSunset()
    {
        if (_sunsetLayer is not { } layer) return;
        _sunsetLayer = null;
        _ = FadeAndRemoveScreenAsync(layer, 900);
    }

    private Geometry TumbleweedGeometry(double r)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int loop = 0; loop < 9; loop++)
            {
                double a0 = Rand(0, Math.PI * 2);
                ctx.BeginFigure(new Point(r + Math.Cos(a0) * r * 0.9, r + Math.Sin(a0) * r * 0.9), false, false);
                for (int k = 1; k <= 6; k++)
                {
                    double a = a0 + k * Rand(0.6, 1.3);
                    double rr = r * Rand(0.35, 0.95);
                    ctx.QuadraticBezierTo(new Point(r + Math.Cos(a - 0.4) * r, r + Math.Sin(a - 0.4) * r),
                        new Point(r + Math.Cos(a) * rr, r + Math.Sin(a) * rr), true, true);
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Xeno Laser: a charge at the Order, then a burning beam onto the Unit.</summary>
    private async Task LaserAsync(Point from, CardView target)
    {
        Sfx.Play(SfxKind.Laser);
        var to = BoardCenterOf(target);
        var green = Color.FromRgb(74, 222, 128);
        var charge = new Ellipse { Width = 20, Height = 20, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, green.R, green.G, green.B)) };
        var cs = new ScaleTransform(1, 1, 10, 10);
        charge.RenderTransform = cs;
        AddFx(charge, from.X - 10, from.Y - 10, 50);
        _ = Anim.To(cs, ScaleTransform.ScaleXProperty, 5, 400, Anim.In);
        await Anim.To(cs, ScaleTransform.ScaleYProperty, 5, 400, Anim.In);

        var beam = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = Solid(green), StrokeThickness = 16, StrokeEndLineCap = PenLineCap.Round,
            Effect = new DropShadowEffect { Color = green, BlurRadius = 30, ShadowDepth = 0 } };
        var core = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = Brushes.White, StrokeThickness = 5 };
        AddFx(beam, 0, 0, 50);
        AddFx(core, 0, 0, 51);
        for (int i = 0; i < 3; i++)
        {
            SparkBurst(to, green, 8, 50, 6);
            await Anim.Delay(130);
        }
        _ = FadeAndRemoveAsync(beam, 300);
        _ = FadeAndRemoveAsync(core, 250);
        _ = FadeAndRemoveAsync(charge, 250);
    }

    /// <summary>Respond in Kind: a mirror flash sweeps across the Unit as the blow is turned back.</summary>
    private async Task MirrorFlashAsync(CardView v)
    {
        Sfx.Play(SfxKind.Mirror);
        var r = BoardRectOf(v);
        var band = new Rectangle
        {
            Width = r.Width * 0.6,
            Height = r.Height * 1.4,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 255, 255, 255), 0),
                new(Color.FromArgb(230, 255, 255, 255), 0.5),
                new(Color.FromArgb(0, 255, 255, 255), 1),
            }, 0),
        };
        var t = new TranslateTransform(-r.Width, 0);
        var g = new TransformGroup();
        g.Children.Add(new RotateTransform(20));
        g.Children.Add(t);
        band.RenderTransform = g;
        AddFx(band, r.X, r.Y - r.Height * 0.2, 14);
        _ = FloatTextAtAsync("REFLECTED", ProjectToFx(Mid(r)), Chrome);
        await Anim.To(t, TranslateTransform.XProperty, r.Width * 1.4, 380, Anim.InOut);
        RemoveFx(band);
    }

    // =================================================================== control

    /// <summary>Commandeer: a grappling claw shoots from the new controller's side and yanks the Order across.</summary>
    private async Task ClawAsync(StackItem item, int newController)
    {
        if (!_stackEntries.TryGetValue(item.Id, out var entry) || !entry.Root.IsLoaded) return;
        Sfx.Play(SfxKind.Claw);
        var target = entry.Root.TranslatePoint(new Point(entry.Root.ActualWidth / 2, entry.Root.ActualHeight / 2), FxCanvas);
        var from = new Point(FxCanvas.ActualWidth * 0.45, newController == Me ? FxCanvas.ActualHeight + 20 : -20);
        var color = newController == Me ? PlayerBolt : OpponentBolt;
        var cable = new Line { X1 = from.X, Y1 = from.Y, X2 = from.X, Y2 = from.Y, Stroke = Solid(Chrome), StrokeThickness = 3,
            Effect = new DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0 } };
        AddScreenFx(cable, 0, 0);
        var head = new TextBlock { Text = "⋔", FontSize = 42, Foreground = Solid(Chrome), FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { Color = color, BlurRadius = 14, ShadowDepth = 0 } };
        var ht = new TranslateTransform(from.X - 12, from.Y - 28);
        head.RenderTransform = ht;
        AddScreenFx(head, 0, 0);

        _ = Anim.To(cable, Line.X2Property, target.X, 260, Anim.Out);
        _ = Anim.To(cable, Line.Y2Property, target.Y, 260, Anim.Out);
        _ = Anim.To(ht, TranslateTransform.XProperty, target.X - 12, 260, Anim.Out);
        await Anim.To(ht, TranslateTransform.YProperty, target.Y - 28, 260, Anim.Out);
        var grab = new Ellipse { Width = 70, Height = 70, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, color.R, color.G, color.B)) };
        AddScreenFx(grab, target.X - 35, target.Y - 35);
        _ = FadeAndRemoveScreenAsync(grab, 300);
        _ = Anim.To(entry.Root, OpacityProperty, 1, 200, null, from: 0.4);
        _ = Anim.To(cable, Line.X2Property, from.X, 300, Anim.In, delayMs: 120);
        _ = Anim.To(cable, Line.Y2Property, from.Y, 300, Anim.In, delayMs: 120);
        _ = Anim.To(ht, TranslateTransform.XProperty, from.X - 12, 300, Anim.In, delayMs: 120);
        await Anim.To(ht, TranslateTransform.YProperty, from.Y - 28, 300, Anim.In, delayMs: 120);
        FxCanvas.Children.Remove(cable);
        FxCanvas.Children.Remove(head);
    }

    /// <summary>One of Us / Arganium: a tractor beam pulls the Unit across the control line.</summary>
    private void TractorBeam(Point from, Point to, double ms)
    {
        Sfx.Play(SfxKind.Tractor);
        var beam = new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = Solid(Cyan, 70),
            StrokeThickness = 90,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Effect = new BlurEffect { Radius = 12 },
        };
        AddFx(beam, 0, 0, 9);
        beam.Opacity = 0;
        _ = Anim.To(beam, OpacityProperty, 1, 150, null);
        _ = FadeAndRemoveAsync(beam, 300, ms);
        for (int i = 0; i < 6; i++)
        {
            var ring = new Ellipse { Width = 80, Height = 80, Stroke = Solid(Cyan, 200), StrokeThickness = 3 };
            var t = new TranslateTransform(from.X - 40, from.Y - 40);
            ring.RenderTransform = t;
            AddFx(ring, 0, 0, 39);
            double delay = i * ms / 7;
            ring.Opacity = 0;
            _ = Anim.To(ring, OpacityProperty, 1, 100, null, delayMs: delay);
            _ = Anim.To(t, TranslateTransform.XProperty, to.X - 40, ms / 2, null, delayMs: delay);
            _ = Anim.To(t, TranslateTransform.YProperty, to.Y - 40, ms / 2, null, delayMs: delay);
            _ = FadeAndRemoveAsync(ring, 150, delay + ms / 2);
        }
    }

    /// <summary>Psychic Coercion: violet tendrils reach from the coercer's side to the node they picked.</summary>
    private void Tendrils(int coercer, Node node)
    {
        Sfx.Play(SfxKind.PsiHum);
        var to = BoardLayout.Center(node);
        var from = new Point(BoardLayout.Width / 2, coercer == Me ? BoardLayout.Height + 60 : -60);
        for (int i = 0; i < 3; i++)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(from.X + (i - 1) * 120, from.Y) };
            fig.Segments.Add(new BezierSegment(
                new Point(from.X + Rand(-300, 300), (from.Y + to.Y) / 2 + Rand(-120, 120)),
                new Point(to.X + Rand(-200, 200), to.Y + (coercer == Me ? 160 : -160)),
                new Point(to.X + (i - 1) * 25, to.Y), true));
            geo.Figures.Add(fig);
            var tendril = new Path
            {
                Data = geo,
                Stroke = Solid(PsiViolet, 210),
                StrokeThickness = 7 - i * 1.5,
                StrokeDashArray = new DoubleCollection { 400 },
                StrokeDashOffset = 400,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect { Color = PsiViolet, BlurRadius = 16, ShadowDepth = 0 },
            };
            AddFx(tendril, 0, 0, 39);
            _ = Anim.To(tendril, Shape.StrokeDashOffsetProperty, 0, 700, Anim.Out, delayMs: i * 90);
            _ = FadeAndRemoveAsync(tendril, 500, 1300);
        }
        _ = Anim.Delay(700).ContinueWith(_ => SparkBurst(to, PsiViolet, 10, 60, 6), TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Shoot First, Think Second: a roulette runs over the legal nodes and stops on the random one.</summary>
    private async Task RouletteAsync(IReadOnlyList<Node> candidates, Node chosen)
    {
        if (candidates.Count == 0) return;
        int steps = 10 + candidates.Count, start = _fxRng.Next(candidates.Count);
        int finalIndex = candidates.ToList().IndexOf(chosen);
        // Make the sequence land exactly on the chosen node.
        int offset = ((finalIndex - start - steps) % candidates.Count + candidates.Count) % candidates.Count;
        steps += offset;
        double delay = 45;
        for (int k = 0; k <= steps; k++)
        {
            var node = candidates[(start + k) % candidates.Count];
            var c = BoardLayout.Center(node);
            var box = new Rectangle
            {
                Width = BoardLayout.SlotW,
                Height = BoardLayout.SlotH,
                RadiusX = 12,
                RadiusY = 12,
                Stroke = Solid(Gold),
                StrokeThickness = k == steps ? 7 : 4,
                Fill = Solid(Gold, 40),
                Effect = new DropShadowEffect { Color = Gold, BlurRadius = 18, ShadowDepth = 0 },
            };
            AddFx(box, c.X - box.Width / 2, c.Y - box.Height / 2, 8);
            Sfx.Play(SfxKind.Tick, 0.6);
            if (k == steps)
            {
                Shockwave(c, Gold);
                _ = FadeAndRemoveAsync(box, 400, 300);
                break;
            }
            _ = FadeAndRemoveAsync(box, delay * 1.5, delay * 0.5);
            await Anim.Delay(delay);
            delay *= 1.12;
        }
        await Anim.Delay(250);
    }

    /// <summary>Recolonize: a thruster burst knocks the hovering card off course (screen space).</summary>
    private void ThrusterBurst(Point at, Vector away)
    {
        Sfx.Play(SfxKind.Thruster);
        if (away.Length > 0.001) away.Normalize();
        for (int i = 0; i < 16; i++)
        {
            var flame = new Ellipse
            {
                Width = Rand(10, 22),
                Height = Rand(10, 22),
                Fill = new RadialGradientBrush(Color.FromRgb(254, 240, 138), Color.FromArgb(0, Ember.R, Ember.G, Ember.B)),
            };
            var t = new TranslateTransform();
            flame.RenderTransform = t;
            AddScreenFx(flame, at.X, at.Y);
            double spread = Rand(-0.5, 0.5);
            var dir = new Vector(away.X * Math.Cos(spread) - away.Y * Math.Sin(spread), away.X * Math.Sin(spread) + away.Y * Math.Cos(spread));
            double d = Rand(60, 140), ms = Rand(250, 450);
            _ = Anim.To(t, TranslateTransform.XProperty, dir.X * d, ms, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, dir.Y * d, ms, Anim.Out);
            _ = FadeAndRemoveScreenAsync(flame, ms);
        }
    }

    // =================================================================== race landings and Leaders

    /// <summary>A landing flourish in the Unit's race style, on top of the usual shockwave.</summary>
    private void RaceLanding(CardInstance card, Point c)
    {
        var race = StaticLayer.Races(State, card).Cast<Race?>().FirstOrDefault() ?? card.Definition.Races.Cast<Race?>().FirstOrDefault();
        switch (race)
        {
            case Race.SpaceCowboy:
                Sfx.Play(SfxKind.Dust, 0.8);
                for (int i = 0; i < 12; i++) DustPuff(new Point(c.X + Rand(-70, 70), c.Y + BoardLayout.CardH / 2 - 10), i % 2 == 0 ? -1 : 1);
                break;
            case Race.Necrolite:
                Sfx.Play(SfxKind.Bones, 0.8);
                GroundCracks(c);
                break;
            case Race.Overmind:
                Sfx.Play(SfxKind.PsiHum, 0.7);
                for (int i = 0; i < 2; i++) PsiRing(c, i * 200);
                break;
            case Race.Blaarg:
                Sfx.Play(SfxKind.Squelch, 0.8);
                SparkBurst(c, SlimeLight, 12, 110, 12);
                break;
            case Race.Synthenoid:
                Sfx.Play(SfxKind.Servo, 0.8);
                SparkBurst(c, Gold, 14, 90, 5);
                for (int i = 0; i < 6; i++) Steam(new Point(c.X + Rand(-60, 60), c.Y + Rand(-20, 60)));
                break;
            case Race.GalacticCouncil:
                Sfx.Play(SfxKind.Holo, 0.8);
                HoloBeam(c);
                break;
        }
    }

    private void DustPuff(Point at, int dir)
    {
        double size = Rand(26, 48);
        var puff = new Ellipse { Width = size, Height = size * 0.7,
            Fill = new RadialGradientBrush(Color.FromArgb(170, Dust.R, Dust.G, Dust.B), Color.FromArgb(0, Dust.R, Dust.G, Dust.B)),
            Effect = new BlurEffect { Radius = 4 }, RenderTransformOrigin = new Point(0.5, 0.5) };
        var t = new TranslateTransform();
        var s = new ScaleTransform(0.5, 0.5);
        var g = new TransformGroup();
        g.Children.Add(s);
        g.Children.Add(t);
        puff.RenderTransform = g;
        AddFx(puff, at.X - size / 2, at.Y - size / 2, 9);
        double ms = Rand(600, 900);
        _ = Anim.To(t, TranslateTransform.XProperty, dir * Rand(50, 130), ms, Anim.Out);
        _ = Anim.To(t, TranslateTransform.YProperty, -Rand(5, 30), ms, Anim.Out);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1.6, ms, Anim.Out);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1.6, ms, Anim.Out);
        _ = FadeAndRemoveAsync(puff, ms);
    }

    private void GroundCracks(Point c)
    {
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI * 2 * i / 8 + Rand(-0.2, 0.2);
            var pts = new PointCollection { c };
            double r = 0;
            for (int k = 0; k < 5; k++)
            {
                r += Rand(20, 34);
                double wobble = a + Rand(-0.25, 0.25);
                pts.Add(new Point(c.X + Math.Cos(wobble) * r, c.Y + Math.Sin(wobble) * r));
            }
            var crack = new Polyline { Points = pts, Stroke = Solid(Color.FromRgb(20, 26, 12)), StrokeThickness = 4,
                StrokeLineJoin = PenLineJoin.Bevel, Effect = new DropShadowEffect { Color = DecayGreen, BlurRadius = 12, ShadowDepth = 0 } };
            AddFx(crack, 0, 0, 6);
            crack.Opacity = 0;
            _ = Anim.To(crack, OpacityProperty, 1, 120, null, delayMs: i * 20);
            _ = FadeAndRemoveAsync(crack, 700, 900);
        }
        // Bone bits.
        for (int i = 0; i < 7; i++)
        {
            var bone = new Rectangle { Width = 12, Height = 4, RadiusX = 2, RadiusY = 2, Fill = Solid(Color.FromRgb(240, 234, 214)), RenderTransformOrigin = new Point(0.5, 0.5) };
            var t = new TranslateTransform();
            var rot = new RotateTransform();
            var g = new TransformGroup();
            g.Children.Add(rot);
            g.Children.Add(t);
            bone.RenderTransform = g;
            AddFx(bone, c.X, c.Y, 47);
            double a = Rand(0, Math.PI * 2), d = Rand(50, 100), ms = Rand(400, 650);
            _ = Anim.To(t, TranslateTransform.XProperty, Math.Cos(a) * d, ms, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, Math.Sin(a) * d, ms, Anim.Out);
            _ = Anim.To(rot, RotateTransform.AngleProperty, Rand(-360, 360), ms, null);
            _ = FadeAndRemoveAsync(bone, 300, ms);
        }
    }

    private void PsiRing(Point c, double delay)
    {
        var ring = new Ellipse { Width = 220, Height = 220, Stroke = Solid(PsiViolet), StrokeThickness = 5,
            RenderTransformOrigin = new Point(0.5, 0.5), Effect = new BlurEffect { Radius = 3 }, Opacity = 0 };
        var s = new ScaleTransform(0.3, 0.3);
        ring.RenderTransform = s;
        AddFx(ring, c.X - 110, c.Y - 110, 8);
        _ = Anim.To(ring, OpacityProperty, 1, 100, null, delayMs: delay);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1.4, 700, Anim.Out, delayMs: delay);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1.4, 700, Anim.Out, delayMs: delay);
        _ = FadeAndRemoveAsync(ring, 500, delay + 250);
    }

    private void Steam(Point at)
    {
        double size = Rand(24, 40);
        var puff = new Ellipse { Width = size, Height = size,
            Fill = new RadialGradientBrush(Color.FromArgb(170, 240, 245, 250), Color.FromArgb(0, 240, 245, 250)), Effect = new BlurEffect { Radius = 5 } };
        var t = new TranslateTransform();
        puff.RenderTransform = t;
        AddFx(puff, at.X - size / 2, at.Y - size / 2, 47);
        double ms = Rand(700, 1000);
        _ = Anim.To(t, TranslateTransform.YProperty, -Rand(60, 120), ms, Anim.Out);
        _ = Anim.To(t, TranslateTransform.XProperty, Rand(-20, 20), ms, Anim.Out);
        _ = FadeAndRemoveAsync(puff, ms);
    }

    private void HoloBeam(Point c)
    {
        double w = BoardLayout.CardW * 1.1, h = 520;
        var beam = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = new LinearGradientBrush(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), Color.FromArgb(150, Cyan.R, Cyan.G, Cyan.B), 90),
            RenderTransformOrigin = new Point(0.5, 1),
            Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 24, ShadowDepth = 0 },
        };
        var s = new ScaleTransform(1, 0);
        beam.RenderTransform = s;
        AddFx(beam, c.X - w / 2, c.Y + BoardLayout.CardH / 2 - h, 38);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 250, Anim.Out);
        _ = FadeAndRemoveAsync(beam, 500, 350);
        for (int i = 0; i < 4; i++)
        {
            var scan = new Rectangle { Width = w, Height = 3, Fill = Solid(Colors.White, 180) };
            var t = new TranslateTransform();
            scan.RenderTransform = t;
            AddFx(scan, c.X - w / 2, c.Y - BoardLayout.CardH / 2, 39);
            _ = Anim.To(t, TranslateTransform.YProperty, BoardLayout.CardH, 500, null, delayMs: i * 110);
            _ = FadeAndRemoveAsync(scan, 150, 500 + i * 110);
        }
    }

    /// <summary>A Leader lands: brass horns, a heavier shake, a double gold shockwave and a short camera push-in.</summary>
    private void LeaderArrival(Point c)
    {
        Sfx.Play(SfxKind.Horn);
        _ = Anim.Delay(120).ContinueWith(_ => Shockwave(c, Gold), TaskScheduler.FromCurrentSynchronizationContext());
        CameraPush(1.05, 900);
        Shake(strength: 2.2);
    }

    // =================================================================== camera

    private ScaleTransform? _camScale;
    private TranslateTransform? _camShake;

    private void EnsureCamera()
    {
        if (_camScale is not null) return;
        _camScale = new ScaleTransform(1, 1);
        _camShake = new TranslateTransform();
        var g = new TransformGroup();
        g.Children.Add(_camScale);
        g.Children.Add(_camShake);
        Viewport.RenderTransform = g;
    }

    private void CameraPush(double scale, double ms)
    {
        scale = 1 + (scale - 1) * FxSettings.Intensity;
        EnsureCamera();
        _camScale!.CenterX = Viewport.ActualWidth / 2;
        _camScale.CenterY = Viewport.ActualHeight / 2;
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
            a.KeyFrames.Add(new EasingDoubleKeyFrame(scale, KeyTime.FromPercent(0.35), Anim.Out));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), Anim.InOut));
            _camScale.BeginAnimation(p, a);
        }
    }

    // =================================================================== moments

    /// <summary>Mr Morph / Forced Mitosis: liquid chrome washes over the card and it re-forms as the copy.</summary>
    private async Task LiquidMetalAsync(CardView v)
    {
        Sfx.Play(SfxKind.Portal, 0.6);
        var r = BoardRectOf(v);
        var chrome = new Rectangle
        {
            Width = r.Width,
            Height = r.Height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(148, 163, 184), 0),
                new(Color.FromRgb(248, 250, 252), 0.3),
                new(Color.FromRgb(100, 116, 139), 0.55),
                new(Color.FromRgb(241, 245, 249), 0.8),
                new(Color.FromRgb(148, 163, 184), 1),
            }, 60),
            Opacity = 0,
            Effect = new DropShadowEffect { Color = Chrome, BlurRadius = 20, ShadowDepth = 0 },
        };
        AddFx(chrome, r.X, r.Y, 14);
        await Anim.To(chrome, OpacityProperty, 1, 260, Anim.Out);
        var wobble = new ElasticEase { Oscillations = 3, Springiness = 3 };
        _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 1, 700, wobble, from: 0.85);
        _ = Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 1, 700, wobble, from: 1.12);
        v.RefreshFace();
        for (int i = 0; i < 2; i++)
        {
            var ripple = new Ellipse { Width = 200, Height = 200, Stroke = Solid(Chrome), StrokeThickness = 3, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0.9 };
            var s = new ScaleTransform(0.3, 0.3);
            ripple.RenderTransform = s;
            var c = Mid(r);
            AddFx(ripple, c.X - 100, c.Y - 100, 15);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1.3, 600, Anim.Out, delayMs: i * 150);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1.3, 600, Anim.Out, delayMs: i * 150);
            _ = FadeAndRemoveAsync(ripple, 600, i * 150);
        }
        await Anim.Delay(250);
        await FadeAndRemoveAsync(chrome, 450);
    }

    private FrameworkElement? _tacticalGrid;

    /// <summary>Ace, Council Tactician: a holographic tactical grid over the board while the nodes are rearranged.</summary>
    private void ShowTacticalGrid()
    {
        if (_tacticalGrid is not null) return;
        Sfx.Play(SfxKind.Holo, 0.7);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (double x = 0; x <= BoardLayout.Width; x += 50)
            {
                ctx.BeginFigure(new Point(x, 0), false, false);
                ctx.LineTo(new Point(x, BoardLayout.Height), true, false);
            }
            for (double y = 0; y <= BoardLayout.Height; y += 50)
            {
                ctx.BeginFigure(new Point(0, y), false, false);
                ctx.LineTo(new Point(BoardLayout.Width, y), true, false);
            }
        }
        geo.Freeze();
        var host = new Canvas { Width = BoardLayout.Width, Height = BoardLayout.Height };
        host.Children.Add(new Path { Data = geo, Stroke = Solid(Cyan, 45), StrokeThickness = 1 });
        foreach (var n in State.Players.SelectMany(p => Rearrange.NodesOf(State, p.Index)))
        {
            var c = BoardLayout.Center(n);
            var box = new Rectangle { Width = BoardLayout.SlotW, Height = BoardLayout.SlotH, RadiusX = 10, RadiusY = 10,
                Stroke = Solid(Cyan, 160), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
            Canvas.SetLeft(box, c.X - box.Width / 2);
            Canvas.SetTop(box, c.Y - box.Height / 2);
            host.Children.Add(box);
        }
        var scan = new Rectangle { Width = BoardLayout.Width, Height = 6, Fill = Solid(Cyan, 120), Effect = new BlurEffect { Radius = 6 } };
        var st = new TranslateTransform();
        scan.RenderTransform = st;
        host.Children.Add(scan);
        st.BeginAnimation(TranslateTransform.YProperty, Loop(0, BoardLayout.Height, 2.2));
        AddFx(host, 0, 0, 4);
        host.Opacity = 0;
        _ = Anim.To(host, OpacityProperty, 1, 400, Anim.Out);
        _tacticalGrid = host;
    }

    private void HideTacticalGrid()
    {
        if (_tacticalGrid is not { } g) return;
        _tacticalGrid = null;
        _ = FadeAndRemoveAsync(g, 400);
    }

    /// <summary>The Empress / Senate Chambers: a gavel slams down over the table.</summary>
    private async Task GavelAsync(string wonBy)
    {
        var center = new Point(FxCanvas.ActualWidth / 2, FxCanvas.ActualHeight / 2 - 40);
        var glyph = new TextBlock
        {
            Text = "⚖",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 150,
            Foreground = Solid(Gold),
            Effect = new DropShadowEffect { Color = Gold, BlurRadius = 30, ShadowDepth = 0 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var s = new ScaleTransform(3, 3);
        glyph.RenderTransform = s;
        glyph.Opacity = 0;
        AddScreenFx(glyph, center.X - glyph.DesiredSize.Width / 2, center.Y - glyph.DesiredSize.Height / 2, 300);
        _ = Anim.To(glyph, OpacityProperty, 1, 120, null);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 260, Anim.In);
        await Anim.To(s, ScaleTransform.ScaleYProperty, 1, 260, Anim.In);
        Sfx.Play(SfxKind.Gavel);
        Shake(strength: 3);
        Shockwave(new Point(BoardLayout.Width / 2, BoardLayout.ControlLineY), Gold);
        _ = FloatTextAtAsync(wonBy.ToUpperInvariant(), new Point(center.X, center.Y + 110), Gold);
        await Anim.Delay(700);
        _ = FadeAndRemoveScreenAsync(glyph, 400);
    }

    private bool _cleaningUp;
    private bool _sweepShown;

    /// <summary>Clean-up: a broad sweep crosses the table as the Units are cleared away.</summary>
    private void CleanupSweep()
    {
        if (_sweepShown) return;
        _sweepShown = true;
        Sfx.Play(SfxKind.Sweep);
        var band = new Rectangle
        {
            Width = 420,
            Height = BoardLayout.Height + 200,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 255, 255, 255), 0),
                new(Color.FromArgb(55, 200, 220, 255), 0.6),
                new(Color.FromArgb(0, 255, 255, 255), 1),
            }, 0),
        };
        var g = new TransformGroup();
        var t = new TranslateTransform(-500, 0);
        g.Children.Add(new SkewTransform(-15, 0));
        g.Children.Add(t);
        band.RenderTransform = g;
        AddFx(band, 0, -100, 35);
        _ = Anim.To(t, TranslateTransform.XProperty, BoardLayout.Width + 200, 900, Anim.InOut)
            .ContinueWith(_ => RemoveFx(band), TaskScheduler.FromCurrentSynchronizationContext());
    }

    // =================================================================== tension: heartbeat and music

    private bool _roundLive;
    private bool _someonePassed;
    private DispatcherTimer? _heartbeat;

    /// <summary>
    /// Near the end of a close round (someone has passed and the totals are within 1) a heartbeat thumps and the score
    /// pulses; the music's pulse layer follows how close it is.
    /// </summary>
    private void UpdateTension()
    {
        int my = _hud.MyTotal, ai = _hud.AiTotal, diff = Math.Abs(my - ai);
        bool close = _roundLive && _someonePassed && diff <= 1 && my + ai > 0;
        Sfx.SetTension(!_roundLive ? 0 : close ? 1 : diff <= 3 ? 0.45 : 0.15);

        if (close && _heartbeat is null)
        {
            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.05) };
            _heartbeat.Tick += (_, _) => Heartbeat();
            _heartbeat.Start();
            Heartbeat();
        }
        else if (!close && _heartbeat is not null)
        {
            _heartbeat.Stop();
            _heartbeat = null;
        }
    }

    private void Heartbeat()
    {
        Sfx.Play(SfxKind.Heartbeat, 0.8);
        foreach (var tb in new[] { MyTotalText, AiTotalText })
        {
            var (s, _) = TotalTransforms(tb); // shared with the rolling digits (effects pass 3)
            var beat = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(500) };
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromPercent(0.12), Anim.Out));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.35), Anim.In));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.5), Anim.Out));
            beat.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), Anim.In));
            s.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        }
    }
}
