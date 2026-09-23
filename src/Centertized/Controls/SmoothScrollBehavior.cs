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

        var from = scrollViewer.VerticalOffset;
        var target = Math.Clamp(from - e.Delta * PixelsPerWheelNotch, 0, scrollViewer.ScrollableHeight);

        var animation = new DoubleAnimation(from, target, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scrollViewer.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue);
    }
}
