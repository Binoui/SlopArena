#nullable enable
using Godot;

/// <summary>
/// Spawns floating damage numbers when characters are hit.
/// Attached to Main scene, listens for damage events.
/// </summary>
public partial class DamageNumberManager : Node3D
{
    /// <summary>
    /// Spawn a damage number at the given world position.
    /// </summary>
    public void SpawnDamageNumber(float damage, Vector3 position, bool isCritical = false)
    {
        var damageNum = new DamageNumber();
        damageNum.Setup(damage, position, isCritical);
        AddChild(damageNum);
    }
}
