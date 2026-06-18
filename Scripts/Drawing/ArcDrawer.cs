#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Reusable parabolic arc ribbon from launch point to target.
/// Uses shared CombatMath.ComputeProjectileLaunch for physics.
/// Mesh is parented to the scene root (world-space vertices).
/// </summary>
public class ArcDrawer
{
    private MeshInstance3D? _mesh;
    private const int Segments = 20;
    private const float RibbonWidth = 0.08f;

    public void Show(Node parent)
    {
        // Nothing to do — mesh is rebuilt each draw
    }

    public void Hide()
    {
        if (_mesh == null) return;
        _mesh.QueueFree();
        _mesh = null;
    }

    /// <summary>
    /// Draw the arc. Creates/replaces mesh each call.
    /// </summary>
    /// <param name="parent">Scene node to parent the mesh under (usually the root).</param>
    /// <param name="launchPos">World position where the projectile launches from (e.g. hand height).</param>
    /// <param name="targetPos">World position where the projectile should land (ground at target).</param>
    /// <param name="launchAngleDeg">Launch angle above horizontal.</param>
    /// <param name="gravity">Gravity in m/s².</param>
    public void Draw(Node parent, Vector3 launchPos, Vector3 targetPos, float launchAngleDeg, float gravity)
    {
        // Remove old mesh
        if (_mesh != null)
        {
            _mesh.QueueFree();
            _mesh = null;
        }

        float dx = targetPos.X - launchPos.X;
        float dz = targetPos.Z - launchPos.Z;
        float D = Mathf.Sqrt((dx * dx) + (dz * dz));
        if (D < 0.1f) return;

        float launchRad = launchAngleDeg * (Mathf.Pi / 180f);
        float g = gravity;
        float dY = targetPos.Y - launchPos.Y; // negative when throwing downward

        CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
            out float _, out float hSpeed, out float vy);
        float totalTime = D / hSpeed;

        // Direction (in world XZ plane)
        float dirX = dx / D;
        float dirZ = dz / D;
        float perpX = -dirZ * RibbonWidth;
        float perpZ = dirX * RibbonWidth;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.TriangleStrip);

        for (int i = 0; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            float time = t * totalTime;
            float distAtT = hSpeed * time;
            float heightAtT = launchPos.Y + vy * time - 0.5f * g * time * time;

            float wx, wy, wz;
            if (i == Segments)
            {
                // Last vertex snaps to exact target
                wx = targetPos.X;
                wy = targetPos.Y;
                wz = targetPos.Z;
            }
            else
            {
                float fwdX = launchPos.X + dirX * distAtT;
                float fwdZ = launchPos.Z + dirZ * distAtT;
                wx = fwdX;
                wy = heightAtT;
                wz = fwdZ;
            }

            st.AddVertex(new Vector3(wx + perpX, wy, wz + perpZ));
            st.AddVertex(new Vector3(wx - perpX, wy, wz - perpZ));
        }

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.3f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };

        var mesh = st.Commit();
        if (mesh != null) mesh.SurfaceSetMaterial(0, mat);

        _mesh = new MeshInstance3D { Mesh = mesh, Name = "ArcLine" };
        parent.AddChild(_mesh);
    }
}
