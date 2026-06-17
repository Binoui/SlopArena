#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Online PvP match — player + opponent, client-side prediction + server reconciliation.
/// Connects to a headless MatchInstance server via UDP.
/// </summary>
public partial class PvPMatch : Node3D
{
    public PlayerController Player { get; private set; } = null!;
    public PlayerController Opponent { get; private set; } = null!;
    public NetworkClient Net { get; private set; } = null!;
    public TargetRing TargetRing { get; private set; } = null!;

    private const ulong PlayerEntityId = 1;
    private const ulong OpponentEntityId = 2;
    private ArenaDefinition _arenaDef = ArenaRegistry.Get("split");
    private ServerSimulation _localSim = null!;
    private CharacterDefinition _charDef = default!;
    private BakedAnimationData _playerBakedData = null!;
    private SpellVFXManager? _spellVFX;

    // Rollback
    private const int RollbackFrames = 30;
    private uint _sendTick;
    private readonly InputState[] _inputBuffer = new InputState[RollbackFrames];
    private readonly CharacterState[] _stateBuffer = new CharacterState[RollbackFrames];
    private uint _lastConfirmedTick;

    // Server ghost
    private readonly Dictionary<ulong, CharacterState> _serverConfirmedStates = new();
    private readonly Dictionary<ulong, int> _serverAnimFrames = new();
    private readonly Dictionary<ulong, int> _serverPrevAnimIdx = new();

    public async void Start(CharacterClass playerClass, SpellVFXManager? spellVFX, string serverIp = "127.0.0.1", int serverPort = 9876)
    {
        _spellVFX = spellVFX;
        _charDef = CharacterRegistry.Get(playerClass);
        _playerBakedData = LoadBakedData(_charDef);

        _localSim = new ServerSimulation(_arenaDef);

        // Player spawn
        var spawn = _arenaDef.SpawnPoints[5];
        var initialState = new CharacterState
        {
            PX = spawn.X, PY = spawn.Y + 5f, PZ = spawn.Z,
            FacingYaw = spawn.Yaw, JumpsLeft = _charDef.Movement.MaxJumps,
        };
        _localSim.RegisterEntity(PlayerEntityId, _charDef, initialState, _playerBakedData);
        _stateBuffer[0] = initialState;

        // Opponent spawn
        var oppSpawn = _arenaDef.SpawnPoints.Length > 1
            ? _arenaDef.SpawnPoints[1]
            : new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = MathF.PI };
        _localSim.RegisterEntity(OpponentEntityId, _charDef, new CharacterState
        {
            PX = oppSpawn.X, PY = oppSpawn.Y + 1f, PZ = oppSpawn.Z,
            FacingYaw = oppSpawn.Yaw, JumpsLeft = _charDef.Movement.MaxJumps,
        }, _playerBakedData);

        // Network
        Net = new NetworkClient { Name = "NetworkClient" };
        AddChild(Net);
        Net.Connect(PlayerEntityId, serverIp, serverPort);

        // Arena
        var arenaNode = new ArenaManager { Name = "ArenaManager" };
        AddChild(arenaNode);
        arenaNode.LoadArena(_arenaDef.Name);

        // Target ring
        TargetRing = new TargetRing { Name = "TargetRing" };
        AddChild(TargetRing);

        // Player visual
        Player = new PlayerController { Name = "Player" };
        Player.SetClass(playerClass);
        Player.SetBakedData(_playerBakedData);
        AddChild(Player);
        Player.Position = spawn.ToGodot() + new Vector3(5f, 15f, 0f);
        Player.SetupCombat(null!, _arenaDef, PlayerEntityId, _spellVFX);

        // Opponent visual
        Opponent = new PlayerController { Name = "Opponent" };
        Opponent.SetClass(playerClass);
        Opponent.SetNPC(true);
        Opponent.SetBakedData(_playerBakedData);
        Opponent.AddToGroup("enemies");
        AddChild(Opponent);
        Opponent.Position = oppSpawn.ToGodot() + new Vector3(0f, 1f, 0f);
        Opponent.SetupCombat(null!, _arenaDef, OpponentEntityId, null);

