using Godot;
using System;

/// <summary>
/// Hurtbox component (Area3D) - pure marker on entities that can take damage.
/// No collision detection logic here - that's handled by LocalSimulation.
/// 
/// Collision layers:
///   Layer 2: Entities (this)
/// </summary>
public partial class Hurtbox : Area3D
{
    [Export] public Node3D OwnerEntity;

    /// <summary>
    /// Fired when this hurtbox takes a hit.
    /// Parameters: attackerPosition, damage, knockbackForce (Vector3)
    /// </summary>
    public event Action<Vector3, float, Vector3> OnHit;

    public void TakeHit(Vector3 attackerPos, float damage, Vector3 knockbackForce)
    {
        OnHit?.Invoke(attackerPos, damage, knockbackForce);
    }

    public override void _Ready()
    {
        CollisionLayer = 2; // Layer 2: entities
        CollisionMask = 0;

        // Ensure we have a CollisionShape3D
        if (GetChildCount() == 0 || GetChild(0) is not CollisionShape3D)
        {
            var collisionShape = new CollisionShape3D();
            var sphere = new SphereShape3D();
            sphere.Radius = 2.0f;
            collisionShape.Shape = sphere;
            AddChild(collisionShape);
        }
    }
}
