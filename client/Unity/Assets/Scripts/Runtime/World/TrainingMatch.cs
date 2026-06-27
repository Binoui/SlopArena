using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using SlopArena.Client.Input;
using SlopArena.Client.Camera;

namespace SlopArena.Client.World
{
    public class TrainingMatch : MatchBase
    {
        [Header("Entities")]
        [SerializeField] private PlayerRenderer _playerRenderer;
        [SerializeField] private PlayerRenderer _npcRenderer;

        [Header("Input")]
        [SerializeField] private InputController _inputController;
        [SerializeField] private CameraMount _cameraMount;

        [Header("Arena")]
        [SerializeField] private string _arenaName = "training";

        [Header("Characters")]
        [SerializeField] private CharacterClass _playerClass = CharacterClass.Manki;
        [SerializeField] private CharacterClass _npcClass = CharacterClass.Manki;

        [Header("Debug")]
        [SerializeField] private bool _showHitboxes;

        private uint _tick;
        private ServerSimulation _localSim = null!;
        private ArenaDefinition _arenaDef;
        private const ulong PlayerEntityId = 1;
        private const ulong NpcEntityId = 100;

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
            _localSim = new ServerSimulation(arena);

            var playerDef = CharacterRegistry.Get(_playerClass);
            var playerBaked = LoadBakedData(playerDef);
            var npcDef = CharacterRegistry.Get(_npcClass);
            var npcBaked = LoadBakedData(npcDef);

            // Apply model Y offset for visual alignment with capsule
            _playerRenderer.ModelYOffset = playerDef.ModelYOffset;
            if (_npcRenderer != null)
                _npcRenderer.ModelYOffset = npcDef.ModelYOffset;

            // Wire capsule size + hurtbox data for debug visualization
            _playerRenderer.CapsuleRadius = playerDef.CapsuleRadius;
            _playerRenderer.CapsuleHeight = playerDef.CapsuleHeight;
            _playerRenderer.HurtboxBoneDefs = playerDef.HurtboxBoneDefs;
            _playerRenderer.SetBakedData(playerBaked);
            _playerRenderer.SetCharacterDefinition(playerDef);
            _playerRenderer.LoadModel(playerDef);
            if (_npcRenderer != null)
            {
                _npcRenderer.CapsuleRadius = npcDef.CapsuleRadius;
                _npcRenderer.CapsuleHeight = npcDef.CapsuleHeight;
                _npcRenderer.HurtboxBoneDefs = npcDef.HurtboxBoneDefs;
                _npcRenderer.SetBakedData(npcBaked);
                _npcRenderer.SetCharacterDefinition(npcDef);
                _npcRenderer.LoadModel(npcDef);
            }

            // Player spawn
            var pSpawn = arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0] : new SpawnPoint();
            _localSim.RegisterEntity(PlayerEntityId, playerDef, new CharacterState
            {
                PX = pSpawn.X, PY = pSpawn.Y, PZ = pSpawn.Z,
                FacingYaw = pSpawn.Yaw,
                JumpsLeft = playerDef.Movement.MaxJumps,
            }, playerBaked);

            // NPC spawn — 3m in front of player for hit testing
            var nSpawn = arena.SpawnPoints.Length > 1 ? arena.SpawnPoints[1] : new SpawnPoint();
            float npcX = pSpawn.X + Mathf.Cos(pSpawn.Yaw) * 3f;
            float npcZ = pSpawn.Z + Mathf.Sin(pSpawn.Yaw) * 3f;
            _localSim.RegisterEntity(NpcEntityId, npcDef, new CharacterState
            {
                PX = npcX, PY = nSpawn.Y, PZ = npcZ,
                FacingYaw = nSpawn.Yaw + Mathf.PI,
                JumpsLeft = npcDef.Movement.MaxJumps,
            }, npcBaked);

            // Position renderers
            _playerRenderer.transform.position = new Vector3(pSpawn.X, pSpawn.Y, pSpawn.Z);
            if (_npcRenderer != null)
                _npcRenderer.transform.position = new Vector3(npcX, nSpawn.Y, npcZ);

            // Set NPC respawn position to same relative location
            _localSim.SetRespawnPosition(NpcEntityId, npcX, pSpawn.Y + 2f, npcZ);

            // Camera
            if (_cameraMount != null)
            {
                _cameraMount.SetTarget(_playerRenderer.transform);
                _cameraMount.ResetView(_playerRenderer.transform);
            }

            Debug.Log($"[TrainingMatch] Started — arena: {arena.Name}, player at ({pSpawn.X:F1},{pSpawn.Y:F1})");
        }

        private void Update()
        {
            _inputController.Poll();
            if (_showHitboxes && _localSim != null)
                DrawHitboxDebug();
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_localSim == null || _playerRenderer == null) return;

            // Input
            // Poll done in Update() — keep FixedUpdate clean
            byte slot = _inputController.ConsumePendingSlotPress();
            if (slot > 0)
                Debug.Log($"[Input] slot={slot} animLock={_localSim.GetState(PlayerEntityId).AnimLockTicks} tick={_tick}");
            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                isNPC: false,
                isAiming: false,
                slot,
                abilityAimYawRad: null,
                abilityAimDistance: null,
                canMove: null);
            // Tick
            _localSim.Tick(new Dictionary<ulong, InputState>
            {
                { PlayerEntityId, input },
                { NpcEntityId, new InputState() }
            });

            _tick++;
            if (_tick % 120 == 1)
            {
                var ps = _localSim.GetState(PlayerEntityId);
                Debug.Log($"[Training] tick={_tick} pos=({ps.PX:F1},{ps.PY:F2},{ps.PZ:F1}) vy={ps.VY:F2} grounded={ps.IsGrounded}");
            }

#if UNITY_EDITOR
            var hitState = _localSim.GetState(NpcEntityId);
            if (hitState.HitstunTicks > 0)
                Debug.Log($"[Combat] NPC hit! damage={hitState.DamagePercent:F1} hitstun={hitState.HitstunTicks}");
#endif

            // Apply states
            _playerRenderer.ApplyServerState(_localSim.GetState(PlayerEntityId));
            if (_npcRenderer != null)
                _npcRenderer.ApplyServerState(_localSim.GetState(NpcEntityId));
        }

        private void OnDrawGizmos()
        {
            if (_localSim == null) return;
            DrawHitboxGizmos();
        }

        private void DrawHitboxGizmos()
        {
            var hitboxes = _localSim.Resolver.GetActiveHitboxes();
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
            var hitboxes = _localSim.Resolver.GetActiveHitboxes();
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

