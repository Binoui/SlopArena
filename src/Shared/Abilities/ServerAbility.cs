using System;
using System.Collections.Generic;
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
        // Lazily allocated only for multi-event moves that deliberately share a hit identity
        // (for example an early sweetspot handing off to a late sourspot).
        private Dictionary<byte, HashSet<ulong>>? _hitGroups;
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
            CharacterDefinition attackerDef, CharacterDefinition targetDef,
            ref float damage, ref float knockbackForce)
        {
        }

        /// <summary>Target-side counter hook with the launched attacker's definition.</summary>
        public virtual bool TryCounter(ref CharacterState defender, ref CharacterState attacker,
            CharacterDefinition attackerDef, float incomingDamage)
        {
            return false;
        }
        // ── Metadata (set by factory after construction) ──

        /// <summary>Which ability slot (0-5).</summary>
        public byte Slot { get; set; }
        /// <summary>Cooldown in ticks. Applied by EndAbility on natural completion.</summary>
        public ushort Cooldown { get; set; }

        /// <summary>
        /// Whether this ability instance was STARTED while airborne (set by the simulation
        /// at activation from the owner's grounded state). Landing-lag termination
        /// (ADR-0021 §3 / drift fix) uses this, not the grounded state at landing time: an
        /// air move that lands mid-flight ends on the landing frame; a ground move keeps its
        /// ground behavior even if launched and landed mid-move.
        /// </summary>
        public bool AirborneAtStart { get; set; }

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
        /// <summary>
        /// The arena being played, for terrain-aware displacement: an ability that
        /// writes position directly (blinks, teleports, warps) can sample
        /// <see cref="ArenaHeightmap.Sample"/> to resolve a destination that does not
        /// end inside geometry. Set by ServerSimulation before activation.
        /// MAY be null — harnesses that drive abilities without a simulation never set
        /// it, so every consumer must have a no-arena fallback.
        /// </summary>
        public ArenaDefinition? Arena { get; set; }

        // ── Helpers (call from Tick) ──

        /// <summary>
        /// Spawn a hitbox at the character's position + facing-relative offsets.
        /// When evt.BoneName is set and baked data is available, positions at the
        /// bone's world position instead of the fixed OffX/Y/Z offset.
        /// </summary>
        protected void SpawnHitbox(ref CharacterState s, HitboxEvent evt)
        {
            // Position resolution is shared with the Ability Lab preview + tests
            // (spec #119) — one implementation, previews cannot drift from the server.
            HitboxGeometry.ResolvePositions(
                s, evt, BakedData, CharacterDef, AnimationNames, AnimIndex, Slot, !s.IsGrounded,
                out float wx, out float wy, out float wz,
                out float wex, out float wey, out float wez);

            float damage = evt.Damage;
            float radius = evt.Radius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            // Resolve knockback profile to flat values
            var (kbAngle, kbBase, kbGrowth) = evt.Knockback.Resolve();

            // Bone-attached melee hitboxes re-resolve their bone position every tick
            // (SpellResolver.UpdateBoneHitboxes) — the limb sweeps the hitbox. A
            // capsule with EndBoneName tracks its end point the same way.
            bool tracksBone = (evt.BoneName != null || evt.EndBoneName != null) && BakedData != null;

            HashSet<ulong>? sharedHitEntities = null;
            if (evt.HitGroup != 0)
            {
                _hitGroups ??= new Dictionary<byte, HashSet<ulong>>();
                if (!_hitGroups.TryGetValue(evt.HitGroup, out sharedHitEntities))
                {
                    sharedHitEntities = new HashSet<ulong>();
                    _hitGroups.Add(evt.HitGroup, sharedHitEntities);
                }
            }

            Resolver.Spawn(new Hitbox
            {
                X = wx, Y = wy, Z = wz,
                // Tracked hitboxes are re-resolved to the bone's absolute world
                // position each tick; velocity would double-apply the owner's
                // translation on top of that. Untracked bone-anchored hitboxes (no
                // baked data) fall back to entity-relative and inherit the owner's
                // velocity like any entity-anchored hitbox.
                VX = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VX : 0f),
                VY = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VY : 0f),
                VZ = tracksBone ? 0f : ((evt.BoneName != null || evt.EndBoneName != null) ? s.VZ : 0f),
                Radius = radius,
                Shape = evt.Shape,
                EndX = wex, EndY = wey, EndZ = wez,
                Damage = damage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = kbAngle,
                StunTicks = evt.StunTicks,
                DurationTicks = evt.DurationTicks,
                OwnerId = s.EntityId,
                FreezesOwner = true,
                HitsMultipleOpponents = true,
                HitEntities = sharedHitEntities,
                TracksBone = tracksBone,
                SourceEvent = evt,
                Baked = BakedData,
                Def = CharacterDef,
                AnimationNames = AnimationNames,
                AnimIndex = AnimIndex,
                Slot = Slot,
                Airborne = !s.IsGrounded,
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
        /// End the ability naturally: calls OnEnd, returns to Idle.
        /// The simulation applies cooldown after return.
        /// Horizontal velocity is intentionally NOT zeroed (issue #115 / ADR-0015):
        /// momentum survives the attack — lunge drift and pre-attack velocity carry
        /// into the next state, where normal friction/air control resumes.
        /// </summary>
        protected void EndAbility(ref CharacterState s)
        {
            OnEnd(ref s);
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
            if (spec?.Params != null && spec.Params.TryGetValue(key, out float val))
                return val;
            return fallback;
        }
    }
}
