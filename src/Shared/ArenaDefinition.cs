using System;
using System.Linq;

namespace SlopArena.Shared
{
    [Serializable]
    public struct SpawnPoint
    {
        public float X, Y, Z;
        /// <summary>
        /// facing direction in radians
        /// </summary>
        public float Yaw;
    }

    /// <summary>
    /// Precomputed 2D heightfield for fast ground-surface lookup.
    /// Built from collision triangles at bake time.
    /// Each cell stores the highest surface Y at that XZ position.
    /// Bilinear interpolation between 4 neighboring cells on Sample().
    /// </summary>
    [Serializable]
    public struct ArenaHeightmap
    {
        /// <summary>Number of cells along the X axis (columns).</summary>
        public int Width;
        /// <summary>Number of cells along the Z axis (rows).</summary>
        public int Height;
        /// <summary>World-space size of each cell in meters.</summary>
        public float CellSize;
        /// <summary>World-space X of the grid origin (min corner).</summary>
        public float OriginX;
        /// <summary>World-space Z of the grid origin (min corner).</summary>
        public float OriginZ;
        /// <summary>Height values, row-major: index = z * Width + x. float.MinValue = no surface.</summary>
        public float[] Data;

        /// <summary>
        /// Sample the ground surface Y at a world XZ position.
        /// Uses bilinear interpolation between the 4 nearest cells.
        /// Returns float.MinValue if outside the grid or no surface data.
        /// </summary>
        public readonly float Sample(float px, float pz)
        {
            if (Data == null || Width == 0 || Height == 0) return float.MinValue;

            float fx = (px - OriginX) / CellSize;
            float fz = (pz - OriginZ) / CellSize;

            int x0 = (int)MathF.Floor(fx);
            int z0 = (int)MathF.Floor(fz);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            if (x0 < 0 || x1 >= Width || z0 < 0 || z1 >= Height) return float.MinValue;

            float tx = fx - x0;
            float tz = fz - z0;

            float y00 = Data[z0 * Width + x0];
            float y10 = Data[z0 * Width + x1];
            float y01 = Data[z1 * Width + x0];
            float y11 = Data[z1 * Width + x1];

            // Treat MinValue cells as holes — skip in interpolation
            float top = float.MinValue, bot = float.MinValue;
            if (y00 > float.MinValue && y10 > float.MinValue) top = y00 + (y10 - y00) * tx;
            else if (y00 > float.MinValue) top = y00;
            else if (y10 > float.MinValue) top = y10;

            if (y01 > float.MinValue && y11 > float.MinValue) bot = y01 + (y11 - y01) * tx;
            else if (y01 > float.MinValue) bot = y01;
            else if (y11 > float.MinValue) bot = y11;

            if (top > float.MinValue && bot > float.MinValue) return top + (bot - top) * tz;
            if (top > float.MinValue) return top;
            if (bot > float.MinValue) return bot;
            return float.MinValue;
        }
    }

    /// <summary>
    /// A single collision triangle in world space.
    /// Three vertices forming a solid surface the server checks for character collision.
    /// All coordinates are in meters. Normal is computed at runtime.
    /// </summary>
    [Serializable]
    public struct CollisionTriangle
    {
        public float AX, AY, AZ;   // vertex A
        public float BX, BY, BZ;   // vertex B
        public float CX, CY, CZ;   // vertex C
    }

    /// <summary>
    /// Spatial grid for broad-phase collision queries.
    /// Pre-computed from CollisionTriangles at arena load time.
    /// Cells are uniform cubes over the arena bounding box.
    /// </summary>
    [Serializable]
    public struct CollisionGrid
    {
        /// <summary>Size of each cell in world units.</summary>
        public float CellSize;
        /// <summary>Number of cells along each axis.</summary>
        public int CellsX, CellsY, CellsZ;
        /// <summary>Origin of the grid (min corner of the covered volume).</summary>
        public float OriginX, OriginY, OriginZ;
        /// <summary>Prefix-sum into CellTriangles: cell i covers indices CellStarts[i]..CellStarts[i+1]-1.</summary>
        public int[] CellStarts;
        /// <summary>Triangle indices belonging to each cell.</summary>
        public int[] CellTriangles;
    }

