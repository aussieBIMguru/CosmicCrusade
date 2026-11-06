using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Board;

/// <summary>An empty node outline with a label. Can glow (legal target) and show a lock.</summary>
public sealed class NodeSlot : Grid
{
    private readonly Rectangle _frame;
    private readonly Rectangle _highlight;
    private readonly TextBlock _lock;

    public NodeSlot(Node node, Color sideColor)
    {
        Node = node;
        Width = BoardLayout.SlotW;
        Height = BoardLayout.SlotH;
        Background = Brushes.Transparent; // hit-testable

        bool scoring = node.IsScoring;
        _frame = new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Stroke = new SolidColorBrush(Color.FromArgb(scoring ? (byte)170 : (byte)90, sideColor.R, sideColor.G, sideColor.B)),
            StrokeThickness = scoring ? 3 : 2,
            StrokeDashArray = scoring ? null : new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(scoring ? (byte)45 : (byte)22, sideColor.R, sideColor.G, sideColor.B)),
        };
        Children.Add(_frame);

        Children.Add(new TextBlock
        {
            Text = node.Kind == NodeKind.Leader ? "LEADER" : node.Label,
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = node.Kind == NodeKind.Leader ? 30 : 44,
            Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _highlight = new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Stroke = new SolidColorBrush(Color.FromRgb(79, 209, 255)),
            StrokeThickness = 5,
            Fill = new SolidColorBrush(Color.FromArgb(50, 79, 209, 255)),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Color.FromRgb(79, 209, 255), BlurRadius = 30, ShadowDepth = 0 },
        };
        Children.Add(_highlight);

        _lock = new TextBlock
        {
            Text = "\U0001F512",
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_lock);
    }

    public Node Node { get; }
    public bool IsTarget { get; private set; }

    public void SetTarget(bool on)
    {
        IsTarget = on;
        Cursor = on ? System.Windows.Input.Cursors.Hand : null;
        if (on)
        {
            _highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            });
        }
        else
        {
            _highlight.BeginAnimation(OpacityProperty, null);
            _highlight.Opacity = 0;
        }
    }

    private Canvas? _lockBars;

    /// <summary>
    /// Shows whether the node is Locked. While it is, the slot keeps a chained look: crossed chains, three
    /// humming energy bars across the slot and a small padlock (the bars pulse unless effects are reduced).
    /// </summary>
    public void Refresh()
    {
        bool locked = Node.IsLocked;
        _lock.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        if (locked == (_lockBars is not null)) return;
        if (!locked)
        {
            Children.Remove(_lockBars);
            _lockBars = null;
            return;
        }
        _lockBars = BuildLockBars(Width, Height);
        Children.Insert(Children.IndexOf(_lock), _lockBars);
    }

    private static Canvas BuildLockBars(double w, double h)
    {
        var steel = Color.FromRgb(226, 232, 240);
        var energy = Color.FromRgb(125, 211, 252);
        var layer = new Canvas
        {
            Width = w,
            Height = h,
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 14, 14),
        };
        layer.Children.Add(new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromArgb(70, 10, 14, 30)) });

        // Two chains corner to corner: rows of oval links, alternating flat and edge-on.
        foreach (var (x0, y0, x1, y1) in new[] { (8.0, 10.0, w - 8, h - 10), (w - 8, 10.0, 8.0, h - 10) })
        {
            double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double angle = Math.Atan2(y1 - y0, x1 - x0) * 180 / Math.PI;
            const double link = 22;
            int n = (int)(len / (link * 0.72));
            for (int i = 0; i < n; i++)
            {
                double f = (i + 0.5) / n;
                double cx = x0 + (x1 - x0) * f, cy = y0 + (y1 - y0) * f;
                bool edgeOn = i % 2 == 1;
                var ring = new Ellipse
                {
                    Width = link,
                    Height = edgeOn ? 5 : 12,
                    Stroke = new SolidColorBrush(Color.FromArgb(edgeOn ? (byte)230 : (byte)190, steel.R, steel.G, steel.B)),
                    StrokeThickness = edgeOn ? 3 : 2.6,
                    RenderTransform = new RotateTransform(angle, link / 2, (edgeOn ? 5 : 12) / 2.0),
                };
                Canvas.SetLeft(ring, cx - link / 2);
                Canvas.SetTop(ring, cy - (edgeOn ? 5 : 12) / 2.0);
                layer.Children.Add(ring);
            }
        }

        // Energy bars.
        for (int i = 0; i < 3; i++)
        {
            var bar = new Rectangle
            {
                Width = w - 10,
                Height = 7,
                RadiusX = 3.5,
                RadiusY = 3.5,
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Color.FromArgb(60, energy.R, energy.G, energy.B), 0),
                    new(Color.FromArgb(230, 255, 255, 255), 0.5),
                    new(Color.FromArgb(60, energy.R, energy.G, energy.B), 1),
                }, 0),
                Effect = new DropShadowEffect { Color = energy, BlurRadius = 14, ShadowDepth = 0 },
            };
            Canvas.SetLeft(bar, 5);
            Canvas.SetTop(bar, h * (0.28 + i * 0.22) - 3.5);
            layer.Children.Add(bar);
            if (!FxSettings.ReduceEffects)
                bar.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromSeconds(0.9 + i * 0.17))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase(),
                });
        }
        layer.Opacity = 0;
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        return layer;
    }
}

