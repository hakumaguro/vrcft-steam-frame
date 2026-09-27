using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamFrameVRCFTModule;

/// <summary>Tunable parameters, stored as steamframe-config.json next to the module DLL and hot-reloaded while running.</summary>
public sealed class ModuleConfig
{
    public LidConfig Lid { get; set; } = new();
    public WinkConfig Wink { get; set; } = new();
    public BlinkConfig Blink { get; set; } = new();
    public GazeConfig Gaze { get; set; } = new();
    /// <summary>Swap left/right eyes (if the headset reports them the other way round).</summary>
    public bool SwapEyes { get; set; }
    /// <summary>Write 30 ms samples to %TEMP%\steamframe-trace.csv. tools/tune.py turns this on while it records or watches.</summary>
    public bool Trace { get; set; }

    public sealed class LidConfig
    {
        /// <summary>Fraction of the calibrated range at each end that reads as fully closed / fully open.</summary>
        public float Deadband { get; set; } = 0.06f;
        /// <summary>Per-update drift of the adaptive envelopes toward the current value (bigger = adapts faster).</summary>
        public float Tau { get; set; } = 0.0004f;
        public float MaxFloor { get; set; } = 0.40f;
        public float MinCeil { get; set; } = 0.55f;
        public float MinRange { get; set; } = 0.25f;
        /// <summary>Low-pass factor for the envelope tracker (0..1, bigger = less smoothing).</summary>
        public float Smoothing { get; set; } = 0.35f;
        /// <summary>Fixed calibration per eye (raw tracker units). When both closed and open are set for an eye, adaptation is off for it.</summary>
        public float? LeftClosed { get; set; }
        public float? LeftOpen { get; set; }
        public float? RightClosed { get; set; }
        public float? RightOpen { get; set; }
    }

    public sealed class WinkConfig
    {
        /// <summary>Lid difference above which the asymmetry is amplified.</summary>
        public float Threshold { get; set; } = 0.15f;
        public float Range { get; set; } = 0.25f;
        /// <summary>0 disables wink sharpening.</summary>
        public float Strength { get; set; } = 1.0f;
        /// <summary>Wink assist: closing one eye tightens the other, so during a wink the tracker reports the other lid partly closed.
        /// When one eye is at its floor and the other sits clearly above its own floor for <see cref="AssistPersistMs"/>, treat it
        /// as a wink and show the other eye open. Blinks are too short to trigger it.</summary>
        public bool Assist { get; set; }
        /// <summary>Openness the assisted eye is shown at (0..1).</summary>
        public float AssistOpen { get; set; } = 0.95f;
        /// <summary>Mapped lid value at or below this counts as fully closed.</summary>
        public float AssistClosed { get; set; } = 0.06f;
        /// <summary>The other eye must be above this mapped value (i.e. above its closed floor) to count as "partly there".</summary>
        public float AssistMin { get; set; } = 0.08f;
        public int AssistPersistMs { get; set; } = 150;
        public int AssistReleaseMs { get; set; } = 120;
    }

    public sealed class BlinkConfig
    {
        /// <summary>Keep a lid at its lowest recent value this long so fast blinks reach full depth.</summary>
        public int HoldMs { get; set; } = 90;
        public float ReleasePerSec { get; set; } = 10f;
        /// <summary>A lopsided closure (one eye closed, the other open) shorter than this is a blink and closes both eyes;
        /// a longer one is a wink. The tracker often reports blinks as one lid at 0 and the other at 1. 0 disables.</summary>
        public int CoupleMs { get; set; } = 140;
        /// <summary>Normalised lid below this counts as closed, above <see cref="AsymOpen"/> as open, for the lopsided test.</summary>
        public float AsymClosed { get; set; } = 0.35f;
        public float AsymOpen { get; set; } = 0.60f;
        /// <summary>The tracker reports many blinks as one lid closed while the other is pinned at its ceiling (raw ~1.0).
        /// While that signature lasts, both eyes close (no time limit). A real wink keeps the open eye near its normal level.
        /// 0 disables.</summary>
        public float SaturatedRaw { get; set; } = 0.985f;
        /// <summary>The glitch signature must last this long before it counts, so a single saturated frame in a real wink is ignored.</summary>
        public int GlitchMinMs { get; set; } = 40;
    }

