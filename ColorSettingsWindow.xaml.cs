using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PreciseClock;

public partial class ColorSettingsWindow : Window
{
    static readonly (ColorTarget Target, string Title, string Description)[] Targets =
    [
        (ColorTarget.Background, "배경", "창 바탕색. 투명도를 낮추면 뒤 화면이 비쳐 보여요."),
        (ColorTarget.Time, "시각 글자", "큰 숫자 23:11:31. 표준시에 맞추기 전에는 흐리게 보입니다."),
        (ColorTarget.Ms, "밀리초 글자", "옆의 작은 숫자 .384"),
        (ColorTarget.Status, "상태줄 글자", "아래 'KRISS 표준시 · 오차…' 한 줄. 연결 실패 시엔 빨간색으로 바뀝니다."),
    ];

    static readonly (string Name, ClockColors Colors)[] Themes =
    [
        ("기본", ClockColors.Default),
        ("화이트", Theme("#FFF4F4F4", "#FF1C1C1C", "#FF8C8C8C", "#FFA0A0A0")),
        ("LED 그린", Theme("#FF000000", "#FF3DFF5A", "#FF1F9E33", "#FF2F6B39")),
        ("앰버", Theme("#FF0A0600", "#FFFFA31A", "#FFB86E00", "#FF7A5520")),
        ("블루", Theme("#FF0B1220", "#FF8EC5FF", "#FF4A78B0", "#FF3E5778")),
        ("반투명", Theme("#B3000000", "#FFFFFFFF", "#FFBDBDBD", "#FF9A9A9A")),
        ("글자만", Theme("#00000000", "#FFFFFFFF", "#E6FFFFFF", "#B3FFFFFF")),
    ];

    static readonly Brush FrameBrush = Paint.Solid(0xC8, 0xC8, 0xC8);
    static readonly Brush HintBrush = Paint.Solid(0x80, 0x80, 0x80);

    readonly ClockColors _original;
    readonly Action<ClockColors> _preview;
    readonly Dictionary<ColorTarget, (RadioButton Card, Border Swatch)> _cards = [];
    ClockColors _current;
    ColorTarget _target;
    bool _accepted;

    public ColorSettingsWindow(ClockColors colors, Action<ClockColors> preview)
    {
        InitializeComponent();
        _original = _current = colors;
        _preview = preview;

        foreach (var (target, title, description) in Targets)
            AddTargetCard(target, title, description);
        foreach (var (name, theme) in Themes)
            AddThemeButton(name, theme);

        Picker.ColorChanged += color =>
        {
            _current = _current.With(_target, color);
            _cards[_target].Swatch.Background = Paint.Solid(color);
            _preview(_current);
        };
        Select(ColorTarget.Background);

        // 취소·Esc·X 로 닫으면 미리보기를 원래대로
        Closed += (_, _) =>
        {
            if (!_accepted)
                _preview(_original);
        };
    }

    public ClockColors Result => _current;

    void AddTargetCard(ColorTarget target, string title, string description)
    {
        var swatch = new Border { CornerRadius = new CornerRadius(4), Background = Paint.Solid(_current[target]) };
        var swatchFrame = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
        swatchFrame.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("Checker"),
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(1),
        });
        swatchFrame.Children.Add(swatch);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = HintBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var content = new DockPanel();
        DockPanel.SetDock(swatchFrame, Dock.Left);
        content.Children.Add(swatchFrame);
        content.Children.Add(text);

        var card = new RadioButton { Style = (Style)FindResource("TargetCard"), Content = content, GroupName = "target" };
        card.Checked += (_, _) => Select(target);
        TargetList.Children.Add(card);
        _cards[target] = (card, swatch);
    }

    void AddThemeButton(string name, ClockColors theme)
    {
        var time = new TextBlock { FontSize = 17, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        time.Inlines.Add(new Run("12:34") { Foreground = Paint.Solid(theme.Time) });
        time.Inlines.Add(new Run(".5") { Foreground = Paint.Solid(theme.Ms), FontSize = 11 });

        var preview = new Grid { Width = 78, Height = 42 };
        preview.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = (Brush)FindResource("DarkChecker") });
        preview.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = Paint.Solid(theme.Background),
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(1),
            Child = time,
        });

        var item = new StackPanel { Margin = new Thickness(0, 0, 10, 6), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        item.Children.Add(preview);
        item.Children.Add(new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        item.MouseLeftButtonUp += (_, _) => ApplyTheme(theme);
        ThemeList.Children.Add(item);
    }

    void Select(ColorTarget target)
    {
        _target = target;
        _cards[target].Card.IsChecked = true;
        Picker.SetColor(_current[target], _original[target]);
    }

    void ApplyTheme(ClockColors theme)
    {
        _current = theme;
        foreach (var (target, (_, swatch)) in _cards)
            swatch.Background = Paint.Solid(theme[target]);
        Picker.SetColor(theme[_target], _original[_target]);
        _preview(_current);
    }

    void OnResetClick(object sender, RoutedEventArgs e) => ApplyTheme(ClockColors.Default);

    void OnOkClick(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        DialogResult = true;
    }

    static ClockColors Theme(string background, string time, string ms, string status) =>
        new(ClockColors.Hex(background), ClockColors.Hex(time), ClockColors.Hex(ms), ClockColors.Hex(status));
}
