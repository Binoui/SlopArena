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

        private uint _tick;
        private ServerSimulation _localSim = null!;
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
            _localSim = new ServerSimulation(arena);

            var charDef = CharacterRegistry.Get(CharacterClass.Manki);
            var baked = LoadBakedData(charDef);

            // Apply model Y offset for visual alignment with capsule
            _playerRenderer.ModelYOffset = charDef.ModelYOffset;
            if (_npcRenderer != null)
                _npcRenderer.ModelYOffset = charDef.ModelYOffset;

            // Wire capsule size + hurtbox data for debug visualization
            _playerRenderer.CapsuleRadius = charDef.CapsuleRadius;
            _playerRenderer.CapsuleHeight = charDef.CapsuleHeight;
            _playerRenderer.HurtboxBoneDefs = charDef.HurtboxBoneDefs;
            if (_npcRenderer != null)
            {
                _npcRenderer.CapsuleRadius = charDef.CapsuleRadius;
                _npcRenderer.CapsuleHeight = charDef.CapsuleHeight;
                _npcRenderer.HurtboxBoneDefs = charDef.HurtboxBoneDefs;
            }

            // Player spawn
            var pSpawn = arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0] : new SpawnPoint();
            _localSim.RegisterEntity(PlayerEntityId, charDef, new CharacterState
            {
                PX = pSpawn.X, PY = pSpawn.Y, PZ = pSpawn.Z,
                FacingYaw = pSpawn.Yaw,
                JumpsLeft = charDef.Movement.MaxJumps,
            }, baked);

            // NPC spawn
            var nSpawn = arena.SpawnPoints.Length > 1 ? arena.SpawnPoints[1] : new SpawnPoint();
            _localSim.RegisterEntity(NpcEntityId, charDef, new CharacterState
            {
                PX = nSpawn.X, PY = nSpawn.Y, PZ = nSpawn.Z,
                FacingYaw = nSpawn.Yaw + Mathf.PI,
                JumpsLeft = charDef.Movement.MaxJumps,
            }, baked);

            // Position renderers
            _playerRenderer.transform.position = new Vector3(pSpawn.X, pSpawn.Y, pSpawn.Z);
            if (_npcRenderer != null)
                _npcRenderer.transform.position = new Vector3(nSpawn.X, nSpawn.Y, nSpawn.Z);

            // Camera
            if (_cameraMount != null)
            {
                _cameraMount.SetTarget(_playerRenderer.transform);
                _cameraMount.ResetView(_playerRenderer.transform);
            }

            Debug.Log($"[TrainingMatch] Started — arena: {arena.Name}, player at ({pSpawn.X:F1},{pSpawn.Y:F1})");
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_localSim == null || _playerRenderer == null) return;

            // Input
            _inputController.Poll();
            byte slot = _inputController.ConsumePendingSlotPress();
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

            // Apply states
            _playerRenderer.ApplyServerState(_localSim.GetState(PlayerEntityId));
            if (_npcRenderer != null)
                _npcRenderer.ApplyServerState(_localSim.GetState(NpcEntityId));
        }
    }
}
