using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Kistu's Q — Counter. A stationary parry with an active window.
///
/// If an incoming hit lands on Kistu while the window is open (TryCounter, called from
/// ResolveHits before damage is applied), the hit is fully absorbed and a riposte is applied
/// to the attacker: bonus %, then a launch (knockback, no lingering stun — consistent with the
/// kit's pure-knockback design). Launching the attacker flips them to Hitstun, which interrupts
/// their attack. Whiffing the window leaves Kistu in recovery.
///
/// Params: "duration", "window_start", "window_end", "riposte_damage",
/// "riposte_base", "riposte_growth", "riposte_angle", "riposte_stun".
/// </summary>
public sealed class KistuCounter : ServerAbility
{
    private ushort _ticks;
    private ushort _duration;
    private bool _countered;
    private ushort _windowStart;
    private ushort _windowEnd;
    private float _riposteDamage;
    private float _riposteBase;
    private float _riposteGrowth;
    private sbyte _riposteAngle;
    private ushort _riposteStun;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;
        _countered = false;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        // Stationary parry — no residual movement.
        SetVelocity(ref s, 0f, s.IsGrounded ? 0f : s.VY, 0f);

        _windowStart = (ushort)GetParam(def, "window_start", 4f);
        _windowEnd = (ushort)GetParam(def, "window_end", 18f);
        _duration = (ushort)GetParam(def, "duration", 40f);
        s.AnimLockTicks = _duration;
        _riposteDamage = GetParam(def, "riposte_damage", 12f);
        _riposteBase = GetParam(def, "riposte_base", 12f);
        _riposteGrowth = GetParam(def, "riposte_growth", 6f);
        _riposteAngle = (sbyte)GetParam(def, "riposte_angle", 60f);
        _riposteStun = (ushort)GetParam(def, "riposte_stun", 30f);
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;
        // Riposte animation once countered, else parry stance.
        AnimIndex = _countered ? (byte)1 : (byte)0;

        if (_ticks >= _duration)
            EndAbility(ref s);
    }

    public override bool TryCounter(ref CharacterState defender, ref CharacterState attacker, float incomingDamage)
    {
        if (_countered) return false;
        if (_ticks < _windowStart || _ticks > _windowEnd) return false;

        _countered = true;

        // Push the attacker away from the defender.
        float dx = attacker.PX - defender.PX;
        float dz = attacker.PZ - defender.PZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float dirX, dirZ;
        if (dist > 0.001f) { dirX = dx / dist; dirZ = dz / dist; }
        else { dirX = MathF.Sin(defender.FacingYaw); dirZ = MathF.Cos(defender.FacingYaw); }

        attacker.DamagePercent += (ushort)_riposteDamage;
        if (attacker.DamagePercent > 999) attacker.DamagePercent = 999;
        Simulation.ApplyKnockback(ref attacker, dirX, dirZ, _riposteAngle, _riposteBase, _riposteGrowth, _riposteStun);
        attacker.HitstunTicks = _riposteStun;

        return true;
    }
}
