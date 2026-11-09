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
    /// <summary>tools/tune.py touches this file every second while it needs samples. The trace runs while it is fresh,
    /// so a tool that is killed cannot leave tracing on (the old way edited the config and could leave it stuck on).</summary>
    private static readonly string TraceHeartbeatPath = Path.Combine(Path.GetTempPath(), "steamframe-trace.on");
    private const long MaxLogBytes = 1_000_000, MaxTraceBytes = 20_000_000;
    private const long TraceHeartbeatMs = 3000;
    /// <summary>A gap in the eye data shorter than this is counted as a dropout instead of logged as "no eye data".</summary>
    private const long ShortGapMs = 5000;
    /// <summary>New gaze values per second in this band, most with both eyes open, mean slowed eye cameras (15/s seen
    /// on SteamOS 0.5.7+). Normal: 45 at SteamOS 0.5.8's default, ~87 at 90; not worn: 0-2, or ~32 of noise.</summary>
    private const double SlowRateMin = 5, SlowRateMax = 25;

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

    private string _source = "", _lids = "none", _loggedSource = "";
    private int _lidsIdentical;
    private long _lastStatus;
    private bool _tracing;
    // data gaps: "none" is logged only once it lasts ShortGapMs; shorter gaps are counted
    private long _noneSince;
    private bool _noneLogged;
    private readonly Queue<long> _dropouts = new();
    private int _dropoutsUnlogged;
    private long _lastDropoutLog;
    // eye-camera rate, from the number of new gaze values per second while Steam Link data flows
    private readonly Queue<(double all, double open)> _rates = new();
    private long _rateChanges, _rateChangesOpen, _rateTicks;
    private double? _eyeRate;
    private bool _eyeRateSlow;
    /// <summary>Last message logged per topic, so a state that comes back after a short gap is not logged again.</summary>
    private readonly Dictionary<string, string> _logged = new();
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
    // learned open level for SteamVR's eyelids, saved next to the config so the next start begins from it
    private readonly OpenLevelLearner _learn = new();
    private string _learnPath = "steamframe-learned.json";
    private (float?, float?) _seenOpen;
    private long _lastGazeChanges = -1, _lastLearnSave;
    private float _loggedOpenL, _loggedOpenR;

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
        _learnPath = Path.Combine(Path.GetDirectoryName(_cfgPath) ?? ".", "steamframe-learned.json");
        ReloadConfig(force: true);
        _seenOpen = (_cfg.SteamVrLid.LeftOpen, _cfg.SteamVrLid.RightOpen);
        if (_learn.Load(_learnPath)) FileLog(FormattableString.Invariant($"open eyelid level from the last wear: L {_learn.OpenL:F2} R {_learn.OpenR:F2}"));
        else SeedFromConfig("no learned level yet");
        if (_learn.OpenL == null)
            FileLog("no learned or calibrated open eyelid level yet: the first ~25 s of wear use the old self-adjusting levels, then the learned one");

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

    private void SeedFromConfig(string why)
    {
        if (_cfg.SteamVrLid.LeftOpen is not float l || _cfg.SteamVrLid.RightOpen is not float r) return;
        if (l < OpenLevelLearner.MinLevel || r < OpenLevelLearner.MinLevel) return;
        _learn.Seed(l, r);
        FileLog(FormattableString.Invariant($"open eyelid level seeded from the config ({why}): L {l:F2} R {r:F2}"));
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
            // a calibration wrote new open levels: start learning from them
            var open = (next.SteamVrLid.LeftOpen, next.SteamVrLid.RightOpen);
            if (!force && open != _seenOpen) { _seenOpen = open; SeedFromConfig("new calibrated levels"); }
            _cfgStamp = File.Exists(_cfgPath) ? File.GetLastWriteTimeUtc(_cfgPath) : DateTime.MinValue;
            FileLog("config loaded");
        }
        catch { }
    }

    public override void Update()
    {
        Thread.Sleep(10);
        long t0 = Environment.TickCount64;
        if (t0 - _lastCfgCheck > 1000) { _lastCfgCheck = t0; ReloadConfig(); _tracing = _cfg.Trace || TraceRequested(); }
        WriteStatus(t0);
        if (Status != ModuleState.Active || (_osc == null && _frame == null)) return;

        if (_frame != null && t0 - _frame.LastAnyTicks < 500 && FrameActive(out var f))
        {
            SetSource("frameeyeosc", t0);
            SetLids("frameeyeosc");
            UpdateFromFrame(f, t0);
            return;
        }
        bool steamLinkFresh = _osc != null && _osc.LastPacketTicks != 0 && t0 - _osc.LastPacketTicks < 1000;
        SetSource(steamLinkFresh ? "steamlink" : "none", t0);
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

        if (_tracing && nowMs - _lastTrace >= 30)   // off by default; tools/tune.py switches it on while it needs samples
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
            // Learn the open level from new camera frames only: SteamVR repeats the last value in between, and a headset
            // that is not worn sends the same values over and over.
            bool learn = c.SteamVrLid.LearnOpen ?? true;
            long changes = Interlocked.Read(ref osc.GazeChanges);
            if (learn && changes != _lastGazeChanges && _lastGazeChanges >= 0)
            {
                _learn.TauMs = c.SteamVrLid.LearnMinutes * 60_000.0;
                _learn.Add(ol, or, nowMs);
            }
            _lastGazeChanges = changes;
            // No glitch rule here: on this scale an open eye reads exactly 1.0 during a normal wink.
            (mol, mor) = _lidsSteam.Process(ol, or, c, c.SteamVrLid, maxFloor: 0f, glitchRule: false, nowMs,
                learn ? _learn.OpenL : null, learn ? _learn.OpenR : null);
            // The length of the gaze point is no sign of closed eyes here: it is the distance the eyes converge at, and it
            // is below CollapsedGazeMag for most of the time while looking at something near.
            holdGaze = _lidsSteam.LinearL < 0.5f && _lidsSteam.LinearR < 0.5f;   // before the squint/sleepy curve
            NoteLidsIdentical(osc.LidsIdentical);
            if (_tracing && nowMs - _lastTrace >= 30)
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

    /// <summary>True while tools/tune.py keeps the heartbeat file fresh.</summary>
    private static bool TraceRequested()
    {
        try
        {
            var fi = new FileInfo(TraceHeartbeatPath);
            return fi.Exists && (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalMilliseconds < TraceHeartbeatMs;
        }
        catch { return false; }
    }

    /// <summary>Log a message for a topic unless it is the same as the last one logged for that topic.</summary>
    private void LogOnce(string topic, string msg)
    {
        if (_logged.TryGetValue(topic, out var last) && last == msg) return;
        _logged[topic] = msg;
        FileLog(msg);
    }

    private void SetSource(string s, long now)
    {
        if (s == _source) return;
        string prev = _source;
        _source = s;
        if (s == "none")
        {
            // logged by NoteGap only if it lasts: Steam Link over Wi-Fi often drops out for a second or two
            _noneSince = now; _noneLogged = false;
            return;
        }
        _neutralSent = false;
        if (prev == "none" && !_noneLogged && _loggedSource != "") { _dropouts.Enqueue(now); _dropoutsUnlogged++; }
        if (s == _loggedSource) return;
        _loggedSource = s;
        FileLog(s == "frameeyeosc" ? "eye data: frameeyeosc (per-eye gaze and eyelids)" : "eye data: Steam Link (no frameeyeosc data)");
    }

    /// <summary>Once a second: log a gap that has lasted, and summarise short dropouts once a minute.</summary>
    private void NoteGap(long now)
    {
        if (_source == "none" && !_noneLogged && now - _noneSince >= ShortGapMs)
        {
            _noneLogged = true;
            if (_loggedSource != "none")
            {
                _loggedSource = "none";
                FileLog("eye data: none (headset off, or nothing is sending)");
                _logged.Remove("lids"); _logged.Remove("dominant");   // log them again when the data comes back
            }
        }
        while (_dropouts.Count > 0 && now - _dropouts.Peek() > 60_000) _dropouts.Dequeue();
        if (_dropoutsUnlogged > 0 && now - _lastDropoutLog >= 60_000)
        {
            _lastDropoutLog = now;
            FileLog($"eye data dropped out {_dropoutsUnlogged} time(s) for under {ShortGapMs / 1000} s since the last note");
            _dropoutsUnlogged = 0;
        }
    }

    /// <summary>Once a second: new gaze values per second over the last 10 s of Steam Link data.</summary>
    private void NoteEyeRate(long now)
    {
        if (_osc == null || _source != "steamlink")
        {
            _rates.Clear(); _rateTicks = 0; _eyeRate = null;
            return;
        }
        long all = Interlocked.Read(ref _osc.GazeChanges), open = Interlocked.Read(ref _osc.GazeChangesOpen);
        if (_rateTicks != 0 && now > _rateTicks)
        {
            double secs = (now - _rateTicks) / 1000.0;
            _rates.Enqueue(((all - _rateChanges) / secs, (open - _rateChangesOpen) / secs));
            while (_rates.Count > 10) _rates.Dequeue();
        }
        _rateChanges = all; _rateChangesOpen = open; _rateTicks = now;
        if (_rates.Count < 10) return;
        var sorted = _rates.Select(r => r.all).OrderBy(r => r).ToArray();
        _eyeRate = Math.Round((sorted[4] + sorted[5]) / 2, 1);   // median of the last 10 seconds
        // Judged per second, with hysteresis: an average would pass through the slow band whenever the rate changes
        // (putting the headset on or off), so a single changing second must not count. A second counts as slow only
        // while most of its new values have both eyes open: a headset that is not worn sends changing noise with an
        // eye "closed" most of the time (2026-11-09: 32 a second, a third with both eyes open).
        int inBand = _rates.Count(r => r.all >= SlowRateMin && r.all < SlowRateMax && r.open >= 0.6 * r.all);
        bool slow = _eyeRateSlow ? inBand > 2 : inBand >= 8;
        if (slow == _eyeRateSlow) return;
        _eyeRateSlow = slow;
        FileLog(slow
            ? $"eye data is slow: {_eyeRate} new values a second (normal is 40-90); SteamVR may have slowed the eye cameras (eyeTrackingRateMin)"
            : $"eye data is no longer slow (now {_eyeRate} new values a second)");
    }

    /// <summary>Where the eyelids come from: frameeyeosc, steamvr (per eye, sent by SteamVR), guessed, none.</summary>
    private void SetLids(string s)
    {
        if (s == _lids) return;
        _lids = s;
        if (s == "steamvr") _lidsSteam.Reset(); else if (s == "frameeyeosc") _lidsFrame.Reset();
        if (s != "steamvr") _lidsIdentical = 0;
        if (s == "steamvr") LogOnce("lids", "eyelids: per eye, from SteamVR");
        else if (s == "guessed") LogOnce("lids", "eyelids: guessed from the gaze (SteamVR sends no eyelid values; that needs SteamVR 2.18 and SteamOS 0.4.3)");
    }

    private void NoteLidsIdentical(int state)
    {
        if (state == _lidsIdentical) return;
        _lidsIdentical = state;
        if (state == 1) LogOnce("dominant", "both eyelids carry the same value: \"Track Dominant Eye Only\" seems to be on in the headset, so winks are not possible");
        else if (state == 2) LogOnce("dominant", "the eyelids differ per eye (\"Track Dominant Eye Only\" is off)");
    }

    /// <summary>Small JSON snapshot (once a second) so the doctor can tell whether data is flowing without a trace.</summary>
    private void WriteStatus(long now, bool force = false)
    {
        if (!force && now - _lastStatus < 1000) return;
        _lastStatus = now;
        NoteGap(now);
        NoteEyeRate(now);
        if (_learn.Dirty && now - _lastLearnSave >= 60_000) SaveLearned(now);
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
            ["eyeRate"] = _eyeRate,
            ["eyeRateSlow"] = _eyeRateSlow && _eyeRate != null,
            ["dropoutsLastMin"] = _dropouts.Count,
            ["openFrom"] = _lids != "steamvr" ? null
                : (_cfg.SteamVrLid.LearnOpen ?? true) && _learn.OpenL != null ? "learned"
                : _cfg.SteamVrLid.LeftOpen != null ? "calibrated" : "adaptive",
            ["learnedOpen"] = _learn.OpenL is float lo && _learn.OpenR is float ro ? new[] { MathF.Round(lo, 3), MathF.Round(ro, 3) } : null,
            ["trace"] = _tracing,
            ["config"] = _cfgPath,
            ["startError"] = _startError,
            ["configError"] = _configError,
        };
        try { File.WriteAllText(StatusPath, System.Text.Json.JsonSerializer.Serialize(status)); } catch { }
    }

    private void SaveLearned(long now)
    {
        _lastLearnSave = now;
        _learn.Save(_learnPath);
        if (_learn.OpenL is float l && _learn.OpenR is float r && (MathF.Abs(l - _loggedOpenL) >= 0.02f || MathF.Abs(r - _loggedOpenR) >= 0.02f))
        {
            _loggedOpenL = l; _loggedOpenR = r;
            FileLog(FormattableString.Invariant($"learned open eyelid level: L {l:F2} R {r:F2}"));
        }
    }

    public override void Teardown()
    {
        if (_learn.Dirty) SaveLearned(Environment.TickCount64);
        _osc?.Dispose(); _frame?.Dispose();
    }
}
