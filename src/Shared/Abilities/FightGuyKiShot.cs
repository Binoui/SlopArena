namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's A — Ki Shot: hold to aim the camera, release to fire a
    /// camera-directed ki projectile. The hold/aim/release lifecycle is owned
    /// here (the cooked timeline freezes its stage clock while Aiming, see
    /// <see cref="IAimHoldCapability"/>); the projectile spawns at the authored
    /// startup tick after release.
    /// </summary>
    public sealed class FightGuyKiShot : ServerAbility, IAimHoldCapability
    {
        private readonly CookedKiShotCapabilityParameters _parameters;
        private enum KiShotPhase { Aim, Fire }

        private KiShotPhase _phase;
        private ushort _phaseTicks;
        private float _cachedAimYaw;
        private float _cachedAimPitch;

        public FightGuyKiShot(CookedKiShotCapabilityParameters parameters)
            => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _phase = KiShotPhase.Aim;
            _phaseTicks = 0;
            _cachedAimYaw = s.AimYaw;
            _cachedAimPitch = s.AimPitch;

            // Aim stance: hold = aim, release = fire. Debounce the first ticks
            // so a press that is never held still requires a clean release edge.
            s.State = ActionState.Aiming;
            s.IsAiming = true;
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.AnimLockTicks = 8;
            s.ChargeTicks = 0;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _phaseTicks++;

            // ── Aim phase: track the aim while held; release fires (8-tick debounce) ──
            if (_phase == KiShotPhase.Aim)
            {
                // Track the aim while held — the projectile uses the direction at
                // RELEASE, not at press. Only refresh while the key is still held:
                // the release tick's input may carry zeroed aim values.
                if (input.IsAiming)
                {
                    _cachedAimYaw = s.AimYaw;
                    _cachedAimPitch = s.AimPitch;
                }
                if (_phaseTicks <= 8 || input.IsAiming)
                    return;

                _phase = KiShotPhase.Fire;
                _phaseTicks = 0;
                s.State = ActionState.Attacking;
                s.IsAiming = false;
                s.AttackElapsedTicks = 0;
                s.AnimLockTicks = _parameters.DurationTicks;
                return;
            }

            // ── Fire phase: launch at the authored startup tick ──
            if (_phaseTicks != _parameters.StartupTicks)
                return;

            AnimIndex = 1;
            float cosPitch = MathF.Cos(_cachedAimPitch);
            float speed = _parameters.ProjectileSpeed;
            float vx = speed * cosPitch * MathF.Sin(_cachedAimYaw);
            float vy = speed * MathF.Sin(_cachedAimPitch);
            float vz = speed * cosPitch * MathF.Cos(_cachedAimYaw);
            float damage = _parameters.Damage;
            float radius = _parameters.HitboxRadius;
            ApplyBuffBonuses(ref s, ref damage, ref radius);

            Resolver.Spawn(new Hitbox
            {
                X = s.PX,
                Y = s.PY + _parameters.LaunchOffsetY,
                Z = s.PZ,
                VX = vx,
                VY = vy,
                VZ = vz,
                Radius = radius,
                Shape = HitboxShape.Sphere,
                Damage = damage,
                BaseKnockback = _parameters.KnockbackBase,
                KnockbackGrowth = _parameters.KnockbackGrowth,
                KnockbackAngle = (sbyte)_parameters.KnockbackAngle,
                StunTicks = _parameters.StunTicks,
                DurationTicks = _parameters.MaxFlightTicks,
                OwnerId = s.EntityId,
                Gravity = _parameters.Gravity,
            });
        }

        public override void OnEnd(ref CharacterState s)
        {
            s.IsAiming = false;
        }

        public override void OnCancel(ref CharacterState s)
        {
            s.IsAiming = false;
        }
    }
}
