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
    }

    public sealed class BlinkConfig
    {
        /// <summary>Keep a lid at its lowest recent value this long so fast blinks reach full depth.</summary>
        public int HoldMs { get; set; } = 90;
        public float ReleasePerSec { get; set; } = 10f;
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

    public static ModuleConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ModuleConfig>(File.ReadAllText(path), Json) ?? new ModuleConfig();
            var cfg = new ModuleConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, Json));
            return cfg;
        }
        catch { return new ModuleConfig(); }
    }
}
