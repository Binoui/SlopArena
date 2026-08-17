using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Deterministic heuristic bot policy v1 (issue #148): approach the opponent, attack when the
/// opponent is within the bot's (seeded-jittered) perceived range, pick from the move set with
/// seeded variation, back off after each swing and after being hit so fights develop spacing,
/// and jump toward an airborne opponent. The seeded jitter makes the bot sometimes attack at
/// the edge of its reach (whiffs) and use a mix of moves — without it, two identical bots
/// trading at guaranteed-connect range produce degenerate telemetry (100% hit rate, no KOs).
///
/// Attack reach is the move's ACTUAL hitbox reach (OffX/OffZ extent + radius + lunge), NOT the
/// authored <c>AttackRange</c> (the sim's auto-dash engage distance, far beyond where the hitbox
/// connects). The sim normalizes <c>MoveX/Y</c> (no analog easing), so the policy approaches
/// outright rather than trying to slow precisely.
///
/// The sim consumes <see cref="InputState.MoveX"/>/<c>MoveY</c> as WORLD-SPACE X/Z and auto-faces
/// on movement input (<c>Atan2</c>); the policy also snaps facing via <c>AimYaw</c> +
/// <c>FaceToCamera</c> (ADR-0017).
///
/// Invariants: <c>MoveX/MoveY</c> magnitude ≤ 1; no action input while
/// hitstun/hitstop/burst-recovery/landing-lag/anim-lock; no dash on cooldown; no slot press on
/// cooldown. Same <c>Random</c> stream → same decisions.
/// </summary>
public sealed class HeuristicBotPolicy : IBotPolicy
{
    /// <summary>Ground-normal priority pool — ActiveSlot bytes for kit slots 1–4.</summary>
    private static readonly byte[] Slots =
    {
        AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
    };

    /// <summary>Jump toward an airborne opponent once they are this far above the bot (metres).</summary>
    private const float JumpGap = 0.6f;

    /// <summary>Ticks to wait after an attack press before considering the next (anti-buffer spam).</summary>
    private const int PressCooldown = 6;

    /// <summary>Ticks the bot backs off after each swing — the disengage that keeps fights mobile.</summary>
    private const int PostAttackBackoff = 14;

    /// <summary>Ticks the bot backs off after being hit (longer — reset neutral after taking a trade).</summary>
    private const int HitBackoff = 26;

    /// <summary>Victim hurtbox radius added to the hitbox reach to get the connect distance. Empirically
    /// ~0.2 m for the FightGuy hurtboxes (g1/g2/g4 connect at 0.7 m, ForwardReach 0.56–0.61).</summary>
    private const float VictimRadiusMargin = 0.2f;

    /// <summary>Perceived-range jitter band: the bot attacks when dist ≤ base × (MinJitter..MaxJitter),
    /// so it sometimes commits slightly out of reach (whiff) and sometimes cleanly.</summary>
    private const float RangeJitterMin = 1.0f;
    private const float RangeJitterMax = 1.6f;

    /// <summary>Choose a slot only if its connect reach is at least this fraction of the distance —
    /// looser than exact, so near-misses (whiffs) are possible.</summary>
    private const float SlotReachTolerance = 0.85f;

    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory)
    {
        var input = new InputState();
        if (memory.PressCooldownTicks > 0) memory.PressCooldownTicks--;
        if (memory.RepositionTicks > 0) memory.RepositionTicks--;

        bool inAttack = self.AttackSlot > 0;

        // Rising edge of hitstun → reset spacing (the bot just took a hit).
        if (self.HitstunTicks > 0 && !memory.WasInHitstun)
            memory.RepositionTicks = Math.Max(memory.RepositionTicks, HitBackoff);
        memory.WasInHitstun = self.HitstunTicks > 0;

        // Attack ended this tick (AttackSlot just dropped to 0) → the bot is actionable again;
        // force a backoff so it steps out before re-engaging instead of chaining into a deadlock.
        if (memory.WasAttacking && !inAttack)
            memory.RepositionTicks = Math.Max(memory.RepositionTicks, PostAttackBackoff);
        memory.WasAttacking = inAttack;

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float dy = target.PY - self.PY;

        bool moving = self.HitstunTicks == 0 && self.HitstopTicks == 0
            && self.BurstRecoveryTicks == 0 && self.LandingLagTicks == 0;
        bool actionable = moving && self.AnimLockTicks == 0
            && (self.State == ActionState.Idle || self.State == ActionState.Run);

        // ── Disengage: back off for the reposition window (no attacks, face away) ──
        if (actionable && memory.RepositionTicks > 0 && dist > 0.001f)
        {
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            return input;
        }

        // ── Face the opponent (reuse the game's LMB facing snap) ──
        if (moving && dist > 0.001f)
        {
            input.AimYaw = (short)Math.Clamp(MathF.Atan2(dx, dz) * (180f / MathF.PI) * 100f, -32768f, 32767f);
            input.FaceToCamera = true;
        }

        // ── Approach (world space; MoveY IS the world Z axis) ──
        if (moving && dist > 0.001f)
        {
            input.MoveX = dx / dist;
            input.MoveY = dz / dist;
        }

        // ── Attack when the opponent is within the bot's jittered perceived range ──
        if (actionable && memory.PressCooldownTicks == 0 && memory.RepositionTicks == 0 && dist > 0.001f)
        {
            float baseReach = MaxConnectReach(self, def);
            float perceived = baseReach * (RangeJitterMin + (RangeJitterMax - RangeJitterMin) * (float)rng.NextDouble());
            if (dist <= perceived)
            {
                byte? slot = ChooseSlot(self, def, dist, rng);
                if (slot.HasValue)
                {
                    input.ActiveSlot = slot.Value;
                    memory.PressCooldownTicks = PressCooldown;
                    memory.RepositionTicks = PostAttackBackoff;
                    return input;
                }
            }
        }

        // ── Jump toward an airborne opponent above the bot ──
        if (actionable && memory.RepositionTicks == 0 && self.IsGrounded && !target.IsGrounded && dy > JumpGap)
        {
            input.Jump = true;
            input.JumpHeld = true;
        }

        return input;
    }

    /// <summary>Max connect range across the slots for the current state (metres).</summary>
    private static float MaxConnectReach(in CharacterState self, CharacterDefinition def)
    {
        bool air = !self.IsGrounded;
        float max = 0f;
        foreach (byte slot in Slots)
            max = Math.Max(max, ForwardReach(def, slot, air) + VictimRadiusMargin);
        return max;
    }

    /// <summary>Pick a slot whose connect reach is within tolerance of the distance — seeded choice
    /// among the viable pool (near-misses allowed, so whiffs occur). Skips cooldown / data-less slots.</summary>
    private static byte? ChooseSlot(in CharacterState self, CharacterDefinition def, float dist, Random rng)
    {
        bool air = !self.IsGrounded;
        var viable = new byte[Slots.Length];
        int n = 0;
        foreach (byte slot in Slots)
        {
            if (self.GetCooldown(slot) > 0) continue;
            float reach = ForwardReach(def, slot, air) + VictimRadiusMargin;
            if (reach > 0f && reach >= dist * SlotReachTolerance)
                viable[n++] = slot;
        }
        if (n == 0) return null;
        return viable[rng.Next(n)];
    }

    /// <summary>
    /// Forward reach of a slot's hitboxes in the facing frame (metres): max forward extent
    /// (OffX/OffZ distance + radius) over the first-stage hitboxes, plus lunge travel. This is
    /// the ACTUAL hitbox reach — the connect distance is this plus the victim's hurtbox radius.
    /// Deliberately NOT the authored <c>AttackRange</c>. 0 when the slot has no data for the given
    /// airborne state. Public for the reach-envelope tests.
    /// </summary>
    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne)
    {
        var spec = def.GetSlotAbility(activeSlot - 1, airborne);
        if (spec == null || spec.Stages == null || spec.Stages.Length == 0) return 0f;
        var stage = spec.Stages[0];

        float reach = 0f;
        if (stage.HitboxEvents != null)
        {
            foreach (var evt in stage.HitboxEvents)
            {
                // Forward reach = furthest point along the forward (+Z) axis: OffZ + Radius.
                // NOT hypot(OffX,OffZ)+Radius — that counts sideways offsets (large OffX) as
                // forward reach, so a side-kick like a3 High Kick (OffX=0.43, OffZ=-0.16) was
                // reported as 0.86 m when it barely reaches 0.24 m forward.
                float extent = MathF.Max(0f, evt.OffZ + evt.Radius);
                if (extent > reach) reach = extent;
            }
        }
        if (stage.LungeForce > 0f)
            reach += stage.LungeForce * MathF.Min(0.3f, stage.DurationTicks / 60f);
        return reach;
    }
}
