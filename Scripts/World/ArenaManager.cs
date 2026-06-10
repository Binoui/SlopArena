using Godot;
using SlopArena.Shared;

/// <summary>
/// Manages loading, unloading, and querying arenas.
/// Added as a child of Main (Node3D). Handles:
/// - Loading arena PackedScene into the world
/// - Providing spawn points for players/NPCs
/// - Blast zone detection (Y < KillHeight)
/// - Arena bounds for camera/mechanics
/// </summary>
public partial class ArenaManager : Node3D
{
    private ArenaDefinition _currentDef;
    private Node3D _arenaInstance;

    public ArenaDefinition CurrentDef => _currentDef;

    /// <summary>
    /// Load an arena by name from ArenaRegistry.
    /// Unloads the previous arena first if any.
    /// </summary>
    public bool LoadArena(string arenaName)
    {
        var def = ArenaRegistry.Get(arenaName);

        // Unload previous arena
        if (_arenaInstance != null)
        {
            RemoveChild(_arenaInstance);
            _arenaInstance.QueueFree();
            _arenaInstance = null;
        }

        // Load new arena scene
        var scene = ResourceLoader.Load<PackedScene>(def.ScenePath);
        if (scene == null)
        {
            GD.PrintErr($"ArenaManager: Failed to load scene '{def.ScenePath}'");
            return false;
        }

        _arenaInstance = scene.Instantiate<Node3D>();
        _arenaInstance.Name = "Arena";
        AddChild(_arenaInstance);
        _currentDef = def;

        GD.Print($"ArenaManager: Loaded '{def.DisplayName}' ({def.SpawnPoints.Length} spawns)");
        return true;
    }

    /// <summary>
    /// Get a spawn position. Wraps around with modulo if index exceeds available spawns.
    /// </summary>
    public Vector3 GetSpawnPosition(int index)
    {
        if (_currentDef.SpawnPoints.Length == 0)
            return Vector3.Zero;

        var sp = _currentDef.SpawnPoints[index % _currentDef.SpawnPoints.Length];
        return new Vector3(sp.X, sp.Y, sp.Z);
    }

    /// <summary>
    /// Check if a position is below the instant-kill threshold.
    /// </summary>
    public bool IsBelowKillHeight(Vector3 position)
    {
        return position.Y < _currentDef.KillHeight;
    }
}