/// <summary>A deck or pile: a stack of cards with a count.</summary>
public sealed class PileView : Grid
{
    private readonly Image _top;
    private readonly Image _under;
    private readonly TextBlock _count;
    private readonly Rectangle _highlight;
    private readonly bool _isDeck;

    public PileView(PileKind pile)
    {
        Pile = pile;
        _isDeck = pile is PileKind.UnitDeck or PileKind.OrderDeck or PileKind.WarzoneDeck;
        bool isWarzone = pile is PileKind.WarzoneDeck or PileKind.ActiveWarzone;
        var frameColor = isWarzone ? Color.FromArgb(150, 192, 132, 252) : Color.FromArgb(110, 200, 170, 255);
        Width = BoardLayout.SlotW;
        Height = BoardLayout.SlotH + 30;

        var frame = new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Height = BoardLayout.SlotH,
            VerticalAlignment = VerticalAlignment.Top,
            Stroke = new SolidColorBrush(frameColor),
            StrokeThickness = pile == PileKind.ActiveWarzone ? 3 : 2,
            StrokeDashArray = pile == PileKind.ActiveWarzone ? null : new DoubleCollection { 3, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(25, 160, 120, 255)),
        };
        Children.Add(frame);

        _under = new Image
        {
            Width = BoardLayout.CardW,
            Height = BoardLayout.CardH,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
            Source = CardFaceFactory.GetBack(),
            Opacity = 0.6,
            RenderTransform = new TranslateTransform(5, 5),
        };
        Children.Add(_under);

        _top = new Image
        {
            Width = BoardLayout.CardW,
            Height = BoardLayout.CardH,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 0, 0),
            Effect = pile == PileKind.ActiveWarzone
                ? new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Color = Color.FromRgb(192, 132, 252), Opacity = 1 }
                : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = 0.7 },
        };
        Children.Add(_top);
        Cursor = _isDeck ? null : System.Windows.Input.Cursors.Help;

        _highlight = new Rectangle
        {
            RadiusX = 14,
            RadiusY = 14,
            Height = BoardLayout.SlotH,
            VerticalAlignment = VerticalAlignment.Top,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 201, 74)),
            StrokeThickness = 5,
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 201, 74)),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Color.FromRgb(255, 201, 74), BlurRadius = 30, ShadowDepth = 0 },
        };
        Children.Add(_highlight);

        var label = new TextBlock
        {
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 220, 225, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _count = label;
        Children.Add(label);
    }

    public PileKind Pile { get; }
    public bool IsTarget { get; private set; }

    /// <summary>Pulsing gold outline: click this pile to complete the selected action (e.g. issue an Action Order).</summary>
    public void SetTarget(bool on)
    {
        IsTarget = on;
        Cursor = on ? System.Windows.Input.Cursors.Hand : (_isDeck ? null : System.Windows.Input.Cursors.Help);
        if (on)
        {
            _highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(550))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            });
        }
        else
        {
            _highlight.BeginAnimation(OpacityProperty, null);
            _highlight.Opacity = 0;
        }
    }

    public void Update(IReadOnlyList<CardInstance> cards, int? virtualCount = null)
    {
        int n = virtualCount ?? cards.Count;
        _count.Text = Pile == PileKind.ActiveWarzone
            ? (n > 1 ? $"{BoardLayout.PileLabel(Pile)}  ×{n}" : BoardLayout.PileLabel(Pile))
            : $"{BoardLayout.PileLabel(Pile)}  {(virtualCount is null ? n.ToString() : "∞")}";
        bool any = n > 0;
        _under.Visibility = n > 1 ? Visibility.Visible : Visibility.Hidden;
        _top.Visibility = any ? Visibility.Visible : Visibility.Hidden;
        if (!any) return;
        _top.Source = _isDeck || cards.Count == 0 ? CardFaceFactory.GetBack() : CardFaceFactory.GetFace(cards[^1].Definition);
        // Several Active Warzones (Dimensional Rift): show the next one face up underneath.
        _under.Source = Pile == PileKind.ActiveWarzone && cards.Count > 1
            ? CardFaceFactory.GetFace(cards[^2].Definition)
            : CardFaceFactory.GetBack();
        _under.Opacity = Pile == PileKind.ActiveWarzone ? 0.9 : 0.6;
        _top.Opacity = Pile == PileKind.NullifiedUnits ? 0.75 : 1;
    }

    /// <summary>A little bump when something lands on the pile.</summary>
    public void Pulse()
    {
        var st = new ScaleTransform(1, 1, Width / 2, Height / 2);
        RenderTransform = st;
        var a = new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase() };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}

