using System.Windows;
using System.Windows.Media;

namespace CosmicCrusade.App.Controls;

/// <summary>
/// Animated background: drifting, twinkling stars in three parallax layers, soft nebulae and the odd shooting star.
/// Drawn in OnRender each frame (cheap: a few hundred ellipses).
/// </summary>
public sealed class Starfield : FrameworkElement
{
    private sealed class Star
    {
        public double X, Y, Size, Speed, Phase, Twinkle;
        public Color Tint;
    }

    private sealed class Meteor
    {
        public double X, Y, Vx, Vy, Life;
    }

    private readonly List<Star> _stars = new();
    private readonly List<Meteor> _meteors = new();
    private readonly Random _rng = new(4242);
    private TimeSpan _last;
    private double _time;
    private RadialGradientBrush[]? _nebulae;

    public Starfield()
    {
        IsHitTestVisible = false;
        for (int i = 0; i < 320; i++)
        {
            int layer = i % 3;
            _stars.Add(new Star
            {
                X = _rng.NextDouble(),
                Y = _rng.NextDouble(),
                Size = 0.5 + layer * 0.55 + _rng.NextDouble() * 0.6,
                Speed = 0.0015 + layer * 0.0025,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Twinkle = 0.6 + _rng.NextDouble() * 2.4,
                Tint = _rng.Next(6) switch
                {
                    0 => Color.FromRgb(180, 200, 255),
                    1 => Color.FromRgb(255, 220, 190),
                    _ => Colors.White,
                },
            });
        }

        Loaded += (_, _) => CompositionTarget.Rendering += OnFrame;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 0 : Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        if (dt <= 0) return;
        _time += dt;

        foreach (var s in _stars)
        {
            s.X -= s.Speed * dt;
            if (s.X < 0) { s.X += 1; s.Y = _rng.NextDouble(); }
        }

        if (_rng.NextDouble() < dt * 0.12) // roughly one every 8 seconds
        {
            _meteors.Add(new Meteor
            {
                X = 0.3 + _rng.NextDouble() * 0.8,
                Y = _rng.NextDouble() * 0.4,
                Vx = -(0.5 + _rng.NextDouble() * 0.4),
                Vy = 0.22 + _rng.NextDouble() * 0.2,
                Life = 1,
            });
        }
        for (int i = _meteors.Count - 1; i >= 0; i--)
        {
            var m = _meteors[i];
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
            m.Life -= dt * 0.9;
            if (m.Life <= 0) _meteors.RemoveAt(i);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(3, 4, 14), Color.FromRgb(10, 6, 28), 90), null, new Rect(0, 0, w, h));

        _nebulae ??= new[]
        {
            Nebula(Color.FromArgb(70, 120, 40, 200)),
            Nebula(Color.FromArgb(55, 20, 110, 200)),
            Nebula(Color.FromArgb(40, 200, 40, 90)),
        };
        double drift = Math.Sin(_time * 0.05) * 0.03;
        dc.DrawEllipse(_nebulae[0], null, new Point(w * (0.22 + drift), h * 0.3), w * 0.45, h * 0.5);
        dc.DrawEllipse(_nebulae[1], null, new Point(w * (0.8 - drift), h * 0.7), w * 0.5, h * 0.55);
        dc.DrawEllipse(_nebulae[2], null, new Point(w * 0.6, h * (0.15 + drift)), w * 0.3, h * 0.3);

        foreach (var s in _stars)
        {
            double a = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(_time * s.Twinkle + s.Phase));
            var c = Color.FromArgb((byte)(a * 255), s.Tint.R, s.Tint.G, s.Tint.B);
            dc.DrawEllipse(new SolidColorBrush(c), null, new Point(s.X * w, s.Y * h), s.Size, s.Size);
        }

        foreach (var m in _meteors)
        {
            var head = new Point(m.X * w, m.Y * h);
            var tail = new Point(head.X - m.Vx * w * 0.12, head.Y - m.Vy * h * 0.12);
            var brush = new LinearGradientBrush(Color.FromArgb((byte)(220 * m.Life), 255, 255, 255), Colors.Transparent,
                new Point(0, 0), new Point(1, 1)) { MappingMode = BrushMappingMode.Absolute, StartPoint = head, EndPoint = tail };
            dc.DrawLine(new Pen(brush, 2), head, tail);
        }
    }

    private static RadialGradientBrush Nebula(Color c)
    {
        var b = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
