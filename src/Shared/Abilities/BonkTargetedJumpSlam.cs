using System;
using SlopArena.Shared;

namespace SlopArena.Shared.Abilities;

/// <summary>Bonk E: hold a ground cursor, jump ballistically to the target, then slam on landing.</summary>
public sealed class BonkTargetedJumpSlam : ServerAbility
{
    private readonly CookedBonkTargetedJumpSlamCapabilityParameters _parameters;
    private enum Phase { Aim, Jump, Slam }

    private Phase _phase;
    private ushort _phaseTicks;
    private float _aimYaw;
    private float _aimDistance;
    private float _jumpSpeed;
    private bool _slamSpawned;

    public BonkTargetedJumpSlam(CookedBonkTargetedJumpSlamCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Aim;
        _phaseTicks = 0;
        _aimYaw = s.AimYaw;
        _aimDistance = s.AimTargetDistance;
        _jumpSpeed = 0f;
        _slamSpawned = false;

        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.AnimLockTicks = _parameters.MaxAimTicks;
        AnimIndex = 0;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _phaseTicks++;
        switch (_phase)
        {
            case Phase.Aim:
                TickAim(ref s, input, def);
                break;
            case Phase.Jump:
                TickJump(ref s, def);
                break;
            case Phase.Slam:
                TickSlam(ref s);
                break;
        }
    }

    private void TickAim(ref CharacterState s, InputState input, CharacterDefinition def)
    {
        if (input.IsAiming)
        {
            _aimYaw = s.AimYaw;
            _aimDistance = s.AimTargetDistance;
        }

        // A zero cap is the explicit unlimited sentinel; release is still debounced.
        if (_phaseTicks > 8 && (!input.IsAiming || (_parameters.MaxAimTicks > 0 && _phaseTicks >= _parameters.MaxAimTicks)))
            StartJump(ref s, def);
    }

    private void StartJump(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Jump;
        _phaseTicks = 0;
        _aimDistance = Math.Clamp(_aimDistance, _parameters.MinRange, _parameters.MaxRange);
        _aimYaw = NormalizeYaw(_aimYaw);

        float gravity = def.Movement.Gravity;
        float flightSeconds = gravity > 0f
            ? 2f * _parameters.LaunchVerticalSpeed / gravity
            : _parameters.MaxFlightTicks * Simulation.TickDt;
        if (flightSeconds <= 0f)
            flightSeconds = _parameters.MaxFlightTicks * Simulation.TickDt;
        _jumpSpeed = _aimDistance / flightSeconds;

        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.FacingYaw = _aimYaw;
        s.IsGrounded = false;
        s.AirTimeTicks = def.Movement.FloatWindowTicks;
        s.VX = MathF.Sin(_aimYaw) * _jumpSpeed;
        s.VY = _parameters.LaunchVerticalSpeed;
        s.VZ = MathF.Cos(_aimYaw) * _jumpSpeed;
        s.AttackElapsedTicks = 0;
        s.AnimLockTicks = _parameters.MaxFlightTicks;
    }

    private void TickJump(ref CharacterState s, CharacterDefinition def)
    {
        s.IsAiming = false;
        if (s.IsGrounded)
        {
            StartSlam(ref s);
            return;
        }

        if (_phaseTicks >= _parameters.MaxFlightTicks)
        {
            EndAbility(ref s);
            return;
        }

        s.FacingYaw = _aimYaw;
        s.VX = MathF.Sin(_aimYaw) * _jumpSpeed;
        s.VZ = MathF.Cos(_aimYaw) * _jumpSpeed;
    }

    private void StartSlam(ref CharacterState s)
    {
        _phase = Phase.Slam;
        _phaseTicks = 0;
        s.State = ActionState.Attacking;
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        s.AnimLockTicks = _parameters.SlamDurationTicks;
        SpawnSlam(ref s);
    }

    private void TickSlam(ref CharacterState s)
    {
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
        if (_phaseTicks >= _parameters.SlamDurationTicks)
            EndAbility(ref s);
    }

    private void SpawnSlam(ref CharacterState s)
    {
        if (_slamSpawned)
            return;
        _slamSpawned = true;
        SpawnHitbox(ref s, new HitboxEvent
        {
            Shape = HitboxShape.Capsule,
            Radius = _parameters.SlamRadius,
            BoneName = "_weapon_hilt",
            EndBoneName = "_weapon_tip",
            Damage = _parameters.SlamDamage,
            Knockback = new KnockbackData
            {
                Profile = KnockbackProfile.Custom,
                Angle = (sbyte)_parameters.SlamAngle,
                BaseKnockback = _parameters.SlamBaseKnockback,
                KnockbackGrowth = _parameters.SlamKnockbackGrowth,
            },
            StunTicks = _parameters.SlamStunTicks,
            DurationTicks = _parameters.SlamDurationTicks,
            Interruptible = true,
            HitGroup = 0,
        });
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }

    public override void OnCancel(ref CharacterState s)
    {
        s.IsAiming = false;
        s.VX = 0f;
        s.VY = 0f;
        s.VZ = 0f;
    }

    private static float NormalizeYaw(float yaw)
    {
        while (yaw > MathF.PI) yaw -= 2f * MathF.PI;
        while (yaw < -MathF.PI) yaw += 2f * MathF.PI;
        return yaw;
    }
}
