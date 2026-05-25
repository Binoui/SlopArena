using Godot;
using System;

/// <summary>
/// Hitbox component (Area3D) - pure marker with stats.
/// No collision detection logic here - that's handled by LocalSimulation.
/// 
/// Collision layers:
///   Layer 2: Entities (Hurtbox)
///   Layer 3: Friendly projectiles
///   Layer 4: Enemy projectiles
/// </summary>
public partial class Hitbox : Area3D
{
	[Export] public float Damage = 10f;
	[Export] public float KnockbackForce = 25f;
	[Export] public float KnockbackUpward = 5f;
	public Node3D? Attacker { get; set; }
	
	public override void _Ready()
	{
		// Ensure we have a CollisionShape3D
		if (GetChildCount() == 0 || GetChild(0) is not CollisionShape3D)
		{
			var collisionShape = new CollisionShape3D();
			var sphere = new SphereShape3D();
			sphere.Radius = 1.5f;
			collisionShape.Shape = sphere;
			AddChild(collisionShape);
		}
	}
}
