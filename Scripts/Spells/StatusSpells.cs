#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Visual helper methods for class ability special effects.
/// Each method creates a Godot visual (cone, circle, beam, impact, AoE indicator)
/// that lasts for a specified duration.
///
/// These are the ONLY remaining part of the old wizard arena StatusSpells.
/// All spell effect methods were removed — abilities now use AbilityData + AttackStage
/// from CharacterDefinition for gameplay, and ClassAbilities for special effects.
/// </summary>
public static class StatusSpells
{
    /// <summary>
    /// Create a transient cone visual in front of the player.
    /// </summary>
    public static void CreateConeVisual(CombatComponent combat, Vector3 origin, Vector3 direction,
        float range, float radius, Color color, float duration)
    {
        var cone = new MeshInstance3D();
        var coneMesh = new CylinderMesh();
        coneMesh.BottomRadius = radius;
        coneMesh.TopRadius = 0f;
        coneMesh.Height = range;
        coneMesh.RadialSegments = 16;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
        cone.Mesh = coneMesh;
        cone.MaterialOverride = mat;

        AddToScene(combat, cone);
        cone.GlobalPosition = origin + direction * (range * 0.5f);
        cone.LookAt(origin + direction * range, Vector3.Up);
        var timer = combat.GetTree().CreateTimer(duration);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(cone)) cone.QueueFree(); };
    }

    /// <summary>
    /// Create a transient circle/AoE visual at a position.
    /// </summary>
    public static void CreateCircleVisual(CombatComponent combat, Vector3 center,
        float radius, Color color, float duration)
    {
        var circle = new MeshInstance3D();
        var circleMesh = new SphereMesh();
        circleMesh.Radius = radius;
        circleMesh.Height = radius * 2f;
        circleMesh.RadialSegments = 24;
        circleMesh.Rings = 12;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
        circle.Mesh = circleMesh;
        circle.MaterialOverride = mat;

        AddToScene(combat, circle);
        circle.GlobalPosition = new Vector3(center.X, 0.2f, center.Z);
        var timer = combat.GetTree().CreateTimer(duration);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(circle)) circle.QueueFree(); };
    }

    /// <summary>
    /// Create a brief impact flash at a position.
    /// </summary>
    public static void CreateImpactVisual(CombatComponent combat, Vector3 position,
        float radius, Color color)
    {
        var impact = new MeshInstance3D();
        var impactMesh = new SphereMesh();
        impactMesh.Radius = radius;
        impactMesh.Height = radius * 2f;
        impactMesh.RadialSegments = 16;
        impactMesh.Rings = 8;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        impact.Mesh = impactMesh;
        impact.MaterialOverride = mat;

        AddToScene(combat, impact);
        impact.GlobalPosition = position;

        // Fade out and remove
        var timer = combat.GetTree().CreateTimer(0.3f);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(impact)) return;
            var fadeTween = combat.GetTree().CreateTween();
            fadeTween.TweenProperty(impact, "scale", Vector3.Zero, 0.2f);
            fadeTween.Finished += () => { if (GodotObject.IsInstanceValid(impact)) impact.QueueFree(); };
        };
    }

    /// <summary>
    /// Create a beam/line visual from origin in direction for a distance.
    /// </summary>
    public static void CreateBeamVisual(CombatComponent combat, Vector3 origin,
        Vector3 direction, float distance, Color color, float duration)
    {
        var beam = new MeshInstance3D();
        var beamMesh = new BoxMesh();
        beamMesh.Size = new Vector3(0.3f, 0.3f, distance);

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 4f,
            NoDepthTest = true,
        };
        beam.Mesh = beamMesh;
        beam.MaterialOverride = mat;

        AddToScene(combat, beam);
        beam.GlobalPosition = origin + direction * (distance * 0.5f);
        beam.LookAt(origin + direction * distance, Vector3.Up);
        var timer = combat.GetTree().CreateTimer(duration);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(beam)) beam.QueueFree(); };
    }

    /// <summary>
    /// Create a persistent AoE zone indicator (ring).
    /// Returns the Node3D so the caller can manage its lifetime.
    /// </summary>
    public static Node3D CreateAoEIndicator(Vector3 center, float radius, Color color)
    {
        var ring = new MeshInstance3D();
        var ringMesh = new SphereMesh();
        ringMesh.Radius = radius;
        ringMesh.Height = radius * 2f;
        ringMesh.RadialSegments = 24;
        ringMesh.Rings = 12;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
        ring.Mesh = ringMesh;
        ring.MaterialOverride = mat;
        ring.GlobalPosition = new Vector3(center.X, 0.1f, center.Z);

        return ring;
    }

    // ── SCENE HELPERS ──

    private static void AddToScene(CombatComponent combat, Node3D node)
    {
        if (combat.GetOwnerNode() is Node3D owner)
        {
            var tree = owner.GetTree();
            if (tree == null) { node.QueueFree(); return; }
            var current = tree.CurrentScene;
            if (current == null) { node.QueueFree(); return; }
            if (!current.IsInsideTree()) { node.QueueFree(); return; }
            current.AddChild(node);
        }
        else
        {
            node.QueueFree();
        }
    }
}
