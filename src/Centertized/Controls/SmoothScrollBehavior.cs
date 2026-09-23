using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Centertized.Controls;

/// <summary>
/// WPF ScrollViewer defaultně scrolluje kolečkem myši "krokově" (skok po pevných
/// řádcích), ne plynule s hybností jako moderní Windows appky (Nastavení, Edge...).
///
/// První pokus (řetězené DoubleAnimation na "cvak" kolečka) pořád necítil přirozeně -
/// pořádné momentum scrollování je fyzika (rychlost + tření), ne posloupnost animací s
/// pevnou dobou trvání. Tohle počítá rychlost každý snímek přes CompositionTarget.
/// Rendering: každý "cvak" kolečka rychlost zvýší, každý snímek se sníží násobením
/// (tření) - výsledek je plynulé zrychlení při rychlém scrollování a přirozené
/// doznění, podobně jako touchpad/WinUI momentum scroll.
/// </summary>
public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject element, bool value) => element.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject element) => (bool)element.GetValue(EnableProperty);

    // Kolik pixelů/snímek rychlosti přidá jeden "cvak" kolečka (Delta bývá +-120).
    // Celková ujetá dráha jednoho cvaku je geometrická řada = PixelsPerNotch / (1 - Friction) -
    // s původním 90/0.82 to vycházelo na ~500px na jeden cvak (odtud ten "skok skoro
    // až dolů"), tohle dává rozumnějších ~85px.
    private const double PixelsPerNotch = 13;

    // Kolik rychlosti zůstane každý snímek (0-1) - nižší = rychlejší doznění.
    private const double Friction = 0.85;

    // Pojistný strop rychlosti (px/snímek) - i kdyby myš/touchpad poslaly hodně
    // wheel událostí naráz (vysoké rozlišení kolečka apod.), scroll nikdy nevystřelí.
    private const double MaxVelocity = 45;

    // Pod touhle rychlostí (px/snímek) se animace zastaví úplně, ať to nedoznívá donekonečna.
    private const double StopThreshold = 0.05;

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer || !(bool)e.NewValue)
        {
            return;
        }

        var velocity = 0d;
        EventHandler? renderingHandler = null;

        scrollViewer.PreviewMouseWheel += OnWheel;
        scrollViewer.Unloaded += (_, _) =>
        {
            scrollViewer.PreviewMouseWheel -= OnWheel;
            if (renderingHandler is not null)
            {
                CompositionTarget.Rendering -= renderingHandler;
            }
        };

        void OnWheel(object sender, MouseWheelEventArgs args)
        {
            args.Handled = true;
            velocity = Math.Clamp(velocity - args.Delta / 120.0 * PixelsPerNotch, -MaxVelocity, MaxVelocity);

            renderingHandler ??= OnRendering;
            CompositionTarget.Rendering -= renderingHandler; // ať se nepřihlásí dvakrát
            CompositionTarget.Rendering += renderingHandler;
        }

        void OnRendering(object? sender, EventArgs args)
        {
            var proposedOffset = scrollViewer.VerticalOffset + velocity;
            var clampedOffset = Math.Clamp(proposedOffset, 0, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(clampedOffset);

            // Narazili jsme na horní/dolní okraj - rychlost zahodit, ať se dál "netlačí".
            if (clampedOffset != proposedOffset)
            {
                velocity = 0;
            }

            velocity *= Friction;

            if (Math.Abs(velocity) < StopThreshold)
            {
                CompositionTarget.Rendering -= renderingHandler;
                renderingHandler = null;
            }
        }
    }
}
