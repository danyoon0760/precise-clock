using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PreciseClock;

/// <summary>
/// 포토샵식 색 선택기: 선명함×밝기 사각형 + 색상 막대 + 투명도 막대 + HEX/RGB/불투명도 입력.
/// 내부 값은 HSV로 들고 있어서 회색·검정을 지나가도 고르던 색상(Hue)이 사라지지 않는다.
/// </summary>
public partial class ColorPicker : UserControl
{
    static readonly string[] PaletteColors =
    [
        "#FFFFFF", "#D9D9D9", "#A6A6A6", "#737373", "#404040", "#1A1A1A", "#000000",
        "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#39FF14", "#00C7BE", "#32ADE6",
        "#007AFF", "#5856D6", "#AF52DE", "#FF2D55", "#A2845E", "#FFE4B5", "#B0E0FF",
    ];

    static readonly Brush SwatchBorder = Paint.Solid(0xC8, 0xC8, 0xC8);

    double _hue, _sat, _val, _alpha = 1;
    Color _original;
    bool _updatingFields;

    public ColorPicker()
    {
        InitializeComponent();
        foreach (var hex in PaletteColors)
        {
            var color = ClockColors.Hex(hex);
            var swatch = new Border
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(3),
                BorderBrush = SwatchBorder,
                BorderThickness = new Thickness(1),
                Background = Paint.Solid(color),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };
            // 자주 쓰는 색은 색만 바꾸고 지금 투명도는 유지
            swatch.MouseLeftButtonDown += (_, _) => SetRgb(color.R, color.G, color.B, skip: null);
            Palette.Children.Add(swatch);
        }
        Loaded += (_, _) => Refresh(raise: false);
    }

    public event Action<Color>? ColorChanged;

    public Color Color => FromHsv(_hue, _sat, _val, _alpha);

    /// <param name="original">"처음 색" 칸에 보여 줄 색</param>
    public void SetColor(Color color, Color original)
    {
        _original = original;
        OldSwatch.Background = Paint.Solid(original);
        (_hue, _sat, _val) = ToHsv(color);
        _alpha = color.A / 255.0;
        Refresh(raise: false);
    }

    // ── 화면 갱신 ────────────────────────────────────────

    void Refresh(bool raise, TextBox? skip = null)
    {
        var color = Color;
        var opaque = Color.FromRgb(color.R, color.G, color.B);

        HueFill.Fill = Paint.Solid(FromHsv(_hue, 1, 1, 1));
        AlphaFill.Fill = new LinearGradientBrush(opaque, Color.FromArgb(0, color.R, color.G, color.B), 90);
        NewSwatch.Background = Paint.Solid(color);

        double w = SvArea.Width, h = SvArea.Height;
        Canvas.SetLeft(SvThumb, _sat * w - SvThumb.Width / 2);
        Canvas.SetTop(SvThumb, (1 - _val) * h - SvThumb.Height / 2);
        Canvas.SetTop(HueThumb, _hue / 360 * HueArea.Height - HueThumb.Height / 2);
        Canvas.SetTop(AlphaThumb, (1 - _alpha) * AlphaArea.Height - AlphaThumb.Height / 2);

        _updatingFields = true;
        if (skip != HexBox) HexBox.Text = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        if (skip != RBox) RBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
        if (skip != GBox) GBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
        if (skip != BBox) BBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
        if (skip != ABox) ABox.Text = Math.Round(_alpha * 100).ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;

        if (raise)
            ColorChanged?.Invoke(color);
    }

    void SetRgb(byte r, byte g, byte b, TextBox? skip)
    {
        var (hue, sat, val) = ToHsv(Color.FromRgb(r, g, b));
        // 회색/검정은 색상·선명함 정보가 없으니 이전 값을 유지해서 사각형이 엉뚱하게 튀지 않게 함
        if (val > 0)
        {
            if (sat > 0)
                _hue = hue;
            _sat = sat;
        }
        _val = val;
        Refresh(raise: true, skip);
    }

    // ── 마우스 ──────────────────────────────────────────

    void OnAreaDown(object sender, MouseButtonEventArgs e)
    {
        var area = (FrameworkElement)sender;
        area.CaptureMouse();
        UpdateFromMouse(area, e.GetPosition(area));
    }

    void OnAreaMove(object sender, MouseEventArgs e)
    {
        var area = (FrameworkElement)sender;
        if (area.IsMouseCaptured)
            UpdateFromMouse(area, e.GetPosition(area));
    }

    void OnAreaUp(object sender, MouseButtonEventArgs e) => ((FrameworkElement)sender).ReleaseMouseCapture();

    void OnAreaWheel(object sender, MouseWheelEventArgs e)
    {
        double step = Math.Sign(e.Delta) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01);
        if (sender == HueArea)
            _hue = Math.Clamp(_hue - step * 360, 0, 360);
        else
            _alpha = Math.Clamp(_alpha + step, 0, 1);
        Refresh(raise: true);
        e.Handled = true;
    }

    void UpdateFromMouse(FrameworkElement area, Point p)
    {
        double x = Math.Clamp(p.X / area.ActualWidth, 0, 1);
        double y = Math.Clamp(p.Y / area.ActualHeight, 0, 1);
        if (area == SvArea)
        {
            _sat = x;
            _val = 1 - y;
        }
        else if (area == HueArea)
        {
            _hue = y * 360;
        }
        else
        {
            _alpha = 1 - y;
        }
        Refresh(raise: true);
    }

    void OnOldSwatchClick(object sender, MouseButtonEventArgs e)
    {
        (_hue, _sat, _val) = ToHsv(_original);
        _alpha = _original.A / 255.0;
        Refresh(raise: true);
    }

    // ── 입력 칸 ─────────────────────────────────────────

    void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields)
            return;
        string text = HexBox.Text.Trim().TrimStart('#');
        if (text.Length == 3)
            text = string.Concat(text.Select(c => $"{c}{c}"));
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            SetRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, skip: HexBox);
    }

    void OnRgbChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields)
            return;
        if (TryByte(RBox, out byte r) && TryByte(GBox, out byte g) && TryByte(BBox, out byte b))
            SetRgb(r, g, b, skip: (TextBox)sender);
    }

    void OnAlphaChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFields)
            return;
        if (int.TryParse(ABox.Text, out int percent) && percent is >= 0 and <= 100)
        {
            _alpha = percent / 100.0;
            Refresh(raise: true, skip: ABox);
        }
    }

    void OnFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box == HexBox)
            return;
        int step = e.Key switch { Key.Up => 1, Key.Down => -1, _ => 0 };
        if (step == 0)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            step *= 10;
        int max = box == ABox ? 100 : 255;
        int value = int.TryParse(box.Text, out int n) ? n : 0;
        box.Text = Math.Clamp(value + step, 0, max).ToString(CultureInfo.InvariantCulture);
        box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    // 칸을 벗어나면 잘못 입력된 값을 현재 색으로 되돌려 놓음
    void OnFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Refresh(raise: false);

    static bool TryByte(TextBox box, out byte value) =>
        byte.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    // ── HSV 변환 ────────────────────────────────────────

    static Color FromHsv(double hue, double sat, double val, double alpha)
    {
        double c = val * sat;
        double h = (hue % 360) / 60;
        double x = c * (1 - Math.Abs(h % 2 - 1));
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        double m = val - c;
        return Color.FromArgb(ToByte(alpha), ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    static (double Hue, double Sat, double Val) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double hue = d == 0 ? 0
            : max == r ? 60 * (((g - b) / d) % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        if (hue < 0)
            hue += 360;
        return (hue, max == 0 ? 0 : d / max, max);
    }

    static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
