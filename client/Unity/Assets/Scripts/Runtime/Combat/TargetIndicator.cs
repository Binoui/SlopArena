using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Combat spatial-readability indicators, one set per player entity (ADR-0018 /
    /// issue #127). Driven by a getState func (local sim and rollback bridge alike)
    /// and the renderers' tracked positions each frame:
    ///
    /// 1. Ground-shadow ring — pinned to the arena heightmap surface at the player's XZ,
    ///    so you can read position even when airborne above you. White by default; red
    ///    under the local player's lock target (CharacterState.LockOn + TargetEntityId).
    /// 2. Height tether — a vertical line from the ring up to the lock target's model,
    ///    shown whenever that target is airborne. Makes the airborne Y/Z height
    ///    readable throughout a jump, not only during hitstun.
    /// 3. Launch arc — when any entity is launched (State == Hitstun with knockback
    ///    velocity), predict its flight to landing through the real flight law
    ///    (ServerSimulation + the same arena/def the live sim uses) and expose the
    ///    trajectory as editor gizmos. It is intentionally absent from gameplay.
    ///
    /// A ring hides while its player is off the heightmap grid (knocked out of the arena).
    /// The height tether remains a runtime renderer for the locked target while airborne.
    /// </summary>
    public class TargetIndicator : MonoBehaviour
    {
        private readonly struct Ring
        {
            public readonly PlayerRenderer Renderer;
            public readonly ulong EntityId;
            public readonly Transform Transform;
            public readonly MeshRenderer Mesh;

            public Ring(PlayerRenderer renderer, Transform transform, MeshRenderer mesh)
            {
                Renderer = renderer;
                EntityId = renderer != null ? renderer.EntityId : 0;
                Transform = transform;
                Mesh = mesh;
            }
        }

        private const float RingHeightAboveFloor = 0.05f;
        private static readonly Color White = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color Red = new Color(1f, 0.2f, 0.15f, 0.7f);
        private static readonly Color TetherColor = new Color(1f, 0.35f, 0.25f, 0.5f);

        /// <summary>Min knockback speed that counts as a launch (State == Hitstun).</summary>
        private const float LaunchSpeedThreshold = 0.05f;
        /// <summary>Max ticks to forward-simulate a launch (mirrors AbilityLab, ~40s).</summary>
        private const int MaxTrajectoryTicks = 2400;

        private Ring[] _rings = Array.Empty<Ring>();
        private Func<ulong, CharacterState> _getState;
        private ArenaHeightmap _heightmap;
        private ArenaDefinition _arena;
        private ulong _localPlayerId;

        // Per-ring indicator state, indexed identically to _rings.
        private LineRenderer[] _tetherLines = Array.Empty<LineRenderer>();
        private List<(Vector3 pos, char phase)>[] _arcPoints = Array.Empty<List<(Vector3 pos, char phase)>>();
        private bool[] _wasLaunched = Array.Empty<bool>();
        private bool[] _arcActive = Array.Empty<bool>();

        public void Init(Func<ulong, CharacterState> getState, PlayerRenderer[] renderers, ulong localPlayerId, ArenaDefinition arena)
        {
            _getState = getState;
            _localPlayerId = localPlayerId;
            _heightmap = arena.Heightmap;
            _arena = arena;

            _rings = new Ring[renderers?.Length ?? 0];
            _tetherLines = new LineRenderer[_rings.Length];
            _arcPoints = new List<(Vector3, char)>[_rings.Length];
            _wasLaunched = new bool[_rings.Length];
            _arcActive = new bool[_rings.Length];

            for (int i = 0; i < _rings.Length; i++)
            {
                var renderer = renderers[i];
                var ringGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ringGO.name = "GroundShadowRing";
                ringGO.transform.SetParent(transform, false);
                ringGO.transform.localScale = new Vector3(1.2f, 0.04f, 1.2f);
                ringGO.transform.localPosition = Vector3.zero;
                Destroy(ringGO.GetComponent<CapsuleCollider>());
                var mr = ringGO.GetComponent<MeshRenderer>();
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = White;
                mr.sharedMaterial = mat;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rings[i] = new Ring(renderer, ringGO.transform, mr);
                ringGO.SetActive(false);

                _tetherLines[i] = CreateLine(transform, $"HeightTether_{renderer?.EntityId}", TetherColor);
            }
        }

        private void Update()
        {
            if (_getState == null || _rings.Length == 0) return;

            var local = _getState(_localPlayerId);
            bool locked = local.LockOn;
            ulong targetId = local.TargetEntityId;

            for (int i = 0; i < _rings.Length; i++)
            {
                var ring = _rings[i];
                var renderer = ring.Renderer;
                if (renderer == null)
                {
                    ring.Transform.gameObject.SetActive(false);
                    _tetherLines[i].enabled = false;
                    _arcActive[i] = false;
                    continue;
                }

                Vector3 pos = renderer.transform.position;
                float floorY = _heightmap.Sample(pos.x, pos.z);
                if (floorY <= float.MinValue / 2f)
                {
                    // Off the heightmap grid (knocked out of the arena) — no floor to pin to.
                    ring.Transform.gameObject.SetActive(false);
                    _tetherLines[i].enabled = false;
                    _arcActive[i] = false;
                    continue;
                }

                ring.Transform.gameObject.SetActive(true);
                ring.Transform.position = new Vector3(pos.x, floorY + RingHeightAboveFloor, pos.z);
                var state = _getState(ring.EntityId);
                ring.Mesh.material.color = locked && targetId == ring.EntityId ? Red : White;
                UpdateTether(i, ring, pos, floorY, locked, targetId, state);
                UpdateLaunchArc(i, ring, state);
            }
        }

        // ── B: height tether ──

        private void UpdateTether(int i, in Ring ring, Vector3 pos, float floorY, bool locked, ulong targetId, in CharacterState state)
        {
            bool isLockTarget = locked && targetId == ring.EntityId;
            bool show = isLockTarget && !state.IsGrounded;
            if (!show)
            {
                _tetherLines[i].enabled = false;
                return;
            }

            _tetherLines[i].enabled = true;
            _tetherLines[i].positionCount = 2;
            _tetherLines[i].SetPosition(0, new Vector3(pos.x, floorY + RingHeightAboveFloor, pos.z));
            _tetherLines[i].SetPosition(1, new Vector3(pos.x, pos.y, pos.z));
        }

        // ── A: launch trajectory arc ──

        private void UpdateLaunchArc(int i, in Ring ring, in CharacterState state)
        {
            float kvMag = Mathf.Sqrt(
                state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
            bool inLaunch = state.State == ActionState.Hitstun && kvMag > LaunchSpeedThreshold;

            if (inLaunch && !_wasLaunched[i] && ring.Renderer.CharacterDef != null)
            {
                _wasLaunched[i] = true;
                var arc = PredictFlight(state, ring.Renderer.CharacterDef);
                _arcPoints[i] = arc;
                _arcActive[i] = arc.Count > 1;
            }
            else if (!inLaunch)
            {
                _wasLaunched[i] = false;
            }

            if (_arcActive[i] && state.IsGrounded)
                _arcActive[i] = false;
        }

        private void OnDrawGizmos()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (!_arcActive[i] || _arcPoints[i] == null) continue;

                var arc = _arcPoints[i];
                for (int j = 1; j < arc.Count; j++)
                {
                    Gizmos.color = PhaseColor(arc[j].phase);
                    Gizmos.DrawLine(arc[j - 1].pos, arc[j].pos);
                }
            }
        }

        /// <summary>
        /// Forward-simulate the victim's launch to landing through the REAL flight law
        /// (a ServerSimulation over the live arena, seeded with the victim's current
        /// post-launch state). Mirrors AbilityLab.ResolveTrajectory but seeds existing
        /// knockback velocity instead of re-applying the hit.
        /// </summary>
        private List<(Vector3 pos, char phase)> PredictFlight(in CharacterState victim, CharacterDefinition def)
        {
            var sim = new ServerSimulation(_arena);
            sim.RegisterEntity(victim.EntityId, def, victim, baked: null);
            var inputs = new Dictionary<ulong, InputState> { [victim.EntityId] = default };

            var arc = new List<(Vector3 pos, char phase)>
            {
                (new Vector3(victim.PX, victim.PY, victim.PZ), 'H'),
            };
            float maxPy = victim.PY;
            bool apexMarked = false;
            for (int t = 0; t < MaxTrajectoryTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(victim.EntityId);
                bool atApex = !apexMarked && s.PY <= maxPy && t > 0 && !s.IsGrounded && s.HitstunTicks == 0;
                if (s.PY > maxPy) maxPy = s.PY;
                else if (atApex) apexMarked = true;
                char phase = s.IsGrounded ? 'G' : s.HitstunTicks > 0 ? 'H' : atApex ? 'A' : 'F';
                arc.Add((new Vector3(s.PX, s.PY, s.PZ), phase));
                if (s.IsGrounded) break;
            }
            return arc;
        }
        // ── Helpers ──

        private static LineRenderer CreateLine(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startWidth = 0.05f;
            line.endWidth = 0.05f;
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            var c = color;
            c.a = Mathf.Min(c.a, 0.6f);
            line.startColor = line.endColor = c;
            line.receiveShadows = false;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        private static Color PhaseColor(char phase) => phase switch
        {
            'H' => new Color(0f, 1f, 1f, 0.6f),
            'F' => new Color(0.2f, 0.5f, 1f, 0.6f),
            'A' => new Color(1f, 1f, 1f, 0.7f),
            _   => new Color(1f, 0.2f, 0.1f, 0.6f),
        };
    }
}
