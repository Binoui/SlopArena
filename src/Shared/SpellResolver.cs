using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
    /// <summary>
    /// Interface for abilities to register hitboxes without knowing about
    /// hit detection internals.
    /// </summary>
    public interface ISpellResolver
    {
        void Spawn(Hitbox hb);
    }

    /// <summary>
    /// Pure C# hitbox management — no Godot dependency.
    /// Spawn hitboxes via Spawn(), then call Tick() each server tick.
    /// Handles: movement (projectiles), sphere-sphere collision, aging, knockback.
    /// </summary>
    public class SpellResolver : ISpellResolver
    {
        private readonly List<Hitbox> _hitboxes = new();

        /// <summary>
        /// Projectile deactivation events this tick (for explosion spawning).
        /// Position is the last known position before deactivation.
        /// </summary>
        private readonly List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId)> _pendingExplosions = new();

        /// <summary>
        /// Result of a single hitbox-entity collision.
        /// </summary>
        public struct HitResult
        {
            public ulong TargetEntityId;
            public ulong OwnerEntityId;
            public float Damage;
            public float KnockbackX;
            public float KnockbackY;
            public float KnockbackZ;
            public ushort StunTicks;
        }

        /// <summary>
        /// Entity data needed for hit detection.
        /// Supports Sphere and Capsule shapes.
        /// </summary>
        public struct EntityData
        {
            public ulong Id;
            public float PosX, PosY, PosZ;
            public float Radius;
            /// <summary>
            /// Sphere or Capsule
            /// </summary>
            public HitboxShape Shape;
            /// <summary>
            /// Capsule end (0 = sphere)
            /// </summary>
            public float EndX, EndY, EndZ;
            public bool Active;
        }

        /// <summary>
        /// Spawn a new hitbox. Call this from ability code.
        /// </summary>
        public void Spawn(Hitbox hb)
        {
            hb.Active = true;
            hb.AgeTicks = 0;
            _hitboxes.Add(hb);
        }

        /// <summary>
        /// Remove an active hitbox owned by ownerId that matches the predicate,
        /// queue its explosion (if any), and return true. Useful for manual
        /// detonation of deployable abilities (mines, traps, etc.).
        /// </summary>
        public bool RemoveHitbox(ulong ownerId, Func<Hitbox, bool> predicate)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || hb.OwnerId != ownerId) continue;
                if (!predicate(hb)) continue;

                if (hb.Explosion.HasValue)
                    _pendingExplosions.Add((hb.X, hb.Y, hb.Z, hb.Explosion.Value, hb.OwnerId));
                _hitboxes.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clear all hitboxes (e.g., on match reset).
        /// </summary>
        public void Clear() => _hitboxes.Clear();

        /// <summary>
        /// Drain and return all pending explosion events from this tick.
        /// Call after Tick() to spawn explosion hitboxes at the returned positions.
        /// </summary>
        public List<(float x, float y, float z, ProjectileExplosion explosion, ulong ownerId)> DrainPendingExplosions()
        {
            var result = new List<(float, float, float, ProjectileExplosion, ulong)>(_pendingExplosions);
            _pendingExplosions.Clear();
            return result;
        }

        /// <summary>
        /// Check remaining active projectiles for ground collision.
        /// Samples the arena heightmap at each projectile's XZ position.
        /// If the projectile has reached or crossed the terrain surface,
        /// spawns the explosion at ground level and deactivates the projectile.
        /// </summary>
        public void CheckGroundCollision(ArenaDefinition arena)
        {
            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active || !hb.Explosion.HasValue || hb.Gravity <= 0f) continue;

                // Sample ground height at projectile's current XZ
                float groundY = arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0
                    ? arena.Heightmap.Sample(hb.X, hb.Z)
                    : 0f;
                // Fallback for invalid sample (outside heightmap bounds)
                if (groundY == float.MinValue) groundY = 0f;

                if (hb.Y - hb.Radius > groundY) continue;

                // Ground contact: queue explosion at ground level, deactivate
                var exp = hb.Explosion.Value;
                _pendingExplosions.Add((hb.X, groundY, hb.Z, exp, hb.OwnerId));
                _hitboxes.RemoveAt(i);
            }
        }

        /// <summary>
        /// Get a snapshot of all active hitboxes (for debug visualization).
        /// </summary>
        public List<Hitbox> GetActiveHitboxes()
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
        public List<HitResult> Tick(List<EntityData> entities)
        {
            var results = new List<HitResult>();
            var hitThisTick = new HashSet<ulong>();

            for (int i = _hitboxes.Count - 1; i >= 0; i--)
            {
                var hb = _hitboxes[i];
                if (!hb.Active) { _hitboxes.RemoveAt(i); continue; }

                // Save position before movement (for explosion spawning)
                float prevX = hb.X, prevY = hb.Y, prevZ = hb.Z;

                // Move projectile
                hb.X += hb.VX * Simulation.TickDt;
                hb.Y += hb.VY * Simulation.TickDt;
                hb.Z += hb.VZ * Simulation.TickDt;

                // Apply gravity to projectile (if set)
                if (hb.Gravity > 0f)
                    hb.VY -= hb.Gravity * Simulation.TickDt;

                // Check collision against each entity
                foreach (var entity in entities)
                {
                    if (!entity.Active) continue;
                    if (!hb.CanHitOwner && entity.Id == hb.OwnerId) continue;
                    if (hitThisTick.Contains(entity.Id)) continue;

                    bool hit = false;
                    float dist = 0f, dx = 0f, dy = 0f, dz = 0f;

                    if (hb.Shape == HitboxShape.Capsule || entity.Shape == HitboxShape.Capsule)
                    {
                        hit = CapsuleCollision(hb, entity, out dist, out dx, out dy, out dz);
                    }
                    else
                    {
                        // Sphere-sphere (original)
                        dx = entity.PosX - hb.X;
                        dy = entity.PosY - hb.Y;
                        dz = entity.PosZ - hb.Z;
                        float distSq = (dx * dx) + (dy * dy) + (dz * dz);
                        float combinedRadius = hb.Radius + entity.Radius;
                        if (distSq <= combinedRadius * combinedRadius)
                        {
                            dist = MathF.Sqrt(distSq);
                            hit = true;
                        }
                    }

                    if (hit)
                    {
                        // Calculate knockback direction
                        float kbX = dist > 0.001f ? (dx / dist) * hb.KnockbackForce : 0f;
                        float kbZ = dist > 0.001f ? (dz / dist) * hb.KnockbackForce : 0f;

                        results.Add(new HitResult
                        {
                            TargetEntityId = entity.Id,
                            OwnerEntityId = hb.OwnerId,
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
                    // Queue explosion if this has one (use pre-move position)
                    if (hb.Explosion.HasValue)
                        _pendingExplosions.Add((prevX, prevY, prevZ, hb.Explosion.Value, hb.OwnerId));
                    _hitboxes.RemoveAt(i);
                }
                else
                {
                    _hitboxes[i] = hb; // write back velocity/age changes
                }
            }

            return results;
        }

        /// <summary>
        /// Capsule vs Entity collision (handles all shape combos).
        /// Capsule = segment from (X,Y,Z) to (EndX,EndY,EndZ) with radius.
        /// Returns true if collision, with distance and direction vector.
        /// </summary>
        private static bool CapsuleCollision(Hitbox hb, EntityData entity,
            out float dist, out float dx, out float dy, out float dz)
        {
            // Get capsule endpoints for hitbox
            float hbStartX = hb.X, hbStartY = hb.Y, hbStartZ = hb.Z;
            float hbEndX = hb.Shape == HitboxShape.Capsule ? hb.EndX : hb.X;
            float hbEndY = hb.Shape == HitboxShape.Capsule ? hb.EndY : hb.Y;
            float hbEndZ = hb.Shape == HitboxShape.Capsule ? hb.EndZ : hb.Z;

            // Get capsule endpoints for entity
            float entStartX = entity.PosX, entStartY = entity.PosY, entStartZ = entity.PosZ;
            float entEndX = entity.Shape == HitboxShape.Capsule ? entity.EndX : entity.PosX;
            float entEndY = entity.Shape == HitboxShape.Capsule ? entity.EndY : entity.PosY;
            float entEndZ = entity.Shape == HitboxShape.Capsule ? entity.EndZ : entity.PosZ;

            // Find closest points between two segments
            ClosestPointsSegmentSegment(
                hbStartX, hbStartY, hbStartZ, hbEndX, hbEndY, hbEndZ,
                entStartX, entStartY, entStartZ, entEndX, entEndY, entEndZ,
                out float cx1, out float cy1, out float cz1,
                out float cx2, out float cy2, out float cz2);

            dx = cx2 - cx1;
            dy = cy2 - cy1;
            dz = cz2 - cz1;
            float distSq = (dx * dx) + (dy * dy) + (dz * dz);
            float combinedRadius = hb.Radius + entity.Radius;

            if (distSq <= combinedRadius * combinedRadius)
            {
                dist = MathF.Sqrt(distSq);
                // Direction: from hitbox toward entity
                dx = entity.PosX - hb.X;
                dy = entity.PosY - hb.Y;
                dz = entity.PosZ - hb.Z;
                return true;
            }

            dist = 0f;
            return false;
        }

        /// <summary>
        /// Find closest points between two line segments.
        /// Uses the standard algorithm for segment-segment distance.
        /// </summary>
        private static void ClosestPointsSegmentSegment(
            float a0x, float a0y, float a0z, float a1x, float a1y, float a1z,
            float b0x, float b0y, float b0z, float b1x, float b1y, float b1z,
            out float cx, out float cy, out float cz,
            out float dx, out float dy, out float dz)
        {
            float d1x = a1x - a0x, d1y = a1y - a0y, d1z = a1z - a0z;
            float d2x = b1x - b0x, d2y = b1y - b0y, d2z = b1z - b0z;
            float rx = a0x - b0x, ry = a0y - b0y, rz = a0z - b0z;

            float a = (d1x * d1x) + (d1y * d1y) + (d1z * d1z); // |d1|²
            float e = (d2x * d2x) + (d2y * d2y) + (d2z * d2z); // |d2|²
            float f = (d2x * rx) + (d2y * ry) + (d2z * rz);

            float t = 0f, u = 0f;

            if (a <= 1e-6f && e <= 1e-6f)
            {
                // Both degenerate to points
                t = 0f; u = 0f;
            }
            else if (a <= 1e-6f)
            {
                // Segment 1 is a point
                u = Math.Clamp(f / e, 0f, 1f);
            }
            else
            {
                float c = (d1x * rx) + (d1y * ry) + (d1z * rz);

                if (e <= 1e-6f)
                {
                    // Segment 2 is a point
                    t = Math.Clamp(-c / a, 0f, 1f);
                }
                else
                {
                    float b = (d1x * d2x) + (d1y * d2y) + (d1z * d2z);
                    float denom = (a * e) - (b * b);

                    if (MathF.Abs(denom) > 1e-6f)
                    {
                        t = Math.Clamp(((b * f) - (c * e)) / denom, 0f, 1f);
                        u = Math.Clamp(((b * t) + f) / e, 0f, 1f);
                    }
                    else
                    {
                        t = 0f;
                        u = Math.Clamp(f / e, 0f, 1f);
                    }
                }
            }

            cx = a0x + (d1x * t);
            cy = a0y + (d1y * t);
            cz = a0z + (d1z * t);
            dx = b0x + (d2x * u);
            dy = b0y + (d2y * u);
            dz = b0z + (d2z * u);
        }
    }
}
