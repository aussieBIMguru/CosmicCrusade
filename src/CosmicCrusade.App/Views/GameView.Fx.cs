using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CosmicCrusade.App.Board;
using CosmicCrusade.App.Controls;
using Path = System.Windows.Shapes.Path;

namespace CosmicCrusade.App.Views;

/// <summary>
/// The effects library. Everything here draws on <c>BoardCanvas</c> in board coordinates (1600×1000), so it tilts with
/// the table, and cleans up after itself. Effects are fire-and-forget unless the caller awaits them.
/// </summary>
public partial class GameView
{
    private static readonly Color SlimeLight = Color.FromRgb(190, 255, 96);
    private static readonly Color SlimeDark = Color.FromRgb(46, 125, 18);
    private static readonly Color DecayGreen = Color.FromRgb(132, 168, 46);
    private static readonly Color DecayPurple = Color.FromRgb(88, 52, 112);
    private static readonly Color Ember = Color.FromRgb(249, 115, 22);
    private static readonly Color PsiViolet = Color.FromRgb(168, 85, 247);
    private static readonly Color PlayerBolt = Color.FromRgb(96, 205, 255);
    private static readonly Color OpponentBolt = Color.FromRgb(255, 92, 92);

    private readonly Random _fxRng = new();

    // =================================================================== plumbing

    /// <summary>Adds an element to the board at (x, y) above the cards.</summary>
    private T AddFx<T>(T element, double x, double y, int z = 40) where T : UIElement
    {
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        Panel.SetZIndex(element, z);
        BoardCanvas.Children.Add(element);
        return element;
    }

    private void RemoveFx(UIElement element) => BoardCanvas.Children.Remove(element);

    /// <summary>Fades an element out (after an optional delay) and removes it.</summary>
    private async Task FadeAndRemoveAsync(UIElement element, double ms, double delayMs = 0)
    {
        await Anim.To(element, OpacityProperty, 0, ms, Anim.In, delayMs: delayMs);
        RemoveFx(element);
    }

    /// <summary>The centre of a board card as it sits right now.</summary>
    private static Point BoardCenterOf(CardView v) =>
        new(Canvas.GetLeft(v) + v.Width / 2, Canvas.GetTop(v) + v.Height / 2);

    private static Rect BoardRectOf(CardView v) => new(Canvas.GetLeft(v), Canvas.GetTop(v), v.Width, v.Height);

    private double Rand(double min, double max) => min + _fxRng.NextDouble() * (max - min);

    private static SolidColorBrush Solid(Color c, byte alpha = 255) => new(Color.FromArgb(alpha, c.R, c.G, c.B));

