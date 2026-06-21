#nullable enable
using Godot;
using System;

/// <summary>
/// Ground arrow indicator that shows the player's input direction on the ground.
/// Created once and reused; hidden when input is zero.
/// Contains a MeshInstance3D child with a triangle chevron mesh.
/// </summary>
public partial class GroundArrowIndicator : Node3D
{
    private readonly MeshInstance3D _arrow;

    public GroundArrowIndicator()
    {
        Name = "GroundArrow";
        Visible = false;

        // Build a chevron/triangle pointing in +Z (forward)
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        const float size = 0.8f;

        // Tip
        st.AddVertex(new Vector3(0f, 0f, size));
        // Left
        st.AddVertex(new Vector3(-size * 0.5f, 0f, 0f));
        // Right
        st.AddVertex(new Vector3(size * 0.5f, 0f, 0f));

        st.GenerateNormals();

        _arrow = new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 1f, 1f, 0.6f),
                EmissionEnabled = true,
                Emission = new Color(0.8f, 0.8f, 1f),
                EmissionEnergyMultiplier = 1.5f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither,
                DistanceFadeMaxDistance = 50f,
            }
        };

        AddChild(_arrow);
    }

    /// <summary>
    /// Update arrow position and rotation. Hides when direction is near-zero.
    /// </summary>
    /// <param name="worldPosition">World position to place the arrow at (Y will be set to 0.05).</param>
    /// <param name="direction">Camera-relative direction (X=right, Y=forward).</param>
    public void Update(Vector3 worldPosition, Vector2 direction)
    {
        if (direction.LengthSquared() <= 0.001f)
        {
            Visible = false;
            return;
        }

        Visible = true;

        // Position at character's feet on the ground
        Vector3 pos = worldPosition;
        pos.Y = 0.05f;
        Position = pos;

        // Rotate to face the input direction
        float angle = MathF.Atan2(direction.X, direction.Y);
        Rotation = new Vector3(0f, angle, 0f);
    }
}
