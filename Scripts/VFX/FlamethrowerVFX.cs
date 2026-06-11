#nullable enable
using Godot;

/// <summary>
/// Homemade flamethrower VFX (lighter + aerosol spray) - Simplified version.
/// Cone of glowing meshes that look like fire.
/// </summary>
public partial class FlamethrowerVFX : Node3D
{
    private MeshInstance3D[] _flames = new MeshInstance3D[12];

    public override void _Ready()
    {
        // Create 12 glowing spheres in a cone pattern
        for (int i = 0; i < 12; i++)
        {
            var flame = new MeshInstance3D();
            var sphere = new SphereMesh { Radius = 0.2f + (i * 0.05f), Height = 0.4f + (i * 0.1f) };

            // Orange/red gradient material
            float t = i / 12f;
            Color flameColor = new Color(1f, 0.8f - (t * 0.5f), 0.1f - (t * 0.1f));

            var mat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(flameColor.R, flameColor.G, flameColor.B, 0.6f - (t * 0.3f)),
                EmissionEnabled = true,
                Emission = flameColor,
                EmissionEnergyMultiplier = 3.0f - (t * 1.5f),
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled
            };

            flame.Mesh = sphere;
            flame.MaterialOverride = mat;

            // Position along forward cone
            float distance = (i + 1) * 0.4f;
            float spread = (i * 0.15f);
            float angleX = (GD.Randf() - 0.5f) * spread;
            float angleY = (GD.Randf() - 0.5f) * spread;

            flame.Position = new Vector3(angleX, angleY, -distance);

            AddChild(flame);
            _flames[i] = flame;
        }

        // Auto-destroy after 0.5s
        GetTree().CreateTimer(0.5).Timeout += () => QueueFree();
    }

    public void StopFlame()
    {
        GetTree().CreateTimer(0.1).Timeout += () => QueueFree();
    }
}
