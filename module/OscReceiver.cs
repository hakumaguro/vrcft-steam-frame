using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamFrameVRCFTModule;

/// <summary>Listens for the Steam Link driver's eye OSC stream and keeps the latest values.</summary>
public sealed class OscReceiver : IDisposable
{
    private readonly ILogger _log;
    private readonly UdpClient _udp;
    private readonly Thread _thread;
    private volatile bool _run = true;

    public readonly float[] Gaze = new float[3];     // /sl/eyeTrackedGazePoint
    public volatile float EyesClosed;
    public long LastAnyTicks;                        // last time any OSC message arrived
    public long LastPacketTicks;                     // Environment.TickCount64 of last eye packet
    public long Packets;
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, float[]> Latest = new();

    public OscReceiver(ILogger log, int port, IPAddress? bind = null)
    {
        _log = log;
        _udp = new UdpClient(new IPEndPoint(bind ?? IPAddress.Loopback, port)); // throws if port is taken
        _thread = new Thread(Loop) { IsBackground = true, Name = "SteamFrame-OSC" };
        _thread.Start();
    }

    private void Loop()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_run)
        {
            try
            {
                var data = _udp.Receive(ref ep);
                Parse(data, 0, data.Length);
            }
            catch (SocketException) when (!_run) { }
            catch (ObjectDisposedException) { }
            catch (Exception e) { _log.LogWarning("OSC receive error: {0}", e.Message); }
        }
    }

    private void Parse(byte[] b, int start, int end)
    {
        if (end - start >= 8 && b[start] == (byte)'#')          // "#bundle\0"
        {
            int i = start + 16;                                  // skip header + timetag
            while (i + 4 <= end)
            {
                int n = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
                i += 4;
                if (n <= 0 || i + n > end) return;
                Parse(b, i, i + n);
                i += n;
            }
            return;
        }
        Message(b, start, end);
    }

    private static int ReadStr(byte[] b, int i, int end, out string s)
    {
        int e = i;
        while (e < end && b[e] != 0) e++;
        s = Encoding.ASCII.GetString(b, i, e - i);
        return i + ((e - i + 4) & ~3);
    }

    private void Message(byte[] b, int start, int end)
    {
        int i = ReadStr(b, start, end, out var addr);
        if (i >= end || b[i] != (byte)',') return;
        i = ReadStr(b, i, end, out var tags);
        var vals = new List<float>(3);
        foreach (var t in tags.AsSpan(1))
        {
            if (t != 'T' && t != 'F' && i + 4 > end) break;
            if (t == 'f')
            {
                var tmp = new byte[] { b[i + 3], b[i + 2], b[i + 1], b[i] };
                vals.Add(BitConverter.ToSingle(tmp, 0));
                i += 4;
            }
            else if (t == 'T') vals.Add(1f);
            else if (t == 'F') vals.Add(0f);
            else if (t == 'i') { vals.Add((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]); i += 4; }
            else break;
        }

        Latest[addr] = vals.ToArray();
        LastAnyTicks = Environment.TickCount64;
        switch (addr)
        {
            case "/sl/eyeTrackedGazePoint" when vals.Count >= 3:
                Gaze[0] = vals[0]; Gaze[1] = vals[1]; Gaze[2] = vals[2];
                LastPacketTicks = Environment.TickCount64;
                Interlocked.Increment(ref Packets);
                break;
            case "/tracking/eye/EyesClosedAmount" when vals.Count >= 1:
                EyesClosed = Math.Clamp(vals[0], 0f, 1f);
                break;
        }
    }

    public void Dispose()
    {
        _run = false;
        try { _udp.Close(); } catch { }
    }
}
