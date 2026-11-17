using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PreciseClock;

public partial class App : Application
{
    TimeSync? _sync;
    MainWindow? _window;

    // 옵션: --log (동기화 기록을 %APPDATA%\PreciseClock\sync.log 에 남김)
    //       --server a,b,c (NTP 서버 목록 바꾸기, 테스트용)
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 예상 못한 오류가 나도 시계가 말없이 사라지지 않도록 기록만 하고 계속 실행
        DispatcherUnhandledException += (_, args) =>
        {
            WriteError(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteError(args.ExceptionObject);

        string[] servers = TimeSync.DefaultServers;
        bool log = false;
        for (int i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i])
            {
                case "--log":
                    log = true;
                    break;
                case "--server" when i + 1 < e.Args.Length:
                    servers = e.Args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
            }
        }

        _sync = new TimeSync(servers);
        if (log)
            _sync.Log = OpenLog();
        _sync.Start();

        _window = new MainWindow(_sync);
        _window.Show();
#if DEBUG
        // 화면 확인용: 시작하자마자 색상 설정 창 열기
        if (e.Args.Contains("--open-colors"))
            _window.Dispatcher.BeginInvoke(_window.OpenColorSettings, DispatcherPriority.ApplicationIdle);
#endif
    }

    // 윈도우 종료·로그오프 때는 창의 Closing 이벤트가 오지 않으므로 여기서 저장
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _window?.SaveSettings();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sync?.Dispose();
        base.OnExit(e);
    }

    static Action<string> OpenLog()
    {
        Directory.CreateDirectory(Settings.Folder);
        var writer = TextWriter.Synchronized(new StreamWriter(Path.Combine(Settings.Folder, "sync.log"), append: true) { AutoFlush = true });
        return line => writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}");
    }

    static void WriteError(object error)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {error}{Environment.NewLine}");
        }
        catch
        {
            // 기록조차 못 하면 할 수 있는 게 없음
        }
    }
}
