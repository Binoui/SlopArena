using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Pure C# hitbox management — no Godot dependency.
    /// Spawn hitboxes via Spawn(), then call Tick() each server tick.
    /// Handles: movement (projectiles), sphere-sphere collision, aging, knockback.
    /// </summary>
    public static class SpellResolver
    {
        private static List<Hitbox> _hitboxes = new();

        /// <summary>
        /// Result of a single hitbox-entity collision.
        /// </summary>
        public struct HitResult
        {
            public ulong TargetEntityId;
            public float Damage;
            public float KnockbackX;
            public float KnockbackY;
            public float KnockbackZ;
            public ushort StunTicks;
        }

        /// <summary>
        /// Entity data needed for hit detection.
        /// </summary>
        public struct EntityData
        {
            public ulong Id;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float Radius;
            public bool Active;
        }

        /// <summary>
        /// Spawn a new hitbox. Call this from ability code.
        /// </summary>
        public static void Spawn(Hitbox hb)
        {
            hb.Active = true;
            hb.AgeTicks = 0;
            _hitboxes.Add(hb);
        }

        /// <summary>
        /// Clear all hitboxes (e.g., on match reset).
        /// </summary>
        public static void Clear() => _hitboxes.Clear();

        /// <summary>
        /// Get a snapshot of all active hitboxes (for debug visualization).
        /// </summary>
        public static List<Hitbox> GetActiveHitboxes()
        {
            var snapshot = new List<Hitbox>(_hitboxes.Count);
            foreach (var hb in _hitboxes)
            {
                if (hb.Active) snapshot.Add(hb);
            }
            return snapshot;
        }

        /// <summary>
        /// Process one tick: move projectiles, check collisions, age hitboxes.
        /// Returns all hits this tick (one per entity, first hitbox to connect wins).
        /// Already-hit entities in this tick are skipped (no double-hit).
        /// </summary>
        public static List<HitResult> Tick(List<EntityData> entities)
        {
            var results = new List<HitResult>();
            var hitThisTick = new HashSet<ulong>();

            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active) { _hitboxes.RemoveAt(i); continue; }

                // Move projectile
                hb.X += hb.VX * Simulation.TickDt;
                hb.Y += hb.VY * Simulation.TickDt;
                hb.Z += hb.VZ * Simulation.TickDt;

                // Check collision against each entity
                foreach (var entity in entities)
                {
                    if (!entity.Active || entity.Id == hb.OwnerId) continue;
                    if (hitThisTick.Contains(entity.Id)) continue; // already hit this tick

                    float dx = entity.PosX - hb.X;
                    float dy = entity.PosY - hb.Y;
                    float dz = entity.PosZ - hb.Z;
                    float distSq = dx * dx + dy * dy + dz * dz;
                    float combinedRadius = hb.Radius + entity.Radius;

                    if (distSq <= combinedRadius * combinedRadius)
                    {
                        // Hit! Calculate knockback direction
                        float dist = MathF.Sqrt(distSq);
                        float kbX = dist > 0.001f ? (dx / dist) * hb.KnockbackForce : 0f;
                        float kbZ = dist > 0.001f ? (dz / dist) * hb.KnockbackForce : 0f;

                        results.Add(new HitResult
                        {
                            TargetEntityId = entity.Id,
                            Damage = hb.Damage,
                            KnockbackX = kbX,
                            KnockbackY = hb.KnockbackUpward,
                            KnockbackZ = kbZ,
                            StunTicks = hb.StunTicks,
                        });

                        hitThisTick.Add(entity.Id);
                        hb.Active = false; // one-hit per hitbox
                        break;
                    }
                }

                // Age / expire
                hb.AgeTicks++;
                if (hb.AgeTicks >= hb.DurationTicks || !hb.Active)
                {
                    _hitboxes.RemoveAt(i);
                }
                else
                {
                    _hitboxes[i] = hb; // write back velocity/age changes
                }
            }

            return results;
        }
    }
}
