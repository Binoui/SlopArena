#nullable enable
using Godot;
using SlopArena.Shared;
using System;
using System.Collections.Generic;

/// <summary>
/// Godot tool that bakes arena .tscn files to .arena binary data files.
///
/// How it works:
///   1. Scans res://assets/arenas/ for all .tscn files
///   2. For each, loads the scene and walks the node tree
///   3. Finds CSGBox3D nodes with use_collision=true
///   4. Detects walkable surfaces (thin horizontal boxes) vs walls/obstacles
///   5. Finds Marker3D nodes named "SpawnPoint" for spawn positions
///   6. Writes a .arena binary file to res://data/arenas/
///
/// Usage: Open tools/bake_arenas.tscn in Godot and run the scene.
/// </summary>
[Tool]
public partial class ArenaBaker : Node
{
    /// <summary>
    /// Max ratio of size.Y to the largest horizontal dimension.
    /// Boxes with a higher ratio are considered walls/pillars, not platforms.
    /// </summary>
    [Export]
    public float MaxVerticalAspectRatio { get; set; } = 0.33f;

    /// <summary>
    /// Directory containing arena .tscn files.
    /// </summary>
    [Export]
    public string ArenaSourceDir { get; set; } = "res://assets/arenas/";

    /// <summary>
    /// Directory to write .arena files to.
    /// </summary>
    [Export]
    public string ArenaOutputDir { get; set; } = "res://data/arenas/";

    public override void _Ready()
    {
        if (!Engine.IsEditorHint())
        {
            // Allow running from scene for manual bake
            Callable.From(BakeAll).CallDeferred();
        }
    }

