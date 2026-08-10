using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Shared base for hold-to-aim, release-to-fire abilities (AbilityBehavior.AimedProjectile
    /// family: Manki Q/E/R, FightGuy Q, Nilus Q). Owns the whole hold lifecycle so the
    /// per-ability classes are just projectile spawners:
    ///
    ///   - OnStart: Aiming state (hold = aim stance), IsAiming, AnimLockTicks,
    ///     ChargeTicks reset, and the aerial ascent-stop. ServerSimulation.ActivateAbility
    ///     only cancels DOWNWARD VY and re-opens the zero-g float window (AirTimeTicks=0),
    ///     so without zeroing VY here an aim cast mid-jump-rise would climb through the
    ///     float — same fix as AirChargeAttack.OnChargeStart applies to the charge family.
    ///     Simulation's movement gate treats Aiming + AimedProjectile/Projectile behavior
    ///     as a fixed stance (friction only, no input control).
    ///   - Aim phase: 8-tick debounce, optional mid-hold anim swap, manual release
    ///     (!IsAiming) or auto-release at the hold cap. The cap reads the SPEC's
    ///     ChargeHoldTicks — the same field Simulation.cs clamps s.ChargeTicks
    ///     against — so the auto-release can't drift from the clamp (see the original
    ///     NilusVoidRift note for why a Param would).
    ///   - Throw phase: fire once at throw_trigger_tick (IsAiming=false) in the
    ///     Attacking state (action phase, mirroring KistuDashSlash's Aiming → Attacking
    ///     dash transition), end at throw_duration.
    ///
    /// Subclasses only implement the hooks. MankiBazooka / MankiGrapple keep their own
    /// three-phase FSMs (firing/recovery and reeling don't fit this shape); KistuDashSlash
    /// is an aim-to-dash, not a projectile, and stays standalone — both carry the same
    /// ascent-stop inline.
    /// </summary>
    public abstract class AimHoldAbility : ServerAbility
    {
        private bool _fired;

        /// <summary>Max hold ticks before auto-release. Spec ChargeHoldTicks (see class doc).</summary>
        protected virtual ushort GetMaxHoldTicks(CharacterDefinition def)
            => def.GetSlotAbility(Slot, airborne: false)?.ChargeHoldTicks ?? 180;

        /// <summary>AnimLockTicks during the aim hold.</summary>
        protected virtual ushort GetHoldLockTicks(CharacterDefinition def) => 8;

        /// <summary>AnimIndex to swap to after the 8-tick debounce while holding, or -1 for none.</summary>
        protected virtual int GetMidHoldAnimIndex(CharacterDefinition def) => -1;

        /// <summary>AnimIndex for the throw phase (set on release).</summary>
        protected virtual byte GetReleaseAnimIndex(CharacterDefinition def) => 1;

        /// <summary>Called at the very start (after boilerplate). Reset per-cast fields here.</summary>
        protected virtual void OnAimStart(ref CharacterState s, CharacterDefinition def) { }

        /// <summary>Called once when the hold releases. Cache aim (yaw/pitch/distance) here.</summary>
        protected virtual void OnRelease(ref CharacterState s, CharacterDefinition def) { }

        /// <summary>Called once at throw_trigger_tick to spawn the projectile.</summary>
        protected virtual void OnFire(ref CharacterState s, CharacterDefinition def) { }

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _fired = false;

            // Hold = aim stance: Simulation gates movement (fixed-stance friction only)
            // and charge clamping on Aiming + AimedProjectile behavior.
            s.State = ActionState.Aiming;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.IsAiming = true;
            s.AnimLockTicks = GetHoldLockTicks(def);
            s.ChargeTicks = 0;

            OnAimStart(ref s, def);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort maxHoldTicks = GetMaxHoldTicks(def);

            // ── Aim phase ──
            if (s.ComboStage == 0)
            {
                if (s.AttackElapsedTicks > 8)
                {
                    int mid = GetMidHoldAnimIndex(def);
                    if (mid >= 0 && AnimIndex != mid)
                        AnimIndex = (byte)mid;
                }

                bool released = !input.IsAiming || (maxHoldTicks > 0 && s.ChargeTicks >= maxHoldTicks);
                if (s.AttackElapsedTicks > 8 && released)
                {
                    OnRelease(ref s, def);
                    // Throw phase is an action phase — re-enter Attacking (mirrors
                    // KistuDashSlash.StartDash: Aiming → Attacking).
                    s.State = ActionState.Attacking;
                    s.ComboStage = 1;
                    AnimIndex = GetReleaseAnimIndex(def);
                    s.AttackElapsedTicks = 0;
                }
                return;
            }

            // ── Throw phase ──
            ushort throwTick = (ushort)GetParam(def, "throw_trigger_tick", 10f);
            if (!_fired && s.AttackElapsedTicks >= throwTick)
            {
                _fired = true;
                s.IsAiming = false;
                OnFire(ref s, def);
            }

            if (s.AttackElapsedTicks >= (ushort)GetParam(def, "throw_duration", 60f))
                EndAbility(ref s);
        }
    }
}
