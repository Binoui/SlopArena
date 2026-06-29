using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Reusable base class for the "go up → aim at ground → resolve" pattern.
/// Handles the two-phase lifecycle (Aiming stall + Resolving plunge).
/// Subclasses override Resolve() to define the ground impact effect.
/// </summary>
public abstract class AimedGroundAbility : ServerAbility
{
    private enum Phase { Aiming, Resolving }
    private Phase _phase = Phase.Aiming;
    private float _plungeVX, _plungeVZ;
    private ushort _aimWindowTicks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Aiming;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        s.AnimIndex = 0;
        s.IsAiming = true;

        float riseVelocity = GetParam(def, "rise_velocity", 12f);
        _aimWindowTicks = (ushort)GetParam(def, "aim_window_ticks", 30f);
        s.AnimLockTicks = _aimWindowTicks;

        // Launch upward (0 forward speed, positive vertical)
        SetVelocityInFacing(ref s, 0f, riseVelocity);
    }

    public override void OnEnd(ref CharacterState s)
    {
        s.IsAiming = false;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        switch (_phase)
        {
            case Phase.Aiming:
                TickAiming(ref s, def);
                break;
            case Phase.Resolving:
                TickResolving(ref s, def);
                break;
        }
    }

    private void TickAiming(ref CharacterState s, CharacterDefinition def)
    {
        s.IsAiming = true; // maintain aim indicator (SimulateTick overrides from input otherwise)

        // Transition to resolving phase when aim window expires
        if (s.AttackElapsedTicks >= _aimWindowTicks)
        {
            _phase = Phase.Resolving;
            s.IsAiming = false;

            // Compute ground target position from aim input
            float maxRange = GetParam(def, "max_aim_range", 20f);
            float targetDist = Math.Clamp(s.AimTargetDistance, 0.5f, maxRange);
            if (s.AimTargetDistance <= 0.001f)
                targetDist = 5f; // default range if no target input

            float aimCos = MathF.Cos(s.AimYaw);
            float aimSin = MathF.Sin(s.AimYaw);
            float targetX = s.PX + targetDist * aimSin;
            float targetZ = s.PZ + targetDist * aimCos;

            // Subclass decides what happens at the target
            Resolve(ref s, def, targetX, targetZ);

            // Compute plunge velocity toward target
            float plungeSpeed = GetParam(def, "plunge_speed", 40f);
            float dx = targetX - s.PX;
            float dz = targetZ - s.PZ;
            float hDist = MathF.Sqrt((dx * dx) + (dz * dz));
            if (hDist > 0.01f)
            {
                _plungeVX = (dx / hDist) * plungeSpeed;
                _plungeVZ = (dz / hDist) * plungeSpeed;
            }

            float plungeVY = -plungeSpeed * 0.8f;
            SetVelocity(ref s, _plungeVX, plungeVY, _plungeVZ);
        }
    }

    private void TickResolving(ref CharacterState s, CharacterDefinition def)
    {
        float capsuleRadius = def.CapsuleRadius;
        if (s.IsGrounded || s.PY <= 0.3f + capsuleRadius)
        {
            EndAbility(ref s);
        }
    }

    /// <summary>
    /// Called when the aim phase ends, before the plunge begins.
    /// Subclasses should spawn hitboxes or other effects at the target position.
    /// </summary>
    protected abstract void Resolve(ref CharacterState s, CharacterDefinition def, float targetX, float targetZ);
}
