#nullable enable

namespace SlopArena.Shared
{
    /// <summary>Matches Godot's Animation.LoopModeEnum without importing Godot.</summary>
    public enum ClipLoopMode : byte
    {
        None = 0,
        Linear = 1,
        PingPong = 2,
    }

    /// <summary>
    /// Per-clip override for AnimationNodeAnimation properties.
    /// Set only the fields that differ from defaults.
    /// </summary>
    public struct AnimationClipConfig
    {
        /// <summary>Animation name (must match the GLB animation clip name).</summary>
        public string Name;

        /// <summary>Overrides the default loop mode for this clip.</summary>
        public ClipLoopMode? LoopMode;

        /// <summary>Custom timeline start offset in seconds.</summary>
        public float? StartOffset;

        /// <summary>Custom timeline length in seconds.</summary>
        public float? TimelineLength;

        /// <summary>Stretch the clip to fill the custom timeline. Default: false.</summary>
        public bool StretchTimeScale;
    }
}
