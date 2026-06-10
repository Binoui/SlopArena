#nullable enable
using Godot;

/// <summary>
/// VFX for shaking aerosol can during charge (simplified - just a puff).
/// </summary>
public partial class AerosolShakeVFX : Node3D
{
    public override void _Ready()
    {
        // Simple gray puff
        var puff = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.3f };

        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.7f, 0.7f, 0.8f, 0.3f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled
        };

        puff.Mesh = sphere;
        puff.MaterialOverride = mat;
        AddChild(puff);

        // Fade & grow
        var tween = GetTree().CreateTween();
        tween.TweenProperty(puff, "scale", Vector3.One * 2f, 0.5f);
        tween.Parallel().TweenProperty(mat, "albedo_color:a", 0f, 0.5f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    public void StopShake()
    {
        QueueFree();
    }
}