        TargetRing.Setup(Player, Opponent, OpponentEntityId, Array.Empty<PlayerController>(), 0);

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        try { HeightmapGenerator.Generate(GetWorld3D()); }
        catch (Exception ex) { GD.PrintErr($"Heightmap failed: {ex.Message}"); }
    }

    // ═══ PHYSICS TICK: predict + send ═══

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || _localSim == null) return;

        var input = Player.GetCurrentInput();
        _sendTick++;
        _inputBuffer[_sendTick % RollbackFrames] = input;

        _localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, input } });

        var predicted = _localSim.GetState(PlayerEntityId);
        _stateBuffer[_sendTick % RollbackFrames] = predicted;

        Net.SendInput(input, _sendTick);
        Player.ApplyServerState(predicted);

        var oppState = _localSim.GetState(OpponentEntityId);
        Opponent.ApplyServerState(oppState);
    }

    // ═══ RENDER: reconcile with server ═══

    public override void _Process(double delta)
    {
        if (Net == null) return;

        var serverStates = Net.ReceiveStates();
        foreach (var kvp in serverStates)
        {
            _serverConfirmedStates[kvp.Key] = kvp.Value.state;
        }

        // Player reconciliation
        if (serverStates.TryGetValue(PlayerEntityId, out var server))
        {
            uint serverTick = server.tick;
            CharacterState serverState = server.state;

            if (serverTick > _lastConfirmedTick)
            {
                _lastConfirmedTick = serverTick;
                int idx = (int)(serverTick % RollbackFrames);
                var predicted = _stateBuffer[idx];

                float dx = predicted.PX - serverState.PX;
                float dy = predicted.PY - serverState.PY;
                float dz = predicted.PZ - serverState.PZ;
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq > 0.25f)
                {
                    GD.Print($"[Rollback] Tick {serverTick}: d=({dx:F2},{dy:F2},{dz:F2}) dist={MathF.Sqrt(distSq):F3}m");

                    var oldOppState = _localSim.GetState(OpponentEntityId);
                    _localSim = new ServerSimulation(_arenaDef);
                    _localSim.RegisterEntity(PlayerEntityId, _charDef, serverState, _playerBakedData);

                    var oppState = _serverConfirmedStates.TryGetValue(OpponentEntityId, out var os)
                        ? os : oldOppState;
                    _localSim.RegisterEntity(OpponentEntityId, _charDef, oppState, _playerBakedData);

                    uint currentTick = _sendTick;
                    for (uint t = serverTick + 1; t <= currentTick; t++)
                    {
                        var pastInput = _inputBuffer[t % RollbackFrames];
                        _localSim.Tick(new Dictionary<ulong, InputState> { { PlayerEntityId, pastInput } });
                    }

                    var corrected = _localSim.GetState(PlayerEntityId);
                    _stateBuffer[currentTick % RollbackFrames] = corrected;
                    Player.ApplyServerState(corrected);
                }
            }
        }

        // Opponent: server authority
        if (serverStates.TryGetValue(OpponentEntityId, out var oppServer))
            Opponent.ApplyServerState(oppServer.state);
    }

    // ═══ TARGETING ═══

    public ulong GetTarget() => TargetRing.CurrentTarget;
    public bool HasTarget() => TargetRing.HasTarget;
    public void SetTarget(ulong id) => TargetRing.SetTarget(id);

    // ═══ DEBUG ═══

    public (List<Hitbox> hitboxes, List<SpellResolver.EntityData> localEntities, List<SpellResolver.EntityData> serverEntities) GetDebugData()
    {
        var hitboxes = _localSim.Resolver.GetActiveHitboxes();
        var localEntities = _localSim?.GetLastEntityData() ?? new();
        var serverEntities = BuildServerGhostEntities();
        return (hitboxes, localEntities, serverEntities);
    }

    private List<SpellResolver.EntityData> BuildServerGhostEntities()
    {
        var result = new List<SpellResolver.EntityData>();
        if (_localSim == null) return result;

        if (_serverConfirmedStates.Count == 0)
        {
            result.AddRange(_localSim.GetLastEntityData());
            return result;
        }

        foreach (var kvp in _serverConfirmedStates)
        {
            var rawEntities = ServerSimulation.BuildEntitiesFromState(
                kvp.Value, _charDef, _playerBakedData, "idle", 0);
            for (int i = 0; i < rawEntities.Count; i++)
            {
                var e = rawEntities[i];
                e.Id = kvp.Key;
                rawEntities[i] = e;
                result.Add(e);
            }
        }
        return result;
    }

    private static BakedAnimationData? LoadBakedData(CharacterDefinition def)
    {
        if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
        try
        {
            using var f = Godot.FileAccess.Open(def.BakedDataPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null) return null;
            var binData = f.GetBuffer((long)f.GetLength());
            return BakedAnimationData.LoadFromBin(binData);
        }
        catch { return null; }
    }
}