    /// <summary>
    /// Pure data definition of an arena. No Godot types.
    /// Used by both client (ArenaManager) and server (for bounds/spawning).
    ///
    /// Blast Zone Design:
    /// - KillHeight defines the blast zone (Y coordinate below which = elimination)
    /// - Each map can have different blast zones for balance
    /// - Smaller stages typically have closer blast zones (easier to KO)
    /// - Larger stages can have farther blast zones (longer survival, more comeback potential)
    /// - Example: Final Destination (flat) vs Battlefield (platforms) in Smash
    /// </summary>
    [Serializable]
    public struct ArenaDefinition
    {
        /// <summary>
        /// machine-readable key
        /// </summary>
        public string Name;
        /// <summary>
        /// human-readable
        /// </summary>
        public string DisplayName;
        /// <summary>Hex color string for stage select card placeholder e.g. "#2a4a2a"</summary>
        public string PreviewColor;
        /// <summary>
        /// res://assets/arenas/xxx.tscn
        /// </summary>
        public string ScenePath;
        /// <summary>
        /// Y below this = BLAST ZONE (instant elimination)
        /// </summary>
        public float KillHeight;
        /// <summary>
        /// Precomputed heightmap for fast ground-surface lookup.
        /// Replaces PlatformDef[] + FloorHeight. Built from collision triangles at bake time.
        /// Null for hardcoded arenas that haven't been baked yet.
        /// </summary>
        public ArenaHeightmap Heightmap;
        /// <summary>
        /// arena bounds X (used for camera/mechanics)
        /// </summary>
        public float MinX, MaxX;
        /// <summary>
        /// arena bounds Z (used for camera/mechanics)
        /// </summary>
        public float MinZ, MaxZ;
        public SpawnPoint[] SpawnPoints;
        /// <summary>
        /// Collision triangle mesh for server-side stage collision.
        /// Characters collide with these triangles during movement and knockback.
        /// Baked from Unity scene colliders, loaded by server for client-side prediction + server authority.
        /// </summary>
        public CollisionTriangle[] CollisionTriangles;
        /// <summary>
        /// Pre-computed spatial grid for broad-phase collision queries.
        /// Built from CollisionTriangles when the arena is loaded.
        /// Null until BuildSpatialGrid() is called (happens in ArenaRegistry on arena load).
        /// </summary>
        public CollisionGrid SpatialGrid;
    }

