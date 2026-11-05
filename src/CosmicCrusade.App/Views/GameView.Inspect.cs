using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CosmicCrusade.App.Controls;
using CosmicCrusade.App.Rendering;
using CosmicCrusade.Core.Model;

namespace CosmicCrusade.App.Views;

/// <summary>
/// Reading cards without looking at the side panel:
/// - Hover magnifier: a large vector copy of the hovered hand card, floating just above the lifted card.
///   It is not hit-testable, so the real hand card keeps the mouse (clicks and hover work as before).
/// - Right-click inspect: right-click any face-up card (hand, board, face-up pile, side-panel zoom) to open it
///   full height in the middle of the play area. Any click or Esc closes it.
/// Both use their own fixed timings, not the animation-speed slider, so they always feel snappy.
/// </summary>
public partial class GameView
{
    private const double MagnifyCardW = 280;
    private const double MagnifyMinW = 190;
    private const double HandHoverLift = 88;   // must match HandCard_MouseEnter
    private const double HandHoverScale = 1.1; // must match HandCard_MouseEnter

    private CardView? _magnifier;
    private CardView? _magnifiedSource;
    private CardView? _magnifyPending;
    private DispatcherTimer? _magnifyShowTimer;
    private DispatcherTimer? _magnifyHideTimer;

    private bool IsInspectOpen => InspectOverlay.Visibility == Visibility.Visible;

    // =================================================================== hover magnifier

    /// <summary>Called on hand-card hover. Switches instantly if a magnifier is already up, else waits briefly
    /// so sweeping the mouse across the hand doesn't flash previews.</summary>
    private void ScheduleMagnify(CardView v)
    {
        _magnifyHideTimer?.Stop();
        if (MagnifyCheck.IsChecked != true || IsInspectOpen) return;

        if (_magnifier is not null)
        {
            ShowMagnifier(v, quick: true);
            return;
        }

        _magnifyPending = v;
        _magnifyShowTimer ??= CreateTimer(130, () =>
        {
            var target = _magnifyPending;
            _magnifyPending = null;
            if (target is not null && target.IsMouseOver) ShowMagnifier(target, quick: false);
        });
        _magnifyShowTimer.Stop();
        _magnifyShowTimer.Start();
    }

    /// <summary>Called on hand-card leave. A short grace period lets the mouse move to a neighbouring card
    /// without the magnifier closing and reopening.</summary>
    private void UnscheduleMagnify(CardView v)
    {
        if (_magnifyPending == v)
        {
            _magnifyShowTimer?.Stop();
            _magnifyPending = null;
        }
        if (_magnifiedSource != v) return;

        _magnifyHideTimer ??= CreateTimer(70, HideMagnifier);
        _magnifyHideTimer.Stop();
        _magnifyHideTimer.Start();
    }

    private void ShowMagnifier(CardView source, bool quick)
    {
        if (source.Card is null || source.FaceDown || !_handViews.Contains(source) || source.Tag is not HandSpot spot) return;
        if (_magnifiedSource == source && _magnifier is not null) return;

        RemoveMagnifierVisual();

        // Where the lifted hand card's top edge will be, in MagnifyCanvas coordinates.
        var handOrigin = HandCanvas.TranslatePoint(new Point(0, 0), MagnifyCanvas);
        double handCardH = HandCardW / CardFaceFactory.Aspect;
        double liftedTop = handOrigin.Y + spot.Y - HandHoverLift - handCardH * (HandHoverScale - 1) / 2;
        double bottom = liftedTop - 10;

        // Shrink to fit the space above the hand; never smaller than MagnifyMinW.
        double w = MagnifyCardW;
        double maxH = bottom - 8;
        if (w / CardFaceFactory.Aspect > maxH) w = Math.Max(MagnifyMinW, maxH * CardFaceFactory.Aspect);
        double h = w / CardFaceFactory.Aspect;
        double top = Math.Max(8, bottom - h);

        double centreX = handOrigin.X + spot.X + HandCardW / 2;
        double left = Math.Clamp(centreX - w / 2, 8, Math.Max(8, MagnifyCanvas.ActualWidth - w - 8));

        var view = new CardView(source.Card, faceDown: false, w, vector: true);
        view.SetGlow(CardLayout.Instance.Accent(source.Card.Definition));
        view.Scale.CenterX = w / 2;
        view.Scale.CenterY = h; // grow up out of the hand card
        Canvas.SetLeft(view, left);
        Canvas.SetTop(view, top);
        MagnifyCanvas.Children.Add(view);

        _magnifier = view;
        _magnifiedSource = source;

        double ms = quick ? 90 : 150;
        double fromScale = quick ? 0.94 : 0.7;
        Fixed(view, OpacityProperty, 0, 1, ms);
        Fixed(view.Scale, ScaleTransform.ScaleXProperty, fromScale, 1, ms, new CubicEase { EasingMode = EasingMode.EaseOut });
        Fixed(view.Scale, ScaleTransform.ScaleYProperty, fromScale, 1, ms, new CubicEase { EasingMode = EasingMode.EaseOut });
    }

