using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using CosmicCrusade.Core.Model;
using Path = System.Windows.Shapes.Path;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Effects pass 3, backlog A: an ambient effect (and, where it fits, a sound) for every remaining Warzone.
/// Board ambients draw on <c>BoardCanvas</c> inside the 1600×1000 board; "under the table" layers use z 2–4 (below the
/// cards at 10), weather and light over the table z 33–37. Planet Earth and Cyber Moon rise in <c>SkyCanvas</c>, behind
/// the board. Every ambient respects <see cref="FxSettings"/>: Low quality draws fewer pieces and no blurs.
/// </summary>
public partial class GameView
{
    private static readonly Color Hazard = Color.FromRgb(250, 204, 21);
    private static readonly Color Ice = Color.FromRgb(186, 230, 253);
    private static readonly Color Acid = Color.FromRgb(163, 230, 53);
    private static readonly Color Amber = Color.FromRgb(251, 191, 36);
    private static readonly Color Marble = Color.FromRgb(241, 236, 228);

    /// <summary>Where the most recent action happened on the board (The Oculus looks there).</summary>
    private Point _focusPoint = new(BoardLayout.Width / 2, BoardLayout.ControlLineY);

    private void LookAt(Point boardPoint) => _focusPoint = boardPoint;

    /// <summary>Path markup with numbers formatted the same way on every machine.</summary>
    private static Geometry Geo(FormattableString markup) => Geometry.Parse(FormattableString.Invariant(markup));

    // =================================================================== plumbing

    private static Effect? Glow(Color c, double radius) =>
        FxSettings.HighQuality ? new DropShadowEffect { Color = c, BlurRadius = radius, ShadowDepth = 0 } : null;

    private static Effect? SoftBlur(double radius) => FxSettings.HighQuality ? new BlurEffect { Radius = radius } : null;

