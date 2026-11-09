namespace SteamFrameVRCFTModule;

/// <summary>Turns raw per-eye lid openness (0 closed .. 1 open, tracker units) into the openness shown on the avatar:
/// per-eye calibration -> optional One Euro filter -> blink glitch rule -> lopsided-closure timer -> wink assist ->
/// wink sharpening (optionally only while one eye is nearly closed) -> peak hold -> squint/sleepy output curve.
/// One instance per data source, so each keeps its own calibration and timers. No VRCFT types, so recorded sessions can
/// be replayed through it offline.</summary>
public sealed class LidPipeline
{
    /// <summary>Per-eye lid calibration. Each eye reports its own "closed" and "open" raw level, so each one tracks
    /// a smoothed floor/ceiling (or uses a fixed calibration from the tuning tool).</summary>
    public sealed class LidCal
    {
        public float Sm = 0.7f, Lo = 0.10f, Hi = 0.85f;
        /// <summary>The last <see cref="Map"/> used fixed levels (only then do the squint/sleepy levels apply).</summary>
        public bool Fixed;
        private float _fx, _fdx;
        private bool _fInit;

        public float Map(float raw, ModuleConfig.LidConfig c, float? fixedClosed, float? fixedOpen, float maxFloor)
        {
            Fixed = fixedClosed is float fc && fixedOpen is float fo && fo - fc > 0.05f;
            if (Fixed)
            {
                Lo = fixedClosed!.Value; Hi = fixedOpen!.Value;
            }
            else
            {
                Sm += (raw - Sm) * c.Smoothing;
                if (Sm < Lo) Lo = Sm; else Lo += (Sm - Lo) * c.Tau;
                if (Sm > Hi) Hi = Sm; else Hi += (Sm - Hi) * c.Tau;
                Lo = Math.Clamp(Lo, 0f, maxFloor);
                Hi = Math.Clamp(Hi, MathF.Min(1f, MathF.Max(c.MinCeil, Lo + c.MinRange)), 1f);   // lower bound never above 1
            }
            return Linear(raw, c.Deadband);
        }

        private float Linear(float raw, float deadband)
        {
            float n = (raw - Lo) / (Hi - Lo);
            return Math.Clamp((n - deadband) / (1f - 2f * deadband), 0f, 1f);
        }

        /// <summary>One Euro filter (Casiez et al. 2012): the cutoff rises with the lid's speed, so a held lid is steady
        /// and a blink passes with little lag.</summary>
        public float Smooth(float v, float dt, ModuleConfig.LidLevels lv)
        {
            if (!_fInit || dt <= 0f) { _fx = v; _fdx = 0f; _fInit = true; return v; }
            static float Alpha(float cutoff, float dt) => 1f / (1f + 1f / (2f * MathF.PI * cutoff * dt));
            _fdx += ((v - _fx) / dt - _fdx) * Alpha(1f, dt);
            _fx += (v - _fx) * Alpha(lv.FilterMinCutoff + lv.FilterBeta * MathF.Abs(_fdx), dt);
            return _fx;
        }

        public void ResetFilter() => _fInit = false;

        /// <summary>Bend a linear level through the calibrated squint and sleepy points: each point's raw level is mapped
        /// with the same linear formula and becomes a knot at its target. Knots that are not strictly inside (0, 1) and in
        /// order are skipped, so without them this returns <paramref name="v"/> unchanged.</summary>
        public float Curve(float v, float? squintRaw, float? sleepyRaw, ModuleConfig.LidLevels lv, float deadband)
        {
            // raw points need fixed levels; sleepyLevel is already on the linear scale (shared by both eyes) and wins
            float? xs = Fixed && squintRaw is float rs ? Linear(rs, deadband) : null;
            float? xz = lv.SleepyLevel ?? (Fixed && sleepyRaw is float rz ? Linear(rz, deadband) : null);
            if (xs == null && xz == null) return v;
            float x0 = 0f, y0 = 0f;
            foreach (var (knot, target) in new[] { (xs, lv.TargetSquint), (xz, lv.TargetSleepy) })
            {
                if (knot is not float x) continue;
                if (x <= x0 || x >= 1f || target <= y0 || target >= 1f) continue;
                if (v <= x) return y0 + (v - x0) * (target - y0) / (x - x0);
                x0 = x; y0 = target;
            }
            return y0 + (v - x0) * (1f - y0) / (1f - x0);
        }
    }

