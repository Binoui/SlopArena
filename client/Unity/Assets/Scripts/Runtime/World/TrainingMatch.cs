using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;
using SlopArena.Client.Combat;
using SlopArena.Client.UI;
using SlopArena.Client.Simulation;
namespace SlopArena.Client.World
{
    public enum NpcAiMode
    {
        Attack,
        Idle
    }
    
    public class TrainingMatch : MatchBase
    {
        [Header("Entities (NPC)")]
        [SerializeField] private PlayerRenderer _npcRenderer;

        [Header("Characters (NPC)")]
        [SerializeField] private CharacterClass _npcClass = CharacterClass.Manki;

        [Header("Debug")]
        [Header("Combat")]
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private NpcAiMode _npcAiMode = NpcAiMode.Attack;

        [Header("Hitboxes")]
        [SerializeField] private bool _showHitboxes;

        private LocalSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        private uint _tick;
        private ArenaDefinition _arenaDef;
        private const ulong NpcEntityId = 100;
        private byte _npcLastDeaths;

        protected override void OnMatchStart()
        {
            // Load arena from baked file if it exists, otherwise fall back to hardcoded registry
            string arenaPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data", "arenas", _arenaName + ".arena"));
            ArenaDefinition arena;
            if (File.Exists(arenaPath))
            {
                var loaded = ArenaBinaryFormat.LoadFromFile(arenaPath);
                arena = loaded ?? ArenaRegistry.Get(_arenaName);
                Debug.Log($"[TrainingMatch] Loaded arena from file: {arenaPath} — {arena.CollisionTriangles?.Length ?? 0} tris, heightmap={arena.Heightmap.Width}x{arena.Heightmap.Height}");
            }
            else
            {
                arena = ArenaRegistry.Get(_arenaName);
                Debug.Log($"[TrainingMatch] Using hardcoded arena: {_arenaName} — no file at {arenaPath}");
            }

            // Wire sim debug logging to Unity console
            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);
            _arenaDef = arena;

            // Bridge (local)
            _bridge = new LocalSimulationBridge(arena);
            _combatFeedback.SetSimulation(_bridge.InternalSim);

            var playerDef = CharacterRegistry.Get(_playerClass);
            _playerDef = playerDef;
            var playerBaked = LoadBakedData(playerDef);
            var npcDef = CharacterRegistry.Get(_npcClass);
            var npcBaked = LoadBakedData(npcDef);

            // Shared player renderer + HUD setup
            SetupPlayerRenderer(playerDef, playerBaked);
            SetupHUD(playerDef);

            // NPC renderer
            if (_npcRenderer != null)
            {
                _npcRenderer.ModelYOffset = npcDef.ModelYOffset;
                _npcRenderer.CapsuleRadius = npcDef.CapsuleRadius;
                _npcRenderer.CapsuleHeight = npcDef.CapsuleHeight;
                _npcRenderer.HurtboxBoneDefs = npcDef.HurtboxBoneDefs;
                _npcRenderer.SetBakedData(npcBaked);
                _npcRenderer.SetCharacterDefinition(npcDef);
                _npcRenderer.LoadModel(npcDef);
                _npcRenderer.GetComponent<WeaponAttach>()
                    ?.Init(_npcRenderer, Resources.Load<WeaponAttachConfig>($"WeaponConfigs/{_npcClass}"));
                _npcRenderer.InitBillboard(_bridge.InternalSim.GetState, NpcEntityId);
            }

            // Player spawn
            var pSpawn = arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0] : new SpawnPoint();
            _bridge.RegisterEntity(PlayerEntityId, playerDef, new CharacterState
            {
                PX = pSpawn.X, PY = pSpawn.Y, PZ = pSpawn.Z,
                FacingYaw = pSpawn.Yaw,
                JumpsLeft = playerDef.Movement.MaxJumps,
            }, playerBaked);

