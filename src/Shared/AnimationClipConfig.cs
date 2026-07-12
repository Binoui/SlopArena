#nullable enable

namespace SlopArena.Shared
{
    /// <summary>
    /// How a clip behaves when its natural length is exceeded.
    /// </summary>
    public enum ExtrapolationMode : byte
    {
        /// <summary>Stop at end (Animancer default, holds last frame).</summary>
        None = 0,
        /// <summary>Explicit hold at last frame (same result as None).</summary>
        Hold = 1,
        /// <summary>Continue curve trajectory via last two position keyframes.</summary>
        Continuous = 2,
    }

    /// <summary>
    /// Per-clip override for animation settings.
    /// Set only the fields that differ from defaults.
    /// </summary>
    public struct AnimationClipConfig
    {
        /// <summary>Animation name (must match the clip name in the config resource).</summary>
        public string Name;

        /// <summary>How this clip behaves past its natural length.</summary>
        public ExtrapolationMode Extrapolation;

        /// <summary>Override playback speed (frames per second). 0 = use clip's native framerate.</summary>
        public float FrameRateOverride;
    }
}
