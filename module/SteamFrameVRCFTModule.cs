using Microsoft.Extensions.Logging;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;

namespace SteamFrameVRCFTModule;

/// <summary>
/// Eye tracking for Steam Frame. Two sources, best first:
///  1. frameeyeosc running on the headset (real per-eye gaze + eyelid openness) -> UDP 9020
///  2. the Steam Link driver's OSC output (SteamVR OSC Output Port 9015, share eye tracking on) -> one gaze direction
///     for both eyes, plus per-eye eyelids when SteamVR sends them (SteamVR 2.18.2+ with SteamOS 0.4.3+). Older
///     versions send no eyelid value, so the blink is guessed there.
/// </summary>
public class SteamFrameVRCFTModule : ExtTrackingModule
{
    private const int SteamLinkPort = 9015;
    private const int FramePort = 9020;
    private const float GazeUnit = MathF.PI / 4f;   // frameeyeosc: +-1 == +-45 degrees

    private const int SteamLidFreshMs = 1000;       // per-eye lids from SteamVR older than this are not used (same limit as the gaze)
    // Steam Link blink heuristic, for SteamVR versions that send no eyelid values
    private const float OpenBelowY = 0.90f, ClosedAboveY = 1.00f, CollapsedGazeMag = 1.5f;

    private static readonly string TracePath = Path.Combine(Path.GetTempPath(), "steamframe-trace.csv");
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "steamframe-module.log");
    private static readonly string StatusPath = Path.Combine(Path.GetTempPath(), "steamframe-status.json");
    private const long MaxLogBytes = 1_000_000, MaxTraceBytes = 20_000_000;

    /// <summary>Append to a file, moving it to *.old once it passes maxBytes so it never grows without limit.</summary>
    private static void AppendCapped(string path, string text, long maxBytes)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > maxBytes) File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, text);
        }
        catch { }
    }

    private static void FileLog(string m) => AppendCapped(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {m}{Environment.NewLine}", MaxLogBytes);

    private string _source = "", _lids = "none";
    private int _lidsIdentical;
    private long _lastStatus;
    private string _startError = "";
    private string _configError = "";
    private bool _neutralSent;

    private OscReceiver? _osc, _frame;
    private long _lastTrace, _lastCfgCheck;
    private float _closed, _gx, _gy;

    private string _cfgPath = "steamframe-config.json";
    private DateTime _cfgStamp;
    private ModuleConfig _cfg = new();

    private readonly LidPipeline _lidsFrame = new(), _lidsSteam = new();

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        ModuleInformation.Name = "Steam Frame Eye Tracking";
        FileLog($"Initialize eyeAvailable={eyeAvailable} exprAvailable={expressionAvailable}");
        if (!eyeAvailable)
        {
            _startError = "Eye tracking is already claimed by another VRCFT module (for example a webcam module); disable it in VRCFT. ";
            WriteStatus(Environment.TickCount64, force: true);
            Logger.LogInformation("Eye tracking already claimed by another module, staying idle.");
            return (false, false);
        }

        var loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
        _cfgPath = Path.Combine(string.IsNullOrEmpty(loc) ? "." : Path.GetDirectoryName(loc) ?? ".", "steamframe-config.json");
        ReloadConfig(force: true);

        // Each source is optional: the module works as long as at least one port can be opened.
        try { _frame = new OscReceiver(Logger, FramePort, System.Net.IPAddress.Any); FileLog($"listening on 0.0.0.0:{FramePort} (frameeyeosc)"); }
        catch (Exception e)
        {
            _startError += $"UDP {FramePort} (frameeyeosc) is in use: {e.Message}. ";
            Logger.LogWarning("Cannot listen on UDP {0} for frameeyeosc: {1}", FramePort, e.Message);
        }
        try { _osc = new OscReceiver(Logger, SteamLinkPort); FileLog($"listening on 127.0.0.1:{SteamLinkPort} (Steam Link)"); }
        catch (Exception e)
        {
            _startError += $"UDP {SteamLinkPort} (Steam Link) is in use, probably by the stock SteamLink VRCFT module; uninstall it. ";
            Logger.LogWarning("Cannot listen on UDP {0}: {1}. Is the stock SteamLink module still installed? Continuing without the Steam Link fallback.", SteamLinkPort, e.Message);
        }
        FileLog($"config {_cfgPath}");
        WriteStatus(Environment.TickCount64, force: true);
        if (_frame == null && _osc == null)
        {
            Logger.LogError("Steam Frame module: no data source could be opened. {0}", _startError);
            return (false, false);
        }
        return (true, false);
    }

    private void ReloadConfig(bool force = false)
    {
        try
        {
            var stamp = File.Exists(_cfgPath) ? File.GetLastWriteTimeUtc(_cfgPath) : DateTime.MinValue;
            if (!force && stamp == _cfgStamp) return;
            var next = ModuleConfig.TryLoad(_cfgPath, out var error);
            if (next == null)
            {
                // Keep the configuration that was working and say why the new one was not used. The timestamp is not
                // recorded, so the file is read again next second (a temporary lock clears; a fixed file loads).
                var msg = $"steamframe-config.json was not applied ({error}); still using the previous settings";
                if (msg != _configError) { FileLog(msg); Logger.LogWarning("{0}", msg); }
                _configError = msg;
                return;
            }
            _cfg = next;
            _configError = "";
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
        WriteStatus(t0);
        if (Status != ModuleState.Active || (_osc == null && _frame == null)) return;

        if (_frame != null && t0 - _frame.LastAnyTicks < 500 && FrameActive(out var f))
        {
            SetSource("frameeyeosc");
            SetLids("frameeyeosc");
            UpdateFromFrame(f, t0);
            return;
        }
        bool steamLinkFresh = _osc != null && _osc.LastPacketTicks != 0 && t0 - _osc.LastPacketTicks < 1000;
        SetSource(steamLinkFresh ? "steamlink" : "none");
        if (steamLinkFresh) { _neutralSent = false; UpdateFromSteamLink(t0); }
        else { SetLids("none"); SetNeutralEyes(); }
    }

    // ---- source 1: frameeyeosc (per-eye) -------------------------------------------------------------

    private void UpdateFromFrame((float lx, float ly, float rx, float ry, float ol, float or) f, long nowMs)
    {
        var c = _cfg;
        if (c.SwapEyes) f = (f.rx, f.ry, f.lx, f.ly, f.or, f.ol);

        float sx = c.Gaze.Scale * GazeUnit * (c.Gaze.InvertX ? -1f : 1f);
        float sy = c.Gaze.Scale * GazeUnit * (c.Gaze.InvertY ? -1f : 1f);
        var e = UnifiedTracking.Data.Eye;
        e.Left.Gaze.x = f.lx * sx;  e.Left.Gaze.y = f.ly * sy;
        e.Right.Gaze.x = f.rx * sx; e.Right.Gaze.y = f.ry * sy;

        var (mol, mor) = _lidsFrame.Process(f.ol, f.or, c, c.Lid, c.Lid.MaxFloor, glitchRule: true, nowMs);
        e.Left.Openness = mol; e.Right.Openness = mor;

        e.Left.PupilDiameter_MM = 5f; e.Right.PupilDiameter_MM = 5f;   // unsupported, but must be set
        e._maxDilation = 10; e._minDilation = 0;

        if (c.Trace && nowMs - _lastTrace >= 30)   // off by default; tools/tune.py switches it on while it needs samples
        {
            _lastTrace = nowMs;
            // invariant culture: with a decimal comma the CSV would gain extra columns and tune.py would misread it
            AppendCapped(TracePath, FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff},FRAME,{f.lx:F3},{f.ly:F3},{f.rx:F3},{f.ry:F3},{f.ol:F3},{f.or:F3},{mol:F3},{mor:F3},{_lidsFrame.L.Lo:F2},{_lidsFrame.L.Hi:F2},{_lidsFrame.R.Lo:F2},{_lidsFrame.R.Hi:F2}") + Environment.NewLine, MaxTraceBytes);
        }
    }

    private bool FrameActive(out (float lx, float ly, float rx, float ry, float ol, float or) f)
    {
        float G(string n, float d = 0f) => _frame!.Latest.TryGetValue("/avatar/parameters/FT/v2/" + n, out var v) && v.Length > 0 ? v[0] : d;
        bool active = !_frame!.Latest.TryGetValue("/avatar/parameters/FT/EyeTrackingActive", out var a) || a.Length == 0 || a[0] > 0.5f;
        f = (G("EyeLeftX"), G("EyeLeftY"), G("EyeRightX"), G("EyeRightY"), G("EyeLidLeft", 1f), G("EyeLidRight", 1f));
        return active && _frame.Latest.ContainsKey("/avatar/parameters/FT/v2/EyeLeftX");
    }

    // ---- source 2: Steam Link driver OSC ------------------------------------------------------------

    private void UpdateFromSteamLink(long nowMs)
    {
        var osc = _osc!;
        var c = _cfg;
        float gmag = MathF.Sqrt(osc.Gaze[0] * osc.Gaze[0] + osc.Gaze[1] * osc.Gaze[1] + osc.Gaze[2] * osc.Gaze[2]);
        bool perEye = osc.LastLidTicks != 0 && nowMs - osc.LastLidTicks < SteamLidFreshMs;
        SetLids(perEye ? "steamvr" : "guessed");
        float mol, mor;
        bool holdGaze;
        if (perEye)
        {
            // SteamVR sends closedness per eye (0 open .. 1 closed, already 0 when closed), so only the open level adapts.
            float ol = 1f - osc.ClosedL, or = 1f - osc.ClosedR;
            if (c.SwapEyes) (ol, or) = (or, ol);
            // No glitch rule here: on this scale an open eye reads exactly 1.0 during a normal wink.
            (mol, mor) = _lidsSteam.Process(ol, or, c, c.SteamVrLid, maxFloor: 0f, glitchRule: false, nowMs);
            // The length of the gaze point is no sign of closed eyes here: it is the distance the eyes converge at, and it
            // is below CollapsedGazeMag for most of the time while looking at something near.
            holdGaze = mol < 0.5f && mor < 0.5f;
            NoteLidsIdentical(osc.LidsIdentical);
            if (c.Trace && nowMs - _lastTrace >= 30)
            {
                _lastTrace = nowMs;
                float tx = _gx / GazeUnit, ty = _gy / GazeUnit;   // same columns and units as the FRAME rows
                AppendCapped(TracePath, FormattableString.Invariant($"{DateTime.Now:HH:mm:ss.fff},SLINK,{tx:F3},{ty:F3},{tx:F3},{ty:F3},{ol:F3},{or:F3},{mol:F3},{mor:F3},{_lidsSteam.L.Lo:F2},{_lidsSteam.L.Hi:F2},{_lidsSteam.R.Lo:F2},{_lidsSteam.R.Hi:F2}") + Environment.NewLine, MaxTraceBytes);
            }
        }
        else
        {
            // Blink heuristic: without eyelid values, closed eyes show as a rolled-up gaze: the normalised vertical eye
            // value (LeftEyeY/RightEyeY, normally ~0.2-0.85) saturates at ~1.0+.
            float ly = osc.Latest.TryGetValue("/avatar/parameters/LeftEyeY", out var lv) && lv.Length > 0 ? lv[0] : 0.5f;
            float ry = osc.Latest.TryGetValue("/avatar/parameters/RightEyeY", out var rv) && rv.Length > 0 ? rv[0] : ly;
            // Rolling the eyes up while open also pushes Y to ~1.0, but then the gaze vector stays valid (|g| ~25).
            // Closed eyes collapse the gaze vector (|g| ~0.1-0.5), so require both.
            float rawClosed = gmag < CollapsedGazeMag
                ? Math.Clamp((MathF.Max(ly, ry) - OpenBelowY) / (ClosedAboveY - OpenBelowY), 0f, 1f)
                : 0f;
            _closed += (rawClosed - _closed) * (rawClosed > _closed ? 0.6f : 0.4f);
            _closed = MathF.Max(_closed, osc.EyesClosed);
            mol = mor = 1f - _closed;
            holdGaze = _closed >= 0.5f;
        }

        if (!holdGaze)   // keep the last open-eye gaze while blinking
        {
            _gx = MathF.Atan2(osc.Gaze[0], -osc.Gaze[2]);
            _gy = MathF.Atan2(osc.Gaze[1], -osc.Gaze[2]);
            if (float.IsNaN(_gx)) _gx = 0; if (float.IsNaN(_gy)) _gy = 0;
        }

        var eye = UnifiedTracking.Data.Eye;
        eye.Left.Gaze.x = _gx;  eye.Left.Gaze.y = _gy;
        eye.Right.Gaze.x = _gx; eye.Right.Gaze.y = _gy;
        eye.Left.Openness = mol; eye.Right.Openness = mor;
        eye.Left.PupilDiameter_MM = 5f; eye.Right.PupilDiameter_MM = 5f;
        eye._maxDilation = 10; eye._minDilation = 0;
    }

    /// <summary>No fresh data from either source: look straight ahead with open eyes instead of freezing on the last reading
    /// (which could be mid-blink). Sent once per no-data period.</summary>
    private void SetNeutralEyes()
    {
        if (_neutralSent) return;
        _neutralSent = true;
        _closed = 0f; _gx = 0f; _gy = 0f;
        var e = UnifiedTracking.Data.Eye;
        e.Left.Gaze.x = 0f; e.Left.Gaze.y = 0f; e.Right.Gaze.x = 0f; e.Right.Gaze.y = 0f;
        e.Left.Openness = 1f; e.Right.Openness = 1f;
        e.Left.PupilDiameter_MM = 5f; e.Right.PupilDiameter_MM = 5f;
        e._maxDilation = 10; e._minDilation = 0;
    }

    // ---- status for tools/tune.py doctor ---------------------------------------------------------------

    private void SetSource(string s)
    {
        if (s == _source) return;
        _source = s;
        if (s != "none") _neutralSent = false;
        FileLog(s switch
        {
            "frameeyeosc" => "eye data: frameeyeosc (per-eye gaze and eyelids)",
            "steamlink" => "eye data: Steam Link (no frameeyeosc data)",
            _ => "eye data: none (headset off, or nothing is sending)",
        });
    }

    /// <summary>Where the eyelids come from: frameeyeosc, steamvr (per eye, sent by SteamVR), guessed, none.</summary>
    private void SetLids(string s)
    {
        if (s == _lids) return;
        _lids = s;
        if (s == "steamvr") _lidsSteam.Reset(); else if (s == "frameeyeosc") _lidsFrame.Reset();
        if (s != "steamvr") _lidsIdentical = 0;
        if (s == "steamvr") FileLog("eyelids: per eye, from SteamVR");
        else if (s == "guessed") FileLog("eyelids: guessed from the gaze (SteamVR sends no eyelid values; that needs SteamVR 2.18.2 and SteamOS 0.4.3)");
    }

    private void NoteLidsIdentical(int state)
    {
        if (state == _lidsIdentical) return;
        _lidsIdentical = state;
        if (state == 1) FileLog("both eyelids carry the same value: \"Track Dominant Eye Only\" seems to be on in the headset, so winks are not possible");
        else if (state == 2) FileLog("the eyelids differ per eye (\"Track Dominant Eye Only\" is off)");
    }

    /// <summary>Small JSON snapshot (once a second) so the doctor can tell whether data is flowing without a trace.</summary>
    private void WriteStatus(long now, bool force = false)
    {
        if (!force && now - _lastStatus < 1000) return;
        _lastStatus = now;
        static long Age(long now, long t) => t == 0 ? -1 : now - t;
        var status = new Dictionary<string, object?>
        {
            ["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["version"] = typeof(SteamFrameVRCFTModule).Assembly.GetName().Version?.ToString(),
            ["source"] = _source == "" ? "starting" : _source,
            ["frameListening"] = _frame != null,
            ["frameAgeMs"] = _frame == null ? -1 : Age(now, _frame.LastAnyTicks),
            ["steamLinkListening"] = _osc != null,
            ["steamLinkAgeMs"] = _osc == null ? -1 : Age(now, _osc.LastPacketTicks),
            ["lids"] = _lids,
            ["dominantEyeOnly"] = _lids != "steamvr" || _lidsIdentical == 0 ? null : _lidsIdentical == 1,
            ["trace"] = _cfg.Trace,
            ["config"] = _cfgPath,
            ["startError"] = _startError,
            ["configError"] = _configError,
        };
        try { File.WriteAllText(StatusPath, System.Text.Json.JsonSerializer.Serialize(status)); } catch { }
    }

    public override void Teardown() { _osc?.Dispose(); _frame?.Dispose(); }
}
