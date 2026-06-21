using System;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# ability execution logic. Called by Simulation.SimulateTick.
    /// Each method takes a specific AbilityData instance — no slot→ability switches.
    /// </summary>
    public static class AbilityExecutor
    {
        /// <summary>
        /// Try to start an attack from the given ability.
        /// Handles both fresh attacks (comboStage=0) and combo chains (same slot, next stage).
        /// Returns true if an attack was started.
        /// </summary>
        public static bool TryStart(ref CharacterState s, AbilitySpec ability, byte slot, ushort cooldown)
        {
            if (cooldown > 0) return false;

            // Combo chain: same slot, next stage exists
            if (slot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.State = ActionState.Attacking;
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                s.ComboTimerTicks = stage.ChainWindowTicks;
                return true;
            }

            // Fresh attack
            if (ability.Stages.Length > 0)
            {
                var stage = ability.Stages[0];
                s.State = ActionState.Attacking;
                s.AnimLockTicks = stage.DurationTicks;
                s.ComboStage = 0;
                s.AttackElapsedTicks = 0;
                s.AttackSlot = slot;
                s.ComboTimerTicks = stage.ChainWindowTicks;

                // Lunge: forward burst during the attack (DKO-style)
                if (stage.LungeForce > 0f)
                {
                    s.VX = MathF.Sin(s.FacingYaw) * stage.LungeForce;
                    s.VZ = MathF.Cos(s.FacingYaw) * stage.LungeForce;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Process an active attack: check buffered/immediate chain advance,
        /// or transition back to idle when the animation lock expires.
        /// Returns true if the attack continued (chain advanced), false if it ended.
        /// </summary>
        public static bool ProcessActive(ref CharacterState s, AbilitySpec ability, ref InputState input)
        {
            // Apply per-stage movement velocity (backflip, jump arcs, etc.)
            if (s.ComboStage < ability.Stages.Length)
            {
                var currentStage = ability.Stages[s.ComboStage];
                if (currentStage.MoveX != 0f || currentStage.MoveY != 0f || currentStage.MoveZ != 0f)
                {
                    s.VX = currentStage.MoveX;
                    s.VY = currentStage.MoveY;
                    s.VZ = currentStage.MoveZ;
                }
            }

            if (s.AnimLockTicks > 0) return true; // still locked, nothing to do

            // 1. Buffered chain (click buffered during lock, set by CanBuffer check in Simulation)
            if (s.BufferedSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                s.BufferedSlot = 0;
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                s.ComboTimerTicks = stage.ChainWindowTicks;
                return true;
            }

            // 2. Immediate chain (click on same frame lock expired)
            if (input.ActiveSlot == s.AttackSlot && s.ComboStage < ability.Stages.Length - 1)
            {
                input.ActiveSlot = 0; // consumed
                s.ComboStage++;
                var stage = ability.Stages[s.ComboStage];
                s.AnimLockTicks = stage.DurationTicks;
                s.AttackElapsedTicks = 0;
                s.ComboTimerTicks = stage.ChainWindowTicks;
                return true;
            }

            // 3. No chaining → back to idle
            s.State = ActionState.Idle;
            s.ComboStage = 0;
            s.AttackSlot = 0;
            s.AttackElapsedTicks = 0;
            return false;
        }

        /// <summary>
        /// Check if an input can be buffered for this ability during an active attack.
        /// Returns true if the input should be stored in BufferedSlot.
        /// </summary>
        public static bool CanBufferCombo(CharacterState s, AbilitySpec ability, ushort cooldown)
        {
            // Only same-slot during active attack, and there's a next stage
            if (s.State != ActionState.Attacking) return false;
            if (s.AttackElapsedTicks == 0) return false; // don't buffer the click that started the attack
            if (cooldown > 0) return false;
            return s.ComboStage < ability.Stages.Length - 1;
        }

        /// <summary>
        /// Get cooldown ticks for a slot (1-6).
        /// </summary>
        public static ushort GetCooldown(CharacterState s, byte slot) => slot switch
        {
            1 => s.Cooldown0,
            2 => s.Cooldown1,
            3 => s.Cooldown2,
            4 => s.Cooldown3,
            5 => s.Cooldown4,
            6 => s.Cooldown5,
            _ => 0,
        };

        /// <summary>
        /// Set cooldown ticks for a slot (1-6).
        /// </summary>
        public static void SetCooldown(ref CharacterState s, byte slot, ushort ticks)
        {
            switch (slot)
            {
                case 1: s.Cooldown0 = ticks; break;
                case 2: s.Cooldown1 = ticks; break;
                case 3: s.Cooldown2 = ticks; break;
                case 4: s.Cooldown3 = ticks; break;
                case 5: s.Cooldown4 = ticks; break;
                case 6: s.Cooldown5 = ticks; break;
            }
        }
    }
}