    /// <summary>A small burst of glowing dots (impacts, cog sparks).</summary>
    private void SparkBurst(Point at, Color color, int count = 8, double distance = 50, double size = 6)
    {
        count = FxSettings.Count(count);
        for (int i = 0; i < count; i++)
        {
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Solid(Lighten(color, 80)),
                Effect = new DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0 },
            };
            var t = new TranslateTransform();
            dot.RenderTransform = t;
            AddFx(dot, at.X - size / 2, at.Y - size / 2, 45);
            double a = Rand(0, Math.PI * 2), d = Rand(distance * 0.4, distance), ms = Rand(220, 420);
            _ = Anim.To(t, TranslateTransform.XProperty, Math.Cos(a) * d, ms, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, Math.Sin(a) * d, ms, Anim.Out);
            _ = FadeAndRemoveAsync(dot, ms);
        }
    }

    private static Color Lighten(Color c, int by) =>
        Color.FromRgb((byte)Math.Min(255, c.R + by), (byte)Math.Min(255, c.G + by), (byte)Math.Min(255, c.B + by));

    // =================================================================== Assemble: cogs

    /// <summary>Two meshing cogs spin up over the Unit while the part is fitted.</summary>
    private async Task CogSpinAsync(Point center, double ms = 1000)
    {
        Sfx.Play(SfxKind.Cog);
        var big = Gear(12, 52, 42, 15);
        var small = Gear(8, 34, 26, 10);
        // Mesh: the centres are a tooth-depth less than the sum of the outer radii apart.
        var bigAt = new Point(center.X - 22, center.Y + 14);
        var smallAt = new Point(bigAt.X + 52 + 34 - 8, bigAt.Y - 30);
        AddFx(big.Path, bigAt.X - big.R, bigAt.Y - big.R, 46);
        AddFx(small.Path, smallAt.X - small.R, smallAt.Y - small.R, 47);

        double turns = 0.75;
        var tasks = new List<Task>();
        foreach (var (g, dir, ratio) in new[] { (big, 1.0, 1.0), (small, -1.0, 12 / 8.0) })
        {
            tasks.Add(Anim.To(g.Scale, ScaleTransform.ScaleXProperty, 1, 260, Anim.Back, from: 0.1));
            tasks.Add(Anim.To(g.Scale, ScaleTransform.ScaleYProperty, 1, 260, Anim.Back, from: 0.1));
            tasks.Add(Anim.To(g.Rotate, RotateTransform.AngleProperty, dir * 360 * turns * ratio, ms, Anim.InOut, from: 0));
        }

        // Sparks where the teeth meet.
        var mesh = new Point((bigAt.X + smallAt.X) / 2 + 4, (bigAt.Y + smallAt.Y) / 2);
        for (int i = 0; i < 4; i++)
        {
            await Anim.Delay(ms / 5);
            SparkBurst(mesh, Gold, 6, 34, 5);
        }
        await Task.WhenAll(tasks);
        _ = FadeAndRemoveAsync(big.Path, 260);
        await FadeAndRemoveAsync(small.Path, 260);
    }

    private sealed record GearShape(Path Path, double R, ScaleTransform Scale, RotateTransform Rotate);

    private static GearShape Gear(int teeth, double outer, double inner, double hole)
    {
        var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = geo.Open())
        {
            double step = Math.PI * 2 / teeth;
            Point P(double a, double r) => new(outer + Math.Cos(a) * r, outer + Math.Sin(a) * r);
            ctx.BeginFigure(P(0, inner), true, true);
            for (int k = 0; k < teeth; k++)
            {
                double a0 = k * step;
                ctx.LineTo(P(a0 + step * 0.18, outer), true, true);
                ctx.LineTo(P(a0 + step * 0.48, outer), true, true);
                ctx.LineTo(P(a0 + step * 0.66, inner), true, true);
                ctx.LineTo(P(a0 + step, inner), true, true);
            }
            // The axle hole.
            ctx.BeginFigure(new Point(outer + hole, outer), true, true);
            ctx.ArcTo(new Point(outer - hole, outer), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true, true);
            ctx.ArcTo(new Point(outer + hole, outer), new Size(hole, hole), 0, false, SweepDirection.Clockwise, true, true);
        }
        geo.Freeze();

        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        var group = new TransformGroup();
        group.Children.Add(rotate);
        group.Children.Add(scale);
        var path = new Path
        {
            Data = geo,
            Width = outer * 2,
            Height = outer * 2,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(245, 236, 210), 0),
                    new(Color.FromRgb(156, 163, 175), 0.45),
                    new(Color.FromRgb(214, 170, 72), 1),
                }, 45),
            Stroke = new SolidColorBrush(Color.FromRgb(55, 48, 40)),
            StrokeThickness = 2.5,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group,
            Effect = new DropShadowEffect { Color = Gold, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 },
        };
        return new GearShape(path, outer, scale, rotate);
    }

    // =================================================================== Manipulate: slime

    /// <summary>A green slime splat lands on the Unit, throws droplets, drips and fades.</summary>
    private async Task SlimeSplatAsync(Point center, CardView? card)
    {
        Sfx.Play(SfxKind.Splat);
        var blob = new Path
        {
            Data = BlobGeometry(60, 14, 0.42),
            Width = 120,
            Height = 120,
            Fill = new RadialGradientBrush(SlimeLight, SlimeDark) { GradientOrigin = new Point(0.38, 0.32) },
            Stroke = new SolidColorBrush(Color.FromRgb(30, 90, 10)),
            StrokeThickness = 3,
            Opacity = 0.93,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { Color = SlimeDark, BlurRadius = 16, ShadowDepth = 0 },
        };
        var blobScale = new ScaleTransform(0.15, 0.15);
        blob.RenderTransform = blobScale;
        AddFx(blob, center.X - 60, center.Y - 60, 48);

        var shine = new Ellipse { Width = 26, Height = 14, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) };
        shine.RenderTransform = new RotateTransform(-30);
        shine.Opacity = 0;
        AddFx(shine, center.X - 30, center.Y - 34, 49);

        // Splat in, with the card squashing under the hit.
        _ = Anim.To(blobScale, ScaleTransform.ScaleXProperty, 1, 200, Anim.Back);
        _ = Anim.To(blobScale, ScaleTransform.ScaleYProperty, 1, 200, Anim.Back);
        _ = Anim.To(shine, OpacityProperty, 1, 200, Anim.Out, delayMs: 120);
        if (card is not null)
        {
            _ = Anim.To(card.Scale, ScaleTransform.ScaleXProperty, 1, 380, new ElasticEase { Oscillations = 2, Springiness = 4 }, from: 1.14);
            _ = Anim.To(card.Scale, ScaleTransform.ScaleYProperty, 1, 380, new ElasticEase { Oscillations = 2, Springiness = 4 }, from: 0.86);
        }

        // Droplets fly out and stick.
        var drops = new List<UIElement>();
        for (int i = 0; i < 11; i++)
        {
            double size = Rand(8, 18);
            var d = new Ellipse
            {
                Width = size,
                Height = size * Rand(0.8, 1.2),
                Fill = new RadialGradientBrush(SlimeLight, SlimeDark),
                Opacity = 0.9,
            };
            var t = new TranslateTransform();
            d.RenderTransform = t;
            AddFx(d, center.X - size / 2, center.Y - size / 2, 47);
            drops.Add(d);
            double a = Rand(0, Math.PI * 2), dist = Rand(60, 130);
            _ = Anim.To(t, TranslateTransform.XProperty, Math.Cos(a) * dist, 260, Anim.Out);
            _ = Anim.To(t, TranslateTransform.YProperty, Math.Sin(a) * dist, 260, Anim.Out);
        }

        // Drips run down from the blob.
        for (int i = 0; i < 4; i++)
        {
            var drip = new Ellipse { Width = 8, Height = 14, Fill = Solid(SlimeDark, 230) };
            var t = new TranslateTransform();
            var s = new ScaleTransform(1, 1, 4, 0);
            var g = new TransformGroup();
            g.Children.Add(s);
            g.Children.Add(t);
            drip.RenderTransform = g;
            AddFx(drip, center.X - 34 + i * 20 + Rand(-5, 5), center.Y + Rand(20, 40), 47);
            drops.Add(drip);
            _ = Anim.To(t, TranslateTransform.YProperty, Rand(40, 75), 900, Anim.In, delayMs: 200 + i * 60);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 2.2, 900, Anim.In, delayMs: 200 + i * 60);
        }

        await Anim.Delay(900);
        foreach (var d in drops) _ = FadeAndRemoveAsync(d, 500);
        _ = FadeAndRemoveAsync(shine, 400);
        await FadeAndRemoveAsync(blob, 500);
    }

    /// <summary>A lumpy closed blob (smooth curve through jittered radii).</summary>
    private Geometry BlobGeometry(double radius, int lobes, double jitter)
    {
        var pts = new List<Point>();
        for (int i = 0; i < lobes; i++)
        {
            double a = Math.PI * 2 * i / lobes;
            double r = radius * (1 - jitter / 2 + Rand(0, jitter)) * (i % 3 == 0 ? 1.05 : 0.9);
            pts.Add(new Point(radius + Math.Cos(a) * r, radius + Math.Sin(a) * r));
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            ctx.BeginFigure(Mid(pts[^1], pts[0]), true, true);
            for (int i = 0; i < pts.Count; i++)
                ctx.QuadraticBezierTo(pts[i], Mid(pts[i], pts[(i + 1) % pts.Count]), true, true);
        }
        geo.Freeze();
        return geo;
    }

    // =================================================================== Duel: blasters and smoke

    /// <summary>Blaster bolts fly back and forth between the two Units; the loser(s) get smoked out.</summary>
    private async Task BlasterDuelAsync(CardView a, CardView b, int sideA, int sideB, IReadOnlyCollection<CardView> losers,
        IReadOnlyCollection<CardView> winners)
    {
        var pa = BoardCenterOf(a);
        var pb = BoardCenterOf(b);
        var colorA = sideA == Me ? PlayerBolt : OpponentBolt;
        var colorB = sideB == Me ? PlayerBolt : OpponentBolt;
        if (sideA == sideB) colorB = Gold;

        // Four volleys, alternating, slightly overlapping.
        var shots = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            bool fromA = i % 2 == 0;
            shots.Add(BoltAsync(fromA ? pa : pb, fromA ? pb : pa, fromA ? colorA : colorB, fromA ? b : a));
            await Anim.Delay(230);
        }
        await Task.WhenAll(shots);

        foreach (var w in winners)
        {
            w.SetGlow(Gold, pulse: true);
            _ = Anim.Delay(700).ContinueWith(_ => w.SetGlow(null), TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (losers.Count > 0)
        {
            Sfx.Play(SfxKind.Smoke);
            await Task.WhenAll(losers.Select(SmokeOutAsync));
        }
    }

    private async Task BoltAsync(Point from, Point to, Color color, CardView target)
    {
        Sfx.Play(SfxKind.Blaster, 0.8);
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

        // Muzzle flash.
        var flash = new Ellipse { Width = 40, Height = 40, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, color.R, color.G, color.B)) };
        AddFx(flash, from.X - 20, from.Y - 20, 50);
        _ = FadeAndRemoveAsync(flash, 160);

        // The bolt: a hot white core with a coloured glow, pointing where it flies.
        const double len = 54, thick = 9;
        var bolt = new Rectangle
        {
            Width = len,
            Height = thick,
            RadiusX = thick / 2,
            RadiusY = thick / 2,
            Fill = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, 0),
            Stroke = Solid(color),
            StrokeThickness = 2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { Color = color, BlurRadius = 22, ShadowDepth = 0 },
        };
        var t = new TranslateTransform();
        var g = new TransformGroup();
        g.Children.Add(new RotateTransform(angle));
        g.Children.Add(t);
        bolt.RenderTransform = g;
        AddFx(bolt, from.X - len / 2, from.Y - thick / 2, 51);

        double ms = 170;
        _ = Anim.To(t, TranslateTransform.XProperty, to.X - from.X, ms, null);
        await Anim.To(t, TranslateTransform.YProperty, to.Y - from.Y, ms, null);
        RemoveFx(bolt);

        // Impact.
        Sfx.Play(SfxKind.Impact, 0.6);
        SparkBurst(to, color, 9, 46, 6);
        var hit = new Ellipse { Width = 56, Height = 56, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, color.R, color.G, color.B)) };
        AddFx(hit, to.X - 28, to.Y - 28, 50);
        _ = FadeAndRemoveAsync(hit, 200);
        _ = Anim.To(target.Rotate, RotateTransform.AngleProperty, 0, 220, Anim.Out, from: Rand(-5, 5));
    }

    /// <summary>Scorched: the card darkens and grey smoke billows off it, with a few embers.</summary>
    private async Task SmokeOutAsync(CardView v)
    {
        var rect = BoardRectOf(v);
        var soot = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new RadialGradientBrush(Color.FromArgb(120, 20, 16, 12), Color.FromArgb(230, 10, 8, 6)),
            Opacity = 0,
        };
        AddFx(soot, rect.X, rect.Y, 11);
        _ = Anim.To(soot, OpacityProperty, 0.85, 500, Anim.Out);
        v.SetGlow(Color.FromRgb(60, 60, 60));

        var puffs = new List<Task>();
        for (int i = 0; i < FxSettings.Count(14); i++)
        {
            double size = Rand(34, 64);
            byte grey = (byte)Rand(90, 170);
            var puff = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new RadialGradientBrush(Color.FromArgb(200, grey, grey, grey), Color.FromArgb(0, grey, grey, grey)),
                Effect = new BlurEffect { Radius = 6 },
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            var t = new TranslateTransform();
            var s = new ScaleTransform(0.5, 0.5);
            var g = new TransformGroup();
            g.Children.Add(s);
            g.Children.Add(t);
            puff.RenderTransform = g;
            AddFx(puff, rect.X + Rand(0, rect.Width) - size / 2, rect.Y + Rand(rect.Height * 0.3, rect.Height) - size / 2, 52);
            double ms = Rand(900, 1400), delay = i * 45;
            _ = Anim.To(t, TranslateTransform.YProperty, -Rand(90, 170), ms, Anim.Out, delayMs: delay);
            _ = Anim.To(t, TranslateTransform.XProperty, Rand(-40, 40), ms, Anim.Out, delayMs: delay);
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, Rand(1.5, 2.2), ms, Anim.Out, delayMs: delay);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, Rand(1.5, 2.2), ms, Anim.Out, delayMs: delay);
            puffs.Add(FadeAndRemoveAsync(puff, ms, delay));
        }
        for (int i = 0; i < 8; i++)
        {
            var ember = new Ellipse { Width = 5, Height = 5, Fill = Solid(Ember), Effect = new DropShadowEffect { Color = Ember, BlurRadius = 8, ShadowDepth = 0 } };
            var t = new TranslateTransform();
            ember.RenderTransform = t;
            AddFx(ember, rect.X + Rand(10, rect.Width - 10), rect.Y + Rand(rect.Height * 0.4, rect.Height - 10), 53);
            double ms = Rand(600, 1000);
            _ = Anim.To(t, TranslateTransform.YProperty, -Rand(60, 130), ms, Anim.Out);
            _ = Anim.To(t, TranslateTransform.XProperty, Rand(-25, 25), ms, Anim.Out);
            _ = FadeAndRemoveAsync(ember, ms);
        }
        await Anim.Delay(700);
        _ = FadeAndRemoveAsync(soot, 600, 600); // the Nullify that follows takes the card itself
    }

    // =================================================================== card draws: psionic aura

    private readonly Dictionary<int, DateTime> _auraShown = new();

    /// <summary>
    /// An Overmind-style psionic pulse: rings ripple out, a violet glow breathes and motes orbit. Shown once per
    /// effect even when it draws several cards.
    /// </summary>
    private void PsionicAura(Point center, int sourceId)
    {
        var now = DateTime.UtcNow;
        if (_auraShown.TryGetValue(sourceId, out var last) && (now - last).TotalMilliseconds < 1500) return;
        _auraShown[sourceId] = now;
        Sfx.Play(SfxKind.Aura);

        // The breathing glow.
        var glow = new Ellipse
        {
            Width = 260,
            Height = 260,
            Fill = new RadialGradientBrush(Color.FromArgb(150, PsiViolet.R, PsiViolet.G, PsiViolet.B), Color.FromArgb(0, PsiViolet.R, PsiViolet.G, PsiViolet.B)),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = 0,
        };
        var gs = new ScaleTransform(0.5, 0.5);
        glow.RenderTransform = gs;
        AddFx(glow, center.X - 130, center.Y - 130, 8);
        _ = Anim.To(glow, OpacityProperty, 1, 300, Anim.Out);
        _ = Anim.To(gs, ScaleTransform.ScaleXProperty, 1.1, 1100, new SineEase());
        _ = Anim.To(gs, ScaleTransform.ScaleYProperty, 1.1, 1100, new SineEase());
        _ = FadeAndRemoveAsync(glow, 500, 900);

        // Three ripples.
        for (int i = 0; i < 3; i++)
        {
            var ring = new Ellipse
            {
                Width = 240,
                Height = 240,
                Stroke = Solid(Lighten(PsiViolet, 40)),
                StrokeThickness = 5,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new BlurEffect { Radius = 4 },
            };
            var s = new ScaleTransform(0.25, 0.25);
            ring.RenderTransform = s;
            AddFx(ring, center.X - 120, center.Y - 120, 44);
            double delay = i * 220;
            _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1.35, 900, Anim.Out, delayMs: delay);
            _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1.35, 900, Anim.Out, delayMs: delay);
            ring.Opacity = 0;
            _ = Anim.To(ring, OpacityProperty, 1, 120, null, delayMs: delay)
                .ContinueWith(_ => FadeAndRemoveAsync(ring, 780), TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Orbiting motes.
        var orbit = new Canvas { Width = 0, Height = 0 };
        var spin = new RotateTransform(0);
        orbit.RenderTransform = spin;
        for (int i = 0; i < 10; i++)
        {
            double a = Math.PI * 2 * i / 10, r = 84 + (i % 2) * 18;
            var mote = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Brushes.White,
                Effect = new DropShadowEffect { Color = PsiViolet, BlurRadius = 12, ShadowDepth = 0 },
            };
            Canvas.SetLeft(mote, Math.Cos(a) * r - 3.5);
            Canvas.SetTop(mote, Math.Sin(a) * r - 3.5);
            orbit.Children.Add(mote);
        }
        AddFx(orbit, center.X, center.Y, 45);
        _ = Anim.To(spin, RotateTransform.AngleProperty, 300, 1300, Anim.InOut);
        _ = FadeAndRemoveAsync(orbit, 400, 900);
    }

    // =================================================================== Nullify: death

    /// <summary>
    /// The Unit dies: a white flash, it burns dark, then disintegrates into ash and embers from the bottom up while a
    /// skull rises. The card view is removed afterwards.
    /// </summary>
    private async Task DeathAsync(CardView v)
    {
        Sfx.Play(SfxKind.Death);
        var rect = BoardRectOf(v);
        var center = BoardCenterOf(v);
        v.HideRank();
        v.SetGlow(Red, pulse: false);

        var flash = new Rectangle { Width = rect.Width, Height = rect.Height, RadiusX = 8, RadiusY = 8, Fill = Brushes.White, Opacity = FxSettings.Flash(0.95) };
        AddFx(flash, rect.X, rect.Y, 12);
        _ = FadeAndRemoveAsync(flash, 160);

        var burn = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new LinearGradientBrush(Color.FromArgb(220, 127, 29, 29), Color.FromArgb(240, 12, 6, 6), 90),
            Opacity = 0,
        };
        AddFx(burn, rect.X, rect.Y, 12);
        await Anim.To(burn, OpacityProperty, 0.8, 260, Anim.Out);

        // Ash: a grid of flakes that drift up and away, bottom rows first.
        const int cols = 7, rows = 9;
        double fw = rect.Width / cols, fh = rect.Height / rows;
        var palette = new[] { Color.FromRgb(40, 36, 34), Color.FromRgb(90, 30, 24), Ember, Color.FromRgb(70, 64, 60) };
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (_fxRng.NextDouble() < 0.35) continue;
                var col = palette[_fxRng.Next(palette.Length)];
                var flake = new Rectangle { Width = fw * 0.8, Height = fh * 0.8, Fill = Solid(col) };
                var t = new TranslateTransform();
                var rot = new RotateTransform(0, fw * 0.4, fh * 0.4);
                var g = new TransformGroup();
                g.Children.Add(rot);
                g.Children.Add(t);
                flake.RenderTransform = g;
                if (col == Ember) flake.Effect = new DropShadowEffect { Color = Ember, BlurRadius = 8, ShadowDepth = 0 };
                AddFx(flake, rect.X + c * fw, rect.Y + r * fh, 13);
                double delay = (rows - 1 - r) * 55 + Rand(0, 60), ms = Rand(650, 1050);
                _ = Anim.To(t, TranslateTransform.YProperty, -Rand(70, 170), ms, Anim.Out, delayMs: delay);
                _ = Anim.To(t, TranslateTransform.XProperty, Rand(-20, 70), ms, Anim.Out, delayMs: delay);
                _ = Anim.To(rot, RotateTransform.AngleProperty, Rand(-180, 180), ms, null, delayMs: delay);
                _ = FadeAndRemoveAsync(flake, ms, delay);
            }
        }

        // The card itself and its burn layer vanish as the ash leaves.
        _ = Anim.To(v.Scale, ScaleTransform.ScaleXProperty, 0.92, 600, Anim.In);
        _ = Anim.To(v.Scale, ScaleTransform.ScaleYProperty, 0.92, 600, Anim.In);
        _ = Anim.To(v, OpacityProperty, 0, 420, Anim.In, delayMs: 120);
        _ = FadeAndRemoveAsync(burn, 420, 140);

        // The skull.
        var skull = new TextBlock
        {
            Text = "☠",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 70,
            Foreground = new SolidColorBrush(Color.FromRgb(254, 226, 226)),
            Effect = new DropShadowEffect { Color = Red, BlurRadius = 22, ShadowDepth = 0 },
        };
        var st = new TranslateTransform();
        skull.RenderTransform = st;
        skull.Opacity = 0;
        AddFx(skull, center.X - 30, center.Y - 50, 60);
        _ = Anim.To(skull, OpacityProperty, 1, 200, Anim.Out);
        _ = Anim.To(st, TranslateTransform.YProperty, -90, 1100, Anim.Out);
        _ = FadeAndRemoveAsync(skull, 450, 650);

        await Anim.Delay(620);
        RemoveFx(v);
    }

    // =================================================================== Necrolites: decay

    /// <summary>
    /// Reanimation / becoming a Necrolite: a sickly green rot flickers over the card, ghostly wisps rise and ooze drips
    /// off the bottom edge.
    /// </summary>
    private async Task DecayAsync(CardView v, string caption)
    {
        Sfx.Play(SfxKind.Decay);
        var rect = BoardRectOf(v);
        var center = BoardCenterOf(v);
        v.SetGlow(DecayGreen, pulse: true);

        var rot = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0, DecayPurple.R, DecayPurple.G, DecayPurple.B), 0),
                    new(Color.FromArgb(150, DecayPurple.R, DecayPurple.G, DecayPurple.B), 0.45),
                    new(Color.FromArgb(220, DecayGreen.R, DecayGreen.G, DecayGreen.B), 1),
                }, 90),
            Opacity = 0,
        };
        AddFx(rot, rect.X, rect.Y, 12);
        var flicker = new DoubleAnimationUsingKeyFrames { Duration = Anim.Ms(1500) };
        foreach (var (value, at) in new[] { (0.7, 0.1), (0.3, 0.22), (0.75, 0.35), (0.45, 0.5), (0.7, 0.62), (0.35, 0.8), (0.0, 1.0) })
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromPercent(at)));
        rot.BeginAnimation(OpacityProperty, flicker);

        // Ghostly wisps rising and swaying.
        for (int i = 0; i < 9; i++)
        {
            var wisp = new Ellipse
            {
                Width = Rand(18, 30),
                Height = Rand(40, 70),
                Fill = new RadialGradientBrush(Color.FromArgb(170, 200, 230, 160), Color.FromArgb(0, 120, 160, 80)),
                Effect = new BlurEffect { Radius = 5 },
            };
            var t = new TranslateTransform();
            wisp.RenderTransform = t;
            AddFx(wisp, rect.X + Rand(10, rect.Width - 30), rect.Y + rect.Height * Rand(0.4, 0.9), 54);
            double delay = i * 110, ms = Rand(1000, 1400);
            _ = Anim.To(t, TranslateTransform.YProperty, -Rand(110, 190), ms, Anim.Out, delayMs: delay);
            var sway = new DoubleAnimation(-14, 14, Anim.Ms(ms / 3)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), BeginTime = TimeSpan.FromMilliseconds(delay * Anim.SpeedFactor) };
            t.BeginAnimation(TranslateTransform.XProperty, sway);
            wisp.Opacity = 0;
            _ = Anim.To(wisp, OpacityProperty, 1, 250, null, delayMs: delay)
                .ContinueWith(_ => FadeAndRemoveAsync(wisp, ms - 250), TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Ooze drips off the bottom edge.
        for (int i = 0; i < 5; i++)
        {
            var drip = new Ellipse { Width = 9, Height = 16, Fill = Solid(Color.FromRgb(74, 102, 20), 235) };
            var t = new TranslateTransform();
            drip.RenderTransform = t;
            AddFx(drip, rect.X + 14 + i * (rect.Width - 28) / 4, rect.Bottom - 12, 13);
            double delay = 250 + Rand(0, 500);
            _ = Anim.To(t, TranslateTransform.YProperty, Rand(35, 70), 800, Anim.In, delayMs: delay);
            _ = FadeAndRemoveAsync(drip, 300, delay + 700);
        }

        _ = FloatTextAtAsync(caption, ProjectToFx(center), DecayGreen);
        await Anim.Delay(1500);
        RemoveFx(rot);
        v.SetGlow(null);
    }

    // =================================================================== protection: shield bubble

    /// <summary>A hexagonal energy shield flares around a Unit that wasn't Nullified.</summary>
    private void ShieldBubble(Point center)
    {
        Sfx.Play(SfxKind.Shield);
        const double r = 150;
        var hex = new Polygon
        {
            Stroke = Solid(Cyan),
            StrokeThickness = 6,
            Fill = new RadialGradientBrush(Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B), Color.FromArgb(90, Cyan.R, Cyan.G, Cyan.B)),
            Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 24, ShadowDepth = 0 },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var points = new PointCollection();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 3 * i + Math.PI / 6;
            points.Add(new Point(r + Math.Cos(a) * r, r + Math.Sin(a) * r));
        }
        hex.Points = points;
        hex.Width = hex.Height = r * 2;
        var s = new ScaleTransform(0.55, 0.55);
        hex.RenderTransform = s;
        AddFx(hex, center.X - r, center.Y - r, 50);
        _ = Anim.To(s, ScaleTransform.ScaleXProperty, 1, 380, Anim.Back);
        _ = Anim.To(s, ScaleTransform.ScaleYProperty, 1, 380, Anim.Back);
        _ = FadeAndRemoveAsync(hex, 500, 450);
    }

    // =================================================================== round won: fireworks

    /// <summary>Fireworks over the table (screen space, on the effects layer).</summary>
    private void Fireworks(int bursts, Color main)
    {
        Sfx.Play(SfxKind.Fanfare);
        var colors = new[] { main, Gold, Cyan, Purple, Colors.White };
        if (FxSettings.ReduceEffects || !FxSettings.HighQuality) bursts = Math.Max(1, bursts / 2);
        for (int b = 0; b < bursts; b++)
        {
            double cx = FxCanvas.ActualWidth * Rand(0.2, 0.8);
            double cy = FxCanvas.ActualHeight * Rand(0.15, 0.5);
            var color = colors[b % colors.Length];
            double delay = b * 260;
            for (int i = 0; i < 26; i++)
            {
                var spark = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Solid(Lighten(color, 60)),
                    Effect = new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0 },
                    IsHitTestVisible = false,
                    Opacity = 0,
                };
                var t = new TranslateTransform(cx, cy);
                spark.RenderTransform = t;
                FxCanvas.Children.Add(spark);
                double a = Math.PI * 2 * i / 26 + Rand(-0.1, 0.1), d = Rand(90, 150), ms = Rand(800, 1100);
                _ = Anim.To(spark, OpacityProperty, 1, 60, null, delayMs: delay);
                _ = Anim.To(t, TranslateTransform.XProperty, cx + Math.Cos(a) * d, ms, Anim.Out, delayMs: delay);
                _ = Anim.To(t, TranslateTransform.YProperty, cy + Math.Sin(a) * d + 40, ms, Anim.Out, delayMs: delay);
                _ = Anim.To(spark, OpacityProperty, 0, ms * 0.6, Anim.In, delayMs: delay + ms * 0.4)
                    .ContinueWith(_ => FxCanvas.Children.Remove(spark), TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
    }
}
