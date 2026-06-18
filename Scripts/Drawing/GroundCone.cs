#nullable enable
using Godot;

/// <summary>
/// Reusable ground-projected cone indicator (for aimed charge abilities).
/// </summary>
public class GroundCone
{
    private MeshInstance3D? _mesh;
    private float _groundY;
    private float _coneAngle;
    private float _coneRange;

    public void Show(Node parent, float coneAngleDeg, float coneRange, float groundY)
    {
        _coneAngle = coneAngleDeg;
        _coneRange = coneRange;
        _groundY = groundY;
        BuildMesh();
        if (_mesh != null) parent.AddChild(_mesh);
    }

    public void Hide()
    {
        if (_mesh == null) return;
        _mesh.QueueFree();
        _mesh = null;
    }

    public void SetPosition(Vector3 worldPos, float yawRad)
    {
        if (_mesh == null) return;
        _mesh.GlobalPosition = new Vector3(worldPos.X, _groundY + 0.05f, worldPos.Z);
        _mesh.GlobalRotation = new Vector3(0f, yawRad, 0f);
    }

    private void BuildMesh()
    {
        if (_mesh != null) return;

        float halfAngle = Mathf.DegToRad(_coneAngle) * 0.5f;
        float range = _coneRange;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        st.AddVertex(Vector3.Zero);
        st.AddVertex(new Vector3(-Mathf.Sin(halfAngle) * range, 0f, Mathf.Cos(halfAngle) * range));
        st.AddVertex(new Vector3(Mathf.Sin(halfAngle) * range, 0f, Mathf.Cos(halfAngle) * range));

        st.AddIndex(0);
        st.AddIndex(1);
        st.AddIndex(2);

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.5f, 0f, 0.25f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };

        var mesh = st.Commit();
        if (mesh != null) mesh.SurfaceSetMaterial(0, mat);
        _mesh = new MeshInstance3D { Mesh = mesh, Name = "ConeIndicator" };
    }
}
