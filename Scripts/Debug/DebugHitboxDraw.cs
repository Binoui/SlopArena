#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Debug visualization for hitboxes (red) and hurtboxes (blue).
/// Attach as child of a PlayerController (or any entity with a simulation).
/// Renders wireframe spheres using ImmediateMesh + 3D labels.
/// </summary>
public partial class DebugHitboxDraw : MeshInstance3D
{
    private ImmediateMesh _mesh = null!;
    private List<Label3D> _labels = new();
    private int _labelPoolSize = 0;

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

        int labelIndex = 0;

        // ── Draw hitboxes (red) ──
        if (hitboxes.Count > 0)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _mesh.SurfaceSetColor(new Color(1f, 0.2f, 0.2f, 0.8f)); // red
            foreach (var hb in hitboxes)
            {
                float x = hb.X - worldOffset.X;
                float y = hb.Y - worldOffset.Y;
                float z = hb.Z - worldOffset.Z;
                DrawWireSphere(_mesh, x, y, z, hb.Radius, 12);

                // Label: DMG, KB, Ticks
                string labelText = $"HIT\nDMG:{hb.Damage:F1}\nKB:{hb.KnockbackForce:F1}\n{hb.AgeTicks}/{hb.DurationTicks}t";
                UpdateLabel(labelIndex++, new Vector3(x, y + hb.Radius + 0.3f, z), labelText, new Color(1f, 0.3f, 0.3f));
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
                float localX = x - worldOffset.X;
                float localY = y - worldOffset.Y;
                float localZ = z - worldOffset.Z;
                DrawWireSphere(_mesh, localX, localY, localZ, radius, 12);

                // Label: HURT + radius
                string labelText = $"HURT\nR:{radius:F1}";
                UpdateLabel(labelIndex++, new Vector3(localX, localY + radius + 0.3f, localZ), labelText, new Color(0.3f, 0.6f, 1f));
            }
            _mesh.SurfaceEnd();
        }

        // Hide unused labels
        for (int i = labelIndex; i < _labelPoolSize; i++)
        {
            _labels[i].Visible = false;
        }
    }

    private void UpdateLabel(int index, Vector3 position, string text, Color color)
    {
        // Expand pool if needed
        while (index >= _labelPoolSize)
        {
            var label = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                PixelSize = 0.003f,
                OutlineSize = 4,
                OutlineModulate = new Color(0, 0, 0, 0.7f),
                Font = ThemeDB.FallbackFont,
                FontSize = 24,
            };
            AddChild(label);
            _labels.Add(label);
            _labelPoolSize++;
        }

        var lbl = _labels[index];
        lbl.Visible = true;
        lbl.Position = position;
        lbl.Text = text;
        lbl.Modulate = color;
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
