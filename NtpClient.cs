using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PreciseClock;

/// <summary>
/// SNTP 1회 교환 결과. 로컬 시각(송신/수신)은 Stopwatch 타임스탬프, 서버 시각은 UTC 틱.
/// </summary>
public readonly record struct NtpSample(long SendTs, long RecvTs, long ServerRecvTicks, long ServerSendTicks);

public enum NtpError { Network, Timeout, KissOfDeath, Unsynchronized, Invalid }

public sealed class NtpException(NtpError kind, string message) : Exception(message)
{
    public NtpError Kind { get; } = kind;
}

public static class NtpClient
{
    public const int Port = 123;
    const int PacketSize = 48;
    static readonly long NtpEpochTicks = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public static IPAddress Resolve(string host)
    {
        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.First();
    }

    /// <summary>
    /// 동기 소켓으로 요청 1번을 보낸다. 비동기 대신 블로킹 수신을 쓰는 이유는
    /// 응답 도착 직후 바로 타임스탬프를 찍어 스케줄링 지연이 오프셋에 섞이지 않게 하기 위함.
    /// </summary>
    public static NtpSample Query(IPEndPoint server, int timeoutMs)
    {
        var request = new byte[PacketSize];
        request[0] = 0x23; // LI=0, VN=4, Mode=3(client)
        // 송신 타임스탬프 자리에 난수를 넣으면 서버가 origin 필드로 되돌려준다 → 엉뚱한 응답 걸러냄
        var nonce = request.AsSpan(40, 8);
        RandomNumberGenerator.Fill(nonce);

        using var socket = Open(server);

        var reply = new byte[512];
        long deadline = Stopwatch.GetTimestamp() + timeoutMs * Stopwatch.Frequency / 1000;
        long sendTs = Stopwatch.GetTimestamp();
        try
        {
            socket.Send(request);
        }
        catch (SocketException)
        {
            // 와이파이 끊김·절전 복귀 직후 등 경로가 없을 때
            throw new NtpException(NtpError.Network, "네트워크 연결 안 됨");
        }

        while (true)
        {
            int remainingMs = (int)((deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency);
            if (remainingMs <= 0)
                throw new NtpException(NtpError.Timeout, "응답 없음");
            socket.ReceiveTimeout = remainingMs;

            int n;
            try
            {
                n = socket.Receive(reply);
            }
            catch (SocketException e)
            {
                throw new NtpException(NtpError.Timeout, e.SocketErrorCode == SocketError.TimedOut ? "응답 없음" : "연결 실패");
            }
            long recvTs = Stopwatch.GetTimestamp();

            if (n < PacketSize || !reply.AsSpan(24, 8).SequenceEqual(nonce))
                continue;

            int leap = reply[0] >> 6;
            int mode = reply[0] & 0x7;
            int stratum = reply[1];
            if (mode != 4)
                throw new NtpException(NtpError.Invalid, $"잘못된 응답(mode {mode})");
            if (stratum == 0)
                throw new NtpException(NtpError.KissOfDeath, "서버 거부(" + Encoding.ASCII.GetString(reply, 12, 4).TrimEnd('\0') + ")");
            if (leap == 3 || stratum > 15)
                throw new NtpException(NtpError.Unsynchronized, "서버 미동기 상태");

            long serverRecv = ReadTimestamp(reply, 32);
            long serverSend = ReadTimestamp(reply, 40);
            if (serverSend < serverRecv)
                throw new NtpException(NtpError.Invalid, "잘못된 타임스탬프");

            return new NtpSample(sendTs, recvTs, serverRecv, serverSend);
        }
    }

    static Socket Open(IPEndPoint server)
    {
        var socket = new Socket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Connect(server);
            return socket;
        }
        catch (SocketException)
        {
            socket.Dispose();
            throw new NtpException(NtpError.Network, "네트워크 연결 안 됨");
        }
    }

    static long ReadTimestamp(byte[] buffer, int offset)
    {
        uint seconds = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
        uint fraction = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + 4));
        // 최상위 비트가 0이면 2036년 이후(NTP era 1)로 해석
        long fullSeconds = seconds >= 0x8000_0000u ? seconds : seconds + 0x1_0000_0000L;
        long fractionTicks = (long)(((ulong)fraction * TimeSpan.TicksPerSecond) >> 32);
        return NtpEpochTicks + fullSeconds * TimeSpan.TicksPerSecond + fractionTicks;
    }
}
