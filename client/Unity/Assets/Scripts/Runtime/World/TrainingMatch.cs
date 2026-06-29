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
using UnityEngine.InputSystem;
namespace SlopArena.Client.World
{
    public enum NpcAiMode
    {
        Attack,
        Idle
    }
    
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
        [Header("Combat")]
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private NpcAiMode _npcAiMode = NpcAiMode.Attack;

        [Header("Aiming")]
        [SerializeField] private AimIndicator _aimIndicator;
        [SerializeField] private LayerMask _aimGroundMask = 1;
        private float _cachedCameraYaw;
        private float _cachedCameraPitch;
        private bool _isAimingThisTick;
        [SerializeField] private HUDManager _hudManager;
        [SerializeField] private bool _showHitboxes;

        private uint _tick;
        private ServerSimulation _localSim = null!;
        private ArenaDefinition _arenaDef;
        private CharacterDefinition _playerDef = null!;
        private const ulong PlayerEntityId = 1;
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
            _localSim = new ServerSimulation(arena);
            _combatFeedback.SetSimulation(_localSim);
            _hudManager?.Initialize(_localSim, PlayerEntityId);


            var playerDef = CharacterRegistry.Get(_playerClass);
            _playerDef = playerDef;
            var playerBaked = LoadBakedData(playerDef);
            var npcDef = CharacterRegistry.Get(_npcClass);
            var npcBaked = LoadBakedData(npcDef);

            // Set cooldown max ticks from character definition
            for (int slot = 0; slot < 6; slot++)
            {
                var spec = playerDef.GetSlotAbility(slot, airborne: false);
                if (spec != null)
                    _hudManager?.SetSlotMaxCooldown(slot, spec.CooldownTicks);
                var specAir = playerDef.GetSlotAbility(slot, airborne: true);
                if (specAir != null && specAir.CooldownTicks > spec?.CooldownTicks)
                    _hudManager?.SetSlotMaxCooldown(slot, specAir.CooldownTicks);
            }

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

            // NPC spawn at fixed position
            float npcX = 0f;
            float npcZ = 0f;
            _localSim.RegisterEntity(NpcEntityId, npcDef, new CharacterState
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

            // Set NPC respawn position to same relative location
            _localSim.SetRespawnPosition(NpcEntityId, npcX, 5f, npcZ);

            // Camera
            if (_cameraMount != null)
            {
                _cameraMount.SetTarget(_playerRenderer.transform);
                _cameraMount.ResetView(_playerRenderer.transform);
            }

            // Initialize aim indicator (auto-create if not wired in Inspector)
            if (_aimIndicator == null)
            {
                var go = new GameObject("AimIndicator");
                go.transform.SetParent(transform, false);
                _aimIndicator = go.AddComponent<AimIndicator>();
                Debug.Log("[TrainingMatch] Auto-created AimIndicator (no Inspector wiring needed)");
            }
            _aimIndicator.SetCharacter(_playerRenderer.transform, playerDef.CapsuleHeight);
            {
                var qSpec = playerDef.GetSlotAbility(2, false);
                var p = qSpec?.Params;
                float maxRange = p?.GetValueOrDefault("max_range", 12f) ?? 12f;
                _aimIndicator.SetMaxRange(maxRange);
                _aimIndicator.SetAbilityParams(
                    p?.GetValueOrDefault("gravity", 30f) ?? 30f,
                    p?.GetValueOrDefault("launch_angle", 30f) ?? 30f,
                    p?.GetValueOrDefault("launch_offset_y", 1.2f) ?? 1.2f
                );
            }
            _aimIndicator.gameObject.SetActive(true);

            Debug.Log($"[TrainingMatch] Started — arena: {arena.Name}, player at ({pSpawn.X:F1},{pSpawn.Y:F1})");
        }

        private void Update()
        {
            _inputController.Poll();
            if (_showHitboxes && _localSim != null)
                DrawHitboxDebug();

            if (_isAimingThisTick && _cameraMount != null)
            {
                _cameraMount.SetCameraYawDeg(_cachedCameraYaw);
                _cameraMount.SetCameraPitchDeg(_cachedCameraPitch);
            }
            else if (_cameraMount != null)
            {
                _cachedCameraYaw = _cameraMount.GetCameraYawDeg();
                _cachedCameraPitch = _cameraMount.GetCameraPitchDeg();
            }
        }

