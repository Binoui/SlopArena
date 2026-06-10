#nullable enable
using Godot;
using System;

/// <summary>
/// Floating damage number that appears on hit (like fighting games).
/// Spawns at hit position, floats up, and fades out.
/// </summary>
public partial class DamageNumber : Label3D
{
    private float _lifetime = 0f;
    private const float TotalLifetime = 1.2f; // 1.2 seconds
    private const float RiseSpeed = 3f; // Units per second
    private Vector3 _velocity = Vector3.Zero;

    public void Setup(float damage, Vector3 position, bool isCritical = false)
    {
        // Position
        GlobalPosition = position;

        // Text
        int dmg = Mathf.RoundToInt(damage);
        Text = $"{dmg}%";

        // Size based on damage
        if (dmg < 5)
            PixelSize = 0.008f; // Small
        else if (dmg < 15)
            PixelSize = 0.012f; // Medium
        else
            PixelSize = 0.016f; // Large

        // Color based on damage
        if (isCritical || dmg >= 20)
            Modulate = new Color(1.0f, 0.3f, 0.1f); // Red-orange (big hit)
        else if (dmg >= 10)
            Modulate = new Color(1.0f, 0.8f, 0.2f); // Yellow (medium)
        else
            Modulate = new Color(1.0f, 1.0f, 1.0f); // White (small)

        // Billboard mode (always face camera)
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;

        // Outline for visibility
        OutlineSize = 4;
        OutlineModulate = new Color(0f, 0f, 0f, 0.8f);

        // Random horizontal velocity (slight drift)
        _velocity = new Vector3(
            (GD.Randf() - 0.5f) * 0.5f, // X: -0.25 to 0.25
            RiseSpeed,                   // Y: up
            (GD.Randf() - 0.5f) * 0.5f  // Z: -0.25 to 0.25
        );
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _lifetime += dt;

        // Move upward with velocity
        GlobalPosition += _velocity * dt;

        // Slow down over time (deceleration)
        _velocity *= 0.95f;

        // Fade out in last 0.5s
        float fadeStart = TotalLifetime - 0.5f;
        if (_lifetime > fadeStart)
        {
            float fadeProgress = (_lifetime - fadeStart) / 0.5f;
            float alpha = 1f - fadeProgress;
            Modulate = new Color(Modulate.R, Modulate.G, Modulate.B, alpha);
        }

        // Destroy after lifetime
        if (_lifetime >= TotalLifetime)
        {
            QueueFree();
        }
    }
}