            // NPC spawn at fixed position
            float npcX = 0f;
            float npcZ = 0f;
            _bridge.RegisterEntity(NpcEntityId, npcDef, new CharacterState
            {
                PX = npcX, PY = 5f, PZ = npcZ,
                FacingYaw = Mathf.PI,
                JumpsLeft = npcDef.Movement.MaxJumps,
            }, npcBaked);

            // Position renderers
            _playerRenderer.transform.position = new Vector3(pSpawn.X, pSpawn.Y, pSpawn.Z);
            if (_npcRenderer != null)
                _npcRenderer.transform.position = new Vector3(npcX, 5f, npcZ);
            _npcLastDeaths = 0;

            // Set NPC respawn position
            _bridge.SetRespawnPosition(NpcEntityId, npcX, 5f, npcZ);

            // Shared camera + aim setup
            SetupCamera();
            SetupAimHandler(playerDef);
        }

        private void Update()
        {
            _inputController.Poll();
            if (_showHitboxes && _bridge != null)
                DrawHitboxDebug();
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_bridge == null || _playerRenderer == null) return;

            // Poll done in Update() — keep FixedUpdate clean
            byte slot = _inputController.ConsumePendingSlotPress();

            // ── Aim ──
            var aimCtx = _aimHandler != null
                ? _aimHandler.Evaluate(_bridge.GetState(PlayerEntityId), slot, _playerDef, _inputController)
                : AimContext.None;
            _showCrosshair = _aimHandler?.ShowCrosshair ?? false;

            // ── Build input ──
            byte targetEntityId = PickScreenTarget(
                _npcRenderer != null ? new[] { _npcRenderer } : System.Array.Empty<PlayerRenderer>(),
                _mainCamera ??= _cameraMount?.RenderCamera ?? UnityEngine.Camera.main);

            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                isNPC: false,
                pendingSlotPress: slot,
                aimCtx: aimCtx,
                canMove: null,
                targetEntityId: targetEntityId);

            // NPC AI
            var npcState = _bridge.GetState(NpcEntityId);
            var playerState = _bridge.GetState(PlayerEntityId);
            var npcInput = BuildNpcInput(npcState, playerState, _tick);

            // Tick
            _bridge.Tick(new Dictionary<ulong, InputState>
            {
                { PlayerEntityId, input },
                { NpcEntityId, npcInput }
            });

            // Track NPC death for visual feedback
            var npcStateAfter = _bridge.GetState(NpcEntityId);
            if (npcStateAfter.Deaths != _npcLastDeaths)
            {
                _npcLastDeaths = npcStateAfter.Deaths;
                if (_npcRenderer != null)
                    _npcRenderer.OnDeath();
            }
            _combatFeedback.OnTick();
            _hudManager?.Refresh();

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps3 = _bridge.GetState(PlayerEntityId);
                Debug.Log($"[Training] tick={_tick} pos=({ps3.PX:F1},{ps3.PY:F2},{ps3.PZ:F1}) vy={ps3.VY:F2} grounded={ps3.IsGrounded}");
            }

#if UNITY_EDITOR
            var hitState = _bridge.GetState(NpcEntityId);
            if (hitState.HitstunTicks > 0)
                Debug.Log($"[Combat] NPC hit! damage={hitState.DamagePercent:F1} hitstun={hitState.HitstunTicks}");
