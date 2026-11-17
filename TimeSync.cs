using System.Diagnostics;
using System.Net;
using Microsoft.Win32;

namespace PreciseClock;

public enum SyncState { Syncing, Synced, Failed }

/// <param name="ErrorMs">채택한 측정의 왕복지연/2 — 측정 순간에 보정값이 틀릴 수 있는 최대 범위</param>
/// <param name="LastSyncTs">마지막으로 보정값을 채택한 시점(Stopwatch 타임스탬프)</param>
/// <param name="DriftKnown">Stopwatch 주파수 오차를 추정해 보정 중인지</param>
public sealed record SyncStatus(SyncState State, string? Server, double ErrorMs, long? LastSyncTs, bool DriftKnown, string? Message);

/// <summary>
/// KRISS NTP 서버와 주기적으로 비교해 "진짜 UTC"를 계산한다.
///
/// 로컬 기준은 시스템 시계가 아니라 Stopwatch(QPC)다. 시작 시 한 번만 시스템 시계에 붙여 두고
/// 이후로는 단조 증가하는 Stopwatch 경과시간만 쓰므로, 윈도우가 중간에 시계를 바꿔도 화면이 튀지 않는다.
/// 서버와의 차이(offset)와 Stopwatch 자체의 주파수 오차(drift)를 함께 추정한다.
/// </summary>
public sealed class TimeSync : IDisposable
{
    public static readonly string[] DefaultServers = ["ntp.kriss.re.kr", "time.kriss.re.kr", "time2.kriss.re.kr"];

    const int FirstRoundSamples = 6;
    const int RoundSamples = 4;
    const int TimeoutMs = 1500;
    // 공개 NTP 서버의 요청 제한(rate limit)에 걸리지 않도록 샘플 간격은 2초
    static readonly TimeSpan SampleSpacing = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RoundInterval = TimeSpan.FromMinutes(2);
    // 실패하면 15초 → 30초 → 1분 → 2분 간격으로 다시 시도
    static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(15);

    const double MaxSlewTicks = 50 * TimeSpan.TicksPerMillisecond;
    const double SlewSeconds = 2.0;
    const double MaxDrift = 500e-6;
    const double MaxDriftUncertainty = 50e-6;
    const double MinDriftIntervalTicks = 60 * TimeSpan.TicksPerSecond;
    // 마지막 측정 이후 오차가 커지는 속도의 상한. drift를 모르면 일반 PC 수정 진동자 수준(±60ppm),
    // 추정해서 보정 중이면 온도 변화 등으로 남는 정도(±10ppm)로 본다.
    const double UnknownDriftBound = 60e-6;
    const double ResidualDriftBound = 10e-6;

    static readonly double TicksPerTs = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    readonly long _baseTs = Stopwatch.GetTimestamp();
    readonly long _baseUtcTicks = DateTime.UtcNow.Ticks;

    readonly string[] _servers;
    readonly ManualResetEvent _stop = new(false);
    readonly AutoResetEvent _resync = new(false);
    readonly object _lock = new();
    Thread? _thread;

    volatile Model? _model;
    volatile SyncStatus _status = new(SyncState.Syncing, null, 0, null, false, null);
    Measurement? _lastRound;
    bool _hasDrift;

    public TimeSync(string[] servers) => _servers = servers;

    public Action<string>? Log { get; set; }

    public SyncStatus Status => _status;

    /// <summary>보정된 현재 UTC. 아직 한 번도 동기화 못 했으면 시스템 시계를 주고 false.</summary>
    public bool TryGetUtcNow(out DateTime utc)
    {
        var model = _model;
        long ts = Stopwatch.GetTimestamp();
        if (model is null)
        {
            utc = DateTime.UtcNow;
            return false;
        }
        utc = new DateTime(TrueTicks(model, ts), DateTimeKind.Utc);
        return true;
    }

    /// <summary>PC 시계 − 표준시. 양수면 PC가 빠름.</summary>
    public TimeSpan? PcOffset
    {
        get
        {
            var model = _model;
            if (model is null)
                return null;
            long ts = Stopwatch.GetTimestamp();
            long system = DateTime.UtcNow.Ticks;
            return TimeSpan.FromTicks(system - TrueTicks(model, ts));
        }
    }

