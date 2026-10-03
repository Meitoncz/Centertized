using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Centertized.Controls;

/// <summary>
/// By default a WPF ScrollViewer scrolls with the mouse wheel in "steps" (jumping by fixed
/// lines), not smoothly like modern Windows apps (Settings, Edge...). This is an
/// attached property that hooks PreviewMouseWheel on the ScrollViewer and replaces the
/// stepwise VerticalOffset change with an animation.
///
/// VerticalOffset itself is not an animatable DependencyProperty (it is an ordinary
/// CLR property changed via ScrollToVerticalOffset()), so a helper
/// "proxy" DP is animated and ScrollToVerticalOffset is called only in its PropertyChanged callback.
/// </summary>
public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset", typeof(double), typeof(SmoothScrollBehavior), new PropertyMetadata(0d, OnAnimatedOffsetChanged));

    // Target of the running animation that a further wheel "click" continues from - without
    // this, with fast scrolling (acceleration) every next step would be computed from the
    // current, not yet settled animation position, which looked jerky/bouncy.
    private static readonly DependencyProperty PendingTargetProperty = DependencyProperty.RegisterAttached(
        "PendingTarget", typeof(double?), typeof(SmoothScrollBehavior), new PropertyMetadata(null));

    // How many pixels per wheel "click" - Delta is typically +-120, this gives a
    // step similar to what people are used to from Windows Settings.
    private const double PixelsPerWheelNotch = 1.2;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(260));

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var enable = (bool)e.NewValue;

        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            if (enable)
            {
                scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            }

            return;
        }

        // Another element with scrolling inside (ListBox, ItemsControl...): the ScrollViewer lives in its
        // template, which may not exist yet when this is enabled (e.g. the element is Collapsed), so
        // the wheel is awaited on the host and the inner ScrollViewer is looked up on the first turn.
        if (d is UIElement host)
        {
            host.PreviewMouseWheel -= OnHostPreviewMouseWheel;
            if (enable)
            {
                host.PreviewMouseWheel += OnHostPreviewMouseWheel;
            }
        }
    }

    private static void OnHostPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer((DependencyObject)sender) is { } scrollViewer)
        {
            Animate(scrollViewer, e);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer self)
        {
            return self;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) => Animate((ScrollViewer)sender, e);

    private static void Animate(ScrollViewer scrollViewer, MouseWheelEventArgs e)
    {
        e.Handled = true;

        // Continue from the target of the previous (still running) animation, not from the current
        // "in-flight" position - otherwise quick successive wheel clicks effectively
        // shorten the distance of each step and the scroll jerks.
        var baseline = (double?)scrollViewer.GetValue(PendingTargetProperty) ?? scrollViewer.VerticalOffset;
        var target = Math.Clamp(baseline - e.Delta * PixelsPerWheelNotch, 0, scrollViewer.ScrollableHeight);
        scrollViewer.SetValue(PendingTargetProperty, target);

        var animation = new DoubleAnimation(scrollViewer.VerticalOffset, target, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => scrollViewer.ClearValue(PendingTargetProperty);
        scrollViewer.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue);
    }
}
