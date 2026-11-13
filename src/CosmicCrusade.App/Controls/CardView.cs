using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Controls;

/// <summary>
/// A card on screen: rendered face (or back), live rank badge and a glow that can pulse.
/// Its RenderTransform is a TransformGroup { Flip (scale about centre), Scale, Rotate, Translate } for animations.
/// </summary>
public sealed class CardView : Grid
{
    private readonly Image _image;
    private readonly Border _rankBadge;
    private readonly TextBlock _rankText;
    private readonly DropShadowEffect _glow;
    private bool _faceDown;
    private readonly FrameworkElement _implMark;
    private readonly Canvas _foil;
    private readonly TranslateTransform _foilSweep;
    private bool _foilRunning;
    private FrameworkElement? _shield;
    private FrameworkElement? _goo;
    private static readonly Random FoilRng = new();

    public CardView(CardInstance? card, bool faceDown, double width, bool vector = false)
    {
        Card = card;
        Width = width;
        Height = width / CardFaceFactory.Aspect;
        UseVector = vector;
        SnapsToDevicePixels = true;

        _image = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);

        (_foil, _foilSweep) = CreateFoil(Width, Height);
        Children.Add(_foil);

        _rankText = new TextBlock
        {
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 22,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rankBadge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(235, 12, 14, 34)),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -10, -10, 0),
            Child = _rankText,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 },
        };
        Children.Add(_rankBadge);

        _implMark = ImplementationMark.Create(Math.Clamp(width * 0.075, 7, 22), Implementation.Full);
        _implMark.HorizontalAlignment = HorizontalAlignment.Right;
        _implMark.VerticalAlignment = VerticalAlignment.Bottom;
        _implMark.Margin = new Thickness(0, 0, width * 0.05, width * 0.05);
        Children.Add(_implMark);

        _glow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Colors.Black, Opacity = 0.65, Direction = 270 };
        Effect = _glow;

        Flip = new ScaleTransform(1, 1, Width / 2, Height / 2);
        Scale = new ScaleTransform(1, 1, Width / 2, Height / 2); // scale about the centre
        Rotate = new RotateTransform(0);
        Translate = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(Flip);
        group.Children.Add(Scale);
        group.Children.Add(Rotate);
        group.Children.Add(Translate);
        RenderTransform = group;

        FaceDown = faceDown;
    }

    public CardInstance? Card { get; }
    public bool UseVector { get; }

    public ScaleTransform Flip { get; }
    public ScaleTransform Scale { get; }
    public RotateTransform Rotate { get; }
    public TranslateTransform Translate { get; }

    public bool FaceDown
    {
        get => _faceDown;
        set
        {
            _faceDown = value;
            if (value || Card is null)
                _image.Source = UseVector ? CardFaceFactory.GetVectorBack() : CardFaceFactory.GetBack();
            else
                _image.Source = UseVector ? CardFaceFactory.GetVectorFace(Card.Definition) : CardFaceFactory.GetFace(Card.Definition);

            // "Not implemented yet" watermark: an empty box (nothing runs) or a half-filled box (some effects run).
            UpdateFoil(!value && Card is not null && Card.Definition.IsLeader);

            var status = value || Card is null ? Implementation.Full : CardStatus.Of(Card.Definition);
            ImplementationMark.Apply(_implMark, status);
            _implMark.ToolTip = status == Implementation.Full ? null : CardStatus.Describe(Card!.Definition);
        }
    }

    // ------------------------------------------------------------------ Leader foil

    /// <summary>
    /// Leaders are foil cards: a faint iridescent tint, a thin rainbow edge and a light band that sweeps across every
    /// few seconds. Built once per view, shown (and animated) only while a Leader is face up.
    /// </summary>
    private static (Canvas Layer, TranslateTransform Sweep) CreateFoil(double w, double h)
    {
        double radius = w * 0.045;
        var layer = new Canvas
        {
            Width = w,
            Height = h,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius),
        };

        Color A(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
        var tint = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(A(30, 255, 120, 200), 0),
                new(A(22, 255, 230, 120), 0.3),
                new(A(26, 120, 255, 200), 0.55),
                new(A(22, 120, 170, 255), 0.8),
                new(A(30, 220, 130, 255), 1),
            }, 35),
        };
        layer.Children.Add(tint);

        var sweep = new TranslateTransform(-w * 1.2, 0);
        var band = new System.Windows.Shapes.Rectangle
        {
            Width = w * 0.55,
            Height = h * 1.6,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(A(0, 255, 255, 255), 0),
                new(A(40, 255, 170, 220), 0.3),
                new(A(95, 255, 255, 255), 0.5),
                new(A(40, 150, 220, 255), 0.7),
                new(A(0, 255, 255, 255), 1),
            }, 0),
        };
        var g = new TransformGroup();
        g.Children.Add(new RotateTransform(22));
        g.Children.Add(sweep);
        band.RenderTransform = g;
        Canvas.SetTop(band, -h * 0.3);
        layer.Children.Add(band);

        var edge = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            RadiusX = radius,
            RadiusY = radius,
            StrokeThickness = Math.Max(1.5, w * 0.012),
            Stroke = new LinearGradientBrush(new GradientStopCollection
            {
                new(A(200, 255, 215, 120), 0),
                new(A(170, 255, 140, 220), 0.35),
                new(A(170, 130, 220, 255), 0.7),
                new(A(200, 255, 215, 120), 1),
            }, 45),
        };
        layer.Children.Add(edge);
        return (layer, sweep);
    }

    private void UpdateFoil(bool show)
    {
        _foil.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show == _foilRunning) return;
        _foilRunning = show;
        if (!show)
        {
            _foilSweep.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }

        // Sweep across in 1.8 s, then rest; each card starts at a random moment so they don't move in step.
        double w = Width;
        var a = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(5.5),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(FoilRng.NextDouble() * 4),
        };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(-w * 1.2, KeyTime.FromPercent(0)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(w * 1.4, KeyTime.FromPercent(0.33), new SineEase { EasingMode = EasingMode.EaseInOut }));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(w * 1.4, KeyTime.FromPercent(1)));
        _foilSweep.BeginAnimation(TranslateTransform.XProperty, a);
    }

    // ------------------------------------------------------------------ status overlays

    /// <summary>
    /// A faint, slowly breathing hex-shield shimmer while the Unit can't be Nullified or is Immune (Force Field,
    /// Crimson Redeemer, Vigan...).
    /// </summary>
    public void SetShield(bool on)
    {
        if (on == (_shield is not null)) return;
        if (!on)
        {
            Children.Remove(_shield);
            _shield = null;
            return;
        }
        var cyan = Color.FromRgb(79, 209, 255);
        var hexes = new System.Windows.Shapes.Path
        {
            Data = HexGrid(Width, Height, Width / 7),
            Stroke = new SolidColorBrush(Color.FromArgb(150, cyan.R, cyan.G, cyan.B)),
            StrokeThickness = 1.2,
            Fill = new RadialGradientBrush(Color.FromArgb(0, cyan.R, cyan.G, cyan.B), Color.FromArgb(70, cyan.R, cyan.G, cyan.B)),
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), Width * 0.045, Width * 0.045),
            Effect = new DropShadowEffect { Color = cyan, BlurRadius = 8, ShadowDepth = 0 },
        };
        hexes.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 0.75, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase(),
        });
        _shield = hexes;
        Children.Insert(Children.IndexOf(_foil) + 1, hexes);
    }

    private static Geometry HexGrid(double w, double h, double r)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double dx = r * Math.Sqrt(3), dy = r * 1.5;
            int row = 0;
            for (double y = 0; y < h + r; y += dy, row++)
            {
                for (double x = (row % 2) * dx / 2; x < w + r; x += dx)
                {
                    for (int i = 0; i <= 6; i++)
                    {
                        double a = Math.PI / 3 * i + Math.PI / 6;
                        var p = new Point(x + Math.Cos(a) * r, y + Math.Sin(a) * r);
                        if (i == 0) ctx.BeginFigure(p, false, true);
                        else ctx.LineTo(p, true, false);
                    }
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>
    /// Suppressed (Gloop!, Suppressing Goop, Refraction Network, Atmosphere Generator): green goo coats the card and
    /// dulls it, with drips hanging off the top edge.
    /// </summary>
    public void SetGoo(bool on)
    {
        if (on == (_goo is not null)) return;
        if (!on)
        {
            Children.Remove(_goo);
            _goo = null;
            return;
        }
        var layer = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), Width * 0.045, Width * 0.045) };
        layer.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = Width,
            Height = Height,
            Fill = new LinearGradientBrush(Color.FromArgb(150, 60, 110, 30), Color.FromArgb(90, 30, 60, 20), 90),
        });
        var rng = new Random(Card?.InstanceId ?? 0);
        for (int i = 0; i < 6; i++)
        {
            double w = Width * (0.08 + rng.NextDouble() * 0.08);
            double hDrip = Height * (0.08 + rng.NextDouble() * 0.22);
            var drip = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = hDrip,
                RadiusX = w / 2,
                RadiusY = w / 2,
                Fill = new SolidColorBrush(Color.FromArgb(220, 110, 190, 40)),
            };
            Canvas.SetLeft(drip, Width * (i + 0.2 + rng.NextDouble() * 0.5) / 6.2);
            Canvas.SetTop(drip, -w / 2);
            layer.Children.Add(drip);
        }
        layer.Opacity = 0;
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        _goo = layer;
        Children.Insert(Children.IndexOf(_foil) + 1, layer);
    }

    // ------------------------------------------------------------------ effects pass 3 overlays

    private FrameworkElement? _dormant;
    private FrameworkElement? _frost;
    private bool _bobbing;

    /// <summary>
    /// Standby Units are not in play: the card is dimmed under a cold blue film with frosted edges, and it stops
    /// bobbing.
    /// </summary>
    public void SetDormant(bool on)
    {
        if (on == (_dormant is not null)) return;
        if (!on)
        {
            Children.Remove(_dormant);
            _dormant = null;
            return;
        }
        var layer = new Canvas
        {
            Width = Width,
            Height = Height,
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), Width * 0.045, Width * 0.045),
        };
        layer.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = Width,
            Height = Height,
            Fill = new LinearGradientBrush(Color.FromArgb(120, 150, 190, 230), Color.FromArgb(150, 10, 18, 40), 90),
        });
        layer.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = Width,
            Height = Height,
            RadiusX = Width * 0.045,
            RadiusY = Width * 0.045,
            Stroke = new SolidColorBrush(Color.FromArgb(170, 200, 230, 255)),
            StrokeThickness = Math.Max(2, Width * 0.025),
            Effect = new BlurEffect { Radius = Math.Max(2, Width * 0.03) },
        });
        layer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = FrostCorners(Width, Height, Card?.InstanceId ?? 1, 0.22),
            Stroke = new SolidColorBrush(Color.FromArgb(150, 230, 245, 255)),
            StrokeThickness = Math.Max(1, Width * 0.008),
        });
        _dormant = layer;
        Children.Insert(Children.IndexOf(_foil) + 1, layer);
        layer.Opacity = 0;
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
    }

    /// <summary>Cryogenic Chambers: frost crystals creep in from the corners of an affected card.</summary>
    public void SetFrost(bool on)
    {
        if (on == (_frost is not null)) return;
        if (!on)
        {
            Children.Remove(_frost);
            _frost = null;
            return;
        }
        var ice = Color.FromRgb(186, 230, 253);
        var path = new System.Windows.Shapes.Path
        {
            Data = FrostCorners(Width, Height, (Card?.InstanceId ?? 7) * 31, 0.42),
            Stroke = new SolidColorBrush(Color.FromArgb(220, 240, 250, 255)),
            StrokeThickness = Math.Max(1.2, Width * 0.012),
            Fill = new SolidColorBrush(Color.FromArgb(40, ice.R, ice.G, ice.B)),
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), Width * 0.045, Width * 0.045),
            Effect = new DropShadowEffect { Color = ice, BlurRadius = 8, ShadowDepth = 0 },
            StrokeDashArray = new DoubleCollection { 600 },
            StrokeDashOffset = 600,
        };
        // Creep: the crystal lines draw themselves in.
        path.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(600, 0, TimeSpan.FromSeconds(2.4)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _frost = path;
        Children.Insert(Children.IndexOf(_foil) + 1, path);
    }

    /// <summary>Branching ice crystals growing in from each corner (deterministic per seed).</summary>
    private static Geometry FrostCorners(double w, double h, int seed, double reach)
    {
        var rng = new Random(seed);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var corners = new[] { (0.0, 0.0, 1.0, 1.0), (w, 0.0, -1.0, 1.0), (0.0, h, 1.0, -1.0), (w, h, -1.0, -1.0) };
            foreach (var (cx, cy, sx, sy) in corners)
            {
                for (int b = 0; b < 4; b++)
                {
                    double a = (0.15 + b * 0.4 + rng.NextDouble() * 0.2) * Math.PI / 2 / 1.4;
                    double len = Math.Min(w, h) * reach * (0.6 + rng.NextDouble() * 0.5);
                    var start = new Point(cx, cy);
                    var end = new Point(cx + sx * Math.Cos(a) * len, cy + sy * Math.Sin(a) * len);
                    ctx.BeginFigure(start, false, false);
                    ctx.LineTo(end, true, false);
                    // Side twigs.
                    for (int k = 1; k <= 3; k++)
                    {
                        double f = k / 4.0;
                        var p = new Point(cx + (end.X - cx) * f, cy + (end.Y - cy) * f);
                        double tw = len * 0.22 * (1 - f);
                        foreach (int side in new[] { -1, 1 })
                        {
                            double ta = a + side * 0.7;
                            ctx.BeginFigure(p, false, false);
                            ctx.LineTo(new Point(p.X + sx * Math.Cos(ta) * tw, p.Y + sy * Math.Sin(ta) * tw), true, false);
                        }
                    }
                }
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Idle life: board cards bob very slightly (each at its own pace). Off for Standby cards and Low quality.</summary>
    public void SetIdleBob(bool on, double phaseSeconds = 0)
    {
        if (on == _bobbing) return;
        _bobbing = on;
        if (!on)
        {
            Translate.BeginAnimation(TranslateTransform.YProperty, null);
            Translate.Y = 0;
            return;
        }
        Translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-1.6, 1.6, TimeSpan.FromSeconds(2.2 + phaseSeconds % 0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(phaseSeconds % 2.0), // staggered so cards don't move in step
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>Polarity Storm: the Rank badge flips over with a zap.</summary>
    public void FlipRankBadge()
    {
        if (_rankBadge.Visibility != Visibility.Visible) return;
        var spin = new ScaleTransform(1, 1, 18, 18);
        _rankBadge.RenderTransform = spin;
        spin.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, -1, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            EasingFunction = new SineEase(),
        });
    }

    /// <summary>Redraws the face (a Unit that became a copy of another card shows the copied card).</summary>
    public void RefreshFace() => FaceDown = _faceDown;

    /// <summary>Shows the rank badge: green when above the printed rank, red when below.</summary>
    public void ShowRank(int rank, int printed)
    {
        _rankBadge.Visibility = Visibility.Visible;
        _rankText.Text = rank.ToString();
        _rankBadge.BorderBrush = rank > printed ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
            : rank < printed ? new SolidColorBrush(Color.FromRgb(248, 113, 113))
            : Brushes.White;
    }

    public void HideRank() => _rankBadge.Visibility = Visibility.Collapsed;

    /// <summary>Coloured glow (null = normal drop shadow). Pulse animates the glow strength forever.</summary>
    public void SetGlow(Color? color, bool pulse = false)
    {
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        if (color is null)
        {
            _glow.Color = Colors.Black;
            _glow.BlurRadius = 14;
            _glow.ShadowDepth = 3;
            _glow.Opacity = 0.65;
            return;
        }

        _glow.Color = color.Value;
        _glow.ShadowDepth = 0;
        _glow.BlurRadius = 28;
        _glow.Opacity = 1;
        if (pulse)
        {
            var anim = new DoubleAnimation(16, 40, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            };
            _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, anim);
        }
    }
}
