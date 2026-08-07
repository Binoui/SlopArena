namespace SlopArena.Shared
{
	/// <summary>
	/// Burst tuning values (ADR-0014, issue #99). One per-entity tool on a long cooldown
	/// with two uses: defensive (escape from hitstop/hitstun/knockback) and offensive
	/// (cancel an attack lock into a forward hitbox). All values tunable in one file.
	/// </summary>
	public static class BurstConfig
	{
		public const ushort CooldownTicks = 3600;                    // 60 s
		public const ushort DefensiveInvincibilityTicks = 10;        // startup telegraph
		public const ushort DefensiveRecoveryTicks = 25;             // punish window
		public const ushort OffensiveRecoveryTicks = 12;             // short — near-true follow-up
		public const float HitboxDamage = 4f;
		public const float HitboxBaseKnockback = 10f;                // fixed launch — growth 0 = zero damage scaling
		public const float HitboxKnockbackGrowth = 0f;
		public const sbyte HitboxAngle = 20;
		public const ushort HitboxStunTicks = 7;
		public const ushort HitboxDurationTicks = 8;
		public const float HitboxRadius = 1.2f;
		public const float HitboxForwardOffset = 1.2f;               // spawn distance in front of user
		public const float HitboxHeightOffset = 0.6f;                // above capsule center
		public const float AttackerPushBaseKnockback = 6f;           // defensive shove — growth 0, stun 0
		public const sbyte AttackerPushAngle = 10;
	}
}