    /// <summary>
    /// Utility methods for ArenaDefinition.
    /// </summary>
    public static class ArenaCollision
    {
        /// <summary>
        /// Build a spatial grid from the arena's collision triangles.
        /// Grid cells are cubes of the given size. Each triangle is assigned to every cell
        /// its bounding box overlaps. The result is stored in arena.SpatialGrid.
        /// If CollisionTriangles is null or empty, SpatialGrid is left as default.
        /// </summary>
        public static CollisionGrid BuildSpatialGrid(in ArenaDefinition arena, float cellSize = 4f)
        {
            if (arena.CollisionTriangles == null || arena.CollisionTriangles.Length == 0)
                return default;

            var tris = arena.CollisionTriangles;

            // Compute grid dimensions from arena bounds + triangle extent
            float minY = float.MaxValue, maxY = float.MinValue;
            float minX = arena.MinX, maxX = arena.MaxX;
            float minZ = arena.MinZ, maxZ = arena.MaxZ;
            for (int i = 0; i < tris.Length; i++)
            {
                var t = tris[i];
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                if (tMinY < minY) minY = tMinY;
                if (tMaxY > maxY) maxY = tMaxY;
            }

            // Ensure min height spans the full expected play area
            if (minY > arena.KillHeight) minY = arena.KillHeight;
            float surfaceMaxY = arena.Heightmap.Data != null && arena.Heightmap.Data.Length > 0
                ? arena.Heightmap.Data.Max()
                : arena.KillHeight + 20f;
            if (maxY < surfaceMaxY + 20f) maxY = surfaceMaxY + 20f;

            int cellsX = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellSize));
            int cellsY = Math.Max(1, (int)MathF.Ceiling((maxY - minY) / cellSize));
            int cellsZ = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cellSize));
            int totalCells = cellsX * cellsY * cellsZ;

            // Count triangles per cell (first pass) and assign (second pass)
            var counts = new int[totalCells];

            for (int ti = 0; ti < tris.Length; ti++)
            {
                var t = tris[ti];
                float tMinX = t.AX; if (t.BX < tMinX) tMinX = t.BX; if (t.CX < tMinX) tMinX = t.CX;
                float tMaxX = t.AX; if (t.BX > tMaxX) tMaxX = t.BX; if (t.CX > tMaxX) tMaxX = t.CX;
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                float tMinZ = t.AZ; if (t.BZ < tMinZ) tMinZ = t.BZ; if (t.CZ < tMinZ) tMinZ = t.CZ;
                float tMaxZ = t.AZ; if (t.BZ > tMaxZ) tMaxZ = t.BZ; if (t.CZ > tMaxZ) tMaxZ = t.CZ;

                int ixMin = (int)((tMinX - minX) / cellSize);
                int ixMax = (int)((tMaxX - minX) / cellSize);
                int iyMin = (int)((tMinY - minY) / cellSize);
                int iyMax = (int)((tMaxY - minY) / cellSize);
                int izMin = (int)((tMinZ - minZ) / cellSize);
                int izMax = (int)((tMaxZ - minZ) / cellSize);
                ClampRange(ref ixMin, ref ixMax, 0, cellsX - 1);
                ClampRange(ref iyMin, ref iyMax, 0, cellsY - 1);
                ClampRange(ref izMin, ref izMax, 0, cellsZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                    for (int iy = iyMin; iy <= iyMax; iy++)
                        for (int ix = ixMin; ix <= ixMax; ix++)
                            counts[iz * cellsX * cellsY + iy * cellsX + ix]++;
            }

            // Build prefix sum
            var starts = new int[totalCells + 1];
            for (int i = 0; i < totalCells; i++)
                starts[i + 1] = starts[i] + counts[i];

            var triIndices = new int[starts[totalCells]];
            var nextSlot = new int[totalCells];
            Array.Copy(starts, 0, nextSlot, 0, totalCells);
            // nextSlot[i] now tracks the current write position for cell i
            // (starts[i] is the start, nextSlot[i] advanced during assignment)

            for (int ti = 0; ti < tris.Length; ti++)
            {
                var t = tris[ti];
                float tMinX = t.AX; if (t.BX < tMinX) tMinX = t.BX; if (t.CX < tMinX) tMinX = t.CX;
                float tMaxX = t.AX; if (t.BX > tMaxX) tMaxX = t.BX; if (t.CX > tMaxX) tMaxX = t.CX;
                float tMinY = t.AY; if (t.BY < tMinY) tMinY = t.BY; if (t.CY < tMinY) tMinY = t.CY;
                float tMaxY = t.AY; if (t.BY > tMaxY) tMaxY = t.BY; if (t.CY > tMaxY) tMaxY = t.CY;
                float tMinZ = t.AZ; if (t.BZ < tMinZ) tMinZ = t.BZ; if (t.CZ < tMinZ) tMinZ = t.CZ;
                float tMaxZ = t.AZ; if (t.BZ > tMaxZ) tMaxZ = t.BZ; if (t.CZ > tMaxZ) tMaxZ = t.CZ;

                int ixMin = (int)((tMinX - minX) / cellSize);
                int ixMax = (int)((tMaxX - minX) / cellSize);
                int iyMin = (int)((tMinY - minY) / cellSize);
                int iyMax = (int)((tMaxY - minY) / cellSize);
                int izMin = (int)((tMinZ - minZ) / cellSize);
                int izMax = (int)((tMaxZ - minZ) / cellSize);
                ClampRange(ref ixMin, ref ixMax, 0, cellsX - 1);
                ClampRange(ref iyMin, ref iyMax, 0, cellsY - 1);
                ClampRange(ref izMin, ref izMax, 0, cellsZ - 1);

                for (int iz = izMin; iz <= izMax; iz++)
                    for (int iy = iyMin; iy <= iyMax; iy++)
                        for (int ix = ixMin; ix <= ixMax; ix++)
                        {
                            int cell = iz * cellsX * cellsY + iy * cellsX + ix;
                            triIndices[nextSlot[cell]++] = ti;
                        }
            }

            Console.WriteLine($"[ArenaCollision] Built spatial grid for '{arena.Name}': " +
                $"{tris.Length} triangles, {totalCells} cells ({cellsX}×{cellsY}×{cellsZ}), " +
                $"avg {starts[totalCells] / (float)totalCells:F1} tri/cell");

            return new CollisionGrid
            {
                CellSize = cellSize,
                CellsX = cellsX,
                CellsY = cellsY,
                CellsZ = cellsZ,
                OriginX = minX,
                OriginY = minY,
                OriginZ = minZ,
                CellStarts = starts,
                CellTriangles = triIndices,
            };
        }

        private static void ClampRange(ref int lo, ref int hi, int min, int max)
        {
            if (lo < min) lo = min;
            if (hi > max) hi = max;
        }
    }

    public static class ArenaRegistry
    {
        private static ArenaDefinition[] _hardcoded = null!;
        private static bool _initialized = false;

        public static ArenaDefinition[] All
        {
            get
            {
                EnsureInitialized();
                return _hardcoded;
            }
        }

        /// <summary>
        /// Try to load arena definitions from .arena files in a directory.
        /// Falls back to hardcoded data if no files found or on error.
        /// Call once at startup, e.g.: ArenaRegistry.LoadFromDirectory("data/arenas");
        /// </summary>
        public static void LoadFromDirectory(string directoryPath)
        {
            try
            {
                if (!System.IO.Directory.Exists(directoryPath))
                {
                    Console.WriteLine($"[ArenaRegistry] Directory not found: {directoryPath} — using hardcoded arenas");
                    EnsureInitialized();
                    return;
                }

                var files = System.IO.Directory.GetFiles(directoryPath, "*.arena");
                if (files.Length == 0)
                {
                    Console.WriteLine($"[ArenaRegistry] No .arena files in {directoryPath} — using hardcoded arenas");
                    EnsureInitialized();
                    return;
                }

                var loaded = new System.Collections.Generic.List<ArenaDefinition>();
                foreach (var file in files)
                {
                    var arena = ArenaBinaryFormat.LoadFromFile(file);
                    if (arena.HasValue)
                    {
                        var a = arena.Value;
                        a.SpatialGrid = ArenaCollision.BuildSpatialGrid(in a);
                        loaded.Add(a);
                        Console.WriteLine($"[ArenaRegistry] Loaded: {a.Name} ({file})");
                    }
                    else
                    {
                        Console.WriteLine($"[ArenaRegistry] Failed to load: {file}");
                    }
                }

                if (loaded.Count > 0)
                {
                    _hardcoded = loaded.ToArray();
                    _initialized = true;
                    Console.WriteLine($"[ArenaRegistry] Loaded {loaded.Count} arenas from {directoryPath}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArenaRegistry] Error loading from {directoryPath}: {ex.Message}");
            }

            // Fallback
            EnsureInitialized();
        }

        public static ArenaDefinition Get(string name)
        {
            EnsureInitialized();
            foreach (var a in _hardcoded)
                if (a.Name == name) return a;
            return _hardcoded[0];
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            var arenas = BuildAll();
            for (int i = 0; i < arenas.Length; i++)
            {
                arenas[i].SpatialGrid = ArenaCollision.BuildSpatialGrid(in arenas[i]);
            }
            _hardcoded = arenas;
            _initialized = true;
        }

        private static ArenaDefinition[] BuildAll()
        {
            return new[]
            {
                // The Pit: Large stage → deeper blast zone for longer matches
                // CSGBox3D floor at position (40, -1, 40) with size (80, 2, 80)
                // Surface Y = -1 + 1 = 0
                new ArenaDefinition
                {
                    Name = "pit",
                    DisplayName = "The Pit",
                    PreviewColor = "#1a2e1a",
                    ScenePath = "res://assets/arenas/arena_pit.tscn",
                    KillHeight = -15f,  // Deep blast zone (80x80 large stage)
                    MinX = 0f, MaxX = 80f,
                    MinZ = 0f, MaxZ = 80f,
                    SpawnPoints = new[]
                    {
                        new SpawnPoint { X = 10f, Y = 0.5f, Z = 10f, Yaw = 0f },
                        new SpawnPoint { X = 70f, Y = 0.5f, Z = 10f, Yaw = MathF.PI },
                        new SpawnPoint { X = 10f, Y = 0.5f, Z = 70f, Yaw = 0f },
                        new SpawnPoint { X = 70f, Y = 0.5f, Z = 70f, Yaw = MathF.PI },
                        new SpawnPoint { X = 40f, Y = 0.5f, Z = 15f, Yaw = 0f },
                        new SpawnPoint { X = 40f, Y = 0.5f, Z = 65f, Yaw = MathF.PI },
                    }
                },
                // Crossroads: Medium stage → balanced blast zone
                // CSGBox3D floors at position Y=-1 with size (..., 2, ...)
                // All surface Y = 0 (cross shape: center + 4 arms)
                new ArenaDefinition
                {
                    Name = "cross",
                    DisplayName = "Crossroads",
                    PreviewColor = "#1a2a3e",
                    ScenePath = "res://assets/arenas/arena_cross.tscn",
                    KillHeight = -10f,  // Medium blast zone (60x60 balanced stage)
                    MinX = 0f, MaxX = 60f,
                    MinZ = 0f, MaxZ = 60f,
                    SpawnPoints = new[]
                    {
                        new SpawnPoint { X = 30f, Y = 0.5f, Z = 5f, Yaw = MathF.PI },
                        new SpawnPoint { X = 30f, Y = 0.5f, Z = 55f, Yaw = 0f },
                        new SpawnPoint { X = 5f, Y = 0.5f, Z = 30f, Yaw = MathF.PI / 2f },
                        new SpawnPoint { X = 55f, Y = 0.5f, Z = 30f, Yaw = -MathF.PI / 2f },
                        new SpawnPoint { X = 30f, Y = 0.5f, Z = 30f, Yaw = 0f },
                        new SpawnPoint { X = 22f, Y = 0.5f, Z = 22f, Yaw = 0f },
                    }
                },
                // The Split: Small competitive stage → shallow blast zone for fast KOs
                // Floor: 4 lower quadrants at Y=0
                // UpperCenter platform at Y=3, ramps at Y=1
                new ArenaDefinition
                {
                    Name = "split",
                    DisplayName = "The Split",
                    PreviewColor = "#2e1a1a",
                    ScenePath = "res://assets/arenas/arena_split.tscn",
                    KillHeight = -6f,   // Shallow blast zone (60x60 small competitive stage)
                    MinX = 0f, MaxX = 60f,
                    MinZ = 0f, MaxZ = 60f,
                    SpawnPoints = new[]
                    {
                        new SpawnPoint { X = 15f, Y = 1.5f, Z = 15f, Yaw = 0f },
                        new SpawnPoint { X = 45f, Y = 1.5f, Z = 15f, Yaw = MathF.PI },
                        new SpawnPoint { X = 15f, Y = 1.5f, Z = 45f, Yaw = 0f },
                        new SpawnPoint { X = 45f, Y = 1.5f, Z = 45f, Yaw = MathF.PI },
                        new SpawnPoint { X = 30f, Y = 3.5f, Z = 30f, Yaw = 0f },
                        new SpawnPoint { X = 22f, Y = 1.5f, Z = 30f, Yaw = 0f },
                    }
                },
                // Training Room: Flat floor, no platforms, single spawn at center
                new ArenaDefinition
                {
                    Name = "training",
                    DisplayName = "Training Room",
                    PreviewColor = "#1a1a1a",
                    KillHeight = -15f,
                    MinX = -25f, MaxX = 25f,
                    MinZ = -25f, MaxZ = 25f,
                    SpawnPoints = new[]
                    {
                        new SpawnPoint { X = 0f, Y = 5f, Z = 0f, Yaw = 0f },
                        new SpawnPoint { X = 10f, Y = 0f, Z = 0f, Yaw = MathF.PI },
                    }
                },
                // Sanctum: Large multi-level arena
                // Floor at Y=0, CentralPlatform at Y=5, Galleries at Y=8, CenterPiece at Y=6
                new ArenaDefinition
                {
                    Name = "sanctum",
                    DisplayName = "Sanctum",
                    PreviewColor = "#2a1a2e",
                    ScenePath = "res://assets/arenas/arena_sanctum.tscn",
                    KillHeight = -20f,
                    MinX = 0f, MaxX = 200f,
                    MinZ = 0f, MaxZ = 200f,
                    SpawnPoints = new[]
                    {
                        new SpawnPoint { X = 100f, Y = 1.5f, Z = 50f, Yaw = MathF.PI },
                        new SpawnPoint { X = 100f, Y = 1.5f, Z = 150f, Yaw = 0f },
                        new SpawnPoint { X = 50f, Y = 1.5f, Z = 100f, Yaw = MathF.PI / 2f },
                        new SpawnPoint { X = 150f, Y = 1.5f, Z = 100f, Yaw = -MathF.PI / 2f },
                        new SpawnPoint { X = 100f, Y = 6.5f, Z = 100f, Yaw = 0f },
                        new SpawnPoint { X = 100f, Y = 9.5f, Z = 100f, Yaw = 0f },
                    }
                },
            };
        }
    }
}
