#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Debug visualization for hitboxes (red) and hurtboxes (blue).
/// Attach as child of a PlayerController (or any entity with a simulation).
/// Renders wireframe spheres using ImmediateMesh.
/// </summary>
public partial class DebugHitboxDraw : MeshInstance3D
{
    private ImmediateMesh _mesh = null!;

    public override void _Ready()
    {
        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        // Material with vertex colors enabled
        var mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        MaterialOverride = mat;
    }

    public void UpdateHitboxes(List<SlopArena.Shared.Hitbox> hitboxes, List<(float x, float y, float z, float radius)> hurtboxes, Vector3 worldOffset)
    {
        _mesh.ClearSurfaces();

        // ── Draw hitboxes (red) ──
        if (hitboxes.Count > 0)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _mesh.SurfaceSetColor(new Color(1f, 0.2f, 0.2f, 0.8f)); // red
            foreach (var hb in hitboxes)
            {
                DrawWireSphere(_mesh, hb.X - worldOffset.X, hb.Y - worldOffset.Y, hb.Z - worldOffset.Z, hb.Radius, 12);
            }
            _mesh.SurfaceEnd();
        }

        // ── Draw hurtboxes (blue) ──
        if (hurtboxes.Count > 0)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _mesh.SurfaceSetColor(new Color(0.2f, 0.5f, 1f, 0.8f)); // blue
            foreach (var (x, y, z, radius) in hurtboxes)
            {
                DrawWireSphere(_mesh, x - worldOffset.X, y - worldOffset.Y, z - worldOffset.Z, radius, 12);
            }
            _mesh.SurfaceEnd();
        }
    }

    private static void DrawWireSphere(ImmediateMesh mesh, float cx, float cy, float cz, float radius, int rings)
    {
        if (radius <= 0.001f) return;

        // Horizontal ring (XZ plane)
        for (int i = 0; i < rings; i++)
        {
            float a1 = Mathf.Tau * i / rings;
            float a2 = Mathf.Tau * (i + 1) / rings;
            mesh.SurfaceAddVertex(new Vector3(cx + Mathf.Cos(a1) * radius, cy, cz + Mathf.Sin(a1) * radius));
            mesh.SurfaceAddVertex(new Vector3(cx + Mathf.Cos(a2) * radius, cy, cz + Mathf.Sin(a2) * radius));
        }

        // Vertical ring (XY plane)
        for (int i = 0; i < rings; i++)
        {
            float a1 = Mathf.Tau * i / rings;
            float a2 = Mathf.Tau * (i + 1) / rings;
            mesh.SurfaceAddVertex(new Vector3(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius, cz));
            mesh.SurfaceAddVertex(new Vector3(cx + Mathf.Cos(a2) * radius, cy + Mathf.Sin(a2) * radius, cz));
        }

        // Vertical ring (YZ plane)
        for (int i = 0; i < rings; i++)
        {
            float a1 = Mathf.Tau * i / rings;
            float a2 = Mathf.Tau * (i + 1) / rings;
            mesh.SurfaceAddVertex(new Vector3(cx, cy + Mathf.Cos(a1) * radius, cz + Mathf.Sin(a1) * radius));
            mesh.SurfaceAddVertex(new Vector3(cx, cy + Mathf.Cos(a2) * radius, cz + Mathf.Sin(a2) * radius));
        }
    }
}
