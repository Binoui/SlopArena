using System;

namespace MoveBox.Shared
{
	/// <summary>
	/// Result of a hit check between a spell effect and an entity.
	/// Pure data, no Godot dependencies.
	/// </summary>
	public struct HitResult
	{
		public bool DidHit;
		public ulong TargetEntityID;
		public float Damage;
		public float KnockbackX;
		public float KnockbackY;
		public float KnockbackZ;
		
		public HitResult(ulong targetId, float damage, float kbX, float kbY, float kbZ)
		{
			DidHit = true;
			TargetEntityID = targetId;
			Damage = damage;
			KnockbackX = kbX;
			KnockbackY = kbY;
			KnockbackZ = kbZ;
		}
		
		public static HitResult NoHit => new HitResult { DidHit = false };
	}
}
