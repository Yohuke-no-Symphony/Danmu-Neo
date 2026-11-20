using Avalonia.Media;

namespace Yohuke.DanmuNeo.Services.Appearance;

internal static class ColorContrast
{
    private const double MINIMUM_TEXT_CONTRAST = 4.5;

    internal static Color CompositeOverOpaqueBackground(ISolidColorBrush foreground, Color background)
    {
        var color = foreground.Color;
        var opacity = Math.Clamp(color.A / 255d * foreground.Opacity, 0, 1);
        return Color.FromRgb(
            BlendChannel(color.R, background.R, opacity),
            BlendChannel(color.G, background.G, opacity),
            BlendChannel(color.B, background.B, opacity));
    }

    internal static IBrush ResolveForeground(Color background, ISolidColorBrush? preferred)
    {
        var backgroundLuminance = GetLuminance(background);
        if (preferred is not null)
        {
            var renderedForeground = CompositeOverOpaqueBackground(preferred, background);
            if (GetContrastRatio(GetLuminance(renderedForeground), backgroundLuminance) >= MINIMUM_TEXT_CONTRAST)
            {
                return preferred;
            }
        }

        return GetContrastRatio(0, backgroundLuminance) >= GetContrastRatio(1, backgroundLuminance)
            ? Brushes.Black
            : Brushes.White;
    }

    private static byte BlendChannel(byte foreground, byte background, double opacity)
    {
        return (byte)Math.Round(background + (foreground - background) * opacity);
    }

    private static double GetContrastRatio(double firstLuminance, double secondLuminance)
    {
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double GetLuminance(Color color)
    {
        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
