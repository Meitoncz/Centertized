using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // Jiný prvek se scrollováním uvnitř (ListBox, ItemsControl...): ScrollViewer je až v jeho
        // šabloně, která v době zapnutí ještě nemusí existovat (např. prvek je Collapsed), proto se
        // na kolečko čeká na hostiteli a vnitřní ScrollViewer se hledá až při prvním otočení.
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
