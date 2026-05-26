using System;

namespace SlopArena.Shared
{
	/// <summary>
	/// Pure state of a projectile in flight.
	/// No Godot dependencies - used by Server simulation and Client prediction.
	/// </summary>
	public struct ProjectileState
	{
		public ulong ProjectileID;
		public ushort SpellID;
		public float PosX;
		public float PosY;
		public float PosZ;
		public float DirX;
		public float DirY;
		public float DirZ;
		public float DistanceTraveled;
		public bool IsActive;
		
		public ProjectileState(ulong id, ushort spellId, float posX, float posY, float posZ, 
		                       float dirX, float dirY, float dirZ)
		{
			ProjectileID = id;
			SpellID = spellId;
			PosX = posX;
			PosY = posY;
			PosZ = posZ;
			DirX = dirX;
			DirY = dirY;
			DirZ = dirZ;
			DistanceTraveled = 0f;
			IsActive = true;
		}
		
		/// <summary>
		/// Advance the projectile by delta time.
		/// Returns true if the projectile is still in flight.
		/// </summary>
		public bool Update(float delta, SpellDefinition spell)
		{
			if (!IsActive) return false;
			
			float step = spell.Speed * delta;
			PosX += DirX * step;
			PosY += DirY * step;
			PosZ += DirZ * step;
			DistanceTraveled += step;
			
			if (DistanceTraveled >= spell.Range)
			{
				IsActive = false;
				return false;
			}
			
			return true;
		}
	}
}