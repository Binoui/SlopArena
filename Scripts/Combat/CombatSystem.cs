using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// Abstract combat system: handles dealing damage, knockback, hitstun, and DI.
/// Works for any target (players, dummies, enemies).
/// The actual hit detection (range, arc, collision) is done by the caller.
/// </summary>
public static class CombatSystem
{
	/// <summary>
	/// Apply knockback to a target and put them in hitstun.
	/// knockbackDir: normalized direction away from the attacker
	/// force: magnitude of knockback
	/// durationTicks: how long hitstun lasts
	/// input: the victim's current input (for DI at moment of impact)
	/// </summary>
	public static void ApplyKnockback(
		ref float velX, ref float velY, ref float velZ,
		ref ActionState actionState, ref ushort stateTicksRemaining,
		float knockbackDirX, float knockbackDirY, float force, ushort durationTicks,
		ClientInputPacket input)
	{
		PhysicsConfig.ApplyKnockback(
			ref velX, ref velY, ref velZ,
			ref actionState, ref stateTicksRemaining,
			knockbackDirX, knockbackDirY, force, durationTicks,
			input);
	}
	
	/// <summary>
	/// Check if a target at (targetX, targetY) is within attack range and arc of an attacker.
	/// </summary>
	public static bool IsInAttackRange(
		float attackerX, float attackerY, float targetX, float targetY,
		float forwardX, float forwardZ,
		float range, float arcCos, float targetRadius)
	{
		float dx = targetX - attackerX;
		float dy = targetY - attackerY;
		float dist = MathF.Sqrt(dx * dx + dy * dy);
		
		if (dist > range + targetRadius)
			return false;
		
		// Check arc
		float dirX = dx / dist;
		float dirY = dy / dist;
		float dot = dirX * forwardX + dirY * forwardZ;
		
		return dot > arcCos;
	}
	
	/// <summary>
	/// Check if a target is within contact range (for hazards, touching enemies, etc.)
	/// </summary>
	public static bool IsInContactRange(
		float attackerX, float attackerY, float targetX, float targetY,
		float attackerRadius, float targetRadius)
	{
		float dx = targetX - attackerX;
		float dy = targetY - attackerY;
		float dist = MathF.Sqrt(dx * dx + dy * dy);
		return dist < attackerRadius + targetRadius;
	}
	
	/// <summary>
	/// Get the knockback direction from attacker to target (normalized).
	/// </summary>
	public static void GetKnockbackDirection(
		float attackerX, float attackerY, float targetX, float targetY,
		out float dirX, out float dirY)
	{
		float dx = targetX - attackerX;
		float dy = targetY - attackerY;
		float dist = MathF.Sqrt(dx * dx + dy * dy);
		if (dist > 0.001f)
		{
			dirX = dx / dist;
			dirY = dy / dist;
		}
		else
		{
			dirX = 0f;
			dirY = 1f;
		}
	}
}
