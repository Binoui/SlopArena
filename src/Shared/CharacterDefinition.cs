using System;

#nullable enable

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        Manki,
        Bunny
    }

    [Serializable]
    public struct MovementStats
    {
        public float WalkSpeed;
        public float SprintSpeed;
        public float DashSpeed;
        public float AirAcceleration;
        public float JumpForce;
        public float Gravity;
        /// <summary>Reduced gravity while attacking/aiming in the air (m/s²). Default 6f.</summary>
        public float AirFloatGravity;
        public ushort DashDurationTicks;
        public ushort DashCooldownTicks;
        public float GroundFriction;
        public float AirFriction;
        public float MaxFallSpeed;
        public byte MaxJumps;
        /// <summary>Jump squat duration in ticks (1 tick = 1/60s). Character locks during this window before airborne.</summary>
        public ushort JumpSquatTicks;
    }

    public class CharacterDefinition
    {
        

        public CharacterClass Class;
        public string DisplayName = "";
        public MovementStats Movement;

        public float CapsuleRadius;
        public float CapsuleHeight;
        public float HurtboxRadius;

        /// <summary>
        /// World-space offset from character position (legacy, used when no skeleton)
        /// </summary>
        public HurtboxCapsule[]? HurtboxCapsules;

        /// <summary>
        /// Bone-attached hurtboxes (ServerSkeleton-based). Replaces HurtboxCapsules when loaded.
        /// Each entry defines a sphere at a bone position with an offset.
        /// </summary>
        public HurtboxBoneDef[]? HurtboxBoneDefs;
        /// <summary>Path to the GLB file containing the skeleton model.</summary>
        public string GlbPath = "";
        /// <summary>Path to the baked skeleton .bin file (pre-computed bone positions per frame).</summary>
        public string BakedDataPath = "";
        /// <summary>Unity Resources path for the model prefab. E.g. "Characters/Manki"</summary>
        public string ModelResourcePath = "";
        /// <summary>
        /// Scale factor for baked bone positions. Mixamo GLBs are in cm (0.01),
        /// Blender/Maya exports with meters are 1.0. Default: 1.0
        /// </summary>
        public float HurtboxBoneScale = 1.0f;
        /// <summary>
        /// Y offset for the visual model relative to capsule center.
        /// Aligns the model's feet with the capsule bottom.
        /// Calculated as: -(footY * HurtboxBoneScale + CapsuleHeight * 0.5f)
        /// For Mixamo: ≈ -0.52 (Manki), adjust per character.
        /// 0 = model origin at capsule center (if model is already centered).
        /// </summary>
        public float ModelYOffset;
        /// <summary>
        /// Additional downward offset for the sole of the foot (below the lowest bone).
        /// Bones are inside the mesh; this accounts for sole thickness.
        /// Typical: 0.04-0.06m for humanoids, 0 for robots/mechs.
        /// </summary>
        public float ModelSoleOffset;
        /// <summary>
        /// If true, ModelYOffset is computed from the baked skeleton data
        /// (lowest bone position at idle frame 0) instead of using the manual value.
        /// </summary>
        public bool AutoModelYOffset;
        /// <summary>
        /// Scale factor from GLB skeleton units to world meters.
        /// Applied to both the visual model node and the baked bone positions
        /// so hurtboxes and visuals stay aligned.
        /// Manki: 1.0 (Mixamo cm→m handled by GLB import).
        /// Bunny: ~0.022 (Tripo/Hunyuan export in raw units).
        /// </summary>
        public float VisualScale = 1.0f;

        // ── Animation catalog (defaults match Mixamo naming) ──

        /// <summary>Idle animation clip name. Default: "idle"</summary>
        public string IdleAnim = "idle";
        /// <summary>Run animation clip name. Default: "run"</summary>
        public string RunAnim = "run";
        /// <summary>Dash animation clip name. Default: "dash"</summary>
        public string DashAnim = "dash";
        /// <summary>Jump animation clip (BlendSpace1D position -1). Default: "jump"</summary>
        public string JumpAnim = "jump";
        /// <summary>Fall animation clip (BlendSpace1D position +1). Default: "fall"</summary>
        public string FallAnim = "fall";
        /// <summary>Small hit reaction clip. Default: "small_hit"</summary>
        public string HitSmallAnim = "small_hit";
        /// <summary>Medium hit reaction clip. Default: "medium_hit"</summary>
        public string HitMediumAnim = "medium_hit";
        /// <summary>Hard hit reaction clip. Default: "hard_hit"</summary>
        public string HitHardAnim = "hard_hit";
        /// <summary>Landing uses JumpAnim clip with this start offset (seconds). Default: 0.49f</summary>
        public float LandStartOffset = 0.49f;
        /// <summary>Per-clip overrides for non-default timeline/loop settings.</summary>
        public AnimationClipConfig[]? ClipOverrides;

        public AbilitySpec? LMB;
        public AbilitySpec? RMB;
        public AbilitySpec? AirLMB;
        public AbilitySpec? AirRMB;
        public AbilitySpec? Q;
        public AbilitySpec? E;
        public AbilitySpec? R;
        public AbilitySpec? F;
        // No constructor needed — class fields auto-default

        public AbilitySpec GetSlotAbility(int slotIndex, bool airborne = false) => (slotIndex, airborne) switch
        {
            (0, true) => AirLMB,
            (1, true) => AirRMB,
            (0, _) => LMB,
            (1, _) => RMB,
            (2, _) => Q,
            (3, _) => E,
            (4, _) => R,
            (5, _) => F,
            _ => throw new ArgumentOutOfRangeException(nameof(slotIndex))
        };
    }

    /// <summary>
    /// Character registry with lazy initialization.
    /// Factory methods live in separate per-character files (Shared/Characters/).
    /// </summary>
    public static partial class CharacterRegistry
    {
        private static CharacterDefinition[]? _definitions;

        public static CharacterDefinition[] All
        {
            get
            {
                if (_definitions == null)
                    _definitions = BuildRegistry();
                return _definitions;
            }
        }

        public static CharacterDefinition Get(CharacterClass c) => All[(int)c];

        private static CharacterDefinition[] BuildRegistry()
        {
            return new CharacterDefinition[]
            {
                BuildManki(),
                BuildBunny(),
            };
        }
    }
}