    /// <summary>지금 화면 시각이 틀릴 수 있는 최대 범위(ms). 측정 오차 + 그 뒤로 쌓였을 수 있는 오차.</summary>
    public double? ErrorMs
    {
        get
        {
            var status = _status;
            return status.LastSyncTs is null ? null : ErrorTicks(status, Stopwatch.GetTimestamp()) / TimeSpan.TicksPerMillisecond;
        }
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "NTP", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void RequestResync() => _resync.Set();

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _stop.Set();
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;
        // 절전 중 Stopwatch가 멈췄을 수 있으니 drift 비교 기준을 버리고 바로 다시 잰다
        lock (_lock)
            _lastRound = null;
        RequestResync();
    }

    long LocalTicks(long ts) => _baseUtcTicks + (long)((ts - _baseTs) * TicksPerTs);

    long TrueTicks(Model model, long ts) => LocalTicks(ts) + (long)Math.Round(model.OffsetAt(ts));

    static double ErrorTicks(SyncStatus status, long ts)
    {
        if (status.LastSyncTs is not { } last)
            return double.PositiveInfinity;
        double elapsed = Math.Max(0, (ts - last) * TicksPerTs);
        return status.ErrorMs * TimeSpan.TicksPerMillisecond
            + elapsed * (status.DriftKnown ? ResidualDriftBound : UnknownDriftBound);
    }

    void Run()
    {
        int failures = 0;
        while (true)
        {
            bool ok;
            try
            {
                ok = RunRound(_model is null ? FirstRoundSamples : RoundSamples);
            }
            catch (Exception e)
            {
                // 어떤 오류가 나도 동기화 스레드가 죽어서 프로그램이 꺼지지 않게
                Log?.Invoke("예상 못한 오류: " + e);
                SetFailed(e.Message);
                ok = false;
            }
            if (_stop.WaitOne(0))
                return;

            failures = ok ? 0 : failures + 1;
            var wait = ok ? RoundInterval : RetryDelay(failures);
            if (WaitHandle.WaitAny([_stop, _resync], wait) == 0)
                return;
        }
    }

    static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromTicks(Math.Min(RoundInterval.Ticks, FirstRetryDelay.Ticks << Math.Min(failures - 1, 10)));

    /// <summary>서버를 순서대로 시도해 처음 성공한 서버의 결과를 쓴다.</summary>
    bool RunRound(int count)
    {
        _resync.Reset();
        // 첫 동기화 때는 좋은 샘플이 나올 때마다 바로 반영 → 첫 샘플만으로도 화면이 즉시 보정됨
        bool progressive = _model is null;
        var errors = new List<string>();

        foreach (var host in _servers)
        {
            IPEndPoint endpoint;
            try
            {
                endpoint = new IPEndPoint(NtpClient.Resolve(host), NtpClient.Port);
            }
            catch (Exception e)
            {
                Log?.Invoke($"{host}: DNS 실패 {e.Message}");
                errors.Add($"{host}: 주소 찾기 실패");
                continue;
            }

            var (best, error) = SampleServer(host, endpoint, count, progressive);
            if (_stop.WaitOne(0))
                return false;
            if (best is { } chosen)
            {
                if (progressive || ShouldAccept(chosen))
                {
                    Apply(chosen, host, endOfRound: true);
                }
                else
                {
                    Log?.Invoke($"기존 보정 유지 (이번 측정 오차 ±{chosen.Rtt / 2 / TimeSpan.TicksPerMillisecond:0.0}ms 가 더 큼)");
                    _status = _status with { State = SyncState.Synced, Message = null };
                }
                return true;
            }
            errors.Add($"{host}: {error}");
        }

        SetFailed(string.Join(", ", errors));
        return false;
    }

    void SetFailed(string message)
    {
        _status = _status with { State = SyncState.Failed, Message = message };
        Log?.Invoke("동기화 실패: " + message);
    }

    /// <summary>한 서버에 여러 번 물어보고 왕복지연이 가장 짧은 측정값을 고른다.</summary>
    (Measurement? Best, string? LastError) SampleServer(string host, IPEndPoint endpoint, int count, bool progressive)
    {
        Measurement? best = null;
        string? lastError = null;
        for (int i = 0; i < count; i++)
        {
            if (i > 0 && _stop.WaitOne(SampleSpacing))
                break;
            try
            {
                var m = Measure(NtpClient.Query(endpoint, TimeoutMs));
                Log?.Invoke($"{host} #{i + 1} rtt={m.Rtt / TimeSpan.TicksPerMillisecond:0.000}ms offset={m.Offset / TimeSpan.TicksPerMillisecond:0.000}ms");
                if (best is null || m.Rtt < best.Value.Rtt)
                {
                    best = m;
                    if (progressive)
                        Apply(m, host, endOfRound: false);
                }
            }
            catch (NtpException e)
            {
                Log?.Invoke($"{host} #{i + 1} {e.Message}");
                lastError = e.Message;
                // 거부당했거나 네트워크 자체가 없으면 같은 서버에 더 물어봐도 소용없음
                if (e.Kind is NtpError.KissOfDeath or NtpError.Network)
                    break;
            }
        }
        return (best, lastError);
    }

