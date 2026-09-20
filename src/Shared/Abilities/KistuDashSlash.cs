using System;

namespace SlopArena.Shared.Abilities;

/// Kistu's R — Directional Dash Slash (aim-on-ground, release-to-dash).
///
/// Two phases:
///   Aim:   State = ActionState.Aiming — walk/run stays unlocked (SimulateTick runs
///          ProcessNormalMovement for Aiming), jump/dash/other-ability presses are
///          blocked (SimulateTick state gates). No aiming animation: the client plays
///          idle/run while in this state. The mouse rotates the aim direction
///          (input.AimYaw, camera locked client-side), and she turns to FACE it.
///   Dash:  State = ActionState.Attacking — constant velocity toward the cached aim
///          yaw for dash_duration_ticks, covering exactly dash_distance meters.
///          A single bone-tracked capsule hitbox follows the slash from dash start.
///          Plays the R attack clip (AnimationNames[0] = "anim.kistu.r").
///
/// The aim yaw is cached on every aim tick — on the release tick the client sends
/// camera yaw instead of the mouse aim (InputController default), so reading s.AimYaw
/// at release would snap the dash to the camera. The cache is the last mouse aim.
///
/// All damage/knockback/timing data comes from the cooked R hitbox and capability params.
/// </summary>
public sealed class KistuDashSlash : ServerAbility, IAimHoldCapability
{
    private readonly CookedKistuDashSlashCapabilityParameters _parameters;
    private enum Phase { Aim, Dash }

    private Phase _phase;
    private int _phaseTicks;
    private float _dashYaw;

    public KistuDashSlash(CookedKistuDashSlashCapabilityParameters parameters)
        => _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Aim;
        _phaseTicks = 0;
        _dashYaw = s.AimYaw;

        s.State = ActionState.Aiming;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;
        s.IsAiming = true;
        s.StateTicks = 0;
    }


    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _phaseTicks++;

        if (_phase == Phase.Aim)
        {
            if (input.IsAiming)
            {
                _dashYaw = s.AimYaw;
                s.FacingYaw = s.AimYaw;
            }
            if (!input.IsAiming || _phaseTicks >= _parameters.MaxAimTicks)
                StartDash(ref s, def);
            return;
        }

        float speed = _parameters.DashDurationTicks > 0
            ? _parameters.DashDistance / (_parameters.DashDurationTicks * Simulation.TickDt)
            : 0f;
        s.VX = MathF.Sin(_dashYaw) * speed;
        s.VZ = MathF.Cos(_dashYaw) * speed;
        if (_phaseTicks > _parameters.DashDurationTicks)
            EndAbility(ref s);
    }

    private void StartDash(ref CharacterState s, CharacterDefinition def)
    {
        _phase = Phase.Dash;
        _phaseTicks = 1; // StartDash applies the first velocity tick immediately.

        float speed = _parameters.DashDurationTicks > 0
            ? _parameters.DashDistance / (_parameters.DashDurationTicks * Simulation.TickDt)
            : 0f;
        s.State = ActionState.Attacking;
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        s.FacingYaw = _dashYaw;
        s.VX = MathF.Sin(_dashYaw) * speed;
        s.VZ = MathF.Cos(_dashYaw) * speed;
        AnimIndex = 0;
        SpawnDashHitbox(ref s, def);

        ushort dashDuration = _parameters.DashDurationTicks;
        // Keep the lock one tick longer than the dash so the final velocity tick cannot be IASA-cancelled.
        s.AnimLockTicks = (ushort)(dashDuration + 1);
    }
    


    /// <summary>Spawn the authored bone-tracked slash at dash start.</summary>
    private void SpawnDashHitbox(ref CharacterState s, CharacterDefinition def)
    {
        HitboxEvent evt = default;
        bool found = false;
        var cooked = def.GetCookedSlotAbility((byte)(Slot + 1), airborne: false);
        if (cooked != null)
        {
            foreach (var operation in cooked.Timeline.Stages[0].Operations)
            {
                if (operation is not CookedSpawnHitboxOperation spawn)
                    continue;
                var hitbox = spawn.Hitbox;
                evt = new HitboxEvent
                {
                    Shape = hitbox.Shape == AuthoringHitboxShape.Capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
                    Radius = hitbox.Radius,
                    OffX = hitbox.OffsetX,
                    OffY = hitbox.OffsetY,
                    OffZ = hitbox.OffsetZ,
                    EndOffX = hitbox.EndOffsetX,
                    EndOffY = hitbox.EndOffsetY,
                    EndOffZ = hitbox.EndOffsetZ,
                    BoneName = hitbox.StartBoneId,
                    EndBoneName = hitbox.EndBoneId,
                    Damage = hitbox.Damage,
                    Knockback = new KnockbackData
                    {
                        Profile = KnockbackProfile.Custom,
                        Angle = (sbyte)hitbox.Angle,
                        BaseKnockback = hitbox.BaseKnockback,
                        KnockbackGrowth = hitbox.KnockbackGrowth,
                    },
                    StunTicks = hitbox.StunTicks,
                    DurationTicks = hitbox.DurationTicks,
                    Interruptible = hitbox.Interruptible,
                    HitGroup = hitbox.HitGroup,
                    KnockbackDirection = hitbox.KnockbackDirection,
                };
                found = true;
                break;
            }
        }
        if (!found)
        {
            evt = new HitboxEvent
            {
                Shape = HitboxShape.Capsule,
                Radius = 0.5f,
                BoneName = "_weapon_hilt",
                EndBoneName = "_weapon_tip",
                Damage = 1f,
                Knockback = new KnockbackData
                {
                    Profile = KnockbackProfile.Custom,
                    Angle = 45,
                    BaseKnockback = 5f,
                    KnockbackGrowth = 80f,
                },
                StunTicks = 8,
                DurationTicks = 10,
                Interruptible = false,
            };
        }
        SpawnHitbox(ref s, evt);
    }

    /// <summary>
    /// Precise landing (issue #115 carve-out): the dash-slash is a REPOSITION move whose
    /// authored endpoint is the aim distance — like a normal dash, which stops exactly at
    /// expiry (ProcessDash). Momentum-preserve coasts ATTACKS; movement tech lands exactly.
    /// </summary>
    public override void OnEnd(ref CharacterState s)
    {
        s.VX = 0f;
        s.VZ = 0f;
    }
    public override void OnCancel(ref CharacterState s)
    {
        s.IsAiming = false;
        s.VX = 0f;
        s.VZ = 0f;
    }
}
