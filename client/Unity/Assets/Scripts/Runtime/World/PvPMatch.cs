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
        [Header("Entities")]
        [SerializeField] private PlayerRenderer _playerRenderer;
        [SerializeField] private PlayerRenderer _opponentRenderer;

        [Header("Input")]
        [SerializeField] private InputController _inputController;
        [SerializeField] private CameraMount _cameraMount;

        [Header("Network")]
        [SerializeField] private NetworkClient _networkClient;

        [Header("Arena")]
        [SerializeField] private string _arenaName = "training";

        [Header("Characters")]
        [SerializeField] private CharacterClass _playerClass = CharacterClass.Manki;
        [SerializeField] private CharacterClass _opponentClass = CharacterClass.Manki;

        [Header("Aiming")]
        [SerializeField] private AimHandler _aimHandler;
        [SerializeField] private HUDManager _hudManager;

        private const ulong PlayerEntityId = 1;
        private const ulong OpponentEntityId = 2;

        private bool _showCrosshair;
        private uint _tick;
        private CharacterDefinition _playerDef = null!;
        private NetworkSimulationBridge _bridge = null!;
        private UnityEngine.Camera _mainCamera;

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

            Simulation.OnDebugLog = msg => Debug.Log(msg);

            // Bridge
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new NetworkSimulationBridge(_networkClient, PlayerEntityId);

            // Character definitions
            var playerDef = CharacterRegistry.Get(_playerClass);
            _playerDef = playerDef;
            var playerBaked = LoadBakedData(playerDef);

            var opponentDef = CharacterRegistry.Get(_opponentClass);
            var opponentBaked = LoadBakedData(opponentDef);

            // HUD
            _hudManager?.Initialize(() => _bridge.GetState(PlayerEntityId));
            _hudManager?.SetCharacterDefinition(playerDef);
            for (int slot = 0; slot < 6; slot++)
            {
                var spec = playerDef.GetSlotAbility(slot, airborne: false);
                if (spec != null)
                    _hudManager?.SetSlotMaxCooldown(slot, spec.CooldownTicks);
                var specAir = playerDef.GetSlotAbility(slot, airborne: true);
                if (specAir != null && specAir.CooldownTicks > spec?.CooldownTicks)
                    _hudManager?.SetSlotMaxCooldown(slot, specAir.CooldownTicks);
            }

            // Player renderer
            _playerRenderer.ModelYOffset = playerDef.ModelYOffset;
            _playerRenderer.CapsuleRadius = playerDef.CapsuleRadius;
            _playerRenderer.CapsuleHeight = playerDef.CapsuleHeight;
            _playerRenderer.HurtboxBoneDefs = playerDef.HurtboxBoneDefs;
            _playerRenderer.SetBakedData(playerBaked);
            _playerRenderer.SetCharacterDefinition(playerDef);
            _playerRenderer.LoadModel(playerDef);

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

            // Camera
            if (_cameraMount != null)
            {
                _cameraMount.SetTarget(_playerRenderer.transform);
                _cameraMount.ResetView(_playerRenderer.transform);
            }

            // Aim
            _aimHandler?.Init(_cameraMount, _cameraMount?.RenderCamera);
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

        private byte PickScreenTarget(PlayerRenderer[] renderers, UnityEngine.Camera cam)
        {
            if (cam == null || renderers == null || renderers.Length == 0 || _playerRenderer == null)
                return 0;

            byte bestId = 0;
            float bestScreenDist = float.MaxValue;
            Vector2 screenCenter = new(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
            Vector3 playerPos = _playerRenderer.transform.position;

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer == _playerRenderer || renderer.EntityId == 0)
                    continue;

                Vector3 worldPos = renderer.transform.position;
                Vector3 screenPos3 = cam.WorldToScreenPoint(worldPos);
                if (screenPos3.z < 0) continue;

                float screenDist = Vector2.Distance(new Vector2(screenPos3.x, screenPos3.y), screenCenter);
                float worldDist = Vector3.Distance(playerPos, worldPos);

                if (screenDist < bestScreenDist && worldDist <= 20f)
                {
                    bestScreenDist = screenDist;
                    bestId = (byte)renderer.EntityId;
                }
            }
            return bestId;
        }

        private void OnGUI()
        {
            if (!_showCrosshair) return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(cx - 20, cy - 20, 40, 40), "+", style);
        }
    }
}
