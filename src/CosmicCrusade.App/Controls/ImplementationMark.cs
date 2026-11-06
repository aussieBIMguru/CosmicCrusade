using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CosmicCrusade.Core.Rules;

namespace CosmicCrusade.App.Controls;

/// <summary>
/// The corner watermark for cards the engine can't fully run yet: an empty box = not implemented, a half-filled box
/// = partly implemented. Fully implemented cards show nothing. Used on card faces (<see cref="CardView"/>); the setup
/// card list draws the same mark in XAML.
/// </summary>
public static class ImplementationMark
{
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromArgb(235, 240, 240, 250)));
    private static readonly Brush Back = Freeze(new SolidColorBrush(Color.FromArgb(150, 8, 10, 24)));

    public static FrameworkElement Create(double size, Implementation status)
    {
        var fill = new Rectangle
        {
            Fill = Ink,
            Height = size / 2,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var box = new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(Math.Max(1, size / 8)),
            Background = Back,
            Child = fill,
        };
        var host = new Grid
        {
            Width = size,
            Height = size,
            IsHitTestVisible = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black },
        };
        host.Children.Add(box);
        Apply(host, status);
        return host;
    }

    public static void Apply(FrameworkElement mark, Implementation status)
    {
        mark.Visibility = status == Implementation.Full ? Visibility.Collapsed : Visibility.Visible;
        if (mark is Grid { Children.Count: > 0 } g && g.Children[0] is Border { Child: Rectangle fill })
            fill.Visibility = status == Implementation.Partial ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
