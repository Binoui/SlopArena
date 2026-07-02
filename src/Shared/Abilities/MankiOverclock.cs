using System;

namespace SlopArena.Shared.Abilities;

/// <summary>
/// Manki's F (slot 5): Overclock — self-buff for 8 seconds.
/// Runs for 30 ticks (injection animation), then ends.
/// Buff state persists on CharacterState.BuffRemainingTicks + BuffActiveFlags,
/// ticked down by Simulation.TickTimers.
/// While active: attacks gain +3 damage and +0.5 radius (via ServerAbility.ApplyBuffBonuses).
/// </summary>
    public sealed class MankiOverclock : ServerAbility
    {
        private ushort _totalDuration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            ushort duration = (ushort)GetParam(def, "duration_ticks", 480f);

            s.BuffActiveFlags |= (byte)BuffType.Overclock;
            s.BuffRemainingTicks = duration;
            _totalDuration = 30;  // injection animation lock
            s.AnimLockTicks = _totalDuration;
            AnimIndex = 0;
            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            if (s.AttackElapsedTicks >= _totalDuration)
                EndAbility(ref s);
        }
    }
