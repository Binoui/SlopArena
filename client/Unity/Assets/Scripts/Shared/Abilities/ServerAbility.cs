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

        /// <summary>Animation names from the spec. Indexed by AnimIndex.</summary>
        public string[] AnimationNames { get; set; } = Array.Empty<string>();

        // ── Context (set by simulation before first Tick) ──

        /// <summary>Hitbox resolver. Set by ServerSimulation before activation.</summary>
        public ISpellResolver Resolver { get; set; } = null!;

        // ── Helpers (call from Tick) ──

        /// <summary>
        /// Spawn a hitbox at the character's position + facing-relative offsets.
        /// The hitbox is registered with the SpellResolver for this tick.
        /// </summary>
        protected void SpawnHitbox(ref CharacterState s, HitboxEvent evt)
        {
            float cos = MathF.Cos(s.FacingYaw);
            float sin = MathF.Sin(s.FacingYaw);

            float wx = s.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
            float wy = s.PY + evt.OffY;
            float wz = s.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));

            float wex = wx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
            float wey = wy + evt.EndOffY;
            float wez = wz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));

            float damage = evt.Damage;
            float radius = evt.Radius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = wx, Y = wy, Z = wz,
                Radius = radius,
                Shape = evt.Shape,
                EndX = wex, EndY = wey, EndZ = wez,
                Damage = damage,
                KnockbackForce = evt.KnockbackForce,
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
        protected void ApplyBuffBonuses(ref CharacterState s, ref float damage, ref float radius)
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
        /// End the ability naturally: calls OnEnd, sets state to Idle,
        /// and the simulation applies cooldown.
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
            if (spec.Params != null && spec.Params.TryGetValue(key, out float val))
                return val;
            return fallback;
        }
    }
}
