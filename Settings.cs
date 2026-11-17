using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace PreciseClock;

public sealed class Settings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PreciseClock");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 170;
    public bool Topmost { get; set; } = true;
    /// <summary>밀리초 자릿수 0(표시 안 함) ~ 3</summary>
    public int MsDigits { get; set; } = 3;
    public bool ShowStatus { get; set; } = true;

    public string BackgroundColor { get; set; } = ClockColors.ToHex(ClockColors.Default.Background);
    public string TimeColor { get; set; } = ClockColors.ToHex(ClockColors.Default.Time);
    public string MsColor { get; set; } = ClockColors.ToHex(ClockColors.Default.Ms);
    public string StatusColor { get; set; } = ClockColors.ToHex(ClockColors.Default.Status);

    [JsonIgnore]
    public ClockColors Colors
    {
        get => new(
            Parse(BackgroundColor, ClockColors.Default.Background),
            Parse(TimeColor, ClockColors.Default.Time),
            Parse(MsColor, ClockColors.Default.Ms),
            Parse(StatusColor, ClockColors.Default.Status));
        set
        {
            BackgroundColor = ClockColors.ToHex(value.Background);
            TimeColor = ClockColors.ToHex(value.Time);
            MsColor = ClockColors.ToHex(value.Ms);
            StatusColor = ClockColors.ToHex(value.Status);
        }
    }

    static Color Parse(string? text, Color fallback)
    {
        try
        {
            return text is null ? fallback : ClockColors.Hex(text);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            settings.MsDigits = Math.Clamp(settings.MsDigits, 0, 3);
            return settings;
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 설정 저장 실패는 시계 동작과 무관하므로 무시
        }
    }
}
