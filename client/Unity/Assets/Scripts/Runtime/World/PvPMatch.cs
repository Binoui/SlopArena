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
    /// PvP match backed by a remote server. Uses RollbackSimulationBridge (ADR-0011):
    /// the local player predicts continuously; opponents predict while in a movement
    /// state and render raw from the server otherwise.
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
        private RollbackSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        protected override void LeaveMatch()
        {
            // Drop the SignalR lobby so the master server frees this player's
            // slot; the UDP NetworkClient shuts itself down on scene unload
            // (OnDestroy). Best-effort: the connection may already be dead.
            var lobby = SlopArena.Client.ClientSession.ActiveLobby;
            if (lobby != null)
            {
                SlopArena.Client.ClientSession.ActiveLobby = null;
                try
                {
                    _ = lobby.LeaveLobbyAsync(); // best-effort, fire-and-forget
                }
                catch (Exception ex)
                {
                    // Dead/half-open connections can throw synchronously — never
                    // block the scene redirect on a best-effort disconnect.
                    Debug.LogWarning($"[PvPMatch] LeaveLobbyAsync failed (ignored): {ex.Message}");
                }
            }
            base.LeaveMatch();
        }

        protected override void OnMatchStart()
        {
            Debug.Log($"[{GetType().Name}] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={MatchConfig.ArenaName}");
            // Baked arena is required (issue #77): hardcoded ArenaRegistry arenas carry
            // no collision data, so a missing .arena file used to make players fall
            // through the floor. The client only renders; the server is authoritative.
            string? arenaPath = BakedContentPaths.ResolveArena(MatchConfig.ArenaName);
            if (arenaPath == null)
            {
                Debug.LogError($"[PvPMatch] Baked arena '{MatchConfig.ArenaName}' not found (looked in StreamingAssets/arenas and repo data/arenas). " +
                               "Bake the arena or run scripts/build-release.sh. Aborting match start.");
                return;
            }
            var arenaOpt = ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (arenaOpt is not ArenaDefinition arena)
            {
                Debug.LogError($"[PvPMatch] Failed to parse baked arena: {arenaPath}");
                return;
            }
            Debug.Log($"[PvPMatch] Loaded arena: {arenaPath}");

            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);

            // Bridge
            _networkClient.EntityId = PlayerEntityId;
            _bridge = new RollbackSimulationBridge(arena, _networkClient, PlayerEntityId);
            _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);
            SpawnStageVisual(arena);

            // Character definitions
            var playerDef = CharacterRegistry.Get(MatchConfig.PlayerClass);
            var playerBaked = LoadBakedData(playerDef);
            playerDef = ApplyHurtboxOverride(playerDef, playerBaked);
            _playerDef = playerDef;

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
                def = ApplyHurtboxOverride(def, baked);
                renderer.EntityId = opp.EntityId;
                renderer.ModelYOffset = def.ModelYOffset;
                renderer.CapsuleRadius = def.CapsuleRadius;
                renderer.CapsuleHeight = def.CapsuleHeight;
                renderer.HurtboxBoneDefs = def.HurtboxBoneDefs;
                renderer.SetBakedData(baked);
                renderer.SetCharacterDefinition(def);
                renderer.LoadModel(def);
                renderer.transform.position = SpawnPosition(arena, opp.EntityId);
                _opponentRenderers[opp.EntityId] = renderer;

                // Register with the rollback bridge: populates _defs (prediction needs
                // the CharacterDefinition) and seeds the RawTrack initial state so the
                // opponent renders at its spawn until the first server packet. Without
                // this, PvP crashed on the first predictable opponent packet
                // (KeyNotFoundException on defs[EntityId]).
                var oppSpawn = SpawnPointFor(arena, opp.EntityId);
                _bridge.RegisterEntity(opp.EntityId, def, new CharacterState
                {
                    PX = oppSpawn.X, PY = oppSpawn.Y, PZ = oppSpawn.Z,
                    FacingYaw = oppSpawn.Yaw,
                    State = ActionState.Idle,
                    IsGrounded = true,
                    JumpsLeft = def.Movement.MaxJumps,
                    AirDodgesLeft = 1,
                    DamagePercent = 0,
                }, baked);
            }
            _opponentArray = new List<PlayerRenderer>(_opponentRenderers.Values).ToArray();

            // Ground-shadow rings under every player: local + all opponents.
            var lockRenderers = new PlayerRenderer[_opponentArray.Length + 1];
            lockRenderers[0] = _playerRenderer;
            Array.Copy(_opponentArray, 0, lockRenderers, 1, _opponentArray.Length);
            SetupLockIndicator(lockRenderers, arena);

            // Player spawns at its own roster spawn point (entityId 1..N ↔ spawnPoints[0..N-1]).
            _playerRenderer.transform.position = SpawnPosition(arena, PlayerEntityId);

            // Register the self entity: without this the LocalTrack sim is empty, so the
            // PvP player never simulates locally (frozen at origin) and _defs stays empty.
            var selfSpawn = SpawnPointFor(arena, PlayerEntityId);
            _bridge.RegisterEntity(PlayerEntityId, playerDef, new CharacterState
            {
                PX = selfSpawn.X, PY = selfSpawn.Y, PZ = selfSpawn.Z,
                FacingYaw = selfSpawn.Yaw,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = playerDef.Movement.MaxJumps,
                AirDodgesLeft = 1,
                DamagePercent = 0,
            }, playerBaked);

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
            if (IsPaused) return; // pause menu owns Esc + skips polling (issue #77)
            _inputController.Poll();
        }

        /// <summary>Roster spawn point for an entity (entityId 1..N ↔ spawnPoints[0..N-1]),
        /// matching the server's PickSpawn fallback (issue #35).</summary>
        private static SpawnPoint SpawnPointFor(ArenaDefinition arena, ulong entityId)
        {
            int idx = (int)entityId - 1;
            if (idx < 0 || arena.SpawnPoints == null || idx >= arena.SpawnPoints.Length)
                return new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };
            return arena.SpawnPoints[idx];
        }

        private static Vector3 SpawnPosition(ArenaDefinition arena, ulong entityId)
        {
            var s = SpawnPointFor(arena, entityId);
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

            UpdateLockCamera();

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
        /// While target-locked (ADR-0018 / issue #127): move the Cinemachine follow
        /// target to the player↔locked-enemy midpoint so both fighters stay framed.
        /// Restores the player follow target when unlocked or the target renderer
        /// is missing (dead — the sim re-picks next tick).
        /// </summary>
        private void UpdateLockCamera()
        {
            if (_cameraMount == null) return;
            var local = _bridge.GetState(PlayerEntityId);
            if (local.LockOn && local.TargetEntityId != 0
                && _opponentRenderers.TryGetValue(local.TargetEntityId, out var target))
            {
                _cameraMount.SetLockFocus(_playerRenderer.transform, target.transform.position);
                return;
            }
            _cameraMount.ClearLockFocus(_playerRenderer.transform);
        }


        protected override void OnGUI()
        {
            base.OnGUI();
            if (!UnityEngine.Input.GetKey(KeyCode.F3)) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            GUI.Label(new Rect(10, 10, 400, 20), $"Corrections: {_bridge.CorrectionCount}", style);
            GUI.Label(new Rect(10, 30, 400, 20), $"Frontier window: {_bridge.LastFrontierTicks} ticks", style);
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
