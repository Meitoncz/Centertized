using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Centertized.Controls;

/// <summary>
/// WPF ScrollViewer defaultně scrolluje kolečkem myši "krokově" (skok po pevných
/// řádcích), ne plynule jako moderní Windows appky (Nastavení, Edge...). Tohle je
/// attached property, která na ScrollViewer napojí PreviewMouseWheel a nahradí
/// skokovou změnu VerticalOffset animací.
///
/// VerticalOffset samo o sobě není animovatelné DependencyProperty (je to obyčejná
/// CLR vlastnost měněná přes ScrollToVerticalOffset()), proto se animuje pomocná
/// "proxy" DP a v jejím PropertyChanged callbacku se teprve volá ScrollToVerticalOffset.
/// </summary>
public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset", typeof(double), typeof(SmoothScrollBehavior), new PropertyMetadata(0d, OnAnimatedOffsetChanged));

    // Cíl rozjeté animace, na který navazuje případný další "cvak" kolečka - bez
    // tohohle by se při rychlém scrollování (zrychlení) počítal každý další krok
    // z aktuální, ještě neuklidněné pozice animace, což vypadalo trhaně/poskakovaně.
    private static readonly DependencyProperty PendingTargetProperty = DependencyProperty.RegisterAttached(
        "PendingTarget", typeof(double?), typeof(SmoothScrollBehavior), new PropertyMetadata(null));

    // Kolik pixelů na jeden "cvak" kolečka - Delta je typicky +-120, tohle dává
    // podobný krok, na jaký jsou lidé zvyklí z Windows Nastavení.
    private const double PixelsPerWheelNotch = 1.2;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(260));

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = (ScrollViewer)sender;
        e.Handled = true;

        // Navázat na cíl předchozí (ještě běžící) animace, ne na aktuální
        // "rozjetou" pozici - jinak rychlé po sobě jdoucí cvaknutí kolečkem
        // efektivně zkracují ujetou dráhu každého kroku a scroll poskakuje.
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
