using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Base class for server-side abilities that control movement, hitboxes,
    /// and state transitions per tick. Each activation gets a fresh instance.
    ///
    /// Lifecycle:
    ///   OnStart  → called once when ability activates
    ///   Tick     → called every sim tick while active
    ///   OnEnd    → called on NATURAL completion only (NOT on interrupt)
    ///
    /// Interruption (hitstun, death, state override):
    ///   Simulation drops the instance without calling OnEnd.
    ///   Velocity/state is preserved (momentum-granting abilities work correctly).
    /// </summary>
    public abstract class ServerAbility
    {
        // ── Lifecycle (implement in subclasses) ──

        /// <summary>Called once when the ability activates.</summary>
        public abstract void OnStart(ref CharacterState s, CharacterDefinition def);

        /// <summary>Called every sim tick while the ability is active.</summary>
        public abstract void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def);

        /// <summary>
        /// Called on NATURAL completion only (duration expired, EndAbility called).
        /// NOT called on interruption. Override to clean up or apply lingering effects.
        /// </summary>
        public virtual void OnEnd(ref CharacterState s) { }


        /// <summary>
        /// Called when this ability's hitbox connects with a target entity.
        /// Override to apply status effects, conditional damage, or other
        /// hit-time effects (e.g., FightGuy R mark consumption).
        /// </summary>
        public virtual void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce)
        {
        }
        // ── Metadata (set by factory after construction) ──

        /// <summary>Which ability slot (0-5).</summary>
        public byte Slot { get; set; }
        /// <summary>Cooldown in ticks. Applied by EndAbility on natural completion.</summary>
        public ushort Cooldown { get; set; }

        // ── Animation (set during Tick, synced to client via CharacterState.AnimIndex) ──

        /// <summary>
        /// Current animation index into the spec's AnimationNames[].
        /// Set this in Tick() to change the client's animation.
        /// </summary>
        public byte AnimIndex { get; protected set; }

        /// <summary>All entity states (set by ServerSimulation). Used by abilities that need
        /// to inspect other entities (e.g., FightGuy Tempest pull, homing).</summary>
        public Dictionary<ulong, CharacterState>? SimulationStates { get; set; }

        /// <summary>Animation names from the spec. Indexed by AnimIndex.</summary>
        public string[] AnimationNames { get; set; } = Array.Empty<string>();

        // ── Context (set by simulation before first Tick) ──

        /// <summary>Hitbox resolver. Set by ServerSimulation before activation.</summary>
        public ISpellResolver Resolver { get; set; } = null!;
        /// <summary>Baked animation data for bone-attached hitbox resolution. Set by ServerSimulation.</summary>
        public BakedAnimationData? BakedData { get; set; }
        /// <summary>Character definition for the ability owner. Set by ServerSimulation.</summary>
        public CharacterDefinition? CharacterDef { get; set; }

        // ── Helpers (call from Tick) ──

        /// <summary>
        /// Spawn a hitbox at the character's position + facing-relative offsets.
        /// When evt.BoneName is set and baked data is available, positions at the
        /// bone's world position instead of the fixed OffX/Y/Z offset.
        /// </summary>
        protected void SpawnHitbox(ref CharacterState s, HitboxEvent evt)
        {
            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);

            float wx, wy, wz;

            // ── Bone-attached hitbox path (requires baked data + HurtboxBoneDefs) ──
            if (evt.BoneName != null && BakedData != null && CharacterDef?.HurtboxBoneDefs != null)
            {
                int bi = -1;
                for (int i = 0; i < CharacterDef.HurtboxBoneDefs.Length; i++)
                {
                    if (CharacterDef.HurtboxBoneDefs[i].BoneName == evt.BoneName) { bi = i; break; }
                }

                if (bi >= 0)
                {
                    // Resolve animation name from AnimIndex
                    string targetAnim = AnimationNames.Length > AnimIndex ? AnimationNames[AnimIndex] : "idle";
                    int animIdx = BakedData.FindAnimIndex(targetAnim);
                    if (animIdx < 0) { targetAnim = "idle"; animIdx = BakedData.FindAnimIndex(targetAnim); }

                    if (animIdx >= 0)
                    {
                        int fc = BakedData.Animations[animIdx].FrameCount;
                        // Use current stage's duration via AnimIndex, with guard against OOB
                        var spec = CharacterDef?.GetSlotAbility(Slot, false);
                        int durationTicks = (spec != null && AnimIndex < spec.Stages.Length)
                            ? spec.Stages[AnimIndex].DurationTicks
                            : 60;
                        int bakedFrame = durationTicks > 0
                            ? Math.Min(s.AttackElapsedTicks * fc / durationTicks, fc - 1)
                            : Math.Min(s.AttackElapsedTicks, fc - 1);
                        if (bakedFrame >= fc) bakedFrame = fc - 1;

                        if (BakedData.GetBonePosition(targetAnim, bakedFrame, bi, out float bx, out float by, out float bz))
                        {
                            float scale = CharacterDef.HurtboxBoneScale;
                            bx *= scale; by *= scale; bz *= scale;
                            wx = s.PX + ((bx * cos) + (bz * sin));
                            wy = CharacterDef.BoneYToWorldY(s.PY, by);
                            wz = s.PZ + ((-bx * sin) + (bz * cos));
                            wx += (evt.BoneOffX * cos) + (evt.BoneOffZ * sin);
                            wy += evt.BoneOffY;
                            wz += (-evt.BoneOffX * sin) + (evt.BoneOffZ * cos);
                            goto spawnHitbox;
                        }
                    }
                }
            }

            // ── Fallback: entity-relative offset (standard path) ──
            wx = s.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = s.PY + evt.OffY;
            wz = s.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));

        spawnHitbox:
            float wex = wx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
            float wey = wy + evt.EndOffY;
            float wez = wz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));

            float damage = evt.Damage;
            float radius = evt.Radius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = wx, Y = wy, Z = wz,
                // Bone-attached hitboxes get velocity to follow the character
                VX = evt.BoneName != null ? s.VX : 0f,
                VY = evt.BoneName != null ? s.VY : 0f,
                VZ = evt.BoneName != null ? s.VZ : 0f,
                Radius = radius,
                Shape = evt.Shape,
                EndX = wex, EndY = wey, EndZ = wez,
                Damage = damage,
                BaseKnockback = evt.BaseKnockback,
                KnockbackGrowth = evt.KnockbackGrowth,
                KnockbackUpward = evt.KnockbackUpward,
                StunTicks = evt.StunTicks,
                DurationTicks = evt.DurationTicks,
                OwnerId = s.EntityId,
            });
        }

        /// <summary>
        /// Apply active buff bonuses to damage and radius.
        /// Call before SpawnHitbox or Resolver.Spawn in any ability.
        /// Overclock adds +3 damage and +0.5 radius.
        /// </summary>
        public static void ApplyBuffBonuses(ref CharacterState s, ref float damage, ref float radius)
        {
            if ((s.BuffActiveFlags & (byte)BuffType.Overclock) != 0)
            {
                damage += 3f;
                radius += 0.5f;
            }
        }

        /// <summary>Set character velocity (world space).</summary>
        protected void SetVelocity(ref CharacterState s, float vx, float vy, float vz)
        {
            s.VX = vx;
            s.VY = vy;
            s.VZ = vz;
        }

        /// <summary>
        /// Apply velocity in the character's facing direction.
        /// forwardSpeed > 0 = forward, < 0 = backward.
        /// </summary>
        protected void SetVelocityInFacing(ref CharacterState s, float forwardSpeed, float vertical = 0f)
        {
            s.VX = MathF.Sin(s.FacingYaw) * forwardSpeed;
            s.VZ = MathF.Cos(s.FacingYaw) * forwardSpeed;
            s.VY = vertical;
        }

        /// <summary>
        /// End the ability naturally: calls OnEnd, zeros residual horizontal velocity,
        /// sets state to Idle. The simulation applies cooldown after return.
        /// </summary>
        protected void EndAbility(ref CharacterState s)
        {
            OnEnd(ref s);
            // Zero residual horizontal velocity from lunge/kick to prevent drift.
            // All EndAbility callers represent "attack complete, return to neutral".
            s.VX = 0f;
            s.VZ = 0f;
            s.State = ActionState.Idle;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            // Cooldown is applied by the simulation after EndAbility returns
            s.AttackSlot = 0; // signal to simulation that ability ended
        }

        /// <summary>
        /// Read a named float parameter from the ability spec.
        /// Returns fallback if the key is not found.
        /// </summary>
        protected float GetParam(CharacterDefinition def, string key, float fallback = 0f)
        {
            // Slot is 0-based, but GetSlotAbility expects 0-based slot index
            var spec = def.GetSlotAbility(Slot, airborne: false);
            if (spec.Params != null && spec.Params.TryGetValue(key, out float val))
                return val;
            return fallback;
        }
    }
}
