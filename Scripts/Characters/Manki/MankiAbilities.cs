#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Special effects for Manki (Mad Bomber Monkey) abilities.
/// Called AFTER stage resolution by AbilityRegistry.
/// Each method maps to a key in CharacterDefinition's SpecialEffectKeys.
/// </summary>
public static class MankiAbilities
{
    /// <summary>
    /// RMB — Aerosol + Lighter: homemade flamethrower
    /// </summary>
    public static void AerosolFlame(CombatComponent combat)
    {
        Vector3 forward = combat.GetCameraForward();
        Vector3 pos = combat.GetOwnerPosition() + Vector3.Up * 1.2f; // Hand height

        var vfxManager = combat.GetSpellVFX();
        if (vfxManager != null)
        {
            // Spawn flamethrower VFX (briquet + aérosol burst)
            var flame = vfxManager.SpawnFlamethrower(pos, forward);

            // Auto-stop after 0.4s (same duration as attack)
            if (flame != null)
            {
                var tree = combat.GetTree();
                if (tree != null)
                {
                    tree.CreateTimer(0.4).Timeout += () => flame.StopFlame();
                }
            }
        }
        else
        {
            // Fallback: old cone visual if VFX manager not available
            StatusSpells.CreateConeVisual(combat, pos, forward, 5f, 3f, new Color(1f, 0.6f, 0f, 0.3f), 0.4f);
            StatusSpells.CreateImpactVisual(combat, pos + forward * 4f, 1.5f, new Color(1f, 0.8f, 0.2f));
        }
    }

    /// <summary>
    /// Q — Round Bomb: spawn projectile that travels forward and explodes
    /// </summary>
    public static void RoundBomb(CombatComponent combat)
    {
        Vector3 forward = combat.GetCameraForward();
        Vector3 pos = combat.GetOwnerPosition() + Vector3.Up * 1.2f;

        // Spawn a visual projectile with slight arc (upward angle)
        Vector3 arcDir = (forward + Vector3.Up * 0.3f).Normalized();
        float speed = 18f;
        float lifetime = 1.2f;
        SceneTree? tree = combat.GetTree();
        if (tree == null) return;

        var proj = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 12, Rings = 8 };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.7f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.5f, 0f),
            EmissionEnergyMultiplier = 4f,
        };
        proj.Mesh = sphere;
        proj.MaterialOverride = mat;
        tree.CurrentScene?.AddChild(proj);
        proj.GlobalPosition = pos;

        Vector3 targetPos = pos + arcDir * (speed * lifetime) + Vector3.Down * 3f; // arc drops
        var tween = tree.CreateTween();
        tween.TweenProperty(proj, "global_position", targetPos, lifetime).SetTrans(Tween.TransitionType.Quad);
        tween.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(proj))
            {
                StatusSpells.CreateImpactVisual(combat, proj.GlobalPosition, 2f, new Color(1f, 0.7f, 0f));
                StatusSpells.CreateCircleVisual(combat, proj.GlobalPosition, 2.5f, new Color(1f, 0.5f, 0f, 0.3f), 0.3f);
                proj.QueueFree();
            }
        };
    }

    /// <summary>
    /// E — Dynamite Jump: explosion at feet + visual
    /// </summary>
    public static void DynamiteJump(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateImpactVisual(combat, pos + Vector3.Down * 0.5f, 2.5f, new Color(1f, 0.6f, 0f));
        StatusSpells.CreateCircleVisual(combat, pos, 3f, new Color(1f, 0.4f, 0f, 0.25f), 0.3f);
    }

    /// <summary>
    /// R — Dive Bomb: impact explosion on landing
    /// </summary>
    public static void DiveBomb(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateImpactVisual(combat, pos + Vector3.Down * 0.5f, 3f, new Color(1f, 0.7f, 0f));
        StatusSpells.CreateCircleVisual(combat, pos, 3.5f, new Color(1f, 0.5f, 0f, 0.3f), 0.4f);
    }

    /// <summary>
    /// F — Big Boom (Ult): massive AoE explosion
    /// </summary>
    public static void BigBoom(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateImpactVisual(combat, pos, 4f, new Color(1f, 0.9f, 0.2f));
        StatusSpells.CreateCircleVisual(combat, pos, 5f, new Color(1f, 0.6f, 0f, 0.4f), 0.6f);
    }
}
