#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Shared projectile-spawning helper used by character ability classes.
/// Creates a visual projectile (MeshInstance3D + Sphere) with a tween-based
/// movement and Area3D hit detection against dummy NPCs.
///
/// In the future, projectiles will be spawned via the shared simulation
/// and rendered by a ProjectileManager. This is a temporary client-side helper.
/// </summary>
public static class ProjectileHelpers
{
    public static void CreateProjectile(Vector3 origin, Vector3 direction, float speed, float lifetime,
        Color color, SceneTree tree, CombatComponent combat, float damage, Vector3 knockbackForce,
        StatusType? status, float statusDuration)
    {
        var proj = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.4f, Height = 0.8f, RadialSegments = 10, Rings = 6 };
        var mat = new StandardMaterial3D
        {
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 5f,
            AlbedoColor = color,
        };
        proj.Mesh = sphere;
        proj.MaterialOverride = mat;
        proj.GlobalPosition = origin;
        tree.CurrentScene.AddChild(proj);

        Vector3 targetPos = origin + direction * (speed * lifetime);
        var tween = tree.CreateTween();
        tween.TweenProperty(proj, "global_position", targetPos, lifetime).SetTrans(Tween.TransitionType.Linear);
        tween.Finished += () => { if (GodotObject.IsInstanceValid(proj)) proj.QueueFree(); };

        var hitBox = new Area3D();
        var hitShape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 1.2f } };
        hitBox.AddChild(hitShape);
        hitBox.CollisionMask = 2;
        proj.AddChild(hitBox);

        hitBox.BodyEntered += (Node3D body) =>
        {
            if (!GodotObject.IsInstanceValid(proj)) return;
            var node = body as Node;
            while (node != null && node is not CharacterBody3D)
                node = node.GetParent();
            if (node is CharacterBody3D cb)
            {
                string nameStr = cb.Name.ToString();
                if (nameStr.StartsWith("DummyBody_") && int.TryParse(nameStr.AsSpan("DummyBody_".Length), out int idx))
                {
                    ulong entityId = (ulong)(100 + idx);
                    combat.GetSimulation()?.OnEntityHit?.Invoke(entityId, damage, knockbackForce.X, knockbackForce.Y, knockbackForce.Z);
                    if (status.HasValue)
                        combat.ApplyStatusToEntity(entityId, status.Value, statusDuration);
                    StatusSpells.CreateImpactVisual(combat, proj.GlobalPosition, 1.5f, color);
                    proj.QueueFree();
                    tween.Kill();
                }
            }
        };
    }
}
