#nullable enable
using Godot;

/// <summary>
/// Central manager for spawning spell VFX.
/// Attached to Main, provides easy API for spells.
/// </summary>
public partial class SpellVFXManager : Node3D
{
    /// <summary>
    /// Spawn flamethrower VFX (Manki RMB).
    /// Returns the VFX node so caller can control start/stop.
    /// </summary>
    public FlamethrowerVFX? SpawnFlamethrower(Vector3 position, Vector3 direction)
    {
        var vfx = new FlamethrowerVFX();
        AddChild(vfx);
        vfx.GlobalPosition = position;

        // Orient toward direction
        if (direction.LengthSquared() > 0.001f)
        {
            vfx.LookAt(position + direction, Vector3.Up);
        }

        return vfx;
    }

    /// <summary>
    /// Spawn aerosol shake VFX (Manki RMB charge).
    /// Returns the VFX node so caller can control stop.
    /// </summary>
    public AerosolShakeVFX? SpawnAerosolShake(Vector3 position)
    {
        var vfx = new AerosolShakeVFX();
        AddChild(vfx);
        vfx.GlobalPosition = position;
        return vfx;
    }
}
