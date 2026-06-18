#nullable enable
using Godot;

/// <summary>
/// Reusable flat circle indicator on the ground.
/// Built once with SurfaceTool, then repositioned each frame via GlobalPosition.
/// </summary>
public class GroundCircle
{
    private MeshInstance3D? _mesh;
    private readonly float _radius;
    private readonly Color _color;

    public GroundCircle(float radius = 0.5f, Color? color = null)
    {
        _radius = radius;
        _color = color ?? new Color(1f, 0.8f, 0.2f, 0.45f);
    }

    public void Show(Node parent)
    {
        if (_mesh != null) return;

        const int segments = 24;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.TriangleStrip);

        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            float rimX = Mathf.Cos(a) * _radius;
            float rimZ = Mathf.Sin(a) * _radius;
            st.AddVertex(new Vector3(rimX, 0f, rimZ));
            st.AddVertex(Vector3.Zero);
        }

        var mat = new StandardMaterial3D
        {
            AlbedoColor = _color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };

        var mesh = st.Commit();
        if (mesh != null) mesh.SurfaceSetMaterial(0, mat);

        _mesh = new MeshInstance3D { Mesh = mesh, Name = "GroundCircle" };
        parent.AddChild(_mesh);
    }

    public void Hide()
    {
        if (_mesh == null) return;
        _mesh.QueueFree();
        _mesh = null;
    }

    public void SetPosition(Vector3 worldPos)
    {
        if (_mesh == null) return;
        _mesh.GlobalPosition = new Vector3(worldPos.X, worldPos.Y + 0.05f, worldPos.Z);
        _mesh.GlobalRotation = Vector3.Zero;
    }
}
