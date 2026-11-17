using System.Windows.Media;

namespace PreciseClock;

public enum ColorTarget { Background, Time, Ms, Status }

public sealed record ClockColors(Color Background, Color Time, Color Ms, Color Status)
{
    public static ClockColors Default { get; } = new(Hex("#FF0B0B0B"), Hex("#FFEDEDED"), Hex("#FF7A7A7A"), Hex("#FF5A5A5A"));

    public Color this[ColorTarget target] => target switch
    {
        ColorTarget.Background => Background,
        ColorTarget.Time => Time,
        ColorTarget.Ms => Ms,
        _ => Status,
    };

    public ClockColors With(ColorTarget target, Color color) => target switch
    {
        ColorTarget.Background => this with { Background = color },
        ColorTarget.Time => this with { Time = color },
        ColorTarget.Ms => this with { Ms = color },
        _ => this with { Status = color },
    };

    public static Color Hex(string text) => (Color)ColorConverter.ConvertFromString(text);

    public static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}

public static class Paint
{
    /// <summary>바뀌지 않는 단색 브러시 (Freeze 해 두면 WPF가 변경 추적을 안 해서 가벼움)</summary>
    public static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Solid(byte r, byte g, byte b) => Solid(Color.FromRgb(r, g, b));
}
