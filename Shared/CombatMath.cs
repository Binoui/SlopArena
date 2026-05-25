using System;

namespace MoveBox.Shared
{
	/// <summary>
	/// Pure math functions for combat calculations.
	/// No Godot dependencies - usable by Server and Client.
	/// 
	/// All positions are in 3D space (X, Y, Z).
	/// Y is the up axis.
	/// </summary>
	public static class CombatMath
	{
		/// <summary>
		/// Check if a point is within a circle (cylinder) centered at another point.
		/// Used for AoE circle hits and projectile proximity checks.
		/// </summary>
		public static bool IsInCircle(
			float targetX, float targetY, float targetZ,
			float centerX, float centerY, float centerZ,
			float radius)
		{
			float dx = targetX - centerX;
			float dz = targetZ - centerZ;
			float distSq = dx * dx + dz * dz;
			return distSq <= radius * radius;
		}
		
		/// <summary>
		/// Check if a point is within a cone originating from a position.
		/// The cone is defined by its origin, direction, angle (half-angle in radians), and range.
		/// </summary>
		public static bool IsInCone(
			float targetX, float targetY, float targetZ,
			float originX, float originY, float originZ,
			float dirX, float dirZ,
			float halfAngleRad, float range)
		{
			float dx = targetX - originX;
			float dz = targetZ - originZ;
			float distSq = dx * dx + dz * dz;
			
			if (distSq > range * range)
				return false;
			
			if (distSq < 0.001f)
				return true; // At origin
			
			float dist = MathF.Sqrt(distSq);
			float dot = (dx * dirX + dz * dirZ) / dist; // Normalized dot product
			
			// Clamp to avoid NaN from floating point
			dot = Math.Clamp(dot, -1f, 1f);
			float angle = MathF.Acos(dot);
			
			return angle <= halfAngleRad;
		}
		
		/// <summary>
		/// Check if a line segment (projectile path) intersects a circle (entity hitbox).
		/// Uses segment-to-circle distance check.
		/// </summary>
		public static bool LineIntersectsCircle(
			float lineStartX, float lineStartZ,
			float lineEndX, float lineEndZ,
			float circleX, float circleZ,
			float radius)
		{
			float dx = lineEndX - lineStartX;
			float dz = lineEndZ - lineStartZ;
			float fx = lineStartX - circleX;
			float fz = lineStartZ - circleZ;
			
			float a = dx * dx + dz * dz;
			float b = 2f * (fx * dx + fz * dz);
			float c = fx * fx + fz * fz - radius * radius;
			
			float discriminant = b * b - 4f * a * c;
			if (discriminant < 0)
				return false;
			
			discriminant = MathF.Sqrt(discriminant);
			float t1 = (-b - discriminant) / (2f * a);
			float t2 = (-b + discriminant) / (2f * a);
			
			// Check if intersection is within the segment [0, 1]
			return (t1 >= 0 && t1 <= 1) || (t2 >= 0 && t2 <= 1) || (t1 < 0 && t2 > 1);
		}
		
		/// <summary>
		/// Calculate knockback direction from attacker to target (horizontal only).
		/// </summary>
		public static void CalculateKnockback(
			float targetX, float targetY, float targetZ,
			float attackerX, float attackerY, float attackerZ,
			float force, float upward,
			out float kbX, out float kbY, out float kbZ)
		{
			float dx = targetX - attackerX;
			float dz = targetZ - attackerZ;
			float distSq = dx * dx + dz * dz;
			
			if (distSq > 0.001f)
			{
				float dist = MathF.Sqrt(distSq);
				kbX = (dx / dist) * force;
				kbZ = (dz / dist) * force;
			}
			else
			{
				kbX = 0f;
				kbZ = force; // Default forward
			}
			
			kbY = upward;
		}
		
		/// <summary>
		/// Distance between two 3D points (horizontal only, XZ plane).
		/// </summary>
		public static float HorizontalDistance(float x1, float z1, float x2, float z2)
		{
			float dx = x2 - x1;
			float dz = z2 - z1;
			return MathF.Sqrt(dx * dx + dz * dz);
		}
	}
}