#endif

            // Apply states
            _playerRenderer.ApplyServerState(_bridge.GetState(PlayerEntityId));
            if (_npcRenderer != null)
                _npcRenderer.ApplyServerState(_bridge.GetState(NpcEntityId));
        }

        /// <summary>
        /// Builds a synthetic InputState for the NPC dummy.
        /// Computes world-space direction toward player.
        /// Server auto-sets FacingYaw from movement velocity.
        /// </summary>
        private InputState BuildNpcInput(CharacterState npcState, CharacterState playerState, ulong tick)
        {
            return _npcAiMode switch
            {
                NpcAiMode.Idle => BuildIdleInput(),
                _ => BuildAttackInput(npcState, playerState, tick),
            };
        }

        private static InputState BuildIdleInput()
        {
            return new InputState
            {
                MoveX = 0f,
                MoveY = 0f,
                ActiveSlot = 0,
                Jump = false,
            };
        }

        private static InputState BuildAttackInput(CharacterState npcState, CharacterState playerState, ulong tick)
        {
            // Direction from NPC to player (XZ plane, world space)
            float dx = playerState.PX - npcState.PX;
            float dz = playerState.PZ - npcState.PZ;
            float distSq = dx * dx + dz * dz;
            float dist = MathF.Sqrt(distSq);

            // Speed: full >3m, stop inside 2m, half-speed in between
            float speed = distSq > 9f ? 1f : (distSq < 4f ? 0f : 0.5f);

            // Decompose toward-player direction into world-space MoveX (sin) and MoveY (cos)
            float aimYaw = dist > 0.001f ? MathF.Atan2(dx, dz) : 0f;
            float moveX = MathF.Sin(aimYaw) * speed;
            float moveY = MathF.Cos(aimYaw) * speed;

            // Periodically attack (every ~2 seconds = 120 ticks)
            byte slot = (tick % 120 < 3) ? (byte)1 : (byte)0;

            // Jump if player is on higher platform
            bool jump = playerState.PY > npcState.PY + 1.5f && tick % 60 == 0;

            return new InputState
            {
                MoveX = moveX,
                MoveY = moveY,
                ActiveSlot = slot,
                Jump = jump,
            };
        }

        private void OnDrawGizmos()
        {
            if (_bridge == null) return;
            DrawHitboxGizmos();
        }

        private void DrawHitboxGizmos()
        {
            var hitboxes = _bridge.InternalSim.Resolver.GetActiveHitboxes();
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.6f);
            foreach (var hb in hitboxes)
            {
                var center = new Vector3(hb.X, hb.Y, hb.Z);
                if (hb.Shape == HitboxShape.Sphere || (hb.X == hb.EndX && hb.Y == hb.EndY && hb.Z == hb.EndZ))
                {
                    Gizmos.DrawWireSphere(center, hb.Radius);
                }
                else
                {
                    var end = new Vector3(hb.EndX, hb.EndY, hb.EndZ);
                    Gizmos.DrawWireSphere(center, hb.Radius);
                    Gizmos.DrawWireSphere(end, hb.Radius);
                    Gizmos.DrawLine(center, end);
                }
            }
        }

        private void DrawHitboxDebug()
        {
            var hitboxes = _bridge.InternalSim.Resolver.GetActiveHitboxes();
            Color color = new Color(1f, 0.3f, 0f, 0.6f);
            foreach (var hb in hitboxes)
            {
                var center = new Vector3(hb.X, hb.Y, hb.Z);
                if (hb.Shape == HitboxShape.Sphere || (hb.X == hb.EndX && hb.Y == hb.EndY && hb.Z == hb.EndZ))
                {
                    DebugDrawWireSphere(center, hb.Radius, color);
                }
                else
                {
                    var end = new Vector3(hb.EndX, hb.EndY, hb.EndZ);
                    DebugDrawWireSphere(center, hb.Radius, color);
                    DebugDrawWireSphere(end, hb.Radius, color);
                    Debug.DrawLine(center, end, color);
                }
            }
        }

        private static void DebugDrawWireSphere(Vector3 center, float radius, Color color)
        {
            const int segments = 16;
            for (int ring = 0; ring < 3; ring++)
            {
                Vector3 prev = default;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * (Mathf.PI * 2f / segments);
                    Vector3 p = center;
                    if (ring == 0)
                    {
                        p.x += Mathf.Cos(angle) * radius;
                        p.y += Mathf.Sin(angle) * radius;
                    }
                    else if (ring == 1)
                    {
                        p.x += Mathf.Cos(angle) * radius;
                        p.z += Mathf.Sin(angle) * radius;
                    }
                    else
                    {
                        p.y += Mathf.Cos(angle) * radius;
                        p.z += Mathf.Sin(angle) * radius;
                    }
                    if (i > 0)
                        Debug.DrawLine(prev, p, color);
                    prev = p;
                }
            }
        }
    }
}
