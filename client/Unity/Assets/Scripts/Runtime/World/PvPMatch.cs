using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        [Header("Network")]
        [SerializeField] private NetworkClient _networkClient;

        private readonly Dictionary<ulong, PlayerRenderer> _opponentRenderers = new();
        private PlayerRenderer[] _opponentArray = System.Array.Empty<PlayerRenderer>();

        /// <summary>Last server Deaths counter per entity — drives the death/respawn flash (issue #37).</summary>
        private readonly Dictionary<ulong, byte> _lastDeaths = new();

        private uint _tick;
        private MatchState _lastMatchState = MatchState.Waiting;
        private NetworkSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        protected override void OnMatchStart()
        {
            Debug.Log($"[{GetType().Name}] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={MatchConfig.ArenaName}");
            // Arena
            string arenaPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "..", "data", "arenas", MatchConfig.ArenaName + ".arena"));
            ArenaDefinition arena;
            if (File.Exists(arenaPath))
            {
                var loaded = ArenaBinaryFormat.LoadFromFile(arenaPath);
                arena = loaded ?? ArenaRegistry.Get(MatchConfig.ArenaName);
                Debug.Log($"[PvPMatch] Loaded arena: {arenaPath}");
            }
            else
            {
                arena = ArenaRegistry.Get(MatchConfig.ArenaName);
                Debug.Log($"[PvPMatch] Using hardcoded arena: {MatchConfig.ArenaName}");
            }

            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);

            // Bridge
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new NetworkSimulationBridge(_networkClient, PlayerEntityId);
            _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);

            // Character definitions
            var playerDef = CharacterRegistry.Get(MatchConfig.PlayerClass);
            _playerDef = playerDef;
            var playerBaked = LoadBakedData(playerDef);

            // Shared player renderer + HUD setup
            SetupPlayerRenderer(playerDef, playerBaked);
            SetupHUD(playerDef);

            // Opponent renderers — one per MatchConfig.Opponents entry. The scene's
            // _opponentRenderer is the first opponent + the clone template for the rest.
            _opponentRenderers.Clear();
            for (int i = 0; i < MatchConfig.Opponents.Count; i++)
            {
                var opp = MatchConfig.Opponents[i];

                PlayerRenderer renderer;
                if (i == 0 && _opponentRenderer != null)
                {
                    renderer = _opponentRenderer;
                }
                else if (_opponentRenderer != null)
                {
                    var clone = Instantiate(_opponentRenderer.gameObject);
                    clone.name = $"Opponent_{opp.EntityId}";
                    renderer = clone.GetComponent<PlayerRenderer>();
                }
                else
                {
                    Debug.LogWarning($"[PvPMatch] No opponent template in scene — skipping opponent {opp.EntityId}.");
                    continue;
                }

                var def = CharacterRegistry.Get(opp.Class);
                var baked = LoadBakedData(def);
                renderer.ModelYOffset = def.ModelYOffset;
                renderer.CapsuleRadius = def.CapsuleRadius;
                renderer.CapsuleHeight = def.CapsuleHeight;
                renderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
                renderer.SetBakedData(baked);
                renderer.SetCharacterDefinition(def);
                renderer.LoadModel(def);
                renderer.transform.position = SpawnPosition(arena, opp.EntityId);
                _opponentRenderers[opp.EntityId] = renderer;
            }
            _opponentArray = new List<PlayerRenderer>(_opponentRenderers.Values).ToArray();

            // Player spawns at its own roster spawn point (entityId 1..N ↔ spawnPoints[0..N-1]).
            _playerRenderer.transform.position = SpawnPosition(arena, PlayerEntityId);

            // Track initial Deaths so the first state apply doesn't trigger a false flash.
            _lastDeaths.Clear();
            _lastDeaths[PlayerEntityId] = 0;
            foreach (var id in _opponentRenderers.Keys)
                _lastDeaths[id] = 0;

            // Shared camera + aim setup
            SetupCamera();
            SetupAimHandler(playerDef);
        }

        private void Update()
        {
            _inputController.Poll();
        }

        private static Vector3 SpawnPosition(ArenaDefinition arena, ulong entityId)
        {
            int idx = (int)entityId - 1;
            if (idx < 0 || arena.SpawnPoints == null || idx >= arena.SpawnPoints.Length)
                return new Vector3(40f, 0.5f, 40f);
            var s = arena.SpawnPoints[idx];
            return new Vector3(s.X, s.Y, s.Z);
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
                _opponentArray,
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
            foreach (var kv in _opponentRenderers)
                kv.Value.ApplyServerState(_bridge.GetState(kv.Key));

            // Respawn/death feedback: flash + animation reset when the server Deaths
            // counter increments (issue #37, mirrors TrainingMatch NPC handling).
            byte playerDeaths = _bridge.GetState(PlayerEntityId).Deaths;
            if (playerDeaths != _lastDeaths[PlayerEntityId])
            {
                _lastDeaths[PlayerEntityId] = playerDeaths;
                _playerRenderer.OnDeath();
            }
            foreach (var kv in _opponentRenderers)
            {
                byte d = _bridge.GetState(kv.Key).Deaths;
                if (d != _lastDeaths[kv.Key])
                {
                    _lastDeaths[kv.Key] = d;
                    kv.Value.OnDeath();
                }
            }

            // Surface server match state transitions (countdown → fight → results)
            var matchState = _bridge.GetState(PlayerEntityId).MatchState;
            if (matchState != _lastMatchState)
            {
                Debug.Log($"[PvP] MatchState transition: {_lastMatchState} → {matchState}");
                _lastMatchState = matchState;

                if (matchState == MatchState.Ended)
                {
                    BuildAndShowResults();
                }
            }

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps = _bridge.GetState(PlayerEntityId);
                Debug.Log($"[PvP] tick={_tick} connected={_networkClient.IsServerConnected} " +
                          $"pos=({ps.PX:F1},{ps.PY:F2},{ps.PZ:F1}) serverTick={_networkClient.LastServerTick}");
            }
        }

        /// <summary>
        /// Build the final standings from the last server states and schedule the
        /// return to the results screen (issue #40). Runs once on the Ended transition.
        /// </summary>
        private void BuildAndShowResults()
        {
            var states = new Dictionary<ulong, CharacterState>
            {
                { PlayerEntityId, _bridge.GetState(PlayerEntityId) }
            };
            foreach (var id in _opponentRenderers.Keys)
                states[id] = _bridge.GetState(id);

            // Winner via the shared rule — same decision the game server made.
            var outcome = new StockMatchRule((byte)MatchConfig.MaxStocks).Evaluate(states);

            var data = new ClientSession.MatchResultsData
            {
                SharedVictory = outcome.IsSharedVictory,
            };

            if (ClientSession.MatchRoster != null)
            {
                foreach (var roster in ClientSession.MatchRoster)
                {
                    if (roster.EntityId <= 0) continue;
                    var id = (ulong)roster.EntityId;
                    if (!states.TryGetValue(id, out var st)) continue;

                    var className = roster.CharacterSelection;
                    if (string.IsNullOrEmpty(className))
                    {
                        className = id == PlayerEntityId
                            ? MatchConfig.PlayerClass.ToString()
                            : OpponentClass(id);
                    }

                    data.Entries.Add(new ClientSession.ResultEntry
                    {
                        EntityId = id,
                        Name = string.IsNullOrEmpty(roster.Name) ? $"P{id}" : roster.Name,
                        ClassName = className,
                        StocksRemaining = MatchConfig.MaxStocks - st.Deaths,
                        DamagePercent = st.DamagePercent,
                        IsWinner = !outcome.IsSharedVictory && id == outcome.WinnerEntityId,
                    });
                }
            }

            // Rank: most stocks first, then least damage (tie-break).
            data.Entries.Sort((a, b) =>
            {
                int byStocks = b.StocksRemaining.CompareTo(a.StocksRemaining);
                return byStocks != 0 ? byStocks : a.DamagePercent.CompareTo(b.DamagePercent);
            });

            ClientSession.CurrentMatchResults = data;
            Debug.Log($"[PvP] Match ended — {data.Entries.Count} entries, shared={outcome.IsSharedVictory}");

            // 2s beat so the KO moment renders; the server keeps broadcasting the
            // Ended state for its 3s post-match window, then we cut to Results.
            StartCoroutine(ReturnToLobbyAfterDelay());
        }

        private string OpponentClass(ulong entityId)
        {
            foreach (var opp in MatchConfig.Opponents)
                if (opp.EntityId == entityId) return opp.Class.ToString();
            return $"P{entityId}";
        }

        private System.Collections.IEnumerator ReturnToLobbyAfterDelay()
        {
            yield return new WaitForSeconds(2f);
            SceneManager.LoadScene("Results");
        }
    }
}
