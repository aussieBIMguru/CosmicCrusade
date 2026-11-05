using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;

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
        }
    }

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
