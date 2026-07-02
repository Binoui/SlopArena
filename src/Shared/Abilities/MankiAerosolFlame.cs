using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's RMB: hold-to-charge flamethrower cone.
    /// Tap = quick burst, hold 45 ticks = charged version (longer range, more damage).
    /// Duration is tracked via a private _duration field (constant) to avoid the
    /// AttackElapsedTicks-vs-AnimLockTicks counter mismatch bug.
    /// </summary>
    public sealed class MankiAerosolFlame : ServerAbility
    {
        private bool _charged;
        private ushort _chargeTicks;
        private bool _hitboxSpawned;
        private ushort _duration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _charged = false;
            _chargeTicks = 0;
            _hitboxSpawned = false;

            ushort chargeThreshold = (ushort)GetParam(def, "charge_threshold", 45f);

            if (s.ChargeTicks >= chargeThreshold)
                _charged = true;

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = (byte)(_charged ? 1 : 0);
            _duration = (ushort)GetParam(def, _charged ? "charged_duration" : "normal_duration", 50f);
            s.AnimLockTicks = _duration;
            s.ChargeTicks = 0;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort triggerTick = (ushort)GetParam(def, _charged ? "charged_trigger_tick" : "normal_trigger_tick", 10f);

            if (!_hitboxSpawned && s.AttackElapsedTicks >= triggerTick)
            {
                _hitboxSpawned = true;

                float offZ = GetParam(def, _charged ? "charged_off_z" : "normal_off_z", 2.5f);
                float endOffZ = GetParam(def, _charged ? "charged_end_off_z" : "normal_end_off_z", 4.0f);
                float radius = GetParam(def, _charged ? "charged_radius" : "normal_radius", 1.0f);
                ushort hbDuration = (ushort)GetParam(def, _charged ? "charged_hitbox_duration" : "normal_hitbox_duration", 30f);
                float kbBase = GetParam(def, _charged ? "charged_kb_base" : "normal_kb_base", 9.6f);
                float kbGrowth = GetParam(def, _charged ? "charged_kb_growth" : "normal_kb_growth", 14.4f);

                SpawnHitbox(ref s, new HitboxEvent
                {
                    TriggerTick = triggerTick,
                    DurationTicks = hbDuration,
                    Shape = HitboxShape.Capsule,
                    Radius = radius,
                    OffX = 0,
                    OffY = 1.0f,
                    OffZ = offZ,
                    EndOffX = 0,
                    EndOffY = 0,
                    EndOffZ = endOffZ - offZ,
                    Damage = GetParam(def, _charged ? "charged_damage" : "normal_damage", 14f),
                    BaseKnockback = kbBase,
                    KnockbackGrowth = kbGrowth,
                    KnockbackUpward = GetParam(def, _charged ? "charged_knockback_up" : "normal_knockback_up", 8f),
                    StunTicks = (ushort)GetParam(def, _charged ? "charged_stun" : "normal_stun", 20f),
                    Interruptible = true,
                });
            }

            // Use stored _duration (constant) instead of s.AnimLockTicks
            // which TickTimers decrements each frame (opposite direction).
            if (s.AttackElapsedTicks >= _duration)
                EndAbility(ref s);
        }
    }
}
