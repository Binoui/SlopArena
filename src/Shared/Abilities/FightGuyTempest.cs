using System;

namespace SlopArena.Shared.Abilities
{
    /// <summary>
    /// FightGuy's F — Tempest (ultimate): two-stage ability.
    /// Stage 1 (windup, 8 ticks): character locked, no hitbox.
    /// Stage 2 (spin, 60 ticks): sustained pull AoE dragging enemies toward center,
    /// plus a launcher hitbox on the final tick.
    /// FightGuy is locked in place (VX=VZ=0) during the entire ability.
    /// </summary>
    public class FightGuyTempest : ServerAbility
    {
        private ushort _totalTicks;
        private ushort _windupTicks;
        private ushort _spinDuration;

        public override void OnStart(ref CharacterState s, CharacterDefinition def)
        {
            _totalTicks = 0;
            _windupTicks = (ushort)GetParam(def, "windup_ticks", 8f);
            _spinDuration = (ushort)GetParam(def, "spin_duration_ticks", 60f);

            s.State = ActionState.Attacking;
            s.AttackSlot = (byte)(Slot + 1);
            AnimIndex = 0;
            s.ComboStage = 0;
            s.AttackElapsedTicks = 0;
            s.AnimLockTicks = (ushort)(_windupTicks + _spinDuration);

            // Lock in place
            s.VX = 0f;
            s.VZ = 0f;
        }

        public override void Tick(ref CharacterState s, ref InputState input, CharacterDefinition def)
        {
            _totalTicks++;

            // Lock in place every tick
            s.VX = 0f;
            s.VZ = 0f;

            // ── Stage 1: Windup (no hitbox) ──
            if (_totalTicks <= _windupTicks)
            {
                return;
            }

            // ── Stage 2: Spin / Pull ──
            ushort spinElapsed = (ushort)(_totalTicks - _windupTicks);
            AnimIndex = 1;

            // Pull enemies toward center every N ticks
            ushort pullInterval = (ushort)GetParam(def, "pull_interval_ticks", 10f);
            float pullRadius = GetParam(def, "pull_radius", 3.5f);
            float pullForce = GetParam(def, "pull_force", 3f);

            if (spinElapsed % pullInterval == 0 && SimulationStates != null)
            {
                foreach (var kvp in SimulationStates)
                {
                    ulong otherId = kvp.Key;
                    var other = kvp.Value;
                    if (otherId == s.EntityId) continue;

                    float dist = CombatMath.HorizontalDistance(s.PX, s.PZ, other.PX, other.PZ);
                    if (dist <= pullRadius)
                    {
                        // Pull toward FightGuy (calculate direction from NPC to FightGuy)
                        CombatMath.CalculateKnockback(s.PX, s.PZ, other.PX, other.PZ,
                            pullForce, 0, out float kx, out float ky, out float kz);
                        other.VX += kx;
                        other.VZ += kz;
                        SimulationStates[otherId] = other;
                    }
                }
            }
            // Last tick of spin: spawn launcher hitbox
            if (spinElapsed == _spinDuration)
            {
                float launcherDamage = GetParam(def, "launcher_damage", 8f);
                float launcherKBBase = GetParam(def, "launcher_kb_base", 4.8f);
                float launcherKBGrowth = GetParam(def, "launcher_kb_growth", 7.2f);
                float launcherUp = GetParam(def, "launcher_knockback_up", 18f);
                ushort launcherStun = (ushort)GetParam(def, "launcher_stun_ticks", 18f);

                Resolver.Spawn(new Hitbox
                {
                    X = s.PX, Y = s.PY + 0.5f, Z = s.PZ,
                    Radius = pullRadius,
                    Shape = HitboxShape.Sphere,
                    DurationTicks = 4,
                    Damage = launcherDamage,
                    BaseKnockback = launcherKBBase,
                    KnockbackGrowth = launcherKBGrowth,
                    KnockbackUpward = launcherUp,
                    StunTicks = launcherStun,
                    OwnerId = s.EntityId,
                });

                EndAbility(ref s);
            }
        }
    }
}
