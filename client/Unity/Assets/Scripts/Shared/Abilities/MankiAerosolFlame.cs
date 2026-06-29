using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// Manki's RMB: hold-to-charge flamethrower cone.
    /// Tap = quick burst, hold 45 ticks = charged version (longer range, more damage).
    /// </summary>
    public sealed class MankiAerosolFlame : ServerAbility
    {
        private bool _charged;
        private ushort _chargeTicks;
        private bool _hitboxSpawned;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _charged = false;
            _chargeTicks = 0;
            _hitboxSpawned = false;

            ushort chargeThreshold = (ushort)GetParam(def, "charge_threshold", 45f);

            // Check if player held long enough to charge
            if (s.ChargeTicks >= chargeThreshold)
            {
                _charged = true;
            }

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            s.AnimIndex = (byte)(_charged ? 1 : 0); // charged anim vs normal anim
            s.AnimLockTicks = (ushort)GetParam(def, _charged ? "charged_duration" : "normal_duration", 50f);
            s.ChargeTicks = 0; // reset charge accumulator
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            ushort triggerTick = (ushort)GetParam(def, _charged ? "charged_trigger_tick" : "normal_trigger_tick", 10f);

            // Spawn hitbox at trigger tick
            if (!_hitboxSpawned && s.AttackElapsedTicks >= triggerTick)
            {
                _hitboxSpawned = true;

                // Capsule hitbox in front of character
                float offZ = GetParam(def, _charged ? "charged_off_z" : "normal_off_z", 2.5f);
                float endOffZ = GetParam(def, _charged ? "charged_end_off_z" : "normal_end_off_z", 4.0f);
                float radius = GetParam(def, _charged ? "charged_radius" : "normal_radius", 1.0f);
                ushort duration = (ushort)GetParam(def, _charged ? "charged_hitbox_duration" : "normal_hitbox_duration", 30f);

                SpawnHitbox(ref s, new HitboxEvent
                {
                    TriggerTick = triggerTick,
                    DurationTicks = duration,
                    Shape = HitboxShape.Capsule,
                    Radius = radius,
                    OffX = 0,
                    OffY = 1.0f,
                    OffZ = offZ,
                    EndOffX = 0,
                    EndOffY = 0,
                    EndOffZ = endOffZ - offZ, // relative to start
                    Damage = GetParam(def, _charged ? "charged_damage" : "normal_damage", 14f),
                    KnockbackForce = GetParam(def, _charged ? "charged_knockback" : "normal_knockback", 24f),
                    KnockbackUpward = GetParam(def, _charged ? "charged_knockback_up" : "normal_knockback_up", 8f),
                    StunTicks = (ushort)GetParam(def, _charged ? "charged_stun" : "normal_stun", 20f),
                    Interruptible = true,
                });
            }

            // End ability when animation lock expires
            if (s.AttackElapsedTicks >= s.AnimLockTicks)
                EndAbility(ref s);
        }
    }
}
