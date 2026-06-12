#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Server;

/// <summary>
/// Manages the match lifecycle: spawn, game loop (via LocalServerBridge), targeting.
/// The bridge runs a pure C# ServerSimulation each frame.
/// PlayerControllers receive authoritative states and render them.
/// </summary>
public partial class MatchManager : Node3D
{
    public PlayerController Player { get; private set; } = null!;
    public PlayerController[] NPCs { get; private set; } = new PlayerController[5];
    public LocalServerBridge Bridge { get; private set; } = null!;
    public ArenaManager Arena { get; private set; } = null!;

    private MeshInstance3D _targetRing = null!;
    public event Action<ulong>? OnTargetChanged;

    private SpellVFXManager? _spellVFX;
    private const int NpcCount = 5;
    private ArenaDefinition _arenaDef = ArenaRegistry.Get("split");

    // ── START MATCH ──

    public async void StartMatch(CharacterClass playerClass, SpellVFXManager? spellVFX)
    {
        _spellVFX = spellVFX;

        // Bridge (runs pure C# server simulation)
        Bridge = new LocalServerBridge(_arenaDef);
        Bridge.Name = "LocalServerBridge";
        AddChild(Bridge);

        // Arena
        Arena = new ArenaManager { Name = "ArenaManager" };
        AddChild(Arena);
        Arena.LoadArena(_arenaDef.Name);

        // Targeting ring
        _targetRing = CreateTargetRing();
        AddChild(_targetRing);
        _targetRing.Visible = false;

        // Spawn NPCs
        SpawnNPCs();

        // Spawn player
        Player = new PlayerController { Name = "Player" };
        Player.SetClass(playerClass);
        AddChild(Player);
        Player.Position = _arenaDef.SpawnPoints[5].ToGodot() + new Vector3(5f, 15f, 0f);
        Player.SetupCombat(Bridge, _arenaDef, 1, _spellVFX);

        // Register player in bridge
        var playerDef = Player.GetCharacterDef();
        var playerState = new CharacterState
        {
            PX = Player.Position.X, PY = Player.Position.Y, PZ = Player.Position.Z,
            FacingYaw = Player.GlobalRotation.Y,
            JumpsLeft = playerDef.Movement.MaxJumps,
            AirDodgesLeft = 1,
        };
        // Load GLB for bone-accurate hurtboxes
        byte[]? glbData = LoadGlbBytes("res://assets/characters/manki/manki.glb");
        Bridge.RegisterEntity(1, playerDef, playerState, glbData);
        Bridge.StoreDef(1, playerDef);
        if (Player.GetCombatComponent() != null)
            Bridge.CombatComponents[1] = Player.GetCombatComponent();

        // Auto-target on hit
        Bridge.OnEntityHit += (entityId, _, _, _, _) =>
        {
            if (entityId == 1 && _targetRing != null && !_targetRing.Visible)
                SetTarget(100);
        };

        // Void death → trigger Godot respawn
        Bridge.OnEntityOutOfBounds += (id) =>
        {
            if (id == 1) Player?.TriggerRespawn();
            else
            {
                int idx = (int)(id - 100);
                if (idx >= 0 && idx < NpcCount && NPCs[idx] != null)
                    NPCs[idx]!.TriggerRespawn();
            }
        };

        // Status routing
        Bridge.OnStatusApply += (ulong targetId, StatusType type, float duration, ulong sourceId) =>
        {
            if (Bridge.CombatComponents.TryGetValue(targetId, out var tc))
                tc.ApplyStatus(type, duration, sourceId);
        };
        Bridge.OnStatusConsume += (ulong targetId, StatusType type) =>
        {
            if (Bridge.CombatComponents.TryGetValue(targetId, out var tc))
                return tc.ConsumeStatus(type);
            return false;
        };

        // AI
        AddBotAI();

        // Heightmap
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        try { HeightmapGenerator.Generate(GetWorld3D()); }
        catch (Exception ex) { GD.PrintErr($"Heightmap failed: {ex.Message}"); }
    }

    // ── SPAWN ──

    private void SpawnNPCs()
    {
        var npcDef = CharacterRegistry.Get(CharacterClass.Manki);
        for (int i = 0; i < NpcCount; i++)
        {
            var npc = new PlayerController { Name = $"NPC_{i}" };
            npc.SetClass(CharacterClass.Manki);
            npc.SetNPC(true);
            npc.AddToGroup("enemies");
            AddChild(npc);

            var spawn = _arenaDef.SpawnPoints[i];
            npc.Position = new Vector3(spawn.X, spawn.Y + 1f, spawn.Z);
            npc.SetupCombat(Bridge, _arenaDef, (ulong)(100 + i), _spellVFX);

            var state = new CharacterState
            {
                PX = spawn.X, PY = spawn.Y + 1f, PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                JumpsLeft = npcDef.Movement.MaxJumps,
                AirDodgesLeft = 1,
            };
            byte[]? glb = LoadGlbBytes("res://assets/characters/manki/manki.glb");
            Bridge.RegisterEntity((ulong)(100 + i), npcDef, state, glb);
            Bridge.StoreDef((ulong)(100 + i), npcDef);

            var combat = npc.GetCombatComponent();
            if (combat != null)
                Bridge.CombatComponents[(ulong)(100 + i)] = combat;

            NPCs[i] = npc;
        }
    }

