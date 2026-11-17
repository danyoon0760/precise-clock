using System.Diagnostics;

namespace PreciseClock;

/// <summary>시계 아래 상태줄 문구 만들기</summary>
public static class StatusLine
{
    /// <param name="errorMs">지금 시점의 최대 오차 (마지막 측정 뒤로 쌓였을 수 있는 만큼 포함)</param>
    public static (string Text, bool IsError) Format(SyncStatus status, TimeSpan? pcOffset, double? errorMs)
    {
        string pc = pcOffset is { } offset ? FormatPcOffset(offset) : "";
        string error = errorMs is { } ms ? $"오차 ±{FormatError(ms)}" : "";
        return status.State switch
        {
            SyncState.Syncing => ("KRISS 표준시에 맞추는 중…", false),
            SyncState.Synced => ($"KRISS 표준시 · {error} · {pc}", false),
            _ when status.LastSyncTs is { } last => ($"연결 실패 · {FormatAgo(last)} 보정값 사용 중 · {error} · {pc}", true),
            _ => ($"연결 실패 — PC 시계 표시 중 ({status.Message})", true),
        };
    }

    static string FormatError(double ms) => ms < 10 ? $"{ms:0.0}ms" : $"{ms:0}ms";

    static string FormatPcOffset(TimeSpan offset)
    {
        double ms = Math.Abs(offset.TotalMilliseconds);
        if (ms < 1)
            return "PC 시계 정확";
        string amount = ms < 1000 ? $"{ms:0}ms" : $"{ms / 1000:0.000}초";
        return $"PC 시계 {amount} {(offset > TimeSpan.Zero ? "빠름" : "느림")}";
    }

    static string FormatAgo(long ts)
    {
        var elapsed = Stopwatch.GetElapsedTime(ts);
        return elapsed.TotalMinutes < 1 ? "방금"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}분 전"
            : $"{(int)elapsed.TotalHours}시간 전";
    }
}
