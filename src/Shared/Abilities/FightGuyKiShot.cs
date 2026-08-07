using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's Q — Ki Shot: hold to aim (TPS camera), release fires a fast ki blast
    /// in aim direction. Minimal gravity — true ki-blast floating projectile.
    /// On entity hit: applies Marked status (StatusType.Marked = bit 2).
    /// The hold/aim/throw lifecycle lives in <see cref="AimHoldAbility"/>.
    /// </summary>
    public sealed class FightGuyKiShot : AimHoldAbility
    {
        private float _cachedAimYaw;
        private float _cachedAimPitch;

        // AnimationNames = [spell_q_loop, spell_q_attack]; the mid-hold swap and the
        // release index are kept verbatim from the original (release index 2 exceeds the
        // array — preserved intentionally, the renderer indexes by ComboStage, not AnimIndex).
        protected override int GetMidHoldAnimIndex(CharacterDefinition def) => 1;
        protected override byte GetReleaseAnimIndex(CharacterDefinition def) => 2;

        protected override void OnAimStart(ref CharacterState s, CharacterDefinition def)
        {
            _cachedAimYaw = 0f;
            _cachedAimPitch = 0f;
        }

        protected override void OnRelease(ref CharacterState s, CharacterDefinition def)
        {
            _cachedAimYaw = s.AimYaw;
            _cachedAimPitch = s.AimPitch;
        }

        protected override void OnFire(ref CharacterState s, CharacterDefinition def)
        {
            float speed = GetParam(def, "projectile_speed", 25f);
            float pitch = _cachedAimPitch;
            float cosPitch = MathF.Cos(pitch);
            float vx = speed * cosPitch * MathF.Sin(_cachedAimYaw);
            float vy = speed * MathF.Sin(pitch);
            float vz = speed * cosPitch * MathF.Cos(_cachedAimYaw);

            float launchOffsetY = GetParam(def, "launch_offset_y", 1.2f);
            float projRadius = GetParam(def, "hitbox_radius", 0.5f);
            float projDamage = GetParam(def, "damage", 6f);
            ApplyBuffBonuses(ref s, ref projDamage, ref projRadius);

            float kbBase = GetParam(def, "knockback_base", 3f);
            float kbGrowth = GetParam(def, "knockback_growth", 4.5f);
            float kbAngle = GetParam(def, "kb_angle", 30f);
            ushort stunTicks = (ushort)GetParam(def, "stun_ticks", 12f);
            ushort maxFlight = (ushort)GetParam(def, "max_flight_ticks", 90f);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX,
                Y = s.PY + launchOffsetY,
                Z = s.PZ,
                VX = vx, VY = vy, VZ = vz,
                Radius = projRadius,
                Shape = HitboxShape.Sphere,
                EndX = s.PX, EndY = s.PY, EndZ = s.PZ,
                Damage = projDamage,
                BaseKnockback = kbBase,
                KnockbackGrowth = kbGrowth,
                KnockbackAngle = (sbyte)kbAngle,
                StunTicks = stunTicks,
                DurationTicks = maxFlight,
                OwnerId = s.EntityId,
                Gravity = GetParam(def, "gravity", 1f),
            });
        }

        /// <summary>
        /// On hit: apply Marked status to target (bit 2 = StatusType.Marked).
        /// Ignores self-hit (no self-mark).
        /// </summary>
        public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
            CharacterDefinition attackerDef,
            ref float damage, ref float knockbackForce)
        {
            // Skip self-mark
            if (attacker.EntityId == target.EntityId)
                return;

            ushort markDuration = (ushort)GetParam(attackerDef, "mark_duration_ticks", 300f);

            // Apply Marked status (bit 2 = 1 << 2 = 4)
            target.StatusFlags |= (1 << 2);
            target.StatusRemainingTicks = Math.Max(target.StatusRemainingTicks, markDuration);
        }
    }
}