/// <summary>Builds the static board art (felt, grid, control line, side tints) onto the board canvas.</summary>
public static class BoardSurface
{
    public static readonly Color PlayerColor = Color.FromRgb(59, 130, 246);
    public static readonly Color OpponentColor = Color.FromRgb(239, 68, 68);

    public static void DrawBackground(Canvas canvas)
    {
        double w = BoardLayout.Width, h = BoardLayout.Height;

        // Base slab with a subtle metallic gradient and rounded corners.
        var slab = new Rectangle
        {
            Width = w,
            Height = h,
            RadiusX = 46,
            RadiusY = 46,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(38, 16, 30), 0),
                    new GradientStop(Color.FromRgb(16, 14, 38), 0.48),
                    new GradientStop(Color.FromRgb(16, 14, 38), 0.52),
                    new GradientStop(Color.FromRgb(12, 22, 52), 1),
                }, 90),
            Stroke = new LinearGradientBrush(Color.FromRgb(255, 120, 120), Color.FromRgb(120, 170, 255), 90),
            StrokeThickness = 6,
        };
        canvas.Children.Add(slab);

        // Sci-fi grid.
        var grid = new GeometryGroup();
        for (double x = 50; x < w; x += 50) grid.Children.Add(new LineGeometry(new Point(x, 20), new Point(x, h - 20)));
        for (double y = 50; y < h; y += 50) grid.Children.Add(new LineGeometry(new Point(20, y), new Point(w - 20, y)));
        grid.Freeze();
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = grid,
            Stroke = new SolidColorBrush(Color.FromArgb(22, 140, 170, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        });

        // Side glows.
        AddGlow(canvas, new Point(w * 0.4, h * 0.78), PlayerColor);
        AddGlow(canvas, new Point(w * 0.4, h * 0.22), OpponentColor);

        // Control line.
        var line = new Rectangle
        {
            Width = w - 80,
            Height = 6,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 79, 209, 255), 0),
                    new GradientStop(Color.FromArgb(255, 79, 209, 255), 0.2),
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.5),
                    new GradientStop(Color.FromArgb(255, 79, 209, 255), 0.8),
                    new GradientStop(Color.FromArgb(0, 79, 209, 255), 1),
                }, 0),
            Effect = new DropShadowEffect { Color = Color.FromRgb(79, 209, 255), BlurRadius = 24, ShadowDepth = 0 },
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(line, 40);
        Canvas.SetTop(line, BoardLayout.ControlLineY - 3);
        canvas.Children.Add(line);
        line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase(),
        });

        AddSideLabel(canvas, "AI", OpponentColor, BoardLayout.ControlLineY - 62);
        AddSideLabel(canvas, "YOU", PlayerColor, BoardLayout.ControlLineY + 10);
    }

    private static void AddGlow(Canvas canvas, Point center, Color color)
    {
        var e = new Ellipse
        {
            Width = 1300,
            Height = 420,
            Fill = new RadialGradientBrush(Color.FromArgb(60, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(e, center.X - 650);
        Canvas.SetTop(e, center.Y - 210);
        canvas.Children.Add(e);
    }

    private static void AddSideLabel(Canvas canvas, string text, Color c, double top)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Haettenschweiler, Impact"),
            FontSize = 48,
            Foreground = new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, 1095);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }
}