    /// <summary>
    /// 새 측정이 지금 보정값보다 정밀하면 채택. 덜 정밀해도 두 오차 범위를 넘어서 어긋나면
    /// 지금 보정값이 틀린 것(절전 복귀 등)이므로 채택. 그 외에는 네트워크가 잠깐 느렸던 것으로 보고 무시.
    /// </summary>
    bool ShouldAccept(Measurement m)
    {
        var model = _model;
        if (model is null)
            return true;
        double sampleError = m.Rtt / 2;
        double modelError = ErrorTicks(_status, m.Ts);
        double disagreement = Math.Abs(model.OffsetAt(m.Ts) - m.Offset);
        return sampleError <= modelError || disagreement > sampleError + modelError;
    }

    Measurement Measure(NtpSample s)
    {
        long t1 = LocalTicks(s.SendTs);
        long t4 = LocalTicks(s.RecvTs);
        long t2 = s.ServerRecvTicks;
        long t3 = s.ServerSendTicks;
        double offset = ((t2 - t1) + (t3 - t4)) / 2.0;
        double rtt = Math.Max(0, (t4 - t1) - (t3 - t2));
        return new Measurement(s.SendTs + (s.RecvTs - s.SendTs) / 2, offset, rtt);
    }

    void Apply(Measurement m, string host, bool endOfRound)
    {
        lock (_lock)
        {
            var old = _model;
            double drift = old?.Drift ?? 0;
            if (endOfRound)
            {
                drift = UpdateDrift(m, drift);
                _lastRound = m;
            }

            long now = Stopwatch.GetTimestamp();
            var next = new Model(m.Ts, m.Offset, drift, now, 0);
            double slew = 0;
            if (old is not null)
            {
                // 작은 차이는 2초에 걸쳐 부드럽게 따라가고, 큰 차이는 즉시 맞춘다
                double diff = old.OffsetAt(now) - next.OffsetAt(now);
                if (Math.Abs(diff) <= MaxSlewTicks)
                    slew = diff;
            }
            _model = next with { SlewTicks = slew };
            _status = new SyncStatus(SyncState.Synced, host, m.Rtt / 2 / TimeSpan.TicksPerMillisecond, m.Ts, _hasDrift, null);
        }
    }

    /// <summary>지난 라운드와 이번 라운드의 offset 변화로 Stopwatch 주파수 오차를 추정 (지수 평균)</summary>
    double UpdateDrift(Measurement m, double drift)
    {
        if (_lastRound is not { } prev)
            return drift;
        double dt = (m.Ts - prev.Ts) * TicksPerTs;
        if (dt < MinDriftIntervalTicks)
            return drift;

        double measured = (m.Offset - prev.Offset) / dt;
        double uncertainty = (m.Rtt + prev.Rtt) / 2 / dt;
        if (uncertainty >= MaxDriftUncertainty || Math.Abs(measured) >= MaxDrift)
            return drift;

        drift = _hasDrift ? 0.7 * drift + 0.3 * measured : measured;
        _hasDrift = true;
        Log?.Invoke($"drift={drift * 1e6:0.00}ppm (측정 {measured * 1e6:0.00}ppm)");
        return drift;
    }

    /// <param name="Ts">측정 시점(송신·수신의 중간, Stopwatch 타임스탬프)</param>
    /// <param name="Offset">표준시 − 로컬 기준시각 (틱)</param>
    /// <param name="Rtt">네트워크 왕복지연 (틱)</param>
    readonly record struct Measurement(long Ts, double Offset, double Rtt);

    sealed record Model(long AnchorTs, double OffsetTicks, double Drift, long SlewStartTs, double SlewTicks)
    {
        public double OffsetAt(long ts)
        {
            double offset = OffsetTicks + Drift * (ts - AnchorTs) * TicksPerTs;
            if (SlewTicks != 0)
            {
                double remaining = 1 - (ts - SlewStartTs) * TicksPerTs / (SlewSeconds * TimeSpan.TicksPerSecond);
                if (remaining > 0)
                    offset += SlewTicks * remaining;
            }
            return offset;
        }
    }
}
