#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Manages the match with proper client-side prediction + server reconciliation.
///
/// Flow per frame:
///   _PhysicsProcess (60Hz):
///     1. Build input, assign tick
///     2. Store input in buffer
///     3. LocalSim.Tick(input) → predicted state
///     4. Store predicted state in buffer
///     5. Send input (with tick) to server
///     6. Apply predicted state → render
///
///   _Process (as soon as data arrives):
///     7. Receive server states
///     8. For each: compare predicted vs server
///     9. If mismatch: rollback (re-simulate from safe tick)
///     10. Apply corrected states
/// </summary>
public partial class MatchManager : Node3D
{
    public PlayerController Player { get; private set; } = null!;
    public PlayerController[] NPCs { get; private set; } = new PlayerController[5];
    public NetworkClient Net { get; private set; } = null!;

    private MeshInstance3D _targetRing = null!;
    public event Action<ulong>? OnTargetChanged;

    private SpellVFXManager? _spellVFX;
    private const int NpcCount = 5;
    private ArenaDefinition _arenaDef = ArenaRegistry.Get("split");

    // ── Local prediction ──
    private ServerSimulation _localSim = null!;
    private CharacterDefinition _charDef;
    private ulong _playerEntityId = 1;

    // ── Tick + rollback ──
    private const int RollbackFrames = 10;
    private uint _sendTick;
    private readonly InputState[] _inputBuffer = new InputState[RollbackFrames];
    private readonly CharacterState[] _stateBuffer = new CharacterState[RollbackFrames];
    private uint _lastConfirmedTick;

    public async void StartMatch(CharacterClass playerClass, SpellVFXManager? spellVFX)
    {
        _spellVFX = spellVFX;
        _charDef = CharacterRegistry.Get(playerClass);

        // Local simulation
        _localSim = new ServerSimulation(_arenaDef);
        var spawn = _arenaDef.SpawnPoints[5];
        var initialState = new CharacterState
        {
            PX = spawn.X, PY = spawn.Y + 5f, PZ = spawn.Z,
            FacingYaw = spawn.Yaw,
            JumpsLeft = _charDef.Movement.MaxJumps,
        };
        _localSim.RegisterEntity(_playerEntityId, _charDef, initialState);
        for (int i = 0; i < NpcCount; i++)
        {
            var npcSpawn = _arenaDef.SpawnPoints[i];
            _localSim.RegisterEntity((ulong)(100 + i), _charDef, new CharacterState
            {
                PX = npcSpawn.X, PY = npcSpawn.Y + 1f, PZ = npcSpawn.Z,
                FacingYaw = npcSpawn.Yaw,
                JumpsLeft = _charDef.Movement.MaxJumps,
            });
        }

        // Network client
        Net = new NetworkClient { Name = "NetworkClient" };
        AddChild(Net);
        Net.Connect(_playerEntityId);

        // Init tick buffer with initial state
        _stateBuffer[0] = initialState;
        _lastConfirmedTick = 0;

        // Arena visual
        var arenaNode = new ArenaManager { Name = "ArenaManager" };
        AddChild(arenaNode);
        arenaNode.LoadArena(_arenaDef.Name);

        // Targeting ring
        _targetRing = CreateTargetRing();
        AddChild(_targetRing);
        _targetRing.Visible = false;

        // Spawn NPCs (visual)
        SpawnNPCs();

        // Spawn player
        Player = new PlayerController { Name = "Player" };
        Player.SetClass(playerClass);
        AddChild(Player);
        Player.Position = spawn.ToGodot() + new Vector3(5f, 15f, 0f);
        Player.SetupCombat(null!, _arenaDef, _playerEntityId, _spellVFX);

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
            npc.SetClass(CharacterClass.Manki);
            npc.SetNPC(true);
            npc.AddToGroup("enemies");
            AddChild(npc);
            npc.Position = _arenaDef.SpawnPoints[i].ToGodot() + new Vector3(0f, 1f, 0f);
            NPCs[i] = npc;
        }
    }

    // ── PHYSICS TICK: predict + send ──

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || _localSim == null) return;

        // 1. Build & buffer input
        var input = Player.GetCurrentInput();
        _sendTick++;
        _inputBuffer[_sendTick % RollbackFrames] = input;

        // 2. Local prediction
        _localSim.Tick(new Dictionary<ulong, InputState> { { _playerEntityId, input } });

        // 3. Store predicted state
        var predicted = _localSim.GetState(_playerEntityId);
        _stateBuffer[_sendTick % RollbackFrames] = predicted;

        // 4. Send input + tick to server
        Net.SendInput(input, _sendTick);

        // 5. Render predicted state
        Player.ApplyServerState(predicted);

        // NPCs: just gravity (no AI yet)
        for (int i = 0; i < NpcCount; i++)
        {
            ulong eid = (ulong)(100 + i);
            var npcState = _localSim.GetState(eid);
            NPCs[i].ApplyServerState(npcState);
        }
    }

    // ── RENDER: reconcile with server ──

    public override void _Process(double delta)
    {
        if (Net == null) return;

        var serverStates = Net.ReceiveStates();

        // Player: reconcile
        if (serverStates.TryGetValue(_playerEntityId, out var server))
        {
            uint serverTick = server.tick;
            CharacterState serverState = server.state;

            if (serverTick > _lastConfirmedTick)
            {
                _lastConfirmedTick = serverTick;

                // Look up predicted state for this tick
                int idx = (int)(serverTick % RollbackFrames);
                var predicted = _stateBuffer[idx];

                // Compare server vs predicted
                float dy = predicted.PY - serverState.PY;
                if (MathF.Abs(dy) > 0.01f)
                {
                    // ── ROLLBACK ──
                    GD.Print($"[Rollback] Tick {serverTick}: dy={dy:F3}m, resimulating...");

                    // Reset local sim to the server's confirmed state
                    var safeState = serverState;
                    _localSim = new ServerSimulation(_arenaDef);
                    _localSim.RegisterEntity(_playerEntityId, _charDef, safeState);

                    // Re-simulate from serverTick+1 to currentTick
                    uint currentTick = _sendTick;
                    for (uint t = serverTick + 1; t <= currentTick; t++)
                    {
                        var pastInput = _inputBuffer[t % RollbackFrames];
                        _localSim.Tick(new Dictionary<ulong, InputState> { { _playerEntityId, pastInput } });
                    }

                    // Apply corrected state
                    var corrected = _localSim.GetState(_playerEntityId);
                    _stateBuffer[currentTick % RollbackFrames] = corrected;
                    Player.ApplyServerState(corrected);
                }
            }
        }

        // NPCs: apply server state directly (authority)
        for (int i = 0; i < NpcCount; i++)
        {
            ulong eid = (ulong)(100 + i);
            if (serverStates.TryGetValue(eid, out var npcServer))
                NPCs[i].ApplyServerState(npcServer.state);
        }

        // Target ring follow
        if (_targetRing != null && _targetRing.Visible)
        {
            ulong tid = GetTarget();
            if (tid >= 100 && tid < 100 + NpcCount)
            {
                int idx = (int)(tid - 100);
                if (idx >= 0 && idx < NpcCount && NPCs[idx] != null)
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
                if (idx >= 0 && idx < NpcCount && NPCs[idx] != null)
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

    public NetworkClient GetNet() => Net;
}

internal static class SpawnPointExtensions
{
    public static Vector3 ToGodot(this SpawnPoint sp) => new(sp.X, sp.Y, sp.Z);
}
