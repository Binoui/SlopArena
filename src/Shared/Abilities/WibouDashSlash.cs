using System;

namespace SlopArena.Shared.Abilities;

/// Wibou's R — Directional Dash Slash (aim-on-ground, release-to-dash).
///
/// Two phases:
///   Aim:   State = ActionState.Aiming — walk/run stays unlocked (SimulateTick runs
///          ProcessNormalMovement for Aiming), jump/dash/other-ability presses are
///          blocked (SimulateTick state gates). No aiming animation: the client plays
///          idle/run while in this state. The mouse rotates the aim direction
///          (input.AimYaw, camera locked client-side), and she turns to FACE it.
///   Dash:  State = ActionState.Attacking — the cooked timeline owns the
///          slow/fast/settle velocity windows and the slash hitbox. This
///          capability holds aim, commits the cached facing, and ends the dash.
///
/// The aim yaw is cached on every aim tick — on the release tick the client sends
/// camera yaw instead of the mouse aim (InputController default), so reading s.AimYaw
/// at release would snap the dash to the camera. The cache is the last mouse aim.
///
/// Damage, hitbox timing, and movement speeds are authored in the cooked R timeline.
/// </summary>
public sealed class WibouDashSlash : ServerAbility, IAimHoldCapability
{
    private readonly CookedWibouDashSlashCapabilityParameters _parameters;
    private enum Phase { Aim, Dash }

    private Phase _phase;
    private int _phaseTicks;
    private float _dashYaw;

    public WibouDashSlash(CookedWibouDashSlashCapabilityParameters parameters)
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
                StartDash(ref s);
            return;
        }

        if (_phaseTicks > _parameters.DashDurationTicks)
            EndAbility(ref s);
    }

    private void StartDash(ref CharacterState s)
    {
        _phase = Phase.Dash;
        _phaseTicks = 1;
        s.State = ActionState.Attacking;
        s.AttackElapsedTicks = 0;
        s.IsAiming = false;
        s.FacingYaw = _dashYaw;
        s.VX = 0f;
        s.VZ = 0f;
        AnimIndex = 0;

        ushort dashDuration = _parameters.DashDurationTicks;
        // Keep the lock one tick longer than the dash so the final velocity tick cannot be IASA-cancelled.
        s.AnimLockTicks = (ushort)(dashDuration + 1);
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
