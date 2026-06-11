using Godot;
using System;

public static class HeightmapGenerator
{
    /// <summary>
    /// Perform raycasts from above the arena down through CSG collision geometry
    /// to generate a heightmap.bin file.
    /// </summary>
    public static void Generate(World3D world)
    {
        const int gridCount = 100;
        const float spacing = 200f / (gridCount - 1);
        float[] heights = new float[gridCount * gridCount];

        var spaceState = world.DirectSpaceState;
        if (spaceState == null)
        {
            GD.PrintErr("HeightmapGenerator: DirectSpaceState is null!");
            return;
        }

        int hitCount = 0;
        for (int y = 0; y < gridCount; y++)
        {
            for (int x = 0; x < gridCount; x++)
            {
                float px = x * spacing;
                float pz = y * spacing;

                var from = new Vector3(px, 2000f, pz);
                var to = new Vector3(px, -200f, pz);

                var query = PhysicsRayQueryParameters3D.Create(from, to);
                query.CollideWithAreas = false;
                query.CollideWithBodies = true;

                var result = spaceState.IntersectRay(query);
                if (result.Count > 0)
                {
                    Vector3 position = (Vector3)result["position"];
                    heights[(y * gridCount) + x] = position.Y;
                    hitCount++;
                }
                else
                {
                    heights[(y * gridCount) + x] = 0f;
                }
            }
        }

        string path = ProjectSettings.GlobalizePath("res://heightmap.bin");
        try
        {
            using (var file = System.IO.File.Create(path))
            using (var writer = new System.IO.BinaryWriter(file))
            {
                writer.Write(gridCount);
                writer.Write(spacing);
                for (int i = 0; i < heights.Length; i++)
                {
                    writer.Write(heights[i]);
                }
            }
            GD.Print($"Heightmap generated: {hitCount} hits, saved to {path}!");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to save heightmap: {ex.Message}");
        }
    }
}
