using System.Text.Json;

namespace SteamFrameVRCFTModule;

/// <summary>Learns each eye's relaxed open level (raw tracker units) while the headset is worn, so no calibration is
/// needed: the 40th percentile of the recent open-eye samples, the last <see cref="TauMs"/> counting most. Calibrated
/// levels go stale between days (2026-10-09 L 0.90 / R 0.79, 2026-11-09 L 0.79 / R 0.89).
/// Why below the median: during a right wink the open left eye squints, so a left level set too high turns right winks
/// into "both half closed" (replay sweep of 2026-11-09: left open 0.70 / 0.775 / 0.85 -> right wink 61 / 48 / 25 %),
/// while left winks hardly depend on the levels. Natural wear (2026-11-09 morning) gives L 0.73 / R 0.92 at p40 and
/// L 0.82 / R 0.97 at p50, against that day's calibration L 0.79 / R 0.89.
/// No VRCFT types, so recorded traces can be replayed through it.</summary>
public sealed class OpenLevelLearner
{
    private const int Bins = 200;
    /// <summary>A sample counts only while both eyes are above this raw level (blinks, closed eyes and most of a wink
    /// are left out; SteamVR's scale reads closed as 0).</summary>
    public const float IncludeAbove = 0.45f;
    /// <summary>After this long without samples (headset off, or no new camera frames) the next ones start a new wear.</summary>
    public const long GapMs = 5000;
    /// <summary>The first seconds of a wear are left out: putting the headset on, adjusting it.</summary>
    public const long WarmupMs = 20_000;
    /// <summary>After a break this long (headset taken off and put on again, maybe sitting differently), the samples so
    /// far count no more than a seed, so the new wear takes over within about a minute.</summary>
    public const long NewWearMs = 300_000;
    /// <summary>Samples before the learned level is used when there was no seed (about 5 s of eye-camera frames).</summary>
    public const double MinWeight = 200;
    /// <summary>Weight of a seed (a saved or calibrated level), in samples: about 30 s of frames at 45 a second.</summary>
    public const double SeedWeight = 1500;
    public const float MinLevel = 0.5f, MaxLevel = 1f;
    /// <summary>The learned level moves at most this much per second of wear, so the avatar shows no jump when a new
    /// wear's samples take over from the saved level.</summary>
    public const float MaxStepPerSec = 0.02f;

    private readonly double[] _l = new double[Bins], _r = new double[Bins];
    private double _weight;
    private long _lastDecay, _lastSample = long.MinValue / 2, _warmUntil;
    public float? OpenL { get; private set; }
    public float? OpenR { get; private set; }
    public double Weight => _weight;
    /// <summary>Set whenever the learned levels changed since the last <see cref="Save"/>.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Which share of the open-eye samples lies below the learned level (0.5 = the median).</summary>
    public double Percentile { get; set; } = 0.4;

    /// <summary>Time constant of the forgetting (the last ~10 minutes count most by default).</summary>
    public double TauMs { get; set; } = 600_000;

    /// <summary>Forget everything and start from these levels (a saved file, or a new calibration).</summary>
    public void Seed(float openL, float openR)
    {
        Array.Clear(_l); Array.Clear(_r);
        _l[Bin(openL)] = SeedWeight; _r[Bin(openR)] = SeedWeight;
        _weight = SeedWeight;
        OpenL = OpenR = null;          // a seed is taken as it is, without the rate limit
        Update(1f);
    }

    /// <summary>One new eye-camera frame (call only when the data changed; repeated values carry no information).</summary>
    public void Add(float rawL, float rawR, long nowMs)
    {
        // a new wear: skip the warm-up, and do not forget over the gap (only worn time counts), so the last wear's
        // level stays the starting point until the new samples outweigh it (about half a minute)
        if (nowMs - _lastSample > GapMs)
        {
            _warmUntil = nowMs + WarmupMs; _lastDecay = nowMs;
            if (nowMs - _lastSample > NewWearMs && _weight > SeedWeight)
            {
                double f = SeedWeight / _weight;
                for (int i = 0; i < Bins; i++) { _l[i] *= f; _r[i] *= f; }
                _weight = SeedWeight;
            }
        }
        _lastSample = nowMs;
        if (nowMs - _lastDecay >= 1000)
        {
            double f = Math.Exp(-(nowMs - _lastDecay) / TauMs);
            for (int i = 0; i < Bins; i++) { _l[i] *= f; _r[i] *= f; }
            _weight *= f;
            float secs = (nowMs - _lastDecay) / 1000f;
            _lastDecay = nowMs;
            Update(secs);
        }
        if (nowMs < _warmUntil || rawL < IncludeAbove || rawR < IncludeAbove) return;
        _l[Bin(rawL)] += 1; _r[Bin(rawR)] += 1; _weight += 1;
    }

    private static int Bin(float v) => Math.Clamp((int)(v * Bins), 0, Bins - 1);

    private void Update(float secs)
    {
        if (_weight < MinWeight) return;   // keep the previous levels (or none) until there is enough
        if (Quantile(_l, Percentile) is not float l || Quantile(_r, Percentile) is not float r) return;
        float step = MaxStepPerSec * Math.Clamp(secs, 0f, 2f);
        static float Toward(float? from, float to, float step) => from is float f ? f + Math.Clamp(to - f, -step, step) : to;
        float nl = Toward(OpenL, l, step), nr = Toward(OpenR, r, step);
        if (OpenL != nl || OpenR != nr) Dirty = true;
        OpenL = nl; OpenR = nr;
    }

    private static float? Quantile(double[] h, double q)
    {
        double total = 0;
        foreach (var x in h) total += x;
        if (total <= 0) return null;
        double c = 0;
        for (int i = 0; i < Bins; i++)
        {
            c += h[i];
            if (c >= total * q) return Math.Clamp((i + 0.5f) / Bins, MinLevel, MaxLevel);
        }
        return null;
    }

    private sealed class Saved
    {
        public float Left { get; set; }
        public float Right { get; set; }
        public string? Updated { get; set; }
    }

    /// <summary>Seed from a file written by <see cref="Save"/>; false when there is none or it is not usable.</summary>
    public bool Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (s == null || !Ok(s.Left) || !Ok(s.Right)) return false;
            Seed(s.Left, s.Right);
            Dirty = false;
            return true;
        }
        catch { return false; }
        static bool Ok(float v) => float.IsFinite(v) && v >= MinLevel && v <= MaxLevel;
    }

    public void Save(string path)
    {
        if (OpenL is not float l || OpenR is not float r) return;
        try
        {
            var json = JsonSerializer.Serialize(new Saved { Left = l, Right = r, Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
            Dirty = false;
        }
        catch { }
    }
}
