using System.Windows;
using System.Windows.Media.Animation;

namespace CosmicCrusade.App.Controls;

/// <summary>Awaitable WPF animations.</summary>
public static class Anim
{
    /// <summary>Global speed multiplier (HUD slider). 1 = normal, 0.5 = twice as fast.</summary>
    public static double SpeedFactor { get; set; } = 1.0;

    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(Math.Max(1, ms * SpeedFactor)));

    public static Task To(IAnimatable target, DependencyProperty property, double to, double ms,
        IEasingFunction? ease = null, double? from = null, double delayMs = 0)
    {
        var tcs = new TaskCompletionSource();
        var a = new DoubleAnimation
        {
            To = to,
            Duration = Ms(ms),
            EasingFunction = ease,
            BeginTime = TimeSpan.FromMilliseconds(delayMs * SpeedFactor),
            FillBehavior = FillBehavior.HoldEnd,
        };
        if (from is not null) a.From = from;
        a.Completed += (_, _) => tcs.TrySetResult();
        target.BeginAnimation(property, a);
        return tcs.Task;
    }

    /// <summary>Stops an animation but keeps the current animated value as the base value.</summary>
    public static void Settle(DependencyObject target, DependencyProperty property)
    {
        object value = target.GetValue(property);
        ((IAnimatable)target).BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    public static Task Delay(double ms) => Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, ms * SpeedFactor)));

    public static IEasingFunction Out => new CubicEase { EasingMode = EasingMode.EaseOut };
    public static IEasingFunction InOut => new CubicEase { EasingMode = EasingMode.EaseInOut };
    public static IEasingFunction In => new CubicEase { EasingMode = EasingMode.EaseIn };
    public static IEasingFunction Back => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
}