    private void HideMagnifier()
    {
        _magnifyHideTimer?.Stop();
        _magnifyShowTimer?.Stop();
        _magnifyPending = null;
        RemoveMagnifierVisual();
    }

    /// <summary>Hide the magnifier if it belongs to this hand card (e.g. the card just left the hand).</summary>
    private void HideMagnifierFor(CardView handView)
    {
        if (_magnifiedSource == handView || _magnifyPending == handView) HideMagnifier();
    }

    private void RemoveMagnifierVisual()
    {
        if (_magnifier is not null) MagnifyCanvas.Children.Remove(_magnifier);
        _magnifier = null;
        _magnifiedSource = null;
    }

    private void MagnifyCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Can fire during InitializeComponent (IsChecked="True" in XAML), before the overlay canvas exists.
        if (MagnifyCanvas is null || sender is not CheckBox cb) return;
        if (cb.IsChecked != true) HideMagnifier();
    }

    // =================================================================== right-click inspect

    /// <summary>Right-click handler for any CardView. Face-down cards are ignored (and the click falls through,
    /// so right-click on empty space still cancels a selection).</summary>
    private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CardView { Card: { } card, FaceDown: false }) return;
        e.Handled = true;
        OpenInspect(card);
    }

    private void ZoomHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_zoomed?.Card is not { } card) return;
        e.Handled = true;
        OpenInspect(card);
    }

    private void OpenInspect(CardInstance card)
    {
        HideMagnifier();

        double availH = Math.Max(200, PlayArea.ActualHeight - 70); // leave room for the close hint
        double availW = Math.Max(150, PlayArea.ActualWidth - 40);
        double h = Math.Min(availH * 0.92, 900);
        double w = h * CardFaceFactory.Aspect;
        if (w > availW) { w = availW; h = w / CardFaceFactory.Aspect; }

        var view = new CardView(card, faceDown: false, w, vector: true) { IsHitTestVisible = false };
        view.SetGlow(CardLayout.Instance.Accent(card.Definition));
        view.ShowRankIfInPlay(State);
        InspectHost.Child = view;

        InspectOverlay.Visibility = Visibility.Visible;
        Fixed(InspectOverlay, OpacityProperty, 0, 1, 120);
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        Fixed(view.Scale, ScaleTransform.ScaleXProperty, 0.85, 1, 180, ease);
        Fixed(view.Scale, ScaleTransform.ScaleYProperty, 0.85, 1, 180, ease);
    }

    private void CloseInspect()
    {
        InspectOverlay.BeginAnimation(OpacityProperty, null);
        InspectOverlay.Visibility = Visibility.Collapsed;
        InspectHost.Child = null;
    }

    private void InspectOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // don't let the click reach the board underneath
        CloseInspect();
    }

    // =================================================================== helpers

    private DispatcherTimer CreateTimer(double ms, Action tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            tick();
        };
        return t;
    }

    /// <summary>A UI animation with a fixed duration (ignores the animation-speed slider).</summary>
    private static void Fixed(IAnimatable target, DependencyProperty property, double from, double to, double ms,
        IEasingFunction? ease = null)
    {
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        });
        if (target is DependencyObject d) d.SetValue(property, to);
    }
}