    /// <summary>A timer that fires at a random interval between the two bounds (re-rolled each time).</summary>
    private DispatcherTimer Every(double minSeconds, double maxSeconds, Action tick, bool startNow = false)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Rand(minSeconds, maxSeconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Interval = TimeSpan.FromSeconds(Rand(minSeconds, maxSeconds));
            if (_state is not null) tick();
        };
        timer.Start();
        if (startNow) tick();
        return timer;
    }

    /// <summary>Like <see cref="StopWith"/>, but for elements in any panel (board or sky).</summary>
    private static Action StopAnywhere(IEnumerable<UIElement> elements, params DispatcherTimer[] timers) => () =>
    {
        foreach (var t in timers) t.Stop();
        foreach (var e in elements.ToList())
            _ = Anim.To(e, OpacityProperty, 0, 600, Anim.In).ContinueWith(_ =>
            {
                (VisualTreeHelper.GetParent(e) as Panel)?.Children.Remove(e);
                StopAnimationsDeep(e);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    };

    private T FadeIn<T>(T element, double ms = 1000, double to = 1) where T : UIElement
    {
        element.Opacity = 0;
        _ = Anim.To(element, OpacityProperty, to, ms, Anim.Out);
        return element;
    }

    /// <summary>The Warzone ambients of effects pass 3, by card name (null = not one of them).</summary>
    private Action? StartAmbient3(CardInstance card) => card.Definition.Name switch
    {
        "Fallout Shelter" => AmbientFallout(),
        "Cryogenic Chambers" => AmbientCryo(),
        "The Scorched Lands" => AmbientScorched(),
        "Corrosive Channels" => AmbientCorrosive(),
        "Swarm Warrens" => AmbientSwarm(),
        "Central Hive" => AmbientHive(),
        "Nyxthian Hatchery" => AmbientHatchery(),
        "The Oculus" => AmbientOculus(),
        "Oracle's Arch" => AmbientOracle(),
        "Planet Earth" => AmbientPlanet(earth: true),
        "Cyber Moon" => AmbientPlanet(earth: false),
        "Hills of Decay" => AmbientGraveyard(),
        "Galactic Capital" => AmbientSkyline(),
        "Senate Chambers" => AmbientPillars(),
        "The Campaign Trail" => AmbientBunting(),
        "Production Facility" => AmbientConveyor(),
        "Military Academy" => AmbientAcademy(),
        "Mercenary Camp" => AmbientCampfire(),
        "Training Grounds" => AmbientTargets(),
        "Vivisection Laboratory" => AmbientLab(),
        "Blaarxis" => AmbientSlimePools(),
        "Primordial Marsh" => AmbientMarsh(),
        "Refraction Network" => AmbientPrisms(),
        "Vacuum Spire" => AmbientSpire(),
        _ => null,
    };

    // =================================================================== Fallout Shelter

    /// <summary>Radiation trefoils slowly turning round the table, a sickly flicker, and Geiger-counter clicks.</summary>
    private Action AmbientFallout()
    {
        var els = new List<UIElement>();
        var spots = new[] { new Point(1125, 300), new Point(1125, 720), new Point(230, 500), new Point(1560, 500), new Point(40, 40) };
        foreach (var (p, i) in spots.Take(FxSettings.HighQuality ? 5 : 2).Select((pt, idx) => (pt, idx)))
        {
            double r = i < 2 ? 46 : 30;
            var sign = new Path
            {
                Data = Trefoil(r),
                Width = r * 2,
                Height = r * 2,
                Fill = Solid(Hazard, 150),
                Stroke = Solid(Colors.Black, 160),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = Glow(Hazard, 18),
            };
            var spin = new RotateTransform();
            sign.RenderTransform = spin;
            AddFx(FadeIn(sign, 1200, 0.55), p.X - r, p.Y - r, 3);
            spin.BeginAnimation(RotateTransform.AngleProperty, Loop(0, i % 2 == 0 ? 360 : -360, 24 + i * 3));
            sign.BeginAnimation(OpacityProperty, Loop(0.35, 0.7, 2.2 + i * 0.3, reverse: true, beginSeconds: 1.2));
            els.Add(sign);
        }

        // A faint green radioactive wash that flickers now and then.
        var wash = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = new RadialGradientBrush(Color.FromArgb(0, 132, 204, 22), Color.FromArgb(60, 132, 204, 22)),
            Opacity = 0,
        };
        AddFx(wash, 0, 0, 34);
        els.Add(wash);
        var flicker = Every(5, 11, () =>
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(700) };
            foreach (var (v, at) in new[] { (FxSettings.Flash(0.9), 0.1), (0.2, 0.3), (FxSettings.Flash(0.7), 0.45), (0.0, 1.0) })
                a.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromPercent(at)));
            wash.BeginAnimation(OpacityProperty, a);
        });

        // The Geiger counter: clicks in irregular bursts.
        double activity = 0.15;
        var geiger = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        geiger.Tick += (_, _) =>
        {
            activity = Math.Clamp(activity + Rand(-0.06, 0.06), 0.02, 0.45);
            if (_fxRng.NextDouble() < activity) Sfx.Play(SfxKind.Geiger, Rand(0.15, 0.35));
        };
        geiger.Start();
        return StopWith(els, flicker, geiger);
    }

    /// <summary>The radiation symbol: a centre dot and three 60° blades.</summary>
    private static Geometry Trefoil(double r)
    {
        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        double r0 = r * 0.24, r1 = r * 0.32;
        Point P(double deg, double rad) => new(r + Math.Cos(deg * Math.PI / 180) * rad, r + Math.Sin(deg * Math.PI / 180) * rad);
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(r + r0 * 0.8, r), true, true);
            ctx.ArcTo(new Point(r - r0 * 0.8, r), new Size(r0 * 0.8, r0 * 0.8), 0, false, SweepDirection.Clockwise, true, false);
            ctx.ArcTo(new Point(r + r0 * 0.8, r), new Size(r0 * 0.8, r0 * 0.8), 0, false, SweepDirection.Clockwise, true, false);
            foreach (double mid in new[] { -90.0, 30.0, 150.0 })
            {
                double a0 = mid - 30, a1 = mid + 30;
                ctx.BeginFigure(P(a0, r1), true, true);
                ctx.LineTo(P(a0, r), true, false);
                ctx.ArcTo(P(a1, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(P(a1, r1), true, false);
                ctx.ArcTo(P(a0, r1), new Size(r1, r1), 0, false, SweepDirection.Counterclockwise, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    // =================================================================== Cryogenic Chambers

    /// <summary>Frost creeps in from the edges of the board, a cold vignette settles and snow drifts down. Blaarg Units frost over.</summary>
    private Action AmbientCryo()
    {
        Sfx.Play(SfxKind.Frost, 0.7);
        var els = new List<UIElement>();
        var vignette = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, Ice.R, Ice.G, Ice.B), 0.55),
                new(Color.FromArgb(90, Ice.R, Ice.G, Ice.B), 1),
            }),
        };
        AddFx(FadeIn(vignette, 2000), 0, 0, 33);
        els.Add(vignette);

        var frost = new Path
        {
            Data = FrostEdges(FxSettings.HighQuality ? 46 : 20),
            Stroke = Solid(Colors.White, 200),
            StrokeThickness = 2,
            Effect = Glow(Ice, 10),
            StrokeDashArray = new DoubleCollection { 300 },
            StrokeDashOffset = 300,
        };
        AddFx(frost, 0, 0, 33);
        frost.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(300, 0, Anim.Ms(4000)) { EasingFunction = Anim.Out });
        els.Add(frost);

        for (int i = 0; i < FxSettings.Count(36); i++)
        {
            double size = Rand(3, 7);
            var flake = new Ellipse { Width = size, Height = size, Fill = Solid(Colors.White, (byte)Rand(120, 220)) };
            var t = new TranslateTransform();
            flake.RenderTransform = t;
            AddFx(flake, Rand(0, BoardLayout.Width), 0, 36);
            double secs = Rand(9, 16), begin = Rand(0, 9);
            t.BeginAnimation(TranslateTransform.YProperty, Loop(-20, BoardLayout.Height + 20, secs, beginSeconds: begin));
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(10, 40), Rand(10, 40), Rand(2, 4), reverse: true));
            els.Add(flake);
        }
        var creak = Every(10, 18, () => Sfx.Play(SfxKind.Frost, 0.35));
        return StopWith(els, creak);
    }

    /// <summary>Ice crystals branching in from all four board edges.</summary>
    private Geometry FrostEdges(int crystals)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < crystals; i++)
            {
                int edge = i % 4;
                double along = Rand(0, 1);
                Point start = edge switch
                {
                    0 => new Point(along * BoardLayout.Width, 0),
                    1 => new Point(BoardLayout.Width, along * BoardLayout.Height),
                    2 => new Point(along * BoardLayout.Width, BoardLayout.Height),
                    _ => new Point(0, along * BoardLayout.Height),
                };
                double inward = edge switch { 0 => Math.PI / 2, 1 => Math.PI, 2 => -Math.PI / 2, _ => 0 };
                Branch(ctx, start, inward + Rand(-0.5, 0.5), Rand(50, 120), 3);
            }
        }
        geo.Freeze();
        return geo;
    }

    private void Branch(StreamGeometryContext ctx, Point from, double angle, double length, int depth)
    {
        var to = new Point(from.X + Math.Cos(angle) * length, from.Y + Math.Sin(angle) * length);
        ctx.BeginFigure(from, false, false);
        ctx.LineTo(to, true, false);
        if (depth <= 0) return;
        for (int k = 1; k <= 2; k++)
        {
            var p = new Point(from.X + (to.X - from.X) * k / 3, from.Y + (to.Y - from.Y) * k / 3);
            Branch(ctx, p, angle + 0.75, length * 0.38, depth - 1);
            Branch(ctx, p, angle - 0.75, length * 0.38, depth - 1);
        }
    }

    // =================================================================== The Scorched Lands

    /// <summary>Heat haze rises off a scorched board and embers drift up through it, crackling.</summary>
    private Action AmbientScorched()
    {
        var els = new List<UIElement>();
        var heat = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 249, 115, 22), Color.FromArgb(70, 220, 60, 10), 90),
        };
        AddFx(FadeIn(heat, 1500), 0, 0, 2);
        els.Add(heat);

        // Haze: soft warm bands rising and wobbling.
        for (int i = 0; i < FxSettings.Count(6); i++)
        {
            var band = new Rectangle
            {
                Width = BoardLayout.Width + 200,
                Height = 90,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(0, 255, 210, 160), 0),
                    new(Color.FromArgb(26, 255, 210, 160), 0.5),
                    new(Color.FromArgb(0, 255, 210, 160), 1),
                }, 90),
                Effect = SoftBlur(8),
            };
            var t = new TranslateTransform();
            var skew = new SkewTransform();
            var g = new TransformGroup();
            g.Children.Add(skew);
            g.Children.Add(t);
            band.RenderTransform = g;
            AddFx(band, -100, 0, 34);
            double secs = Rand(5, 8);
            t.BeginAnimation(TranslateTransform.YProperty, Loop(BoardLayout.Height, -90, secs, beginSeconds: i * secs / 6));
            skew.BeginAnimation(SkewTransform.AngleXProperty, Loop(-4, 4, Rand(0.8, 1.4), reverse: true));
            els.Add(band);
        }

        for (int i = 0; i < FxSettings.Count(44); i++)
        {
            double size = Rand(3, 7);
            var ember = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Solid(Lighten(Ember, (int)Rand(0, 80))),
                Effect = i % 3 == 0 ? Glow(Ember, 8) : null,
            };
            var t = new TranslateTransform();
            ember.RenderTransform = t;
            AddFx(ember, Rand(0, BoardLayout.Width), 0, 36);
            double secs = Rand(5, 9), begin = Rand(0, 6);
            t.BeginAnimation(TranslateTransform.YProperty, Loop(BoardLayout.Height + 10, -20, secs, beginSeconds: begin));
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(15, 50), Rand(15, 50), Rand(1.2, 2.5), reverse: true));
            ember.BeginAnimation(OpacityProperty, Loop(0.2, 1, Rand(0.4, 1.0), reverse: true));
            els.Add(ember);
        }
        var crackle = Every(5, 9, () => Sfx.Play(SfxKind.Crackle, 0.35), startNow: true);
        return StopWith(els, crackle);
    }

    // =================================================================== Corrosive Channels

    /// <summary>Glowing acid runs down channels between the node columns, dripping and sizzling.</summary>
    private Action AmbientCorrosive()
    {
        var els = new List<UIElement>();
        foreach (double x in new[] { 250.0, 455, 660, 860, 1060 })
        {
            var pts = new PointCollection();
            for (double y = -10; y <= BoardLayout.Height + 10; y += 40)
                pts.Add(new Point(x + Math.Sin(y / 70 + x) * 7, y));
            var bed = new Polyline { Points = pts, Stroke = Solid(Color.FromRgb(30, 50, 10), 170), StrokeThickness = 16, StrokeLineJoin = PenLineJoin.Round };
            var flow = new Polyline
            {
                Points = pts,
                Stroke = new LinearGradientBrush(Lighten(Acid, 40), Color.FromRgb(77, 124, 15), 0),
                StrokeThickness = 8,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Effect = Glow(Acid, 14),
            };
            AddFx(FadeIn(bed, 900), 0, 0, 3);
            AddFx(FadeIn(flow, 900, 0.85), 0, 0, 3);
            flow.BeginAnimation(Shape.StrokeDashOffsetProperty, Loop(0, -50, 2.2));
            els.Add(bed);
            els.Add(flow);
        }

        // Drips and bubbles along the channels.
        var bubbles = Every(0.25, 0.7, () =>
        {
            double x = new[] { 250.0, 455, 660, 860, 1060 }[_fxRng.Next(5)];
            double y = Rand(20, BoardLayout.Height - 20);
            double size = Rand(6, 14);
            var b = new Ellipse { Width = size, Height = size, Stroke = Solid(Lighten(Acid, 60), 220), StrokeThickness = 1.5, Fill = Solid(Acid, 70),
                RenderTransformOrigin = new Point(0.5, 0.5) };
            var s = new ScaleTransform(0.3, 0.3);
            b.RenderTransform = s;
            AddFx(b, x - size / 2, y - size / 2, 4);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1.3, 600, Anim.Out);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1.3, 600, Anim.Out);
            _ = FadeAndRemoveAsync(b, 250, 450);
        });
        var sizzle = Every(7, 12, () => Sfx.Play(SfxKind.Sizzle, 0.3), startNow: true);
        return StopWith(els, bubbles, sizzle);
    }

    // =================================================================== Swarm Warrens

    /// <summary>Little bugs scuttle across the table on wandering paths, legs twitching.</summary>
    private Action AmbientSwarm()
    {
        int alive = 0;
        var spawner = Every(0.8, 1.8, () =>
        {
            // Keep the swarm small: everything animating on the board makes the 3D board texture redraw.
            if (alive >= FxSettings.Count(6)) return;
            alive++;
            SpawnBug(() => alive--);
        }, startNow: true);
        var skitter = Every(6, 11, () => Sfx.Play(SfxKind.Skitter, 0.3));
        return StopWith(Array.Empty<UIElement>(), spawner, skitter);
    }

    private void SpawnBug(Action onGone)
    {
        var bug = new Canvas { Width = 0, Height = 0 };
        var dark = Solid(Color.FromRgb(28, 20, 14));
        var legs = new Canvas();
        var flutter = new ScaleTransform(1, 1);
        legs.RenderTransform = flutter;
        for (int i = -1; i <= 1; i++)
            foreach (int side in new[] { -1, 1 })
                legs.Children.Add(new Line { X1 = i * 5, Y1 = 0, X2 = i * 7 - 2, Y2 = side * 9, Stroke = dark, StrokeThickness = 1.5 });
        bug.Children.Add(legs);
        var body = new Ellipse { Width = 16, Height = 10, Fill = new RadialGradientBrush(Color.FromRgb(120, 84, 40), Color.FromRgb(30, 20, 10)) };
        Canvas.SetLeft(body, -8);
        Canvas.SetTop(body, -5);
        bug.Children.Add(body);
        var head = new Ellipse { Width = 6, Height = 6, Fill = dark };
        Canvas.SetLeft(head, 7);
        Canvas.SetTop(head, -3);
        bug.Children.Add(head);

        // A wandering path from one edge to another.
        Point Edge(int e) => e switch
        {
            0 => new Point(Rand(0, BoardLayout.Width), -20),
            1 => new Point(BoardLayout.Width + 20, Rand(0, BoardLayout.Height)),
            2 => new Point(Rand(0, BoardLayout.Width), BoardLayout.Height + 20),
            _ => new Point(-20, Rand(0, BoardLayout.Height)),
        };
        int from = _fxRng.Next(4);
        var a = Edge(from);
        var b = Edge((from + 1 + _fxRng.Next(3)) % 4);
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new BezierSegment(new Point(Rand(100, 1500), Rand(100, 900)), new Point(Rand(100, 1500), Rand(100, 900)), b, true));
        var path = new PathGeometry();
        path.Figures.Add(fig);
        path.Freeze();

        var mt = new MatrixTransform();
        bug.RenderTransform = mt;
        AddFx(bug, 0, 0, 34);
        double secs = Rand(5, 9) * Anim.SpeedFactor;
        var move = new MatrixAnimationUsingPath
        {
            PathGeometry = path,
            Duration = TimeSpan.FromSeconds(secs),
            DoesRotateWithTangent = true,
        };
        move.Completed += (_, _) =>
        {
            // Stop the leg twitch: a Forever animation keeps ticking after its element is removed, so without this
            // every bug that ever crossed the board kept running (the game slowed down the longer Swarm Warrens was up).
            flutter.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RemoveFx(bug);
            onGone();
        };
        mt.BeginAnimation(MatrixTransform.MatrixProperty, move);
        if (FxSettings.HighQuality)
            flutter.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, -0.6, TimeSpan.FromMilliseconds(140))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    // =================================================================== Central Hive

    /// <summary>A faint honeycomb under the board, pulsing outwards from the centre, and a low buzz.</summary>
    private Action AmbientHive()
    {
        var comb = new Path
        {
            Data = HoneycombGeometry(38),
            Stroke = Solid(Amber, 90),
            StrokeThickness = 2,
            Fill = Solid(Amber, 10),
            // The honeycomb is thousands of hexagons: render it once to a bitmap so its opacity pulse is cheap.
            CacheMode = new BitmapCache(1),
        };
        AddFx(FadeIn(comb, 1500, 0.8), 0, 0, 2);
        comb.BeginAnimation(OpacityProperty, Loop(0.45, 0.9, 1.8, reverse: true, beginSeconds: 1.5));

        var els = new List<UIElement> { comb };
        var pulse = Every(2.6, 3.4, () =>
        {
            // A soft ring from a gradient rather than a blur effect (a blur on an element growing to 2000 px is
            // very expensive inside the 3D board).
            var ring = new Ellipse
            {
                Width = 400,
                Height = 400,
                Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromArgb(0, Amber.R, Amber.G, Amber.B), 0.78),
                        new(Color.FromArgb(110, Amber.R, Amber.G, Amber.B), 0.9),
                        new(Color.FromArgb(0, Amber.R, Amber.G, Amber.B), 1),
                    }),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var s = new ScaleTransform(0.1, 0.1);
            ring.RenderTransform = s;
            AddFx(ring, BoardLayout.Width / 2 - 200, BoardLayout.ControlLineY - 200, 2);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 5, 2600, Anim.Out);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 5, 2600, Anim.Out);
            _ = FadeAndRemoveAsync(ring, 2600);
        }, startNow: true);
        var buzz = Every(9, 14, () => Sfx.Play(SfxKind.HiveBuzz, 0.35), startNow: true);
        return StopWith(els, pulse, buzz);
    }

    private static Geometry HoneycombGeometry(double r)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double dx = r * Math.Sqrt(3), dy = r * 1.5;
            int row = 0;
            for (double y = 0; y < BoardLayout.Height + r; y += dy, row++)
                for (double x = (row % 2) * dx / 2; x < BoardLayout.Width + r; x += dx)
                {
                    for (int i = 0; i <= 6; i++)
                    {
                        double a = Math.PI / 3 * i + Math.PI / 6;
                        var p = new Point(x + Math.Cos(a) * r * 0.92, y + Math.Sin(a) * r * 0.92);
                        if (i == 0) ctx.BeginFigure(p, true, true);
                        else ctx.LineTo(p, true, false);
                    }
                }
        }
        geo.Freeze();
        return geo;
    }

    // =================================================================== Nyxthian Hatchery

    /// <summary>Clutches of translucent eggs pulse in the corners of the board; something moves inside.</summary>
    private Action AmbientHatchery()
    {
        var els = new List<UIElement>();
        var corners = new[] { new Point(30, 30), new Point(BoardLayout.Width - 30, 30), new Point(30, BoardLayout.Height - 30), new Point(BoardLayout.Width - 30, BoardLayout.Height - 30) };
        int n = 0;
        foreach (var c in corners)
        {
            for (int k = 0; k < (FxSettings.HighQuality ? 4 : 2); k++, n++)
            {
                double w = Rand(46, 70), h = w * 1.3;
                var egg = new Grid { Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 0.9) };
                egg.Children.Add(new Ellipse
                {
                    Fill = new RadialGradientBrush(new GradientStopCollection
                    {
                        new(Color.FromArgb(230, 190, 242, 100), 0),
                        new(Color.FromArgb(200, 101, 163, 13), 0.55),
                        new(Color.FromArgb(220, 59, 7, 100), 1),
                    }) { GradientOrigin = new Point(0.4, 0.35) },
                    Stroke = Solid(Color.FromRgb(46, 16, 70), 200),
                    StrokeThickness = 2,
                });
                // The embryo: a dark curl that drifts.
                var embryo = new Ellipse { Width = w * 0.35, Height = h * 0.3, Fill = Solid(Color.FromRgb(30, 10, 40), 140), Margin = new Thickness(0, h * 0.15, 0, 0) };
                var et = new TranslateTransform();
                embryo.RenderTransform = et;
                egg.Children.Add(embryo);
                et.BeginAnimation(TranslateTransform.XProperty, Loop(-w * 0.12, w * 0.12, Rand(1.5, 2.8), reverse: true));
                egg.Effect = Glow(DecayGreen, 18);
                var s = new ScaleTransform(1, 1);
                egg.RenderTransform = s;
                double ox = (k % 2 == 0 ? 1 : -1) * k * 22, oy = (k / 2) * 30 - 10;
                double x = c.X + (c.X < 800 ? ox : -ox), y = c.Y + (c.Y < 500 ? oy : -oy);
                AddFx(FadeIn(egg, 1400), x - w / 2, y - h / 2, 3);
                double beat = Rand(1.1, 1.6);
                s.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(0.94, 1.06, beat, reverse: true, beginSeconds: n * 0.17));
                s.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1.06, 0.94, beat, reverse: true, beginSeconds: n * 0.17));
                els.Add(egg);
            }
        }
        var pulse = Every(5, 9, () => Sfx.Play(SfxKind.EggPulse, 0.4), startNow: true);
        return StopWith(els, pulse);
    }

    // =================================================================== The Oculus / Oracle's Arch

    /// <summary>
    /// The Oculus: a giant eye opens under the table and its pupil follows the action (deploys, Duels, Nullifies). It
    /// blinks now and then.
    /// </summary>
    private Action AmbientOculus()
    {
        Sfx.Play(SfxKind.EyeOpen, 0.8);
        const double w = 760, h = 360;
        var center = new Point(BoardLayout.Width / 2 - 40, BoardLayout.ControlLineY);
        var (eye, pupil, lid) = BuildEye(w, h, Color.FromRgb(220, 38, 38), Color.FromRgb(251, 191, 36));
        AddFx(FadeIn(eye, 1800, 0.42), center.X - w / 2, center.Y - h / 2, 2);

        var look = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        look.Tick += (_, _) =>
        {
            double dx = Math.Clamp((_focusPoint.X - center.X) / 3.2, -w * 0.25, w * 0.25);
            double dy = Math.Clamp((_focusPoint.Y - center.Y) / 3.2, -h * 0.2, h * 0.2);
            _ = Anim.To(pupil, TranslateTransform.XProperty, dx, 500, Anim.InOut);
            _ = Anim.To(pupil, TranslateTransform.YProperty, dy, 500, Anim.InOut);
        };
        look.Start();
        var blink = Every(6, 12, () => Blink(lid));
        return StopWith(new UIElement[] { eye }, look, blink);
    }

    /// <summary>Oracle's Arch: a small eye in a stone arch on the left edge, gazing in turn at each revealed hand.</summary>
    private Action AmbientOracle()
    {
        Sfx.Play(SfxKind.EyeOpen, 0.5);
        var at = new Point(229, BoardLayout.ControlLineY);
        var host = new Canvas { Width = 0, Height = 0 };

        // The arch: two pillars and a round top.
        var arch = new Path
        {
            Data = Geometry.Parse("M -26,70 L -26,-20 A 26,26 0 0 1 26,-20 L 26,70"),
            Stroke = Solid(Marble, 200),
            StrokeThickness = 6,
            Effect = Glow(Purple, 14),
        };
        host.Children.Add(arch);
        var (eye, pupil, lid) = BuildEye(42, 26, Color.FromRgb(126, 34, 206), Color.FromRgb(216, 180, 254));
        Canvas.SetLeft(eye, -21);
        Canvas.SetTop(eye, -27);
        host.Children.Add(eye);

        // The gaze: a faint cone towards the hand it is looking at.
        var cone = new Polygon
        {
            Points = new PointCollection { new(0, -14), new(-90, 520), new(90, 520) },
            Fill = new LinearGradientBrush(Color.FromArgb(90, 216, 180, 254), Color.FromArgb(0, 216, 180, 254), 90),
            RenderTransformOrigin = new Point(0.5, 0),
        };
        var coneFlip = new ScaleTransform(1, 1, 0, -14);
        cone.RenderTransform = coneFlip;
        host.Children.Insert(0, cone);
        AddFx(FadeIn(host, 1200), at.X, at.Y, 4);

        bool down = true;
        var gaze = Every(2.4, 3.6, () =>
        {
            down = !down;
            _ = Anim.To(pupil, TranslateTransform.YProperty, down ? 5 : -5, 500, Anim.InOut);
            _ = Anim.To(coneFlip, ScaleTransform.ScaleYProperty, down ? 1 : -1, 600, Anim.InOut);
        });
        var blink = Every(7, 12, () => Blink(lid));
        return StopWith(new UIElement[] { host }, gaze, blink);
    }

    /// <summary>An almond eye: sclera, iris, slit pupil (its translate moves the gaze) and an eyelid (its scale blinks).</summary>
    private (FrameworkElement Eye, TranslateTransform Pupil, ScaleTransform Lid) BuildEye(double w, double h, Color iris, Color glow)
    {
        var host = new Grid { Width = w, Height = h };
        var almond = Geo($"M 0,{h / 2} Q {w / 2},{-h * 0.45} {w},{h / 2} Q {w / 2},{h * 1.45} 0,{h / 2} Z");
        host.Clip = almond;
        host.Children.Add(new Rectangle { Fill = new RadialGradientBrush(Color.FromRgb(250, 245, 230), Color.FromRgb(180, 160, 140)) });

        var irisHost = new Canvas { Width = w, Height = h };
        var pupilMove = new TranslateTransform();
        irisHost.RenderTransform = pupilMove;
        double d = h * 0.9;
        var irisEl = new Ellipse
        {
            Width = d,
            Height = d,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Lighten(glow, 30), 0),
                new(glow, 0.35),
                new(iris, 0.8),
                new(Color.FromRgb(20, 0, 0), 1),
            }),
        };
        Canvas.SetLeft(irisEl, w / 2 - d / 2);
        Canvas.SetTop(irisEl, h / 2 - d / 2);
        irisHost.Children.Add(irisEl);
        var slit = new Ellipse { Width = d * 0.16, Height = d * 0.8, Fill = Brushes.Black };
        Canvas.SetLeft(slit, w / 2 - d * 0.08);
        Canvas.SetTop(slit, h / 2 - d * 0.4);
        irisHost.Children.Add(slit);
        var shine = new Ellipse { Width = d * 0.14, Height = d * 0.1, Fill = Solid(Colors.White, 200) };
        Canvas.SetLeft(shine, w / 2 + d * 0.12);
        Canvas.SetTop(shine, h / 2 - d * 0.25);
        irisHost.Children.Add(shine);
        host.Children.Add(irisHost);

        var lid = new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(60, 20, 40), Color.FromRgb(20, 6, 16), 90),
            RenderTransformOrigin = new Point(0.5, 0),
        };
        var lidScale = new ScaleTransform(1, 0);
        lid.RenderTransform = lidScale;
        host.Children.Add(lid);

        var outline = new Path { Data = almond, Stroke = Solid(glow, 220), StrokeThickness = Math.Max(2, h * 0.04), Effect = Glow(glow, 24) };
        var wrap = new Grid { Width = w, Height = h };
        wrap.Children.Add(host);
        wrap.Children.Add(outline);
        return (wrap, pupilMove, lidScale);
    }

    private static void Blink(ScaleTransform lid)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(320) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.4), Anim.In));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), Anim.Out));
        lid.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // =================================================================== Planet Earth / Cyber Moon

    /// <summary>
    /// A planet (Earth, top left) or a circuit-lined moon (Cyber Moon, top right) rises in the sky behind the board and
    /// turns slowly.
    /// </summary>
    private Action AmbientPlanet(bool earth)
    {
        Sfx.Play(SfxKind.Shimmer, 0.6);
        double size = Math.Clamp(SkyCanvas.ActualHeight * 0.32, 140, 320);
        var host = new Grid { Width = size, Height = size };
        var disc = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
        var surface = new Canvas { Width = size, Height = size, Clip = disc };

        if (earth)
        {
            surface.Children.Add(new Rectangle { Width = size, Height = size, Fill = new RadialGradientBrush(Color.FromRgb(96, 165, 250), Color.FromRgb(30, 64, 175)) { GradientOrigin = new Point(0.35, 0.35) } });
            var land = new Canvas { Width = size * 2, Height = size };
            for (int i = 0; i < 9; i++)
            {
                var blob = new Path
                {
                    Data = BlobGeometry(Rand(size * 0.1, size * 0.22), 9, 0.6),
                    Fill = Solid(i % 3 == 0 ? Color.FromRgb(132, 204, 22) : Color.FromRgb(34, 139, 34), 230),
                };
                Canvas.SetLeft(blob, (i % 5) * size * 0.4 + Rand(0, size * 0.1));
                Canvas.SetTop(blob, Rand(0, size * 0.7));
                land.Children.Add(blob);
            }
            var spin = new TranslateTransform();
            land.RenderTransform = spin;
            surface.Children.Add(land);
            spin.BeginAnimation(TranslateTransform.XProperty, Loop(0, -size, 60));
            var clouds = new Rectangle { Width = size, Height = size, Fill = new RadialGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(60, 255, 255, 255)) };
            surface.Children.Add(clouds);
        }
        else
        {
            surface.Children.Add(new Rectangle { Width = size, Height = size, Fill = new RadialGradientBrush(Color.FromRgb(203, 213, 225), Color.FromRgb(71, 85, 105)) { GradientOrigin = new Point(0.35, 0.35) } });
            for (int i = 0; i < 8; i++)
            {
                double r = Rand(size * 0.04, size * 0.12);
                var crater = new Ellipse { Width = r * 2, Height = r * 2, Fill = Solid(Color.FromRgb(100, 116, 139), 150), Stroke = Solid(Color.FromRgb(226, 232, 240), 90), StrokeThickness = 1.5 };
                Canvas.SetLeft(crater, Rand(0, size - r * 2));
                Canvas.SetTop(crater, Rand(0, size - r * 2));
                surface.Children.Add(crater);
            }
            // Circuit traces with travelling light.
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < 9; i++)
                {
                    var p = new Point(Rand(0, size), Rand(0, size));
                    ctx.BeginFigure(p, false, false);
                    for (int k = 0; k < 3; k++)
                    {
                        p = k % 2 == 0 ? new Point(p.X + Rand(-size * 0.3, size * 0.3), p.Y) : new Point(p.X, p.Y + Rand(-size * 0.3, size * 0.3));
                        ctx.LineTo(p, true, false);
                    }
                }
            }
            geo.Freeze();
            var traces = new Path { Data = geo, Stroke = Solid(Cyan, 200), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 6 }, Effect = Glow(Cyan, 8) };
            surface.Children.Add(traces);
            traces.BeginAnimation(Shape.StrokeDashOffsetProperty, Loop(0, -40, 2));
        }
        // Terminator shadow and atmosphere rim.
        surface.Children.Add(new Rectangle { Width = size, Height = size, Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(200, 2, 4, 14), 20) });
        host.Children.Add(surface);
        host.Children.Add(new Ellipse { Stroke = Solid(earth ? Color.FromRgb(147, 197, 253) : Cyan, 160), StrokeThickness = 3, Effect = Glow(earth ? Color.FromRgb(147, 197, 253) : Cyan, 30) });

        double w = Math.Max(SkyCanvas.ActualWidth, 600);
        double x = earth ? w * 0.06 : w - size - w * 0.06;
        var rise = new TranslateTransform(0, size * 1.4);
        host.RenderTransform = rise;
        host.IsHitTestVisible = false;
        Canvas.SetLeft(host, x);
        Canvas.SetTop(host, 8);
        SkyCanvas.Children.Add(host);
        host.Opacity = 0;
        _ = Anim.To(host, OpacityProperty, 1, 1500, Anim.Out);
        _ = Anim.To(rise, TranslateTransform.YProperty, 0, 4200, new SineEase { EasingMode = EasingMode.EaseOut });
        return StopAnywhere(new UIElement[] { host });
    }

    // =================================================================== Hills of Decay

    /// <summary>Gravestones rise out of the ground in the gaps of the table, low mist creeps along the edges and a bell tolls.</summary>
    private Action AmbientGraveyard()
    {
        Sfx.Play(SfxKind.Toll, 0.5);
        var els = new List<UIElement>();
        var spots = new[] { new Point(1125, 260), new Point(1110, 820), new Point(1150, 640), new Point(229, 280), new Point(229, 760), new Point(1135, 400) };
        int i = 0;
        foreach (var p in spots.Take(FxSettings.HighQuality ? 6 : 3))
        {
            double w = Rand(34, 46), h = w * Rand(1.2, 1.5);
            var stone = new Path
            {
                Data = Geo($"M 0,{h} L 0,{w / 2} A {w / 2},{w / 2} 0 0 1 {w},{w / 2} L {w},{h} Z"),
                Fill = new LinearGradientBrush(Color.FromRgb(120, 120, 130), Color.FromRgb(50, 52, 60), 90),
                Stroke = Solid(Color.FromRgb(20, 20, 26)),
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 1),
                Effect = Glow(DecayGreen, 12),
            };
            var cross = new Path { Data = Geo($"M {w / 2},{h * 0.3} L {w / 2},{h * 0.7} M {w * 0.32},{h * 0.42} L {w * 0.68},{h * 0.42}"), Stroke = Solid(Color.FromRgb(30, 30, 36)), StrokeThickness = 3 };
            var grave = new Grid { Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 1) };
            grave.Children.Add(stone);
            grave.Children.Add(cross);
            var s = new ScaleTransform(1, 0);
            var tilt = new RotateTransform(Rand(-8, 8));
            var g = new TransformGroup();
            g.Children.Add(s);
            g.Children.Add(tilt);
            grave.RenderTransform = g;
            AddFx(grave, p.X - w / 2, p.Y - h / 2, 3);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 900, Anim.Back, delayMs: i++ * 180);
            els.Add(grave);
        }
        for (int k = 0; k < FxSettings.Count(8); k++)
        {
            double w = Rand(400, 700);
            var mist = new Ellipse
            {
                Width = w,
                Height = Rand(90, 140),
                Fill = new RadialGradientBrush(Color.FromArgb(90, 190, 210, 180), Color.FromArgb(0, 160, 190, 150)),
                Effect = SoftBlur(25),
            };
            var t = new TranslateTransform();
            mist.RenderTransform = t;
            double y = k % 2 == 0 ? BoardLayout.Height - Rand(40, 110) : Rand(-30, 60);
            AddFx(FadeIn(mist, 2000), Rand(-300, BoardLayout.Width - 200), y, 35);
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(80, 200), Rand(80, 200), Rand(12, 20), reverse: true));
            els.Add(mist);
        }
        var toll = Every(16, 26, () => Sfx.Play(SfxKind.Toll, 0.3));
        return StopWith(els, toll);
    }

    // =================================================================== Galactic Capital

    /// <summary>A city skyline along the far edge, windows glittering and beacons blinking on the towers.</summary>
    private Action AmbientSkyline()
    {
        Sfx.Play(SfxKind.Shimmer, 0.5);
        // The towers stand on a horizon just inside the far edge and reach up towards it, so from the player's seat the
        // city sits along the far side of the table.
        const double Horizon = 110;
        var city = new Canvas { Width = BoardLayout.Width, Height = Horizon };
        double x = 0;
        int windows = 0;
        while (x < BoardLayout.Width)
        {
            double bw = Rand(30, 80), bh = Rand(30, 105);
            var tower = new Rectangle { Width = bw, Height = bh, Fill = new LinearGradientBrush(Color.FromRgb(30, 41, 79), Color.FromRgb(10, 14, 32), 90) };
            Canvas.SetLeft(tower, x);
            Canvas.SetTop(tower, Horizon - bh);
            city.Children.Add(tower);
            for (double wy = 8; wy < bh - 6; wy += 11)
                for (double wx = 5; wx < bw - 6; wx += 9)
                {
                    if (_fxRng.NextDouble() < 0.55) continue;
                    var win = new Rectangle { Width = 4, Height = 5, Fill = Solid(_fxRng.NextDouble() < 0.8 ? Amber : Cyan, 220) };
                    Canvas.SetLeft(win, x + wx);
                    Canvas.SetTop(win, Horizon - bh + wy);
                    city.Children.Add(win);
                    if (FxSettings.HighQuality && windows++ % 4 == 0)
                        win.BeginAnimation(OpacityProperty, Loop(0.15, 1, Rand(0.6, 3), reverse: true, beginSeconds: Rand(0, 3)));
                }
            if (bh > 85)
            {
                var beacon = new Ellipse { Width = 6, Height = 6, Fill = Solid(Color.FromRgb(248, 113, 113)), Effect = Glow(Red, 10) };
                Canvas.SetLeft(beacon, x + bw / 2 - 3);
                Canvas.SetTop(beacon, Horizon - bh - 3);
                city.Children.Add(beacon);
                beacon.BeginAnimation(OpacityProperty, Loop(0, 1, 0.9, reverse: true, beginSeconds: Rand(0, 1)));
            }
            x += bw + Rand(2, 10);
        }
        city.Effect = Glow(Cyan, 16);
        AddFx(FadeIn(city, 1500, 0.85), 0, 0, 3);
        return StopWith(new UIElement[] { city });
    }

    // =================================================================== Senate Chambers

    /// <summary>Marble pillars rise along both side edges of the board; a gong sounds.</summary>
    private Action AmbientPillars()
    {
        Sfx.Play(SfxKind.Gong, 0.6);
        var els = new List<UIElement>();
        foreach (double x in new[] { 2.0, BoardLayout.Width - 36 })
        {
            foreach (var (top, height) in new[] { (30.0, 440.0), (530.0, 440.0) })
            {
                var pillar = new Canvas { Width = 34, Height = height, RenderTransformOrigin = new Point(0.5, 1) };
                var shaft = new Rectangle
                {
                    Width = 26,
                    Height = height - 36,
                    Fill = new LinearGradientBrush(new GradientStopCollection
                    {
                        new(Color.FromRgb(170, 160, 150), 0),
                        new(Marble, 0.4),
                        new(Color.FromRgb(200, 192, 182), 0.7),
                        new(Color.FromRgb(140, 130, 120), 1),
                    }, 0),
                };
                Canvas.SetLeft(shaft, 4);
                Canvas.SetTop(shaft, 18);
                pillar.Children.Add(shaft);
                for (int f = 1; f < 4; f++)
                {
                    var flute = new Line { X1 = 4 + f * 6.5, Y1 = 22, X2 = 4 + f * 6.5, Y2 = height - 22, Stroke = Solid(Color.FromRgb(120, 110, 100), 120), StrokeThickness = 1.2 };
                    pillar.Children.Add(flute);
                }
                foreach (double cy in new[] { 0.0, height - 18 })
                {
                    var cap = new Rectangle { Width = 34, Height = 18, RadiusX = 3, RadiusY = 3, Fill = Solid(Marble) };
                    Canvas.SetTop(cap, cy);
                    pillar.Children.Add(cap);
                }
                pillar.Effect = Glow(Gold, 14);
                var s = new ScaleTransform(1, 0);
                pillar.RenderTransform = s;
                AddFx(pillar, x, top, 3);
                _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 1100, Anim.Out, delayMs: els.Count * 150);
                els.Add(pillar);
            }
        }
        return StopWith(els);
    }

    // =================================================================== The Campaign Trail

    /// <summary>Strings of bunting sway along the edges, campaign banners hang in the gaps and confetti falls now and then.</summary>
    private Action AmbientBunting()
    {
        Sfx.Play(SfxKind.Crowd, 0.5);
        var els = new List<UIElement>();
        var colors = new[] { Color.FromRgb(239, 68, 68), Colors.White, Color.FromRgb(59, 130, 246), Gold };
        foreach (double y0 in new[] { 4.0, BoardLayout.Height - 4 })
        {
            for (int span = 0; span < 4; span++)
            {
                double x0 = span * 400, sag = y0 < 500 ? 40 : -40;
                var line = new Canvas { Width = 400, Height = 60, RenderTransformOrigin = new Point(0.5, 0) };
                var rope = new Path { Data = Geo($"M 0,0 Q 200,{sag * 2} 400,0"), Stroke = Solid(Color.FromRgb(230, 220, 200), 200), StrokeThickness = 2 };
                line.Children.Add(rope);
                for (int k = 1; k < 12; k++)
                {
                    double f = k / 12.0, fx = f * 400, fy = 4 * sag * f * (1 - f);
                    double dir = y0 < 500 ? 1 : -1;
                    var flag = new Polygon
                    {
                        Points = new PointCollection { new(fx - 11, fy), new(fx + 11, fy), new(fx, fy + dir * 26) },
                        Fill = Solid(colors[(k + span) % colors.Length], 230),
                    };
                    line.Children.Add(flag);
                }
                var sway = new RotateTransform();
                line.RenderTransform = sway;
                AddFx(FadeIn(line, 1000), x0, y0, 4);
                sway.BeginAnimation(RotateTransform.AngleProperty, Loop(-1.5, 1.5, Rand(1.6, 2.4), reverse: true));
                els.Add(line);
            }
        }
        // Two banners in the free column.
        foreach (var (y, color) in new[] { (300.0, OpponentBolt), (700.0, PlayerBolt) })
        {
            var banner = new Grid { Width = 70, Height = 130, RenderTransformOrigin = new Point(0.5, 0) };
            banner.Children.Add(new Path
            {
                Data = Geometry.Parse("M 0,0 L 70,0 L 70,130 L 35,108 L 0,130 Z"),
                Fill = new LinearGradientBrush(color, Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2)), 90),
                Stroke = Solid(Gold),
                StrokeThickness = 3,
            });
            banner.Children.Add(new TextBlock { Text = "★", FontSize = 40, Foreground = Solid(Gold), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) });
            var sway = new RotateTransform();
            banner.RenderTransform = sway;
            AddFx(FadeIn(banner, 1000), 1090, y - 65, 3);
            sway.BeginAnimation(RotateTransform.AngleProperty, Loop(-3, 3, Rand(2, 3), reverse: true));
            els.Add(banner);
        }
        var confetti = Every(9, 15, () =>
        {
            Sfx.Play(SfxKind.Crowd, 0.25);
            for (int i = 0; i < FxSettings.Count(30); i++)
            {
                var bit = new Rectangle { Width = 8, Height = 4, Fill = Solid(colors[i % colors.Length]), RenderTransformOrigin = new Point(0.5, 0.5) };
                var t = new TranslateTransform();
                var rot = new RotateTransform();
                var g = new TransformGroup();
                g.Children.Add(rot);
                g.Children.Add(t);
                bit.RenderTransform = g;
                AddFx(bit, Rand(0, BoardLayout.Width), -10, 37);
                double ms = Rand(2500, 4000);
                _ = Anim.To(t, TranslateTransform.YProperty, BoardLayout.Height * Rand(0.4, 1), ms, null);
                _ = Anim.To(t, TranslateTransform.XProperty, Rand(-80, 80), ms, null);
                _ = Anim.To(rot, RotateTransform.AngleProperty, Rand(-720, 720), ms, null);
                _ = FadeAndRemoveAsync(bit, 600, ms - 600);
            }
        });
        return StopWith(els, confetti);
    }

    // =================================================================== Production Facility

    /// <summary>A conveyor belt runs along the near edge carrying parts and crates; cogs turn at each end.</summary>
    private Action AmbientConveyor()
    {
        var els = new List<UIElement>();
        const double y = BoardLayout.Height - 30, h = 26;
        var belt = new Rectangle { Width = BoardLayout.Width, Height = h, Fill = new LinearGradientBrush(Color.FromRgb(71, 85, 105), Color.FromRgb(30, 41, 59), 90) };
        AddFx(FadeIn(belt, 800), 0, y, 4);
        var treads = new Line
        {
            X1 = 0, Y1 = h / 2, X2 = BoardLayout.Width, Y2 = h / 2,
            Stroke = Solid(Color.FromRgb(148, 163, 184), 200),
            StrokeThickness = h - 6,
            StrokeDashArray = new DoubleCollection { 0.4, 1.2 },
        };
        AddFx(FadeIn(treads, 800), 0, y, 4);
        treads.BeginAnimation(Shape.StrokeDashOffsetProperty, Loop(0, -1.6 * 2, 1.2));
        els.Add(belt);
        els.Add(treads);
        foreach (double gx in new[] { 0.0, BoardLayout.Width })
        {
            var cog = Gear(10, 30, 23, 8);
            AddFx(cog.Path, gx - 30, y + h / 2 - 30, 5);
            cog.Rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 4));
            els.Add(cog.Path);
        }
        // Parts ride the belt.
        var parts = Every(1.2, 2.4, () =>
        {
            FrameworkElement part = _fxRng.Next(3) switch
            {
                0 => new Rectangle { Width = 26, Height = 20, Fill = Solid(Color.FromRgb(180, 120, 60)), Stroke = Solid(Color.FromRgb(90, 60, 30)), StrokeThickness = 2 },
                1 => Gear(8, 13, 10, 4).Path,
                _ => new Ellipse { Width = 18, Height = 18, Fill = new RadialGradientBrush(Color.FromRgb(226, 232, 240), Color.FromRgb(100, 116, 139)) },
            };
            var t = new TranslateTransform(-40, 0);
            if (part.RenderTransform is TransformGroup tg) tg.Children.Add(t);
            else part.RenderTransform = t;
            AddFx(part, 0, y - 18, 5);
            double ms = 9000;
            _ = Anim.To(t, TranslateTransform.XProperty, BoardLayout.Width + 40, ms, null)
                .ContinueWith(_ => RemoveFx(part), TaskScheduler.FromCurrentSynchronizationContext());
        }, startNow: true);
        var clank = Every(7, 12, () => Sfx.Play(SfxKind.Conveyor, 0.3), startNow: true);
        return StopWith(els, parts, clank);
    }

    // =================================================================== the Warzones that needed an idea

    /// <summary>
    /// Military Academy: three rank chevrons per player show the Deployment Turns left this round (they go dark as
    /// they are used), with a bugle call.
    /// </summary>
    private Action AmbientAcademy()
    {
        Sfx.Play(SfxKind.Bugle, 0.6);
        var els = new List<UIElement>();
        _academyChevrons.Clear();
        foreach (int side in new[] { 0, 1 })
        {
            var list = new List<Path>();
            for (int i = 0; i < 3; i++)
            {
                var chev = new Path
                {
                    Data = Geometry.Parse("M 0,0 L 30,18 L 60,0 L 60,12 L 30,30 L 0,12 Z"),
                    Fill = Solid(Gold),
                    Stroke = Solid(Color.FromRgb(120, 80, 10)),
                    StrokeThickness = 2,
                    Effect = Glow(Gold, 12),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                if (side == 1) chev.RenderTransform = new ScaleTransform(1, -1);
                double yy = side == 0 ? 560 + i * 34 : 410 - i * 34;
                AddFx(FadeIn(chev, 600 + i * 200), 1095, yy, 4);
                list.Add(chev);
                els.Add(chev);
            }
            _academyChevrons[side] = list;
        }
        UpdateAcademyChevrons();
        return StopWith(els);
    }

    private readonly Dictionary<int, List<Path>> _academyChevrons = new();

    /// <summary>Lights as many chevrons as each player has Deployment Turns left under Military Academy.</summary>
    private void UpdateAcademyChevrons()
    {
        if (_state is null || _academyChevrons.Count == 0) return;
        foreach (var (side, list) in _academyChevrons)
        {
            int left = Math.Max(0, 3 - State.Players[side].DeployTurnsThisRound);
            for (int i = 0; i < list.Count; i++)
                list[i].Fill = i < left ? Solid(Gold) : Solid(Color.FromRgb(60, 60, 70), 160);
        }
    }

    /// <summary>Mercenary Camp: a campfire in the free column, smoke curling up, and coins tossed now and then.</summary>
    private Action AmbientCampfire()
    {
        var at = new Point(1130, 930);
        var els = new List<UIElement>();
        for (int i = 0; i < 3; i++)
        {
            var log = new Rectangle { Width = 60, Height = 9, RadiusX = 4, RadiusY = 4, Fill = Solid(Color.FromRgb(92, 54, 24)), RenderTransform = new RotateTransform(-25 + i * 25, 30, 4.5) };
            AddFx(log, at.X - 30, at.Y, 4);
            els.Add(log);
        }
        for (int i = 0; i < 3; i++)
        {
            double w = 44 - i * 10, h = 60 - i * 12;
            var flame = new Path
            {
                Data = Geo($"M 0,{h} Q {-w * 0.1},{h * 0.4} {w / 2},0 Q {w * 1.1},{h * 0.4} {w},{h} Z"),
                Fill = new LinearGradientBrush(i == 2 ? Color.FromRgb(254, 249, 195) : Color.FromRgb(253, 186, 116), i == 0 ? Color.FromRgb(220, 38, 38) : Ember, 90),
                RenderTransformOrigin = new Point(0.5, 1),
                Effect = i == 0 ? Glow(Ember, 30) : null,
            };
            var s = new ScaleTransform(1, 1);
            flame.RenderTransform = s;
            AddFx(flame, at.X - w / 2, at.Y + 4 - h, 5);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(0.85, 1.12, Rand(0.18, 0.3), reverse: true));
            s.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(1.05, 0.92, Rand(0.22, 0.34), reverse: true));
            els.Add(flame);
        }
        var smoke = Every(0.5, 0.9, () => Steam(new Point(at.X + Rand(-8, 8), at.Y - 60)));
        var coins = Every(10, 16, () =>
        {
            Sfx.Play(SfxKind.Coins, 0.35);
            for (int i = 0; i < 4; i++)
            {
                var coin = new Ellipse { Width = 14, Height = 14, Fill = new RadialGradientBrush(Color.FromRgb(254, 240, 138), Color.FromRgb(202, 138, 4)), Stroke = Solid(Color.FromRgb(161, 98, 7)), StrokeThickness = 1.5,
                    RenderTransformOrigin = new Point(0.5, 0.5) };
                var t = new TranslateTransform();
                var flip = new ScaleTransform(1, 1);
                var g = new TransformGroup();
                g.Children.Add(flip);
                g.Children.Add(t);
                coin.RenderTransform = g;
                AddFx(coin, at.X - 7, at.Y - 30, 6);
                double ms = 900, dx = Rand(-60, 60);
                var arc = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(ms) };
                arc.KeyFrames.Add(new EasingDoubleKeyFrame(-Rand(90, 150), KeyTime.FromPercent(0.45), Anim.Out));
                arc.KeyFrames.Add(new EasingDoubleKeyFrame(10, KeyTime.FromPercent(1), Anim.In));
                t.BeginAnimation(TranslateTransform.YProperty, arc);
                _ = Anim.To(t, TranslateTransform.XProperty, dx, ms, null);
                flip.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, -1, Anim.Ms(150)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                _ = FadeAndRemoveAsync(coin, 250, ms);
            }
        });
        var crackle = Every(8, 13, () => Sfx.Play(SfxKind.Crackle, 0.2));
        return StopWith(els, smoke, coins, crackle);
    }

    /// <summary>Training Grounds: practice targets in the free column; darts thunk into them now and then.</summary>
    private Action AmbientTargets()
    {
        var els = new List<UIElement>();
        var targets = new[] { new Point(1125, 270), new Point(1125, 760) };
        foreach (var c in targets)
        {
            var host = new Grid { Width = 110, Height = 110, RenderTransformOrigin = new Point(0.5, 0.5) };
            for (int r = 0; r < 4; r++)
                host.Children.Add(new Ellipse { Width = 110 - r * 26, Height = 110 - r * 26, Fill = Solid(r % 2 == 0 ? Color.FromRgb(220, 38, 38) : Colors.White, 200) });
            host.Effect = Glow(Red, 14);
            AddFx(FadeIn(host, 900, 0.75), c.X - 55, c.Y - 55, 3);
            els.Add(host);
        }
        var darts = Every(4, 8, () =>
        {
            var c = targets[_fxRng.Next(targets.Length)];
            var hit = new Point(c.X + Rand(-26, 26), c.Y + Rand(-26, 26));
            var dart = new Line { X1 = 0, Y1 = 0, X2 = 22, Y2 = -14, Stroke = Solid(Gold), StrokeThickness = 3 };
            var t = new TranslateTransform(-300, 200);
            dart.RenderTransform = t;
            AddFx(dart, hit.X, hit.Y, 6);
            _ = Anim.To(t, TranslateTransform.XProperty, 0, 220, null);
            _ = Anim.To(t, TranslateTransform.YProperty, 0, 220, null).ContinueWith(_ =>
            {
                Sfx.Play(SfxKind.Bullseye, 0.35);
                SparkBurst(hit, Gold, 5, 24, 4);
                _ = FadeAndRemoveAsync(dart, 500, 1800);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        });
        return StopWith(els, darts);
    }

    /// <summary>Vivisection Laboratory: surgical lamps light the rows, a red scanning laser sweeps across and a heart monitor traces along the far edge.</summary>
    private Action AmbientLab()
    {
        var els = new List<UIElement>();
        foreach (double y in new[] { 360.0, 640.0 })
        {
            var lamp = new Ellipse
            {
                Width = 900,
                Height = 300,
                Fill = new RadialGradientBrush(Color.FromArgb(55, 240, 253, 250), Color.FromArgb(0, 240, 253, 250)),
            };
            AddFx(FadeIn(lamp, 1200), 760 - 450 + 30, y - 150, 2);
            els.Add(lamp);
        }
        var laser = new Rectangle
        {
            Width = 4,
            Height = BoardLayout.Height,
            Fill = Solid(Color.FromRgb(248, 113, 113), 200),
            Effect = Glow(Red, 16),
        };
        var lt = new TranslateTransform();
        laser.RenderTransform = lt;
        AddFx(laser, 0, 0, 34);
        lt.BeginAnimation(TranslateTransform.XProperty, Loop(0, BoardLayout.Width, 7, reverse: true));
        els.Add(laser);

        // The heart monitor along the far edge.
        var pts = new PointCollection();
        for (double x = 0; x <= BoardLayout.Width; x += 8)
        {
            double phase = x % 200;
            double yv = phase is > 90 and < 100 ? -22 : phase is >= 100 and < 108 ? 16 : phase is >= 108 and < 114 ? -6 : 0;
            pts.Add(new Point(x, 22 + yv));
        }
        var ecg = new Polyline
        {
            Points = pts,
            Stroke = Solid(Color.FromRgb(74, 222, 128), 220),
            StrokeThickness = 2.5,
            Effect = Glow(Green, 10),
            StrokeDashArray = new DoubleCollection { 60, 400 },
        };
        AddFx(ecg, 0, 0, 4);
        ecg.BeginAnimation(Shape.StrokeDashOffsetProperty, Loop(0, -460, 2.2));
        els.Add(ecg);
        var beep = Every(5, 9, () => Sfx.Play(SfxKind.Scalpel, 0.3), startNow: true);
        return StopWith(els, beep);
    }

    /// <summary>Blaarxis: bubbling slime pools ooze in the corners of the board.</summary>
    private Action AmbientSlimePools()
    {
        var els = new List<UIElement>();
        var pools = new[] { new Point(40, 40), new Point(BoardLayout.Width - 40, 40), new Point(40, BoardLayout.Height - 40), new Point(BoardLayout.Width - 40, BoardLayout.Height - 40), new Point(1130, 500) };
        foreach (var c in pools.Take(FxSettings.HighQuality ? 5 : 4))
        {
            double r = c.X == 1130 ? 50 : 90;
            var pool = new Path
            {
                Data = BlobGeometry(r, 12, 0.35),
                Width = r * 2,
                Height = r * 2,
                Fill = new RadialGradientBrush(Color.FromArgb(220, SlimeLight.R, SlimeLight.G, SlimeLight.B), Color.FromArgb(220, SlimeDark.R, SlimeDark.G, SlimeDark.B)) { GradientOrigin = new Point(0.4, 0.35) },
                Stroke = Solid(Color.FromRgb(30, 90, 10)),
                StrokeThickness = 3,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = Glow(SlimeDark, 16),
            };
            var s = new ScaleTransform(1, 1);
            pool.RenderTransform = s;
            AddFx(FadeIn(pool, 1200, 0.85), c.X - r, c.Y - r, 3);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(0.95, 1.05, Rand(1.8, 2.6), reverse: true));
            s.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(1.04, 0.96, Rand(1.6, 2.4), reverse: true));
            els.Add(pool);
        }
        var bubbles = Every(0.4, 1.0, () =>
        {
            var c = pools[_fxRng.Next(pools.Length)];
            double r = c.X == 1130 ? 30 : 55;
            var at = new Point(c.X + Rand(-r, r), c.Y + Rand(-r, r));
            double size = Rand(8, 20);
            var b = new Ellipse { Width = size, Height = size, Fill = Solid(SlimeLight, 160), Stroke = Solid(Colors.White, 120), StrokeThickness = 1, RenderTransformOrigin = new Point(0.5, 0.5) };
            var s = new ScaleTransform(0.2, 0.2);
            b.RenderTransform = s;
            AddFx(b, at.X - size / 2, at.Y - size / 2, 4);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 700, Anim.Out);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 700, Anim.Out).ContinueWith(_ =>
            {
                if (_fxRng.NextDouble() < 0.3) Sfx.Play(SfxKind.Bubble, 0.25, minGapMs: 300);
                SparkBurst(at, SlimeLight, 4, 16, 3);
                RemoveFx(b);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        });
        return StopWith(els, bubbles);
    }

    /// <summary>Primordial Marsh: reeds sway along the edges, murky water patches, fireflies wander and bubbles rise.</summary>
    private Action AmbientMarsh()
    {
        var els = new List<UIElement>();
        var murk = new Rectangle
        {
            Width = BoardLayout.Width,
            Height = BoardLayout.Height,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(70, 20, 60, 30), 0),
                new(Color.FromArgb(0, 20, 60, 30), 0.25),
                new(Color.FromArgb(0, 20, 60, 30), 0.75),
                new(Color.FromArgb(70, 20, 60, 30), 1),
            }, 90),
        };
        AddFx(FadeIn(murk, 1500), 0, 0, 2);
        els.Add(murk);
        for (int i = 0; i < FxSettings.Count(40); i++)
        {
            bool bottom = i % 2 == 0;
            double x = Rand(0, BoardLayout.Width), h = Rand(40, 90);
            var reed = new Path
            {
                Data = Geo($"M 0,0 Q {Rand(-8, 8)},{-h / 2} {Rand(-14, 14)},{-h}"),
                Stroke = Solid(Color.FromRgb((byte)Rand(60, 110), (byte)Rand(110, 150), 40), 220),
                StrokeThickness = Rand(2, 4),
                StrokeEndLineCap = PenLineCap.Round,
            };
            var sway = new SkewTransform();
            var flip = new ScaleTransform(1, bottom ? 1 : -1);
            var g = new TransformGroup();
            g.Children.Add(sway);
            g.Children.Add(flip);
            reed.RenderTransform = g;
            AddFx(reed, x, bottom ? BoardLayout.Height + 4 : -4, 4);
            sway.BeginAnimation(SkewTransform.AngleXProperty, Loop(-8, 8, Rand(1.8, 3.2), reverse: true, beginSeconds: Rand(0, 2)));
            els.Add(reed);
        }
        for (int i = 0; i < FxSettings.Count(16); i++)
        {
            var fly = new Ellipse { Width = 5, Height = 5, Fill = Solid(Color.FromRgb(217, 249, 157)), Effect = Glow(Color.FromRgb(190, 242, 100), 10) };
            var t = new TranslateTransform();
            fly.RenderTransform = t;
            AddFx(fly, Rand(0, BoardLayout.Width), Rand(0, BoardLayout.Height), 36);
            t.BeginAnimation(TranslateTransform.XProperty, Loop(-Rand(40, 120), Rand(40, 120), Rand(4, 8), reverse: true));
            t.BeginAnimation(TranslateTransform.YProperty, Loop(-Rand(30, 80), Rand(30, 80), Rand(3, 6), reverse: true));
            fly.BeginAnimation(OpacityProperty, Loop(0, 1, Rand(0.6, 1.4), reverse: true, beginSeconds: Rand(0, 2)));
            els.Add(fly);
        }
        var bubbles = Every(2, 4, () =>
        {
            Sfx.Play(SfxKind.Bubble, 0.2, minGapMs: 500);
            var at = new Point(Rand(0, BoardLayout.Width), _fxRng.NextDouble() < 0.5 ? Rand(10, 120) : Rand(880, 990));
            SparkBurst(at, Color.FromRgb(132, 204, 22), 5, 20, 5);
        });
        return StopWith(els, bubbles);
    }

    /// <summary>Refraction Network: crystals round the table pass prismatic beams between them; the beams shift every few seconds.</summary>
    private Action AmbientPrisms()
    {
        Sfx.Play(SfxKind.Prism, 0.6);
        var els = new List<UIElement>();
        var crystals = new[] { new Point(30, 30), new Point(800, 18), new Point(1570, 30), new Point(1130, 500), new Point(229, 500), new Point(30, 970), new Point(800, 985), new Point(1570, 970) };
        foreach (var c in crystals)
        {
            var gem = new Polygon
            {
                Points = new PointCollection { new(0, -18), new(12, 0), new(0, 18), new(-12, 0) },
                Fill = new LinearGradientBrush(Color.FromRgb(224, 242, 254), Color.FromRgb(167, 139, 250), 45),
                Stroke = Solid(Colors.White),
                StrokeThickness = 1.5,
                Effect = Glow(Colors.White, 14),
            };
            AddFx(FadeIn(gem, 800), c.X, c.Y, 5);
            els.Add(gem);
        }
        var rainbow = new[] { Color.FromRgb(248, 113, 113), Color.FromRgb(251, 191, 36), Color.FromRgb(74, 222, 128), Color.FromRgb(56, 189, 248), Color.FromRgb(167, 139, 250) };
        var current = new List<UIElement>();
        void Rebeam()
        {
            foreach (var old in current) _ = FadeAndRemoveAsync(old, 700);
            current.Clear();
            int n = FxSettings.HighQuality ? 4 : 2;
            int start = _fxRng.Next(crystals.Length);
            for (int k = 0; k < n; k++)
            {
                var a = crystals[(start + k * 3) % crystals.Length];
                var b = crystals[(start + k * 3 + 1 + _fxRng.Next(crystals.Length - 1)) % crystals.Length];
                var beam = new Line
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                    Stroke = new LinearGradientBrush(new GradientStopCollection(rainbow.Select((c, i) => new GradientStop(Color.FromArgb(150, c.R, c.G, c.B), i / 4.0))), 0),
                    StrokeThickness = 5,
                    Effect = SoftBlur(2),
                };
                AddFx(FadeIn(beam, 700), 0, 0, 33);
                current.Add(beam);
            }
            Sfx.Play(SfxKind.Prism, 0.2, minGapMs: 2000);
        }
        Rebeam();
        var shift = Every(4, 7, Rebeam);
        return () =>
        {
            StopWith(els, shift)();
            foreach (var b in current) _ = FadeAndRemoveAsync(b, 500);
        };
    }

    /// <summary>Vacuum Spire: a tall spire in the free column draws dust up towards its tip; rings pulse up the spire.</summary>
    private Action AmbientSpire()
    {
        var els = new List<UIElement>();
        var tip = new Point(1130, 70);
        var spire = new Path
        {
            Data = Geometry.Parse("M -30,360 L -8,0 L 0,-40 L 8,0 L 30,360 Z"),
            Fill = new LinearGradientBrush(Color.FromRgb(100, 116, 139), Color.FromRgb(15, 23, 42), 0),
            Stroke = Solid(Cyan, 180),
            StrokeThickness = 2,
            Effect = Glow(Cyan, 16),
        };
        AddFx(FadeIn(spire, 1000), tip.X, tip.Y + 40, 3);
        els.Add(spire);
        var rings = Every(1.2, 1.8, () =>
        {
            var ring = new Ellipse { Width = 80, Height = 22, Stroke = Solid(Cyan, 200), StrokeThickness = 2.5 };
            var t = new TranslateTransform(0, 380);
            var s = new ScaleTransform(1, 1, 40, 11);
            var g = new TransformGroup();
            g.Children.Add(s);
            g.Children.Add(t);
            ring.RenderTransform = g;
            AddFx(ring, tip.X - 40, tip.Y, 4);
            _ = Anim.To(t, TranslateTransform.YProperty, 10, 1500, Anim.In);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 0.2, 1500, Anim.In);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 0.2, 1500, Anim.In);
            _ = FadeAndRemoveAsync(ring, 300, 1300);
        }, startNow: true);
        var dust = Every(0.08, 0.2, () =>
        {
            var from = new Point(Rand(700, 1600), Rand(150, 900));
            var mote = new Ellipse { Width = 4, Height = 4, Fill = Solid(Dust, 200) };
            var t = new TranslateTransform();
            mote.RenderTransform = t;
            AddFx(mote, from.X, from.Y, 35);
            double ms = Rand(1400, 2200);
            _ = Anim.To(t, TranslateTransform.XProperty, tip.X - from.X, ms, Anim.In);
            _ = Anim.To(t, TranslateTransform.YProperty, tip.Y - from.Y, ms, Anim.In);
            _ = FadeAndRemoveAsync(mote, 200, ms - 200);
        });
        var suck = Every(8, 13, () => Sfx.Play(SfxKind.Suction, 0.3), startNow: true);
        return StopWith(els, rings, dust, suck);
    }
}
