using SlopArena.Shared

namespace SlopArena.Shared
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
using SlopArena.Shared

		public HitResult(ulong targetId, float damage, float kbX, float kbY, float kbZ)
		{
			DidHit = true;
			TargetEntityID = targetId;
			Damage = damage;
			KnockbackX = kbX;
			KnockbackY = kbY;
			KnockbackZ = kbZ;
using SlopArena.Shared

		public static HitResult NoHit => new HitResult { DidHit = false };
	}
using SlopArena.Shared