    public readonly LidCal L = new(), R = new();
    /// <summary>The last output before the squint/sleepy curve (the scale the gaze-hold rule was tuned on).</summary>
    public float LinearL = 1f, LinearR = 1f;
    private float _holdL = 1f, _holdR = 1f;
    private long _holdUntilL, _holdUntilR, _asymSince, _asymLastSeen, _glitchSince, _glitchLastSeen, _lastTick;
    private bool _asymLeft;   // which eye is the closed one in the current lopsided closure

    /// <summary>Wink assist state: 0 none, 1 right eye closed (left should be open), 2 left eye closed (right should be open).</summary>
    private int _assist;
    private long _assistSince, _assistLast = long.MinValue / 2;

    /// <summary>Forget timers and holds (the calibration envelopes stay), e.g. after the data source was away.</summary>
    public void Reset()
    {
        _holdL = _holdR = 1f;
        _holdUntilL = _holdUntilR = _asymSince = _asymLastSeen = _glitchSince = _glitchLastSeen = _lastTick = 0;
        _assist = 0; _assistSince = 0; _assistLast = long.MinValue / 2;
        L.ResetFilter(); R.ResetFilter();
        LinearL = LinearR = 1f;
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

    /// <returns>true when the assist is holding an eye open (so wink sharpening is skipped).</returns>
    private bool ApplyWinkAssist(ref float mol, ref float mor, long now, ModuleConfig.WinkConfig w)
    {
        int which = 0;
        if (mor < w.AssistClosed && mol > w.AssistMin) which = 1;
        else if (mol < w.AssistClosed && mor > w.AssistMin) which = 2;

        if (which != 0)
        {
            _assistLast = now;
            if (_assistSince == 0) _assistSince = now;
            if (now - _assistSince >= w.AssistPersistMs) _assist = which;
        }
        else if (now - _assistLast > w.AssistReleaseMs)
        {
            _assistSince = 0; _assist = 0;
        }

        if (_assist == 0 || (which != 0 && which != _assist)) return false;
        if (_assist == 1) mol = MathF.Max(mol, w.AssistOpen); else mor = MathF.Max(mor, w.AssistOpen);
        return true;
    }

    private static (float? closed, float? open) Levels(float? closed, float? open, float? learned, float maxFloor)
    {
        if (learned is not float lo) return (closed, open);
        float? c = closed ?? (maxFloor == 0f ? 0f : null);
        return c is float fc && lo - fc > 0.05f ? (fc, lo) : (closed, open);
    }

    /// <param name="levels">Fixed closed/open raw levels per eye; an eye without both adapts.</param>
    /// <param name="maxFloor">Upper limit of the adaptive closed level (0 when the source already reports closed as 0).</param>
    /// <param name="glitchRule">Apply <see cref="ModuleConfig.BlinkConfig.SaturatedRaw"/> (only true for sources where an open
    /// eye does not normally read 1.0).</param>
    /// <param name="learnedOpenL">Learned open level (<see cref="OpenLevelLearner"/>); replaces the configured one. The
    /// closed level is then the configured one, or 0 on a source that reads closed as 0 (maxFloor 0).</param>
    public (float L, float R) Process(float rawL, float rawR, ModuleConfig c, ModuleConfig.LidLevels levels, float maxFloor, bool glitchRule, long nowMs,
        float? learnedOpenL = null, float? learnedOpenR = null)
    {
        (float? cl, float? ol) = Levels(levels.LeftClosed, levels.LeftOpen, learnedOpenL, maxFloor);
        (float? cr, float? or) = Levels(levels.RightClosed, levels.RightOpen, learnedOpenR, maxFloor);
        float mol = L.Map(rawL, c.Lid, cl, ol, maxFloor);
        float mor = R.Map(rawR, c.Lid, cr, or, maxFloor);
        float dt = _lastTick == 0 ? 0.01f : Math.Min((nowMs - _lastTick) / 1000f, 0.1f);
        _lastTick = nowMs;
        if (levels.Filter) { mol = L.Smooth(mol, dt, levels); mor = R.Smooth(mor, dt, levels); }
        // Blink vs wink: a lopsided closure that has not lasted CoupleMs is a blink -> close both eyes.
        bool coupled = false;
        bool asym = c.Blink.CoupleMs > 0 && MathF.Min(mol, mor) < c.Blink.AsymClosed && MathF.Max(mol, mor) > c.Blink.AsymOpen;
        // Glitch signature: one lid closed while the other is pinned at its raw ceiling -> a blink, however long it lasts.
        bool glitch = glitchRule && c.Blink.SaturatedRaw > 0f
            && ((rawR >= c.Blink.SaturatedRaw && mol < c.Blink.AsymClosed) || (rawL >= c.Blink.SaturatedRaw && mor < c.Blink.AsymClosed));
        if (glitch)
        {
            _glitchLastSeen = nowMs;
            if (_glitchSince == 0) _glitchSince = nowMs;
        }
        else if (nowMs - _glitchLastSeen > 20) _glitchSince = 0;
        if (_glitchSince != 0 && nowMs - _glitchSince >= c.Blink.GlitchMinMs)
        {
            mol = mor = MathF.Min(mol, mor); coupled = true;
        }
        else if (asym)
        {
            _asymLastSeen = nowMs;
            if (_asymSince == 0 || (mol < mor) != _asymLeft) { _asymSince = nowMs; _asymLeft = mol < mor; }
            if (nowMs - _asymSince < c.Blink.CoupleMs) { mol = mor = MathF.Min(mol, mor); coupled = true; }
        }
        // The same eye still closed while the other one hovers between the two levels (a squinting open eye during a wink)
        // is the same closure going on: restarting the timer there would close both eyes again in the middle of a wink.
        // With asymHysteresis the closed eye may also rise a little above asymClosed (a one-eye squint hovering at the
        // threshold), which would otherwise restart the timer and pull the open eye down again and again.
        else if (_asymSince != 0 && (_asymLeft ? mol : mor) < c.Blink.AsymClosed + levels.AsymHysteresis && (_asymLeft ? mor : mol) >= c.Blink.AsymClosed)
            _asymLastSeen = nowMs;
        else if (nowMs - _asymLastSeen > 60) _asymSince = 0;   // small gaps do not restart the timer
        bool lifted = false;
        if (coupled) { _assist = 0; _assistSince = 0; }
        else if (c.Wink.Assist) lifted = ApplyWinkAssist(ref mol, ref mor, nowMs, c.Wink);
        if (!coupled && !lifted && (levels.SharpenBelow is not float sb || MathF.Min(mol, mor) < sb)) Sharpen(ref mol, ref mor, c.Wink);
        mol = PeakHold(mol, ref _holdL, ref _holdUntilL, nowMs, dt, c.Blink);
        mor = PeakHold(mor, ref _holdR, ref _holdUntilR, nowMs, dt, c.Blink);
        // last, so every rule above keeps working on the linear scale it was tuned on
        LinearL = mol; LinearR = mor;
        return (L.Curve(mol, levels.LeftSquint, levels.LeftSleepy, levels, c.Lid.Deadband),
                R.Curve(mor, levels.RightSquint, levels.RightSleepy, levels, c.Lid.Deadband));
    }
}
