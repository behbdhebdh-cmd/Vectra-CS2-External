using System.Windows;
using System.Windows.Media;

namespace Vectra.External;

internal static class LuminPalette
{
    public static Brush Background { get; } = Brush("#19191C");
    public static Brush Surface { get; } = Brush("#1C1C21");
    public static Brush Card { get; } = Brush("#24242B");
    public static Brush Widget { get; } = Brush("#303039");
    public static Brush Border { get; } = Brush("#34343F");
    public static Brush Text { get; } = Brush("#F3F3F8");
    public static Brush Muted { get; } = Brush("#9A9AAE");
    public static Brush Accent { get; } = Brush("#B0B4FF");
    public static Brush AccentText { get; } = Brush("#15151B");
    public static Brush Warning { get; } = Brush("#F2BC68");
    public static Brush Danger { get; } = Brush("#FA6672");
    public static Brush GlassBorder { get; } = Brush("#52DDE4EE");
    public static Brush GlassWidget { get; } = Brush("#78272D38");

    public static Brush GlassCard()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#C22C303A"), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#A51D222B"), .58));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#B3262933"), 1));
        brush.Freeze();
        return brush;
    }

    public static Brush Brush(string hex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }
}
