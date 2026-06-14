#nullable enable
using Godot;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Debug visualization for hitboxes (red) and hurtboxes (blue).
/// Supports Sphere and Capsule shapes via ImmediateMesh wireframe.
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
        CastShadow = ShadowCastingSetting.Off;

        var mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        MaterialOverride = mat;
    }

    public void UpdateHitboxes(
        List<Hitbox> hitboxes,
        List<SpellResolver.EntityData> localEntities,
        List<SpellResolver.EntityData> serverEntities,
        Vector3 worldOffset)
    {
        _mesh.ClearSurfaces();
        int labelIndex = 0;

        // ── Hitboxes (red spheres) ──
        if (hitboxes.Count > 0)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            _mesh.SurfaceSetColor(new Color(1f, 0.2f, 0.2f, 0.8f));
            foreach (var hb in hitboxes)
            {
                float x = hb.X - worldOffset.X;
                float y = hb.Y - worldOffset.Y;
                float z = hb.Z - worldOffset.Z;

                if (hb.Shape == HitboxShape.Capsule)
                    DrawWireCapsule(_mesh, x, y, z, hb.EndX - worldOffset.X, hb.EndY - worldOffset.Y, hb.EndZ - worldOffset.Z, hb.Radius, 12);
                else
                    DrawWireSphere(_mesh, x, y, z, hb.Radius, 12);

                string labelText = $"HIT\nDMG:{hb.Damage:F1}\nKB:{hb.KnockbackForce:F1}\n{hb.AgeTicks}/{hb.DurationTicks}t";
                UpdateLabel(labelIndex++, new Vector3(x, y + hb.Radius + 0.3f, z), labelText, new Color(1f, 0.3f, 0.3f));
            }
            _mesh.SurfaceEnd();
        }

        // ── Local hurtboxes (blue — client prediction) ──
        DrawHurtboxList(localEntities, worldOffset, new Color(0.2f, 0.5f, 1f, 0.8f), "LOCAL", ref labelIndex);

        // ── Server ghost hurtboxes (green — server authority) ──
        DrawHurtboxList(serverEntities, worldOffset, new Color(0.2f, 1f, 0.3f, 0.6f), "SRV", ref labelIndex);

        // Hide unused labels
        for (int i = labelIndex; i < _labelPoolSize; i++)
            _labels[i].Visible = false;
    }

    private void DrawHurtboxList(List<SpellResolver.EntityData> entities, Vector3 worldOffset, Color color, string prefix, ref int labelIndex)
    {
        if (entities.Count == 0) return;

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        _mesh.SurfaceSetColor(color);
        foreach (var e in entities)
        {
            float lx = e.PosX - worldOffset.X;
            float ly = e.PosY - worldOffset.Y;
            float lz = e.PosZ - worldOffset.Z;

            if (e.Shape == HitboxShape.Capsule)
            {
                float lex = e.EndX - worldOffset.X;
                float ley = e.EndY - worldOffset.Y;
                float lez = e.EndZ - worldOffset.Z;
                DrawWireCapsule(_mesh, lx, ly, lz, lex, ley, lez, e.Radius, 12);
            }
            else
            {
                DrawWireSphere(_mesh, lx, ly, lz, e.Radius, 12);
            }

            //UpdateLabel(labelIndex++, new Vector3(lx, ly + e.Radius + 0.3f, lz), $"{prefix}\nR:{e.Radius:F1}\n{e.Id}", color);
        }
        _mesh.SurfaceEnd();
    }

    private void UpdateLabel(int index, Vector3 position, string text, Color color)
    {
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

    // ── Wireframe primitives ──

    private static void DrawWireSphere(ImmediateMesh mesh, float cx, float cy, float cz, float radius, int rings)
    {
        if (radius <= 0.001f) return;
        for (int i = 0; i < rings; i++)
        {
            float a1 = Mathf.Tau * i / rings;
            float a2 = Mathf.Tau * (i + 1) / rings;
            // XZ ring
            mesh.SurfaceAddVertex(new Vector3(cx + (Mathf.Cos(a1) * radius), cy, cz + (Mathf.Sin(a1) * radius)));
            mesh.SurfaceAddVertex(new Vector3(cx + (Mathf.Cos(a2) * radius), cy, cz + (Mathf.Sin(a2) * radius)));
            // XY ring
            mesh.SurfaceAddVertex(new Vector3(cx + (Mathf.Cos(a1) * radius), cy + (Mathf.Sin(a1) * radius), cz));
            mesh.SurfaceAddVertex(new Vector3(cx + (Mathf.Cos(a2) * radius), cy + (Mathf.Sin(a2) * radius), cz));
            // YZ ring
            mesh.SurfaceAddVertex(new Vector3(cx, cy + (Mathf.Cos(a1) * radius), cz + (Mathf.Sin(a1) * radius)));
            mesh.SurfaceAddVertex(new Vector3(cx, cy + (Mathf.Cos(a2) * radius), cz + (Mathf.Sin(a2) * radius)));
        }
    }

    private static void DrawWireCapsule(ImmediateMesh mesh, float sx, float sy, float sz,
        float ex, float ey, float ez, float radius, int segments)
    {
        Vector3 start = new(sx, sy, sz);
        Vector3 end = new(ex, ey, ez);
        Vector3 dir = end - start;
        float len = dir.Length();
        if (len < 0.001f)
        {
            DrawWireSphere(mesh, sx, sy, sz, radius, segments);
            return;
        }
        Vector3 ax = dir.Normalized();
        // Find two perpendicular axes for the rings
        Vector3 perp1 = Mathf.Abs(ax.X) < 0.9f ? new Vector3(1, 0, 0).Cross(ax).Normalized() : new Vector3(0, 1, 0).Cross(ax).Normalized();
        Vector3 perp2 = ax.Cross(perp1).Normalized();

        // Start cap ring
        for (int i = 0; i < segments; i++)
        {
            float a1 = Mathf.Tau * i / segments;
            float a2 = Mathf.Tau * (i + 1) / segments;
            Vector3 p1 = start + (perp1 * Mathf.Cos(a1) * radius) + (perp2 * Mathf.Sin(a1) * radius);
            Vector3 p2 = start + (perp1 * Mathf.Cos(a2) * radius) + (perp2 * Mathf.Sin(a2) * radius);
            mesh.SurfaceAddVertex(p1); mesh.SurfaceAddVertex(p2);
        }
        // End cap ring
        for (int i = 0; i < segments; i++)
        {
            float a1 = Mathf.Tau * i / segments;
            float a2 = Mathf.Tau * (i + 1) / segments;
            Vector3 p1 = end + (perp1 * Mathf.Cos(a1) * radius) + (perp2 * Mathf.Sin(a1) * radius);
            Vector3 p2 = end + (perp1 * Mathf.Cos(a2) * radius) + (perp2 * Mathf.Sin(a2) * radius);
            mesh.SurfaceAddVertex(p1); mesh.SurfaceAddVertex(p2);
        }
        // 4 connecting lines
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4;
            Vector3 off = (perp1 * Mathf.Cos(a) * radius) + (perp2 * Mathf.Sin(a) * radius);
            mesh.SurfaceAddVertex(start + off);
            mesh.SurfaceAddVertex(end + off);
        }
    }
}
