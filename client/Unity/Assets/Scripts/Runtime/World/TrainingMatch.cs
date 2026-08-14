using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine;
using UnityEngine.InputSystem;
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

        [Header("Characters (Player)")]
        [SerializeField] private CharacterClass _playerClassOverride;
 
        [Header("Characters (NPC)")]
        [SerializeField] private CharacterClass _npcClass = CharacterClass.Manki;
 
        [Header("Arena")]
        [SerializeField] private string _arenaNameOverride = "";

        [Header("Combat")]
        [SerializeField] private CombatFeedback _combatFeedback;
        [SerializeField] private ProjectileVFXManager _projectileVFX;
        [SerializeField] private NpcAiMode _npcAiMode = NpcAiMode.Attack;

        [Header("Hitboxes")]
        [SerializeField] private bool _showHitboxes;

        private LocalSimulationBridge _bridge = null!;
        protected override ISimulationBridge Bridge => _bridge;

        private uint _tick;
#if UNITY_EDITOR
        // Launch-contract sentinel: logs the applied launch once per hit so the game can
        // be checked against tools/MoveDataReport (fightguy --parity). A mismatch here
        // (e.g. the 2026-08-14 x87 float bug: every hit took the unscaled force path)
        // means the game behaves differently from the report.
        private float _lastContractDamage;
