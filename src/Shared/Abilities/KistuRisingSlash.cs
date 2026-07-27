using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Kistu's R — Rising Slash (signature). A homing rising uppercut-slash that launches.
///
/// - Rises vertically for "rise_ticks", carrying Kistu up (doubles as vertical recovery).
/// - Homes horizontally toward the nearest enemy within "homing_range" so it tracks for juggles.
/// - Spawns its launcher hitbox from the spec's Stages[0].HitboxEvents (authored in KistuData),
///   like every other slot — the launch angle/damage/knockback live in the spec, not in code.
/// - Limited by a refundable charge pool (see ServerSimulation charge-stock gate): each cast
///   spends one charge; landing a hit refunds it (OnHitEntity) so a connected juggle sustains,
///   while whiffing in empty air burns charges — capping recovery to the pool size.
///
/// Charge-pool params (read by the sim): "max_charges", "charge_regen_ticks".
/// Movement params: "rise_speed", "rise_ticks", "homing_range", "homing_speed".
/// </summary>
public sealed class KistuRisingSlash : ServerAbility
{
    private ushort _ticks;

    public override void OnStart(ref CharacterState s, CharacterDefinition def)
    {
        _ticks = 0;

        s.State = ActionState.Attacking;
        s.AttackSlot = (byte)(Slot + 1);
        AnimIndex = 0;
        s.ComboStage = 0;
        s.AttackElapsedTicks = 0;

        float riseSpeed = GetParam(def, "rise_speed", 16f);
        SetVelocity(ref s, 0f, riseSpeed, 0f);
        s.IsGrounded = false; // launch off the ground so the rise isn't clamped

        var spec = def.GetSlotAbility(Slot, airborne: false);
        s.AnimLockTicks = spec?.Stages is { Length: > 0 } ? spec.Stages[0].DurationTicks : (ushort)24;
    }

    public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
    {
        _ticks++;

        float riseSpeed = GetParam(def, "rise_speed", 16f);
        ushort riseTicks = (ushort)GetParam(def, "rise_ticks", 18f);

        // Vertical rise for the rise window, then hold (gravity resumes when the ability ends).
        s.VY = _ticks <= riseTicks ? riseSpeed : 0f;

        // Horizontal homing toward the nearest enemy in range.
        float homingRange = GetParam(def, "homing_range", 7f);
        float homingSpeed = GetParam(def, "homing_speed", 10f);
        ulong closest = FindClosestEnemy(ref s, homingRange, out float cdx, out float cdz, out float cdist);
        if (closest != 0 && cdist > 0.1f)
        {
            s.VX = (cdx / cdist) * homingSpeed;
            s.VZ = (cdz / cdist) * homingSpeed;
        }
        else
        {
            s.VX = 0f;
            s.VZ = 0f;
        }

        // Spawn the launcher hitbox from the spec (authored in KistuData).
        var spec = def.GetSlotAbility(Slot, airborne: false);
        if (spec?.Stages is { Length: > 0 })
        {
            foreach (var evt in spec.Stages[0].HitboxEvents)
            {
                if (evt.TriggerTick == _ticks)
                    SpawnHitbox(ref s, evt);
            }
        }

        if (_ticks >= s.AnimLockTicks)
            EndAbility(ref s);
    }

    /// <summary>Refund the spent charge on a connect so a landed juggle keeps its charges.</summary>
    public override void OnHitEntity(ref CharacterState attacker, ref CharacterState target,
        CharacterDefinition attackerDef, ref float damage, ref float knockbackForce)
    {
        if (attacker.ChargeStockSpent > 0)
        {
            attacker.ChargeStockSpent--;
            // Refunding to a full pool stops regen; clear the stale timer so the NEXT spend
            // starts a fresh full regen period rather than reusing the partial countdown.
            if (attacker.ChargeStockSpent == 0)
                attacker.ChargeStockRegenTicks = 0;
        }
    }

    private ulong FindClosestEnemy(ref CharacterState s, float range, out float dx, out float dz, out float dist)
    {
        dx = 0f; dz = 0f; dist = 0f;
        if (SimulationStates == null) return 0;

        ulong best = 0;
        float bestSq = range * range;
        foreach (var kvp in SimulationStates)
        {
            if (kvp.Key == s.EntityId) continue;
            float ex = kvp.Value.PX - s.PX;
            float ez = kvp.Value.PZ - s.PZ;
            float sq = ex * ex + ez * ez;
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = kvp.Key;
                dx = ex; dz = ez;
            }
        }
        if (best != 0) dist = MathF.Sqrt(dx * dx + dz * dz);
        return best;
    }
}
