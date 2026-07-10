using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using SlopArena.Client.UI;
using SlopArena.Client.Network;
using SlopArena.Client.Simulation;

namespace SlopArena.Client.World
{
    /// <summary>
    /// PvP match backed by a remote server. Uses NetworkSimulationBridge — no local sim.
    /// Phase 1: raw server-state display, no prediction/rollback.
    /// </summary>
    public class PvPMatch : MatchBase
    {
        [Header("Entities (Opponent)")]
        [SerializeField] private PlayerRenderer _opponentRenderer;

        [Header("Characters (Opponent)")]
        [SerializeField] private CharacterClass _opponentClass = CharacterClass.Manki;

        [Header("Network")]
        [SerializeField] private NetworkClient _networkClient;

        private const ulong OpponentEntityId = 2;

        private uint _tick;
        private NetworkSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        protected override void OnMatchStart()
        {
            // Arena
            string arenaPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", "data", "arenas", _arenaName + ".arena"));
            ArenaDefinition arena;
            if (File.Exists(arenaPath))
            {
                var loaded = ArenaBinaryFormat.LoadFromFile(arenaPath);
                arena = loaded ?? ArenaRegistry.Get(_arenaName);
                Debug.Log($"[PvPMatch] Loaded arena: {arenaPath}");
            }
            else
            {
                arena = ArenaRegistry.Get(_arenaName);
                Debug.Log($"[PvPMatch] Using hardcoded arena: {_arenaName}");
            }

            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);

            // Bridge
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new NetworkSimulationBridge(_networkClient, PlayerEntityId);

            // Character definitions
            var playerDef = CharacterRegistry.Get(_playerClass);
            _playerDef = playerDef;
            var playerBaked = LoadBakedData(playerDef);
            var opponentDef = CharacterRegistry.Get(_opponentClass);
            var opponentBaked = LoadBakedData(opponentDef);

            // Shared player renderer + HUD setup
            SetupPlayerRenderer(playerDef, playerBaked);
            SetupHUD(playerDef);

            // Opponent renderer
            if (_opponentRenderer != null)
            {
                _opponentRenderer.ModelYOffset = opponentDef.ModelYOffset;
                _opponentRenderer.CapsuleRadius = opponentDef.CapsuleRadius;
                _opponentRenderer.CapsuleHeight = opponentDef.CapsuleHeight;
                _opponentRenderer.HurtboxBoneDefs = opponentDef.HurtboxBoneDefs;
                _opponentRenderer.SetBakedData(opponentBaked);
                _opponentRenderer.SetCharacterDefinition(opponentDef);
                _opponentRenderer.LoadModel(opponentDef);
            }

            // Position renderers at spawn points
            var spawnPoints = arena.SpawnPoints;
            if (spawnPoints.Length > 0)
                _playerRenderer.transform.position = new Vector3(spawnPoints[0].X, spawnPoints[0].Y, spawnPoints[0].Z);
            if (_opponentRenderer != null && spawnPoints.Length > 1)
                _opponentRenderer.transform.position = new Vector3(spawnPoints[1].X, spawnPoints[1].Y, spawnPoints[1].Z);

            // Shared camera + aim setup
            SetupCamera();
            SetupAimHandler(playerDef);
        }

        private void Update()
        {
            _inputController.Poll();
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_bridge == null || _playerRenderer == null) return;

            byte slot = _inputController.ConsumePendingSlotPress();

            var playerState = _bridge.GetState(PlayerEntityId);
            var aimCtx = _aimHandler != null
                ? _aimHandler.Evaluate(playerState, slot, _playerDef, _inputController)
                : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;

            byte targetEntityId = PickScreenTarget(
                _opponentRenderer != null ? new[] { _opponentRenderer } : Array.Empty<PlayerRenderer>(),
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);

            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                isNPC: false,
                pendingSlotPress: slot,
                aimCtx: aimCtx,
                canMove: null,
                targetEntityId: targetEntityId);

            _bridge.Tick(new Dictionary<ulong, InputState>
            {
                { PlayerEntityId, input }
            });

            _hudManager?.Refresh();

            // Apply server states to renderers
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            if (_opponentRenderer != null)
                _opponentRenderer.ApplyServerState(_bridge.GetState(OpponentEntityId));

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps = _bridge.GetState(PlayerEntityId);
                Debug.Log($"[PvP] tick={_tick} connected={_networkClient.IsServerConnected} " +
                          $"pos=({ps.PX:F1},{ps.PY:F2},{ps.PZ:F1}) serverTick={_networkClient.LastServerTick}");
            }
        }
    }
}