    /// <summary>Bake all arenas found in the source directory.</summary>
    public void BakeAll()
    {
        GD.Print("=== Arena Baker ===");

        var dir = DirAccess.Open(ArenaSourceDir);
        if (dir == null)
        {
            GD.PrintErr($"ArenaBaker: Cannot open source directory: {ArenaSourceDir}");
            return;
        }

        // Ensure output dir exists
        var outDir = DirAccess.Open(ArenaOutputDir);
        if (outDir == null)
        {
            DirAccess.MakeDirRecursiveAbsolute(
                ProjectSettings.GlobalizePath(ArenaOutputDir));
            GD.Print($"Created output directory: {ArenaOutputDir}");
        }

        dir.ListDirBegin();
        int baked = 0;
        int failed = 0;

        while (true)
        {
            string fileName = dir.GetNext();
            if (string.IsNullOrEmpty(fileName)) break;
            if (!fileName.EndsWith(".tscn")) continue;

            string scenePath = ArenaSourceDir.TrimEnd('/') + "/" + fileName;
            GD.Print($"\nBaking: {fileName}...");

            try
            {
                BakeArena(scenePath);
                baked++;
                GD.Print($"  ✓ {fileName}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"  ✗ {fileName}: {ex.Message}");
                failed++;
            }
        }

        dir.ListDirEnd();
        GD.Print($"\n=== Done: {baked} baked, {failed} failed ===");

        if (baked > 0 && failed == 0)
            GetTree().Quit();
    }

    private void BakeArena(string scenePath)
    {
        // Load scene as PackedScene
        var packedScene = ResourceLoader.Load<PackedScene>(scenePath);
        if (packedScene == null)
            throw new Exception($"Failed to load scene: {scenePath}");

        // Instantiate to inspect nodes
        Node? sceneRoot = packedScene.Instantiate();
        if (sceneRoot == null)
            throw new Exception("Failed to instantiate scene");

        try
        {
            // Extract arena name from filename (e.g., "arena_pit.tscn" → "pit")
            string fileName = scenePath.GetFile();
            string arenaName = fileName.Replace(".tscn", "").Replace("arena_", "");
            string displayName = ToDisplayName(arenaName);

            // Walk nodes
            var platforms = new List<PlatformDef>();
            float? floorHeight = null;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            var spawns = new List<SpawnPoint>();
            bool hasSpawnMarkers = false;

            WalkNodes(sceneRoot, new Transform3D(), platforms, ref floorHeight,
                      ref minX, ref maxX, ref minZ, ref maxZ, spawns, ref hasSpawnMarkers);

            if (platforms.Count == 0)
                throw new Exception("No walkable platforms found in scene");

            // Determine floor height (lowest platform surface)
            float actualFloorHeight = floorHeight ?? 0f;

            // Compute bounds from platforms if not set by markers
            if (minX == float.MaxValue) minX = 0f;
            if (maxX == float.MinValue) maxX = 80f;
            if (minZ == float.MaxValue) minZ = 0f;
            if (maxZ == float.MinValue) maxZ = 80f;

            // Auto-generate spawn points if none found
            if (spawns.Count == 0)
            {
                float cx = (minX + maxX) * 0.5f;
                float cz = (minZ + maxZ) * 0.5f;
                float halfW = (maxX - minX) * 0.3f;
                float halfD = (maxZ - minZ) * 0.3f;

                spawns.Add(new SpawnPoint { X = cx - halfW, Y = actualFloorHeight + 0.5f, Z = cz - halfD, Yaw = 0f });
                spawns.Add(new SpawnPoint { X = cx + halfW, Y = actualFloorHeight + 0.5f, Z = cz + halfD, Yaw = MathF.PI });
                spawns.Add(new SpawnPoint { X = cx - halfW, Y = actualFloorHeight + 0.5f, Z = cz + halfD, Yaw = 0f });
                spawns.Add(new SpawnPoint { X = cx + halfW, Y = actualFloorHeight + 0.5f, Z = cz - halfD, Yaw = MathF.PI });
            }

            var arena = new ArenaDefinition
            {
                Name = arenaName,
                DisplayName = displayName,
                ScenePath = scenePath,
                KillHeight = actualFloorHeight - 15f,
                FloorHeight = actualFloorHeight,
                Platforms = platforms.ToArray(),
                MinX = minX, MaxX = maxX,
                MinZ = minZ, MaxZ = maxZ,
                SpawnPoints = spawns.ToArray(),
            };

            // Write
            string outputPath = ArenaOutputDir.TrimEnd('/') + "/" + arenaName + ".arena";
            string globalPath = ProjectSettings.GlobalizePath(outputPath);
            ArenaBinaryFormat.SaveToFile(globalPath, arena);

            GD.Print($"  Platforms: {platforms.Count}, Spawns: {spawns.Count}");
            GD.Print($"  Floor Y: {actualFloorHeight:F1}, Kill Y: {arena.KillHeight:F0}");
            GD.Print($"  Bounds: X[{minX:F0}..{maxX:F0}] Z[{minZ:F0}..{maxZ:F0}]");
            GD.Print($"  Written: {outputPath}");
        }
        finally
        {
            sceneRoot.QueueFree();
        }
    }

    /// <summary>Recursively walk nodes to extract CSGBox3D platforms and spawn markers.</summary>
    private static void WalkNodes(
        Node node,
        Transform3D parentTransform,
        List<PlatformDef> platforms,
        ref float? floorHeight,
        ref float minX, ref float maxX,
        ref float minZ, ref float maxZ,
        List<SpawnPoint> spawns,
        ref bool hasSpawnMarkers)
    {
        // Compute this node's global transform
        Transform3D localTransform;
        if (node is Node3D node3D)
            localTransform = parentTransform * node3D.Transform;
        else
            localTransform = parentTransform;

        // Check for CSGBox3D with collision (check by class name since CSG module may not have C# bindings)
        string className = node.GetType().Name;
        if (className == "CSGBox3D" && (bool)node.Get("use_collision"))
        {
            var size = (Godot.Vector3)node.Get("size");
            float sx = Math.Abs(size.X);
            float sy = Math.Abs(size.Y);
            float sz = Math.Abs(size.Z);

            float maxHoriz = Math.Max(sx, sz);
            float aspectRatio = sy / Math.Max(maxHoriz, 0.001f);

            // Walkable platform = thin Y relative to XZ
            if (aspectRatio <= 0.33f)
            {
                // Get world-space position and size
                Vector3 worldPos = localTransform.Origin;
                // For a CSGBox3D with uniform scaling, the world size = local size * scale
                Vector3 scale = localTransform.Basis.Scale;
                float worldSx = sx * Math.Abs(scale.X);
                float worldSy = sy * Math.Abs(scale.Y);
                float worldSz = sz * Math.Abs(scale.Z);

                // Surface Y = center Y + half height
                float surfaceY = worldPos.Y + (worldSy * 0.5f);
                float halfX = worldSx * 0.5f;
                float halfZ = worldSz * 0.5f;

                platforms.Add(new PlatformDef
                {
                    CenterX = worldPos.X,
                    CenterZ = worldPos.Z,
                    HalfSizeX = halfX,
                    HalfSizeZ = halfZ,
                    SurfaceY = surfaceY,
                });

                // Track floor height (lowest platform surface)
                if (floorHeight == null || surfaceY < floorHeight.Value)
                    floorHeight = surfaceY;

                // Track world bounds
                float left = worldPos.X - halfX;
                float right = worldPos.X + halfX;
                float near = worldPos.Z - halfZ;
                float far = worldPos.Z + halfZ;
                if (left < minX) minX = left;
                if (right > maxX) maxX = right;
                if (near < minZ) minZ = near;
                if (far > maxZ) maxZ = far;
            }
        }

        // Check for SpawnPoint markers (Marker3D named "SpawnPoint" or starting with "Spawn")
        if (node is Marker3D marker)
        {
            string name = marker.Name.ToString().ToLower();
            if (name.Contains("spawn") || name.Contains("spawnpoint"))
            {
                Vector3 pos = localTransform.Origin;
                // Use forward direction (+Z in Godot) as default facing
                float yaw = 0f;
                // Try to extract yaw from rotation
                Vector3 forward = -localTransform.Basis.Z;
                if (forward.LengthSquared() > 0.001f)
                    yaw = MathF.Atan2(forward.X, forward.Z);

                spawns.Add(new SpawnPoint
                {
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    Yaw = yaw,
                });
                hasSpawnMarkers = true;
            }
        }

        // Recurse children
        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            var child = node.GetChild(i);
            WalkNodes(child, localTransform, platforms, ref floorHeight,
                      ref minX, ref maxX, ref minZ, ref maxZ, spawns, ref hasSpawnMarkers);
        }
    }

    /// <summary>Convert snake/kebab name to display name.</summary>
    private static string ToDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(" ", parts);
    }
}
