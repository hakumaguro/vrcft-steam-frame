using Microsoft.Extensions.Logging;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;

namespace SteamFrameVRCFTModule;

/// <summary>
/// Eye tracking for Steam Frame. Two sources, best first:
///  1. frameeyeosc running on the headset (real per-eye gaze + eyelid openness) -> UDP 9020
///  2. the Steam Link driver's OSC output (SteamVR OSC Output Port 9015, share eye tracking on) -> gaze only,
///     with a heuristic blink (the driver's eyelid value is always 0 and both eyes are mirrored).
/// </summary>
public class SteamFrameVRCFTModule : ExtTrackingModule
{
    private const int SteamLinkPort = 9015;
    private const int FramePort = 9020;
    private const float GazeUnit = MathF.PI / 4f;   // frameeyeosc: +-1 == +-45 degrees

    // Steam Link fallback blink heuristic
    private const float OpenBelowY = 0.90f, ClosedAboveY = 1.00f, CollapsedGazeMag = 1.5f;

    private static readonly string TracePath = Path.Combine(Path.GetTempPath(), "steamframe-trace.csv");
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "steamframe-module.log");
    private static void FileLog(string m) { try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {m}{Environment.NewLine}"); } catch { } }

    private OscReceiver? _osc, _frame;
    private bool _usingFrame;
    private long _lastLog, _lastTrace, _lastCfgCheck, _lastLidTick;
    private float _closed, _gx, _gy;

    private string _cfgPath = "steamframe-config.json";
    private DateTime _cfgStamp;
    private ModuleConfig _cfg = new();

    private readonly LidCal _calL = new(), _calR = new();
    private float _holdL = 1f, _holdR = 1f;
    private long _holdUntilL, _holdUntilR, _asymSince, _asymLastSeen;

    /// <summary>Per-eye lid calibration. Each eye reports its own "closed" and "open" raw level, so each one tracks
    /// a smoothed floor/ceiling (or uses a fixed calibration from the tuning tool).</summary>
    private sealed class LidCal
    {
        public float Sm = 0.7f, Lo = 0.10f, Hi = 0.85f;

        public float Map(float raw, ModuleConfig.LidConfig c, float? fixedClosed, float? fixedOpen)
        {
            if (fixedClosed is float fc && fixedOpen is float fo && fo - fc > 0.05f)
            {
                Lo = fc; Hi = fo;
            }
            else
            {
                Sm += (raw - Sm) * c.Smoothing;
                if (Sm < Lo) Lo = Sm; else Lo += (Sm - Lo) * c.Tau;
                if (Sm > Hi) Hi = Sm; else Hi += (Sm - Hi) * c.Tau;
                Lo = Math.Clamp(Lo, 0f, c.MaxFloor);
                Hi = Math.Clamp(Hi, MathF.Max(c.MinCeil, Lo + c.MinRange), 1f);
            }
            float n = (raw - Lo) / (Hi - Lo);
            return Math.Clamp((n - c.Deadband) / (1f - 2f * c.Deadband), 0f, 1f);
        }
    }

    /// <summary>The tracker couples the eyes (closing one drags the other to ~0.5). When the lids differ by more than the
    /// threshold, push the lower one toward closed and the higher one toward open.</summary>
    private static void Sharpen(ref float a, ref float b, ModuleConfig.WinkConfig w)
    {
        if (w.Strength <= 0f) return;
        float d = a - b, ad = MathF.Abs(d);
        if (ad <= w.Threshold) return;
        float k = Math.Clamp((ad - w.Threshold) / MathF.Max(w.Range, 0.01f), 0f, 1f) * w.Strength;
        if (d > 0) { b *= 1f - k; a += (1f - a) * k; } else { a *= 1f - k; b += (1f - b) * k; }
    }

    /// <summary>Keep a lid at its lowest recent value for HoldMs, then release upward, so fast blinks reach full depth.</summary>
    private static float PeakHold(float v, ref float hold, ref long until, long now, float dt, ModuleConfig.BlinkConfig b)
    {
        if (b.HoldMs <= 0) { hold = v; return v; }
        if (v <= hold) { hold = v; until = now + b.HoldMs; }
        else if (now >= until) hold = MathF.Min(v, hold + b.ReleasePerSec * dt);
        return MathF.Min(v, hold);
    }

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        ModuleInformation.Name = "Steam Frame Eye Tracking";
        FileLog($"Initialize eyeAvailable={eyeAvailable} exprAvailable={expressionAvailable}");
        if (!eyeAvailable)
        {
            Logger.LogInformation("Eye tracking already claimed by another module, staying idle.");
            return (false, false);
        }

        var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
        _cfgPath = Path.Combine(string.IsNullOrEmpty(loc) ? "." : Path.GetDirectoryName(loc) ?? ".", "steamframe-config.json");
        ReloadConfig(force: true);

        try { _osc = new OscReceiver(Logger, SteamLinkPort); }
        catch (Exception e)
        {
            Logger.LogError("Cannot listen on UDP {0}: {1}. Is the SteamLink module still installed?", SteamLinkPort, e.Message);
            return (false, false);
        }
        try { _frame = new OscReceiver(Logger, FramePort, System.Net.IPAddress.Any); FileLog($"listening on 0.0.0.0:{FramePort} (frameeyeosc)"); }
        catch (Exception e) { FileLog($"frameeyeosc port {FramePort} unavailable: {e.Message}"); }
        FileLog($"listening on {SteamLinkPort}; config {_cfgPath}");
        return (true, false);
    }

    private void ReloadConfig(bool force = false)
    {
        try
        {
            var stamp = File.Exists(_cfgPath) ? File.GetLastWriteTimeUtc(_cfgPath) : DateTime.MinValue;
            if (!force && stamp == _cfgStamp) return;
            _cfg = ModuleConfig.Load(_cfgPath);
            _cfgStamp = File.Exists(_cfgPath) ? File.GetLastWriteTimeUtc(_cfgPath) : DateTime.MinValue;
            FileLog("config loaded");
        }
        catch { }
    }

    public override void Update()
    {
        Thread.Sleep(10);
        long t0 = Environment.TickCount64;
        if (t0 - _lastCfgCheck > 1000) { _lastCfgCheck = t0; ReloadConfig(); }
        if (t0 - _lastLog > 2000 && (Status != ModuleState.Active || _osc == null))
        { _lastLog = t0; FileLog($"Update skipped: Status={Status} osc={(_osc != null)}"); }
        if (Status != ModuleState.Active || _osc == null) return;

        if (_frame != null && t0 - _frame.LastAnyTicks < 500 && FrameActive(out var f))
        {
            UpdateFromFrame(f, t0);
            return;
        }
        if (_usingFrame) { _usingFrame = false; FileLog("frameeyeosc data lost, falling back to Steam Link stream"); }
        UpdateFromSteamLink();
    }

    // ---- source 1: frameeyeosc (per-eye) -------------------------------------------------------------

    private void UpdateFromFrame((float lx, float ly, float rx, float ry, float ol, float or) f, long nowMs)
    {
        if (!_usingFrame) { _usingFrame = true; FileLog("using frameeyeosc data (per-eye)"); }
        var c = _cfg;
        if (c.SwapEyes) f = (f.rx, f.ry, f.lx, f.ly, f.or, f.ol);

        float sx = c.Gaze.Scale * GazeUnit * (c.Gaze.InvertX ? -1f : 1f);
        float sy = c.Gaze.Scale * GazeUnit * (c.Gaze.InvertY ? -1f : 1f);
        var e = UnifiedTracking.Data.Eye;
        e.Left.Gaze.x = f.lx * sx;  e.Left.Gaze.y = f.ly * sy;
        e.Right.Gaze.x = f.rx * sx; e.Right.Gaze.y = f.ry * sy;

        float mol = _calL.Map(f.ol, c.Lid, c.Lid.LeftClosed, c.Lid.LeftOpen);
        float mor = _calR.Map(f.or, c.Lid, c.Lid.RightClosed, c.Lid.RightOpen);
        // Blink vs wink: a lopsided closure that has not lasted CoupleMs is a blink -> close both eyes.
        bool coupled = false;
        bool asym = c.Blink.CoupleMs > 0 && MathF.Min(mol, mor) < c.Blink.AsymClosed && MathF.Max(mol, mor) > c.Blink.AsymOpen;
        if (asym)
        {
            _asymLastSeen = nowMs;
            if (_asymSince == 0) _asymSince = nowMs;
            if (nowMs - _asymSince < c.Blink.CoupleMs) { mol = mor = MathF.Min(mol, mor); coupled = true; }
        }
        else if (nowMs - _asymLastSeen > 60) _asymSince = 0;   // small gaps do not restart the timer
        if (!coupled) Sharpen(ref mol, ref mor, c.Wink);
        float dt = _lastLidTick == 0 ? 0.01f : Math.Min((nowMs - _lastLidTick) / 1000f, 0.1f);
        _lastLidTick = nowMs;
        mol = PeakHold(mol, ref _holdL, ref _holdUntilL, nowMs, dt, c.Blink);
        mor = PeakHold(mor, ref _holdR, ref _holdUntilR, nowMs, dt, c.Blink);
        e.Left.Openness = mol; e.Right.Openness = mor;

        e.Left.PupilDiameter_MM = 5f; e.Right.PupilDiameter_MM = 5f;   // unsupported, but must be set
        e._maxDilation = 10; e._minDilation = 0;

        if (nowMs - _lastTrace >= 30)
        {
            _lastTrace = nowMs;
            try { File.AppendAllText(TracePath, $"{DateTime.Now:HH:mm:ss.fff},FRAME,{f.lx:F3},{f.ly:F3},{f.rx:F3},{f.ry:F3},{f.ol:F3},{f.or:F3},{mol:F3},{mor:F3},{_calL.Lo:F2},{_calL.Hi:F2},{_calR.Lo:F2},{_calR.Hi:F2}{Environment.NewLine}"); } catch { }
        }
    }

    private bool FrameActive(out (float lx, float ly, float rx, float ry, float ol, float or) f)
    {
        float G(string n, float d = 0f) => _frame!.Latest.TryGetValue("/avatar/parameters/FT/v2/" + n, out var v) && v.Length > 0 ? v[0] : d;
        bool active = !_frame!.Latest.TryGetValue("/avatar/parameters/FT/EyeTrackingActive", out var a) || a.Length == 0 || a[0] > 0.5f;
        f = (G("EyeLeftX"), G("EyeLeftY"), G("EyeRightX"), G("EyeRightY"), G("EyeLidLeft", 1f), G("EyeLidRight", 1f));
        return active && _frame.Latest.ContainsKey("/avatar/parameters/FT/v2/EyeLeftX");
    }

    // ---- source 2: Steam Link driver OSC (fallback) --------------------------------------------------

    private void UpdateFromSteamLink()
    {
        var osc = _osc!;
        // Blink heuristic: the driver's EyesClosedAmount is always 0 on Steam Frame, but when the eyes close they roll up
        // and the normalised vertical eye value (LeftEyeY/RightEyeY, normally ~0.2-0.85) saturates at ~1.0+.
        float ly = osc.Latest.TryGetValue("/avatar/parameters/LeftEyeY", out var lv) && lv.Length > 0 ? lv[0] : 0.5f;
        float ry = osc.Latest.TryGetValue("/avatar/parameters/RightEyeY", out var rv) && rv.Length > 0 ? rv[0] : ly;
        // Rolling the eyes up while open also pushes Y to ~1.0, but then the gaze vector stays valid (|g| ~25).
        // Closed eyes collapse the gaze vector (|g| ~0.1-0.5), so require both.
        float gmag = MathF.Sqrt(osc.Gaze[0] * osc.Gaze[0] + osc.Gaze[1] * osc.Gaze[1] + osc.Gaze[2] * osc.Gaze[2]);
        float rawClosed = gmag < CollapsedGazeMag
            ? Math.Clamp((MathF.Max(ly, ry) - OpenBelowY) / (ClosedAboveY - OpenBelowY), 0f, 1f)
            : 0f;
        _closed += (rawClosed - _closed) * (rawClosed > _closed ? 0.6f : 0.4f);
        _closed = MathF.Max(_closed, osc.EyesClosed);

        if (_closed < 0.5f)   // hold the last open-eye gaze while blinking
        {
            _gx = MathF.Atan2(osc.Gaze[0], -osc.Gaze[2]);
            _gy = MathF.Atan2(osc.Gaze[1], -osc.Gaze[2]);
            if (float.IsNaN(_gx)) _gx = 0; if (float.IsNaN(_gy)) _gy = 0;
        }

        var eye = UnifiedTracking.Data.Eye;
        eye.Left.Gaze.x = _gx;  eye.Left.Gaze.y = _gy;
        eye.Right.Gaze.x = _gx; eye.Right.Gaze.y = _gy;
        float open = 1f - _closed;
        eye.Left.Openness = open; eye.Right.Openness = open;
        eye.Left.PupilDiameter_MM = 5f; eye.Right.PupilDiameter_MM = 5f;
        eye._maxDilation = 10; eye._minDilation = 0;

        long now = Environment.TickCount64;
        if (now - _lastLog > 5000)
        {
            _lastLog = now;
            FileLog($"steamlink: packets={osc.Packets} gaze=({_gx:F3},{_gy:F3}) closed={_closed:F2}");
        }
    }

    public override void Teardown() { _osc?.Dispose(); _frame?.Dispose(); }
}
