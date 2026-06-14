using System;

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
    /// A rectangular walkable surface in the arena.
    /// Defined by the top face of a CSGBox3D in the scene.
    /// SurfaceY is the walkable Y (top of the box).
    /// CenterX/CenterZ + HalfSizeX/HalfSizeZ define the XZ rectangle.
    /// </summary>
    [Serializable]
    public struct PlatformDef
    {
        public float CenterX, CenterZ;
        public float HalfSizeX, HalfSizeZ;
        /// <summary>Y of the walkable top surface</summary>
        public float SurfaceY;
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
        /// <summary>
        /// res://assets/arenas/xxx.tscn
        /// </summary>
        public string ScenePath;
        /// <summary>
        /// Y below this = BLAST ZONE (instant elimination)
        /// </summary>
        public float KillHeight;
        /// <summary>
        /// Y coordinate of the main floor surface (for ground collision)
        /// </summary>
        public float FloorHeight;
        /// <summary>
        /// Walkable platforms (raised surfaces, ramps, galleries).
        /// Each platform is a rectangular surface at a specific Y.
        /// The server checks these for ground collision.
        /// </summary>
        public PlatformDef[] Platforms;
        /// <summary>
        /// arena bounds X (used for camera/mechanics)
        /// </summary>
        public float MinX, MaxX;
        /// <summary>
        /// arena bounds Z (used for camera/mechanics)
        /// </summary>
        public float MinZ, MaxZ;
        public SpawnPoint[] SpawnPoints;
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
                        loaded.Add(arena.Value);
                        Console.WriteLine($"[ArenaRegistry] Loaded: {arena.Value.Name} ({file})");
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
            _hardcoded = BuildAll();
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
                    ScenePath = "res://assets/arenas/arena_pit.tscn",
                    KillHeight = -15f,  // Deep blast zone (80x80 large stage)
                    FloorHeight = 0f,
                    Platforms = new[] {
                        new PlatformDef { CenterX = 40f, CenterZ = 40f, HalfSizeX = 40f, HalfSizeZ = 40f, SurfaceY = 0f },
                    },
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
                    ScenePath = "res://assets/arenas/arena_cross.tscn",
                    KillHeight = -10f,  // Medium blast zone (60x60 balanced stage)
                    FloorHeight = 0f,
                    Platforms = new[] {
                        new PlatformDef { CenterX = 30f, CenterZ = 30f, HalfSizeX = 10f, HalfSizeZ = 10f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 30f, CenterZ = 12f, HalfSizeX = 8f, HalfSizeZ = 6f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 30f, CenterZ = 48f, HalfSizeX = 8f, HalfSizeZ = 6f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 12f, CenterZ = 30f, HalfSizeX = 6f, HalfSizeZ = 8f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 48f, CenterZ = 30f, HalfSizeX = 6f, HalfSizeZ = 8f, SurfaceY = 0f },
                    },
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
                    ScenePath = "res://assets/arenas/arena_split.tscn",
                    KillHeight = -6f,   // Shallow blast zone (60x60 small competitive stage)
                    FloorHeight = 0f,
                    Platforms = new[] {
                        // Lower quadrants (surface Y=0, CSG pos Y=-1 size 2)
                        new PlatformDef { CenterX = 30f, CenterZ = 15f, HalfSizeX = 30f, HalfSizeZ = 15f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 30f, CenterZ = 45f, HalfSizeX = 30f, HalfSizeZ = 15f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 15f, CenterZ = 30f, HalfSizeX = 15f, HalfSizeZ = 15f, SurfaceY = 0f },
                        new PlatformDef { CenterX = 45f, CenterZ = 30f, HalfSizeX = 15f, HalfSizeZ = 15f, SurfaceY = 0f },
                        // Upper center platform (surface Y=3, CSG pos Y=2 size 2)
                        new PlatformDef { CenterX = 30f, CenterZ = 30f, HalfSizeX = 10f, HalfSizeZ = 10f, SurfaceY = 3f },
                        // Ramps (surface Y=1, CSG pos Y=0.5 size 1)
                        new PlatformDef { CenterX = 22f, CenterZ = 22f, HalfSizeX = 2f, HalfSizeZ = 2f, SurfaceY = 1f },
                        new PlatformDef { CenterX = 38f, CenterZ = 22f, HalfSizeX = 2f, HalfSizeZ = 2f, SurfaceY = 1f },
                        new PlatformDef { CenterX = 22f, CenterZ = 38f, HalfSizeX = 2f, HalfSizeZ = 2f, SurfaceY = 1f },
                        new PlatformDef { CenterX = 38f, CenterZ = 38f, HalfSizeX = 2f, HalfSizeZ = 2f, SurfaceY = 1f },
                    },
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
                // Sanctum: Large multi-level arena
                // Floor at Y=0, CentralPlatform at Y=5, Galleries at Y=8, CenterPiece at Y=6
                new ArenaDefinition
                {
                    Name = "sanctum",
                    DisplayName = "Sanctum",
                    ScenePath = "res://assets/arenas/arena_sanctum.tscn",
                    KillHeight = -20f,
                    FloorHeight = 0f,
                    Platforms = new[] {
                        // Main floor (CSG pos Y=-5 size 10 → surface Y=0)
                        new PlatformDef { CenterX = 100f, CenterZ = 100f, HalfSizeX = 100f, HalfSizeZ = 100f, SurfaceY = 0f },
                        // Central raised platform (CSG pos Y=2.5 size 5 → surface Y=5)
                        new PlatformDef { CenterX = 100f, CenterZ = 100f, HalfSizeX = 20f, HalfSizeZ = 20f, SurfaceY = 5f },
                        // Side galleries (CSG pos Y=7.5 size 1 → surface Y=8)
                        new PlatformDef { CenterX = 16f, CenterZ = 100f, HalfSizeX = 6f, HalfSizeZ = 50f, SurfaceY = 8f },
                        new PlatformDef { CenterX = 184f, CenterZ = 100f, HalfSizeX = 6f, HalfSizeZ = 50f, SurfaceY = 8f },
                        // Center piece (CSG pos Y=5.5 size 1 → surface Y=6)
                        new PlatformDef { CenterX = 100f, CenterZ = 100f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 6f },
                        // Stair landings — left side
                        new PlatformDef { CenterX = 12f, CenterZ = 26f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 4f },
                        new PlatformDef { CenterX = 20f, CenterZ = 26f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 8f },
                        // Stair landings — right side
                        new PlatformDef { CenterX = 188f, CenterZ = 26f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 8f },
                        new PlatformDef { CenterX = 180f, CenterZ = 26f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 4f },
                        // Stair landings — left side south
                        new PlatformDef { CenterX = 12f, CenterZ = 174f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 4f },
                        new PlatformDef { CenterX = 20f, CenterZ = 174f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 8f },
                        // Stair landings — right side south
                        new PlatformDef { CenterX = 188f, CenterZ = 174f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 8f },
                        new PlatformDef { CenterX = 180f, CenterZ = 174f, HalfSizeX = 4f, HalfSizeZ = 4f, SurfaceY = 4f },
                    },
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
