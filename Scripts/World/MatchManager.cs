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
	public PlayerController Opponent { get; private set; } = null!;
	public PlayerController[] NPCs { get; private set; } = new PlayerController[5];
	public NetworkClient Net { get; private set; } = null!;

	private MeshInstance3D _targetRing = null!;
	public event Action<ulong>? OnTargetChanged;

	private SpellVFXManager? _spellVFX;
	private const int NpcCount = 5;
	private const ulong OpponentEntityId = 2;
	private ArenaDefinition _arenaDef = ArenaRegistry.Get("split");

	/// <summary>
	/// ── Local prediction ──
	/// </summary>
	private ServerSimulation _localSim = null!;
	private CharacterDefinition _charDef;
	private ulong _playerEntityId = 1;
	private BakedAnimationData _playerBakedData = null!;

	/// <summary>
	/// ── Server ghost (confirmed state from server, drawn in green) ──
	/// </summary>
	private readonly Dictionary<ulong, CharacterState> _serverConfirmedStates = new();
	private readonly Dictionary<ulong, int> _serverAnimFrames = new();
	private readonly Dictionary<ulong, int> _serverPrevAnimIdx = new();
	private uint _lastServerTick;

	/// <summary>
	/// ── Tick + rollback ──
	/// </summary>
	private const int RollbackFrames = 30;
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

		// Load skeleton from GLB
		// Load baked skeleton data
		if (!string.IsNullOrEmpty(_charDef.BakedDataPath))
		{
			try
			{
				using var f = Godot.FileAccess.Open(_charDef.BakedDataPath, Godot.FileAccess.ModeFlags.Read);
				if (f == null)
				{
					GD.PrintErr($"[Match] Cannot open baked data: {_charDef.BakedDataPath}");
				}
				else
				{
					var binData = f.GetBuffer((long)f.GetLength());
					_playerBakedData = BakedAnimationData.LoadFromBin(binData);
					GD.Print($"[Match] Loaded baked data: {_charDef.BakedDataPath} ({binData.Length} bytes, {_playerBakedData.Animations.Length} anims)");
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[Match] Failed to load baked data: {ex.Message}");
				GD.PrintErr("[Match] Will use fallback capsules");
			}
		}
		else
		{
			GD.Print("[Match] No baked data path set, using fallback capsules");
		}

		var initialState = new CharacterState
		{
			PX = spawn.X,
			PY = spawn.Y + 5f,
			PZ = spawn.Z,
			FacingYaw = spawn.Yaw,
			JumpsLeft = _charDef.Movement.MaxJumps,
		};
		_localSim.RegisterEntity(_playerEntityId, _charDef, initialState, _playerBakedData);
		// NPCs use the same baked data (same character class)
		for (int i = 0; i < NpcCount; i++)
		{
			var npcSpawn = _arenaDef.SpawnPoints[i];
			_localSim.RegisterEntity((ulong)(100 + i), _charDef, new CharacterState
			{
				PX = npcSpawn.X,
				PY = npcSpawn.Y + 1f,
				PZ = npcSpawn.Z,
				FacingYaw = npcSpawn.Yaw,
				JumpsLeft = _charDef.Movement.MaxJumps,
			}, _playerBakedData);
		}

		// PvP opponent (entity 2 — opposite spawn from player)
		var oppSpawn = _arenaDef.SpawnPoints.Length > 1
			? _arenaDef.SpawnPoints[1]
			: new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = MathF.PI };
		_localSim.RegisterEntity(OpponentEntityId, _charDef, new CharacterState
		{
			PX = oppSpawn.X,
			PY = oppSpawn.Y + 1f,
			PZ = oppSpawn.Z,
			FacingYaw = oppSpawn.Yaw,
			JumpsLeft = _charDef.Movement.MaxJumps,
		}, _playerBakedData);

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
		Player.SetBakedData(_playerBakedData); // for auto model Y offset
		AddChild(Player);
		Player.Position = spawn.ToGodot() + new Vector3(5f, 15f, 0f);
		Player.SetupCombat(null!, _arenaDef, _playerEntityId, _spellVFX);

		// Spawn opponent (PvP — entity 2, no local input)
		Opponent = new PlayerController { Name = "Opponent" };
		Opponent.SetClass(playerClass); // TODO: get from server match info
		Opponent.SetNPC(true);          // no local input, server-authoritative
		Opponent.AddToGroup("enemies");
		AddChild(Opponent);
		Opponent.Position = oppSpawn.ToGodot() + new Vector3(0f, 1f, 0f);
		Opponent.SetupCombat(null!, _arenaDef, OpponentEntityId, null);

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

		// Debug: first batch of entity data
		if (_sendTick == 10)
		{
			var ents = _localSim.GetLastEntityData();
			GD.Print($"========== [Sim] Entity list: {ents.Count} entries ==========");
			for (int i = 0; i < Math.Min(ents.Count, 8); i++)
			{
				var e = ents[i];
				GD.Print($"  [{i}] id={e.Id} pos=({e.PosX:F2},{e.PosY:F2},{e.PosZ:F2}) r={e.Radius:F2} active={e.Active}");
			}
		}

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

		// Opponent: apply predicted state from local sim
		var oppState = _localSim.GetState(OpponentEntityId);
		Opponent.ApplyServerState(oppState);
	}

	// ── RENDER: reconcile with server ──

	public override void _Process(double delta)
	{
		if (Net == null) return;

		var serverStates = Net.ReceiveStates();

		// Store confirmed server states for ghost visualization
		foreach (var kvp in serverStates)
		{
			_serverConfirmedStates[kvp.Key] = kvp.Value.state;
			_lastServerTick = kvp.Value.tick;
		}

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

				// Compare server vs predicted — 3D distance check
				// Server is 1-2 frames behind, small differences are expected
				float dx = predicted.PX - serverState.PX;
				float dy = predicted.PY - serverState.PY;
				float dz = predicted.PZ - serverState.PZ;
				float distSq = dx * dx + dy * dy + dz * dz;
				if (distSq > 0.25f) // 0.5m threshold
				{
					// ── ROLLBACK ──
					GD.Print($"[Rollback] Tick {serverTick}: d=({dx:F2},{dy:F2},{dz:F2}) dist={MathF.Sqrt(distSq):F3}m, resimulating...");

					// Snapshot NPC states from the current sim before replacing it
					var npcStates = new CharacterState[NpcCount];
					for (int i = 0; i < NpcCount; i++)
						npcStates[i] = _localSim.GetState((ulong)(100 + i));
					var oldOppState = _localSim.GetState(OpponentEntityId);

					// Reset local sim to the server's confirmed state
					var safeState = serverState;
					_localSim = new ServerSimulation(_arenaDef);
					_localSim.RegisterEntity(_playerEntityId, _charDef, safeState, _playerBakedData);

					// Re-register NPCs with their last known state (prefer server-confirmed)
					for (int i = 0; i < NpcCount; i++)
					{
						ulong npcId = (ulong)(100 + i);
						var npcState = _serverConfirmedStates.TryGetValue(npcId, out var s)
							? s : npcStates[i];
						_localSim.RegisterEntity(npcId, _charDef, npcState, _playerBakedData);
					}

					// Re-register opponent
					var oppRollbackState = _serverConfirmedStates.TryGetValue(OpponentEntityId, out var os)
						? os : oldOppState;
					_localSim.RegisterEntity(OpponentEntityId, _charDef, oppRollbackState, _playerBakedData);

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

		// Opponent: apply server state directly (authority)
		if (serverStates.TryGetValue(OpponentEntityId, out var oppServer))
			Opponent.ApplyServerState(oppServer.state);

		// Target ring follow
		if (_targetRing != null && _targetRing.Visible)
		{
			ulong tid = GetTarget();
			if (tid == OpponentEntityId && Opponent != null)
			{
				Vector3 pos = Opponent.GlobalPosition;
				pos.Y = 0.1f;
				_targetRing.Position = pos;
			}
			else if (tid >= 100 && tid < 100 + NpcCount)
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
		bool valid = entityId == OpponentEntityId || (entityId >= 100 && entityId < 100 + NpcCount);
		if (_targetRing != null)
		{
			if (valid)
			{
				Vector3 pos;
				if (entityId == OpponentEntityId && Opponent != null)
					pos = Opponent.GlobalPosition;
				else
				{
					int idx = (int)(entityId - 100);
					pos = idx >= 0 && idx < NpcCount && NPCs[idx] != null
						? NPCs[idx]!.GlobalPosition : Vector3.Zero;
				}
				pos.Y = 0.1f;
				_targetRing.Position = pos;
				_targetRing.Visible = true;
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

	/// <summary>Get debug hitbox/hurtbox data from the local simulation.</summary>
	public (List<Hitbox> hitboxes, List<SpellResolver.EntityData> localEntities, List<SpellResolver.EntityData> serverEntities) GetDebugData()
	{
		var hitboxes = _localSim.Resolver.GetActiveHitboxes();
		var localEntities = _localSim?.GetLastEntityData() ?? new();
		var serverEntities = BuildServerGhostEntities();
		return (hitboxes, localEntities, serverEntities);
	}

	/// <summary>Build server ghost entity data from last confirmed states.</summary>
	private List<SpellResolver.EntityData> BuildServerGhostEntities()
	{
		var result = new List<SpellResolver.EntityData>();
		if (_localSim == null) return result;

		foreach (var kvp in _serverConfirmedStates)
		{
			ulong id = kvp.Key;
			var state = kvp.Value;
			var def = CharacterRegistry.Get(_charDef.Class); // per-character
			var baked = _playerBakedData; // TODO: per-character

			// Determine animation (same logic as ServerSimulation)
			string targetAnim;
			if (state.State == ActionState.Dashing)
				targetAnim = "dash";
			else if (state.State == ActionState.Attacking && state.AttackSlot > 0)
			{
				bool airborne = !state.IsGrounded;
				var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
				int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
				targetAnim = stageIdx >= 0 && stageIdx < ability.AnimationNames.Length
					? ability.AnimationNames[stageIdx] : "melee";
			}
			else if (state.State == ActionState.Hitstun)
				targetAnim = "small_hit";
			else if (!state.IsGrounded)
				targetAnim = state.VY > 0 ? "jump" : "fall";
			else if ((state.VX * state.VX) + (state.VZ * state.VZ) > 1f)
				targetAnim = "run";
			else
				targetAnim = "idle";

			// Track animation frame (same loop/wrap logic as ServerSimulation)
			int animIdx = baked?.FindAnimIndex(targetAnim) ?? -1;
			// Fallback to idle for unbaked anims
			if (animIdx < 0 && baked != null)
			{
				targetAnim = "idle";
				animIdx = baked.FindAnimIndex(targetAnim);
			}
			int frame = _serverAnimFrames.GetValueOrDefault(id, 0);
			int prevIdx = _serverPrevAnimIdx.GetValueOrDefault(id, -1);
			if (prevIdx != animIdx)
			{
				frame = 0;
				_serverPrevAnimIdx[id] = animIdx;
			}
			int fc = (animIdx >= 0 && baked != null) ? baked.Animations[animIdx].FrameCount : 1;
			int nextFrame = frame + 1;
			if (nextFrame >= fc) nextFrame = 0;
			_serverAnimFrames[id] = nextFrame;

			var rawEntities = ServerSimulation.BuildEntitiesFromState(state, def, baked, targetAnim, frame);
			for (int i = 0; i < rawEntities.Count; i++)
			{
				var e = rawEntities[i];
				e.Id = id;
				rawEntities[i] = e;
				result.Add(e);
			}
		}
		return result;
	}
}

internal static class SpawnPointExtensions
{
	public static Vector3 ToGodot(this SpawnPoint sp) => new(sp.X, sp.Y, sp.Z);
}