#endif
        private ArenaDefinition _arenaDef;
        private const ulong NpcEntityId = 100;
        private byte _npcLastDeaths;
        protected override void OnMatchStart()
        {
            string arenaName = string.IsNullOrEmpty(_arenaNameOverride) ? MatchConfig.ArenaName : _arenaNameOverride;
            Debug.Log($"[{GetType().Name}] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={arenaName}");
            // Baked arena is required (issue #77): hardcoded ArenaRegistry arenas carry
            // no collision data, so a missing .arena file used to make players fall
            // through the floor (Simulation grounded at KillHeight + 1).
            string? arenaPath = BakedContentPaths.ResolveArena(arenaName);
            if (arenaPath == null)
            {
                Debug.LogError($"[TrainingMatch] Baked arena '{arenaName}' not found (looked in StreamingAssets/arenas and repo data/arenas). " +
                               "Bake the arena or run scripts/build-release.sh. Aborting match start.");
                return;
            }
            var arenaOpt = ArenaBinaryFormat.LoadFromFile(arenaPath);
            if (arenaOpt is not ArenaDefinition arena)
            {
                Debug.LogError($"[TrainingMatch] Failed to parse baked arena: {arenaPath}");
                return;
            }
            Debug.Log($"[TrainingMatch] Loaded arena: {arenaPath} — {arena.CollisionTriangles?.Length ?? 0} tris, heightmap={arena.Heightmap.Width}x{arena.Heightmap.Height}");

            // Wire sim debug logging to Unity console
            SlopArena.Shared.Simulation.OnDebugLog = msg => Debug.Log(msg);
            _arenaDef = arena;
            SpawnStageVisual(arena);

            // Bridge (local). NoWinMatchRule: training never eliminates or ends —
            // the only way out is the Esc exit below (issue #37 follow-up).
            _bridge = new LocalSimulationBridge(arena, NoWinMatchRule.Instance);
            _combatFeedback.SetSimulation(_bridge.InternalSim);
            if (_projectileVFX == null)
                _projectileVFX = gameObject.AddComponent<ProjectileVFXManager>();
            _projectileVFX.SetSimulation(_bridge.InternalSim);
            var playerClass = _playerClassOverride != CharacterClass.None ? _playerClassOverride : MatchConfig.PlayerClass;
            var playerDef = CharacterRegistry.Get(playerClass);
            var playerBaked = LoadBakedData(playerDef);
            playerDef = ApplyHurtboxOverride(playerDef, playerBaked);
            _playerDef = playerDef;
            var npcDef = CharacterRegistry.Get(_npcClass);
            var npcBaked = LoadBakedData(npcDef);
            npcDef = ApplyHurtboxOverride(npcDef, npcBaked);
 
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

            // Set NPC respawn position (yaw preserves the old SpawnPoints[0] facing)
            _bridge.SetRespawnPosition(NpcEntityId, npcX, 5f, npcZ,
                arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0].Yaw : 0f);

            // Shared camera + aim setup
            SetupCamera();
            SetupAimHandler(playerDef);
        }

        private void Update()
        {
            // While paused the MatchPauseMenu owns Esc and input polling is skipped so
            // no buffered presses leak into the first frame after resume (issue #77).
            if (IsPaused) return;

            _inputController.Poll();

            if (_showHitboxes && _bridge != null)
            {
                DrawHitboxDebug();
                DrawHurtboxDebug();
            }
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
            _projectileVFX?.OnTick();
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

            // One line per hit: the launch the sim actually applied. Compare KV mag and
            // hitstun with the report's trajectory rows (same %) to verify game parity.
            if (hitState.HitstunTicks > 0 && _lastContractDamage != hitState.DamagePercent)
            {
                _lastContractDamage = hitState.DamagePercent;
                float kvMag = Mathf.Sqrt(hitState.KVX * hitState.KVX + hitState.KVY * hitState.KVY + hitState.KVZ * hitState.KVZ);
                Debug.Log($"[Launch] applied KV={kvMag:F2} hitstun={hitState.HitstunTicks} damage={hitState.DamagePercent:F1}");
            }
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
            DrawHurtboxGizmos();
        }

        private void DrawHurtboxGizmos()
        {
            var entities = _bridge.InternalSim.GetLastEntityData();
            foreach (var ed in entities)
            {
                // Color by entity: player=green, NPC=red, others=blue
                Gizmos.color = ed.Id switch
                {
                    var id when id == PlayerEntityId => new Color(0f, 1f, 0.3f, 0.5f),
                    NpcEntityId    => new Color(1f, 0.3f, 0.3f, 0.5f),
                    _              => new Color(0.3f, 0.3f, 1f, 0.5f),
                };
                var center = new Vector3(ed.PosX, ed.PosY, ed.PosZ);
                if (ed.Shape == HitboxShape.Sphere)
                {
                    Gizmos.DrawWireSphere(center, ed.Radius);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(ed.EndX, ed.EndY, ed.EndZ), ed.Radius, Gizmos.DrawLine);
                }
            }
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
                    DrawWireCapsule(center, new Vector3(hb.EndX, hb.EndY, hb.EndZ), hb.Radius, Gizmos.DrawLine);
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
                    DrawWireCapsule(center, new Vector3(hb.EndX, hb.EndY, hb.EndZ), hb.Radius, (p0, p1) => Debug.DrawLine(p0, p1, color));
                }
            }
        }

        private void DrawHurtboxDebug()
        {
            var entities = _bridge.InternalSim.GetLastEntityData();
            foreach (var ed in entities)
            {
                Color color = ed.Id switch
                {
                    var id when id == PlayerEntityId => new Color(0f, 1f, 0.3f, 0.5f),
                    NpcEntityId    => new Color(1f, 0.3f, 0.3f, 0.5f),
                    _              => new Color(0.3f, 0.3f, 1f, 0.5f),
                };
                var center = new Vector3(ed.PosX, ed.PosY, ed.PosZ);
                if (ed.Shape == HitboxShape.Sphere)
                {
                    DebugDrawWireSphere(center, ed.Radius, color);
                }
                else
                {
                    DrawWireCapsule(center, new Vector3(ed.EndX, ed.EndY, ed.EndZ), ed.Radius, (p0, p1) => Debug.DrawLine(p0, p1, color));
                }
            }
        }

        /// <summary>
        /// Draws the true wireframe of a capsule — segment (a→b) swept with radius:
        /// cap circles at both ends, 4 longitudinal cylinder lines, and dome arcs for
        /// the rounded caps. Matches the swept-sphere volume the collision tests use
        /// (SpellResolver.CapsuleCollision), unlike a naive 2-spheres + line.
        /// </summary>
        private static void DrawWireCapsule(Vector3 a, Vector3 b, float radius, Action<Vector3, Vector3> drawLine)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f)
            {
                // Degenerate (start ≈ end): single cross-section circle. Defensive only —
                // callers route equal endpoints to the sphere branch.
                const int seg = 16;
                Vector3 prev = default;
                for (int i = 0; i <= seg; i++)
                {
                    float angle = i * (Mathf.PI * 2f / seg);
                    Vector3 p = a + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (i > 0) drawLine(prev, p);
                    prev = p;
                }
                return;
            }

            Vector3 dir = axis / len;
            Vector3 u = Vector3.Cross(dir, Vector3.up);
            if (u.sqrMagnitude < 1e-6f)
                u = Vector3.Cross(dir, Vector3.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(dir, u);

            const int segments = 16;
            const int planes = 4;

            // ── Cap circles (cylinder cross-section) at both endpoints ──
            Vector3 prevA = default, prevB = default;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
                Vector3 pa = a + radial;
                Vector3 pb = b + radial;
                if (i > 0)
                {
                    drawLine(prevA, pa);
                    drawLine(prevB, pb);
                }
                prevA = pa;
                prevB = pb;
            }

            // ── Longitudinal lines: the cylinder silhouette ──
            for (int p = 0; p < planes; p++)
            {
                float angle = p * (Mathf.PI * 2f / planes);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
                drawLine(a + radial, b + radial);
            }

            // ── Dome arcs: semicircle from one cap-circle point to the opposite one,
            // through the pole (a − dir·r / b + dir·r) — the rounded cap surface ──
            for (int p = 0; p < planes; p++)
            {
                float angle = p * (Mathf.PI * 2f / planes);
                Vector3 radial = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;

                Vector3 prev = a + radial;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f * Mathf.PI;
                    Vector3 point = a + radial * Mathf.Cos(t) - dir * radius * Mathf.Sin(t);
                    drawLine(prev, point);
                    prev = point;
                }

                prev = b + radial;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f * Mathf.PI;
                    Vector3 point = b + radial * Mathf.Cos(t) + dir * radius * Mathf.Sin(t);
                    drawLine(prev, point);
                    prev = point;
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
