#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Sandbox training match — local player + 1 NPC dummy.
/// No network, no server reconciliation. Pure local simulation.
/// </summary>
public partial class TrainingMatch : Node3D
{
    public PlayerController Player { get; private set; } = null!;
    public PlayerController[] NPCs { get; private set; } = new PlayerController[1];
    public TargetRing TargetRing { get; private set; } = null!;

    private const int NpcCount = 1;
    private ArenaDefinition _arenaDef = ArenaRegistry.Get("split");
    private ServerSimulation _localSim = null!;
    private CharacterDefinition _charDef = default!;
    private BakedAnimationData _playerBakedData = null!;
    private SpellVFXManager? _spellVFX;

    public async void Start(CharacterClass playerClass, SpellVFXManager? spellVFX)
    {
        _spellVFX = spellVFX;
        _charDef = CharacterRegistry.Get(playerClass);

        // Load baked skeleton data
        _playerBakedData = LoadBakedData(_charDef);

        // Local simulation
        _localSim = new ServerSimulation(_arenaDef);
        var spawn = _arenaDef.SpawnPoints[5];
        _localSim.RegisterEntity(1, _charDef, new CharacterState
        {
            PX = spawn.X, PY = spawn.Y + 5f, PZ = spawn.Z,
            FacingYaw = spawn.Yaw,
            JumpsLeft = _charDef.Movement.MaxJumps,
        }, _playerBakedData);

        // NPC
        var npcSpawn = _arenaDef.SpawnPoints[0];
        _localSim.RegisterEntity(100, _charDef, new CharacterState
        {
            PX = npcSpawn.X, PY = npcSpawn.Y + 1f, PZ = npcSpawn.Z,
            FacingYaw = npcSpawn.Yaw,
            JumpsLeft = _charDef.Movement.MaxJumps,
        }, _playerBakedData);

        // Arena visual
        var arenaNode = new ArenaManager { Name = "ArenaManager" };
        AddChild(arenaNode);
        arenaNode.LoadArena(_arenaDef.Name);

        // Target ring
        TargetRing = new TargetRing { Name = "TargetRing" };
        AddChild(TargetRing);

        // Spawn NPC visual
        SpawnNPCs();

        // Spawn player
        Player = new PlayerController { Name = "Player" };
        Player.SetClass(playerClass);
        Player.SetBakedData(_playerBakedData);
        AddChild(Player);
        Player.Position = spawn.ToGodot() + new Vector3(5f, 15f, 0f);
        Player.SetupCombat(null!, _arenaDef, 1, _spellVFX);

        // Wire target ring
        TargetRing.Setup(Player, null, 0, NPCs, NpcCount);

        // Heightmap
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        try { HeightmapGenerator.Generate(GetWorld3D()); }
        catch (Exception ex) { GD.PrintErr($"Heightmap failed: {ex.Message}"); }
    }

    private void SpawnNPCs()
    {
        for (int i = 0; i < NpcCount; i++)
        {
            var npc = new PlayerController { Name = $"NPC_{i}" };
            npc.SetClass(i % 2 == 0 ? CharacterClass.Manki : CharacterClass.Bunny);
            npc.SetNPC(true);
            npc.AddToGroup("enemies");
            AddChild(npc);
            npc.Position = _arenaDef.SpawnPoints[i].ToGodot() + new Vector3(0f, 1f, 0f);
            NPCs[i] = npc;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || _localSim == null) return;

        var input = Player.GetCurrentInput();
        _localSim.Tick(new Dictionary<ulong, InputState> { { 1, input } });

        // Apply predicted states
        Player.ApplyServerState(_localSim.GetState(1));
        for (int i = 0; i < NpcCount; i++)
            NPCs[i].ApplyServerState(_localSim.GetState((ulong)(100 + i)));
    }

    public ulong GetTarget() => TargetRing.CurrentTarget;
    public bool HasTarget() => TargetRing.HasTarget;
    public void SetTarget(ulong id) => TargetRing.SetTarget(id);

    /// <summary>Get debug hitbox/hurtbox data.</summary>
    public (List<Hitbox> hitboxes, List<SpellResolver.EntityData> entities) GetDebugData()
    {
        var hitboxes = _localSim.Resolver.GetActiveHitboxes();
        var entities = _localSim?.GetLastEntityData() ?? new();
        return (hitboxes, entities);
    }

    private static BakedAnimationData? LoadBakedData(CharacterDefinition def)
    {
        if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
        try
        {
            using var f = Godot.FileAccess.Open(def.BakedDataPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null)
            {
                GD.PrintErr($"[Training] Cannot open baked data: {def.BakedDataPath}");
                return null;
            }
            var binData = f.GetBuffer((long)f.GetLength());
            var baked = BakedAnimationData.LoadFromBin(binData);
            GD.Print($"[Training] Loaded baked data: {binData.Length} bytes, {baked.Animations.Length} anims");
            return baked;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Training] Failed to load baked data: {ex.Message}");
            return null;
        }
    }
}

internal static class SpawnPointExtensions
{
    public static Vector3 ToGodot(this SpawnPoint sp) => new(sp.X, sp.Y, sp.Z);
}