    public sealed class GazeConfig
    {
        /// <summary>Multiplier on the gaze angle (frameeyeosc sends +-1 == +-45 degrees).</summary>
        public float Scale { get; set; } = 1.0f;
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Load and validate. Returns null (with a reason) when the file is unreadable or invalid, so the caller can keep
    /// the configuration it already has instead of silently dropping the user's calibration. A missing file gets the defaults.</summary>
    public static ModuleConfig? TryLoad(string path, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(path))
            {
                var def = new ModuleConfig();
                File.WriteAllText(path, JsonSerializer.Serialize(def, Json));
                return def;
            }
            var cfg = JsonSerializer.Deserialize<ModuleConfig>(File.ReadAllText(path), Json);
            if (cfg == null) { error = "the file is empty or null"; return null; }
            error = cfg.Validate();
            return error == "" ? cfg : null;
        }
        catch (Exception e) { error = e.Message; return null; }
    }

    /// <summary>Empty string when usable, otherwise the first problem found.</summary>
    public string Validate()
    {
        if (Lid == null || Wink == null || Blink == null || Gaze == null) return "a section (lid, wink, blink or gaze) is missing or null";
        bool Bad(float v, float lo, float hi) => !float.IsFinite(v) || v < lo || v > hi;
        if (Bad(Lid.Deadband, 0f, 0.45f)) return "lid.deadband must be 0..0.45";
        if (Bad(Lid.Smoothing, 0f, 1f) || Bad(Lid.Tau, 0f, 1f)) return "lid.smoothing and lid.tau must be 0..1";
        if (Bad(Lid.MaxFloor, 0f, 1f) || Bad(Lid.MinCeil, 0f, 1f) || Bad(Lid.MinRange, 0.01f, 1f)) return "lid.maxFloor/minCeil/minRange out of range";
        foreach (var (c, o, eye) in new[] { (Lid.LeftClosed, Lid.LeftOpen, "left"), (Lid.RightClosed, Lid.RightOpen, "right") })
        {
            if (c is float fc && Bad(fc, 0f, 1f)) return $"lid.{eye}Closed must be 0..1";
            if (o is float fo && Bad(fo, 0f, 1f)) return $"lid.{eye}Open must be 0..1";
            if (c is float a && o is float b && b - a < 0.05f) return $"lid.{eye}Open must be above lid.{eye}Closed";
        }
        if (Bad(Wink.Threshold, 0f, 1f) || Bad(Wink.Range, 0.01f, 2f) || Bad(Wink.Strength, 0f, 1f)) return "wink.threshold/range/strength out of range";
        if (Bad(Wink.AssistOpen, 0f, 1f) || Bad(Wink.AssistClosed, 0f, 1f) || Bad(Wink.AssistMin, 0f, 1f)) return "wink.assist* levels must be 0..1";
        if (Wink.AssistPersistMs < 0 || Wink.AssistReleaseMs < 0) return "wink.assist*Ms must not be negative";
        if (Blink.HoldMs < 0 || Blink.CoupleMs < 0 || Blink.GlitchMinMs < 0) return "blink.*Ms must not be negative";
        if (Bad(Blink.ReleasePerSec, 0f, 1000f) || Bad(Blink.AsymClosed, 0f, 1f) || Bad(Blink.AsymOpen, 0f, 1f) || Bad(Blink.SaturatedRaw, 0f, 1f))
            return "blink.* values out of range";
        if (Bad(Gaze.Scale, 0f, 10f)) return "gaze.scale must be 0..10";
        return "";
    }
}
