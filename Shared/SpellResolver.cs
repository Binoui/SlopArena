using System;
using System.Collections.Generic;

namespace SlopArena.Shared
{
	/// <summary>
	/// Pure C# spell resolution logic.
	/// No Godot dependency. Can be used by Server, Client, or AI bots.
	/// 
	/// Resolves spell effects against a list of entities:
	/// - Projectile hit detection (line-circle intersection)
	/// - Cone AoE (melee slash, shockwave)
	/// - Circle AoE (explosion, ground effects)
	/// - Knockback calculation
	/// </summary>
	public static class SpellResolver
	{
		/// <summary>
		/// Result of a single spell resolution.
		/// </summary>
		public struct HitResult
		{
			public ulong TargetEntityId;
			public float Damage;
			public float KnockbackX;
			public float KnockbackY;
			public float KnockbackZ;
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
		/// Resolve a projectile hit against entities.
		/// Checks line segment intersection from oldPos to newPos.
		/// Returns the first entity hit, or null.
		/// </summary>
		public static HitResult? ResolveProjectileHit(
			float oldX, float oldZ,
			float newX, float newZ,
			float originX, float originY, float originZ,
			float damage, float knockbackForce, float knockbackUpward,
			ulong casterEntityId,
			List<EntityData> entities)
		{
			foreach (var entity in entities)
			{
				if (!entity.Active || entity.Id == casterEntityId)
					continue;
				
				bool intersects = CombatMath.LineIntersectsCircle(
					oldX, oldZ,
					newX, newZ,
					entity.PosX, entity.PosZ,
					entity.Radius
				);
				
				if (intersects)
				{
				CombatMath.CalculateKnockback(
					entity.PosX, entity.PosY, entity.PosZ,
					originX, originY, originZ,
					knockbackForce, knockbackUpward,
					out float kbX, out float kbY, out float kbZ
				);
					
					return new HitResult
					{
						TargetEntityId = entity.Id,
						Damage = damage,
						KnockbackX = kbX,
						KnockbackY = kbY,
						KnockbackZ = kbZ
					};
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Resolve a circle AoE hit (explosion, ground effect).
		/// Returns all entities hit.
		/// </summary>
		public static List<HitResult> ResolveCircleHit(
			float centerX, float centerY, float centerZ,
			float radius,
			float damage, float knockbackForce, float knockbackUpward,
			ulong casterEntityId,
			List<EntityData> entities)
		{
			var results = new List<HitResult>();
			
			foreach (var entity in entities)
			{
				if (!entity.Active || entity.Id == casterEntityId)
					continue;
				
				bool inRange = CombatMath.IsInCircle(
					entity.PosX, entity.PosY, entity.PosZ,
					centerX, centerY, centerZ,
					radius + entity.Radius
				);
				
				if (inRange)
				{
					CombatMath.CalculateKnockback(
						entity.PosX, entity.PosY, entity.PosZ,
						centerX, centerY, centerZ,
						knockbackForce, knockbackUpward,
						out float kbX, out float kbY, out float kbZ
					);
					
					results.Add(new HitResult
					{
						TargetEntityId = entity.Id,
						Damage = damage,
						KnockbackX = kbX,
						KnockbackY = kbY,
						KnockbackZ = kbZ
					});
				}
			}
			
			return results;
		}
		
		/// <summary>
		/// Resolve a cone AoE hit (melee slash, shockwave).
		/// Returns all entities hit.
		/// </summary>
		public static List<HitResult> ResolveConeHit(
			float originX, float originY, float originZ,
			float dirX, float dirZ,
			float halfAngleRad,
			float range,
			float damage, float knockbackForce, float knockbackUpward,
			ulong casterEntityId,
			List<EntityData> entities)
		{
			var results = new List<HitResult>();
			
			foreach (var entity in entities)
			{
				if (!entity.Active || entity.Id == casterEntityId)
					continue;
				
				bool inCone = CombatMath.IsInCone(
					entity.PosX, entity.PosY, entity.PosZ,
					originX, originY, originZ,
					dirX, dirZ,
					halfAngleRad,
					range + entity.Radius
				);
				
				if (inCone)
				{
					CombatMath.CalculateKnockback(
						entity.PosX, entity.PosY, entity.PosZ,
						originX, originY, originZ,
						knockbackForce, knockbackUpward,
						out float kbX, out float kbY, out float kbZ
					);
					
					results.Add(new HitResult
					{
						TargetEntityId = entity.Id,
						Damage = damage,
						KnockbackX = kbX,
						KnockbackY = kbY,
						KnockbackZ = kbZ
					});
				}
			}
			
			return results;
		}
	}
}