    private void AddBotAI()
    {
        for (int i = 0; i < NpcCount; i++)
        {
            if (NPCs[i] == null) continue;
            var bot = new BotController { Name = $"BotAI_{i}" };
            bot.Setup(NPCs[i]!, Player);
            NPCs[i]!.AddChild(bot);
        }
    }

    // ── GAME LOOP (60Hz, autoritaire) ──

    public override void _PhysicsProcess(double delta)
    {
        // Collect inputs from all entities
        var inputs = new Dictionary<ulong, InputState>();
        if (Player != null && Player.IsAlive())
            inputs[1] = Player.GetCurrentInput();
        for (int i = 0; i < NpcCount; i++)
        {
            if (NPCs[i] != null && NPCs[i]!.IsAlive())
                inputs[(ulong)(100 + i)] = NPCs[i]!.GetCurrentInput();
        }

        // Server tick (authoritative, pure C#)
        Bridge.Tick(inputs);

        // Apply authoritative states back to Godot bodies
        if (Player != null && Player.IsAlive())
        {
            var state = Bridge.GetState(1);
            Player.ApplyServerState(state);
        }
        for (int i = 0; i < NpcCount; i++)
        {
            if (NPCs[i] != null && NPCs[i]!.IsAlive())
            {
                var state = Bridge.GetState((ulong)(100 + i));
                NPCs[i]!.ApplyServerState(state);
            }
        }
    }

    public override void _Process(double delta)
    {
        // Target ring follow
        if (_targetRing != null && _targetRing.Visible)
        {
            ulong tid = GetTarget();
            if (tid >= 100 && tid < 100 + NpcCount)
            {
                int idx = (int)(tid - 100);
                if (idx >= 0 && idx < NpcCount && NPCs[idx] != null && NPCs[idx]!.IsNpcAlive())
                {
                    Vector3 pos = NPCs[idx]!.GlobalPosition;
                    pos.Y = 0.1f;
                    _targetRing.Position = pos;
                }
                else _targetRing.Visible = false;
            }
        }
    }

    // ── TARGET ──

    private ulong _targetId = 0;

    public ulong GetTarget() => _targetId;
    public bool HasTarget() => _targetId > 0;

    public void SetTarget(ulong entityId)
    {
        _targetId = entityId;
        bool valid = entityId >= 100 && entityId < 100 + NpcCount;
        if (_targetRing != null)
        {
            if (valid)
            {
                int idx = (int)(entityId - 100);
                if (idx >= 0 && idx < NpcCount && NPCs[idx] != null && NPCs[idx]!.IsNpcAlive())
                {
                    Vector3 pos = NPCs[idx]!.GlobalPosition;
                    pos.Y = 0.1f;
                    _targetRing.Position = pos;
                    _targetRing.Visible = true;
                }
                else _targetRing.Visible = false;
            }
            else _targetRing.Visible = false;
        }
        OnTargetChanged?.Invoke(entityId);
    }

    private MeshInstance3D CreateTargetRing()
    {
        var ring = new MeshInstance3D();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float innerR = 0.8f, outerR = 2.2f;
        const int segs = 32;
        for (int i = 0; i < segs; i++)
        {
            float a1 = (float)i / segs * Mathf.Tau;
            float a2 = (float)(i + 1) / segs * Mathf.Tau;
            float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
            float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);
            var in1 = new Vector3(c1 * innerR, 0, s1 * innerR);
            var in2 = new Vector3(c2 * innerR, 0, s2 * innerR);
            var out1 = new Vector3(c1 * outerR, 0, s1 * outerR);
            var out2 = new Vector3(c2 * outerR, 0, s2 * outerR);
            st.AddVertex(in1); st.AddVertex(out1); st.AddVertex(in2);
            st.AddVertex(in2); st.AddVertex(out1); st.AddVertex(out2);
        }
        st.GenerateNormals();
        ring.Mesh = st.Commit();
        ring.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.8f, 0.1f),
            EmissionEnergyMultiplier = 3f,
        };
        return ring;
    }

    // ── ACCESSORS ──

    public LocalServerBridge GetBridge() => Bridge;

    /// <summary>Read a GLB file from res:// path as raw bytes (for server skeleton).</summary>
    private static byte[]? LoadGlbBytes(string resPath)
    {
        using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return null;
        return file.GetBuffer((long)file.GetLength());
    }
}

internal static class SpawnPointExtensions
{
    public static Vector3 ToGodot(this SpawnPoint sp) => new(sp.X, sp.Y, sp.Z);
}