        protected override void OnMatchFixedUpdate()
        {
            if (_localSim == null || _playerRenderer == null) return;

            // Input
            // Poll done in Update() — keep FixedUpdate clean
            byte slot = _inputController.ConsumePendingSlotPress();
            if (slot > 0)
                Debug.Log($"[Input] slot={slot} animLock={_localSim.GetState(PlayerEntityId).AnimLockTicks} tick={_tick}");
            bool isAiming = false;
            byte aimingSlot = 0;
            {
                var ps = _localSim.GetState(PlayerEntityId);
                if (ps.State == ActionState.Attacking && ps.AttackSlot > 0)
                {
                    byte slotIdx = (byte)(ps.AttackSlot - 1);
                    var spec = _playerDef.GetSlotAbility(slotIdx, !ps.IsGrounded);
                    if (spec != null && (spec.Behavior == AbilityBehavior.AimedProjectile || spec.Behavior == AbilityBehavior.ChargeAttack))
                    {
                        // Check if the activation key is still held
                        bool keyHeld;
                        string keyDiagnostic;
                        switch (slotIdx)
                        {
                            case 0:
                                keyHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
                                keyDiagnostic = $"LMB held={keyHeld} mouse={(Mouse.current != null ? "ok" : "null")}";
                                break;
                            case 1:
                                keyHeld = _inputController.IsRmbHeld;
                                keyDiagnostic = $"RMB held={keyHeld}";
                                break;
                            case 2:
                                keyHeld = _inputController.IsQKeyHeld;
                                keyDiagnostic = $"Q held={keyHeld}";
                                break;
                            default:
                                keyHeld = true;
                                keyDiagnostic = "default=true";
                                break;
                        }
                        // Debug.Log($"[Aim] tick={_tick} slot={slotIdx} {keyDiagnostic} spec={spec.Behavior}");
                        if (keyHeld)
                        {
                            isAiming = true;
                            aimingSlot = slotIdx;
                        }
                        else
                        {
                            // Debug.Log($"[Aim] tick={_tick} key NOT held — aiming blocked");
                        }
                    }
                    else
                    {
                        // Debug.Log($"[Aim] tick={_tick} spec={(spec?.Behavior.ToString() ?? "null")} is not AimedProjectile");
                    }
                }
                else
                {
                    // Debug.Log($"[Aim] tick={_tick} NOT attacking: state={ps.State} slot={ps.AttackSlot}");
                }
            }
            _isAimingThisTick = isAiming;

            // During aiming, update the ground indicator and get aim data
            float? abilityAimYawRad = null;
            ushort? abilityAimDistance = null;

            // Always capture aim data from indicator BEFORE SetAiming resets _isAiming
            if (_aimIndicator != null && _aimIndicator.IsAiming)
            {
                (abilityAimYawRad, abilityAimDistance) = _aimIndicator.GetAimInput();
            }

            if (isAiming && _aimIndicator != null)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                _aimIndicator.SetAiming(true);
                _aimIndicator.UpdateAim();
            }
            else
            {
                if (_aimIndicator != null)
                    _aimIndicator.SetAiming(false);
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            var (input, _, _) = _inputController.BuildInputState(
                _cameraMount,
                _playerRenderer.transform.eulerAngles.y,
                isNPC: false,
                isAiming: isAiming,
                slot,
                abilityAimYawRad: abilityAimYawRad,
                abilityAimDistance: abilityAimDistance,
                canMove: null);
            // NPC AI
            var npcState = _localSim.GetState(NpcEntityId);
            var playerState = _localSim.GetState(PlayerEntityId);
            var npcInput = BuildNpcInput(npcState, playerState, _tick);
            // Tick
            _localSim.Tick(new Dictionary<ulong, InputState>
            {
                { PlayerEntityId, input },
                { NpcEntityId, npcInput }
            });
            // Track NPC death for visual feedback
            var npcStateAfter = _localSim.GetState(NpcEntityId);
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

