using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Centertized.Services;

/// <summary>
/// Picks the "main" color from an icon, the one a person would assign to it - not a plain average (that would turn
/// an icon with a colorful logo on a transparent/grey background into a muddy grey). Pixels are weighted
/// by saturation and brightness, split into hue buckets and the strongest bucket wins; the color is averaged
/// only from the pixels of the winning bucket. Purely grey icons get a mid grey.
/// </summary>
public static class AccentColorExtractor
{
    private const int HueBuckets = 12;

    public static Color? FromBitmap(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var weight = new double[HueBuckets];
        var red = new double[HueBuckets];
        var green = new double[HueBuckets];
        var blue = new double[HueBuckets];
        double grayTotal = 0;
        double grayCount = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3] / 255.0;
            if (alpha < 0.5)
            {
                continue; // the transparent surroundings of the icon aren't its color
            }

            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            RgbToHsv(r, g, b, out var hue, out var saturation, out var value);

            grayTotal += value;
            grayCount++;

            // Almost black/white/grey pixels (edges, shadows, background) don't make up the icon's color.
            var w = saturation * value * alpha;
            if (saturation < 0.25 || value < 0.2 || w < 0.05)
            {
                continue;
            }

            var bucket = Math.Min(HueBuckets - 1, (int)(hue / 360.0 * HueBuckets));
            weight[bucket] += w;
            red[bucket] += r * w;
            green[bucket] += g * w;
            blue[bucket] += b * w;
        }

        if (grayCount == 0)
        {
            return null;
        }

        var best = 0;
        for (var i = 1; i < HueBuckets; i++)
        {
            if (weight[i] > weight[best])
            {
                best = i;
            }
        }

        if (weight[best] <= 0)
        {
            var gray = (byte)Math.Clamp(grayTotal / grayCount * 255, 110, 200);
            return Color.FromRgb(gray, gray, gray);
        }

        var color = Color.FromRgb(
            (byte)(red[best] / weight[best]),
            (byte)(green[best] / weight[best]),
            (byte)(blue[best] / weight[best]));
        return EnsureVisible(color);
    }

    // The dot must be readable on both dark and light backgrounds - a too dark color is lightened a bit.
    private static Color EnsureVisible(Color color)
    {
        RgbToHsv(color.R, color.G, color.B, out var hue, out var saturation, out var value);
        value = Math.Clamp(value, 0.55, 0.95);
        HsvToRgb(hue, saturation, value, out var r, out var g, out var b);
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void RgbToHsv(double r, double g, double b, out double hue, out double saturation, out double value)
    {
        r /= 255; g /= 255; b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }
    }

    private static void HsvToRgb(double hue, double saturation, double value, out double r, out double g, out double b)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        (r, g, b) = (hue / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        r = (r + m) * 255;
        g = (g + m) * 255;
        b = (b + m) * 255;
    }
}
