using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PreciseClock;

public partial class MainWindow : Window
{
    static readonly TimeSpan Kst = TimeSpan.FromHours(9);
    static readonly Brush StatusErrorBrush = Paint.Solid(0xC0, 0x4A, 0x4A);
    // 동기화 전 시각은 글자색을 이만큼 흐리게
    const double UnsyncedOpacity = 0.35;

    readonly TimeSync _sync;
    readonly Settings _settings = Settings.Load();
    readonly DispatcherTimer _statusTimer;
    Brush _timeBrush = Brushes.White;
    Brush _timeUnsyncedBrush = Brushes.Gray;
    Brush _statusBrush = Brushes.Gray;
    string _shownTime = "";
    string _shownMs = "";
    bool? _shownSynced;

    public MainWindow(TimeSync sync)
    {
        InitializeComponent();
        _sync = sync;
        ApplySettings();

        // 모니터 주사율마다 호출 → 초가 바뀌는 순간을 프레임 단위(~16ms)로 정확히 반영
        CompositionTarget.Rendering += OnRendering;
        _statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdateStatus(), Dispatcher);
        UpdateStatus();
        Closing += (_, _) => SaveSettings();
    }

    bool IsFullscreen => WindowState == WindowState.Maximized;

    // ── 표시 ─────────────────────────────────────────────

    void OnRendering(object? sender, EventArgs e)
    {
        bool synced = _sync.TryGetUtcNow(out var utc);
        var kst = utc + Kst;

        string time = kst.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (time != _shownTime)
            TimeText.Text = _shownTime = time;

        if (_settings.MsDigits > 0)
        {
            string ms = FormatFraction(kst.Millisecond, _settings.MsDigits);
            if (ms != _shownMs)
                MsText.Text = _shownMs = ms;
        }

        if (synced != _shownSynced)
        {
            TimeText.Foreground = synced ? _timeBrush : _timeUnsyncedBrush;
            _shownSynced = synced;
        }
    }

    /// <summary>
    /// 반올림하지 않고 잘라냄: .999 → 1자리면 .9 (초가 넘어가기 전에 .0이 보이면 안 되므로)
    /// </summary>
    static string FormatFraction(int millisecond, int digits) => digits switch
    {
        1 => "." + (millisecond / 100).ToString(CultureInfo.InvariantCulture),
        2 => "." + (millisecond / 10).ToString("00", CultureInfo.InvariantCulture),
        _ => "." + millisecond.ToString("000", CultureInfo.InvariantCulture),
    };

    void UpdateStatus()
    {
        var (text, isError) = StatusLine.Format(_sync.Status, _sync.PcOffset, _sync.ErrorMs);
        StatusText.Text = text;
        StatusText.Foreground = isError ? StatusErrorBrush : _statusBrush;
    }

    // ── 색상 ─────────────────────────────────────────────

    void ApplyColors(ClockColors colors)
    {
        var background = colors.Background;
        // 완전히 투명(알파 0)한 픽셀은 마우스가 뚫고 지나가서 창을 못 잡게 되므로 최소 1을 남김
        if (background.A == 0)
            background.A = 1;
        Background = Paint.Solid(background);

        var dim = colors.Time;
        dim.A = (byte)(dim.A * UnsyncedOpacity);
        _timeBrush = Paint.Solid(colors.Time);
        _timeUnsyncedBrush = Paint.Solid(dim);
        MsText.Foreground = Paint.Solid(colors.Ms);
        _statusBrush = Paint.Solid(colors.Status);

        _shownSynced = null;
        UpdateStatus();
    }

    internal void OpenColorSettings()
    {
        var dialog = new ColorSettingsWindow(_settings.Colors, ApplyColors) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Colors = dialog.Result;
            _settings.Save();
        }
    }

    // ── 조작 ─────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            return;
        }
        if (IsFullscreen)
            return;
        try
        {
            DragMove();
            SaveSettings();
        }
        catch (InvalidOperationException)
        {
            // 버튼이 이미 떼어진 경우
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape:
                if (IsFullscreen)
                    ToggleFullscreen();
                break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.T: SetTopmost(!_settings.Topmost); SaveSettings(); break;
            case Key.M: SetMsDigits((_settings.MsDigits + 3) % 4); SaveSettings(); break; // 3 → 2 → 1 → 끔 → 3
            case Key.I: SetStatusVisible(!_settings.ShowStatus); SaveSettings(); break;
            case Key.C: OpenColorSettings(); break;
            case Key.R: _sync.RequestResync(); break;
            default: return;
        }
        e.Handled = true;
    }

    void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        TopmostItem.IsChecked = _settings.Topmost;
        StatusItem.IsChecked = _settings.ShowStatus;
        foreach (var item in new[] { Ms0Item, Ms1Item, Ms2Item, Ms3Item })
            item.IsChecked = MsDigitsOf(item) == _settings.MsDigits;
    }

    static int MsDigitsOf(MenuItem item) => int.Parse((string)item.Tag, CultureInfo.InvariantCulture);

    void OnTopmostClick(object sender, RoutedEventArgs e)
    {
        SetTopmost(!_settings.Topmost);
        SaveSettings();
    }

    void OnMsDigitsClick(object sender, RoutedEventArgs e)
    {
        SetMsDigits(MsDigitsOf((MenuItem)sender));
        SaveSettings();
    }

    void OnStatusClick(object sender, RoutedEventArgs e)
    {
        SetStatusVisible(!_settings.ShowStatus);
        SaveSettings();
    }
    void OnColorsClick(object sender, RoutedEventArgs e) => OpenColorSettings();
    void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    void OnResyncClick(object sender, RoutedEventArgs e) => _sync.RequestResync();
    void OnExitClick(object sender, RoutedEventArgs e) => Close();

    void ToggleFullscreen() =>
        WindowState = IsFullscreen ? WindowState.Normal : WindowState.Maximized;

    void SetTopmost(bool topmost)
    {
        _settings.Topmost = topmost;
        Topmost = topmost;
    }

    void SetMsDigits(int digits)
    {
        _settings.MsDigits = digits;
        MsText.Visibility = digits > 0 ? Visibility.Visible : Visibility.Collapsed;
        _shownMs = "";
    }

    void SetStatusVisible(bool visible)
    {
        _settings.ShowStatus = visible;
        StatusText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 설정 ─────────────────────────────────────────────

    void ApplySettings()
    {
        Width = _settings.Width;
        Height = _settings.Height;
        SetTopmost(_settings.Topmost);
        SetMsDigits(_settings.MsDigits);
        SetStatusVisible(_settings.ShowStatus);
        ApplyColors(_settings.Colors);
        PlaceWindow();
    }

    /// <summary>저장된 위치가 지금 화면 안에 있으면 그대로, 아니면(모니터 분리 등) 가운데로</summary>
    void PlaceWindow()
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (_settings.Left is { } left && _settings.Top is { } top &&
            screen.IntersectsWith(new Rect(left, top, Width, Height)))
        {
            Left = left;
            Top = top;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        }
    }

    /// <summary>바꿀 때마다 바로 저장. 윈도우 종료·로그오프 때는 창 닫기(Closing)가 안 불려서 그때만 믿으면 날아감.</summary>
    internal void SaveSettings()
    {
        var bounds = IsFullscreen ? RestoreBounds : new Rect(Left, Top, ActualWidth, ActualHeight);
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Save();
    }
}
