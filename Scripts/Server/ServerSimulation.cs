using System;
using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Server
{
    public class ServerSimulation
    {
        private readonly ArenaDefinition _arena;
        private readonly Dictionary<ulong, CharacterState> _states = new();
        private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
        private readonly Dictionary<ulong, ServerSkeleton> _skeletons = new();
        private readonly Dictionary<ulong, (string anim, float time)> _animState = new();

        public ServerSimulation(ArenaDefinition arena) => _arena = arena;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, ServerSkeleton? skeleton = null)
        {
            _defs[id] = def;
            _states[id] = initialState;
            if (skeleton != null) _skeletons[id] = skeleton;
            _animState[id] = ("idle", 0);
        }

        public void RemoveEntity(ulong id)
        {
            _states.Remove(id);
            _defs.Remove(id);
            _skeletons.Remove(id);
            _animState.Remove(id);
        }

        public CharacterState GetState(ulong id) => _states.TryGetValue(id, out var s) ? s : default;
        public Dictionary<ulong, CharacterState> GetAllStates() => _states;

        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            // ── Step 1: Simulate each entity ──
            foreach (var kvp in _states)
            {
                ulong id = kvp.Key;
                var state = kvp.Value;
                var def = _defs[id];
                var input = inputs.TryGetValue(id, out var i) ? i : default;
                Simulation.SimulateTick(ref state, def, input, _arena);
                _states[id] = state;
            }

            // ── Step 2: Build entity list ──
            var entityList = new List<SpellResolver.EntityData>();
            foreach (var kvp in _states)
            {
                ulong id = kvp.Key;
                var state = kvp.Value;
                var def = _defs[id];

                // Use skeleton-based hurtboxes if available (precise bone positions)
                if (_skeletons.TryGetValue(id, out var skel) && def.HurtboxBoneDefs != null && def.HurtboxBoneDefs.Length > 0)
                {
                    // Determine animation from action state
                    string targetAnim;
                    if (state.State == ActionState.Dashing)
                        targetAnim = "dash";
                    else if (state.State == ActionState.Attacking)
                        targetAnim = "melee";
                    else if (state.State == ActionState.Hitstun)
                        targetAnim = "small_hit";
                    else if (!state.IsGrounded)
                        targetAnim = state.VY > 0 ? "jump" : "fall";
                    else if (state.VX * state.VX + state.VZ * state.VZ > 1f)
                        targetAnim = "run";
                    else
                        targetAnim = "idle";

                    // Handle specific attacks via slot data is too complex for now
                    // For RMB attacks: "rmb_attack"
                    // For LMB chain: "jab" → "melee" → "flying_kick"

                    // Advance animation time
                    var anState = _animState[id];
                    if (anState.anim != targetAnim)
                    {
                        anState = (targetAnim, 0); // reset on transition
                    }
                    anState.time += Simulation.TickDt;
                    _animState[id] = anState;

                    // Sample skeleton at current animation time
                    int animIdx = FindAnimIndex(skel, anState.anim);
                    if (animIdx >= 0)
                    {
                        skel.SampleAnimation(animIdx, anState.time);
                        skel.ComputeWorldTransforms();
                    }
                    float px = state.PX, py = state.PY, pz = state.PZ;
                    float yaw = state.FacingYaw;
                    float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);

                    foreach (var hbd in def.HurtboxBoneDefs)
                    {
                        if (!skel.GetBoneWorldPosition(hbd.BoneName, out float bx, out float by, out float bz))
                            continue;

                        // Bone → world: rotate by yaw, then add character position
                        float wx = px + (bx * cos - bz * sin);
                        float wy = py + by;
                        float wz = pz + (bx * sin + bz * cos);

                        // Apply local-space offset
                        wx += hbd.OffX; wy += hbd.OffY; wz += hbd.OffZ;

                        entityList.Add(new SpellResolver.EntityData
                        {
                            Id = id,
                            PosX = wx, PosY = wy, PosZ = wz,
                            Radius = hbd.Radius,
                            Shape = HitboxShape.Sphere,
                            EndX = wx, EndY = wy, EndZ = wz,
                            Active = true,
                        });
                    }
                }
                else
                {
                    // Fallback: fixed capsules
                    float cos = MathF.Cos(state.FacingYaw);
                    float sin = MathF.Sin(state.FacingYaw);
                    foreach (var cap in def.HurtboxCapsules)
                    {
                        float sx = state.PX + cap.Sx * cos - cap.Sz * sin;
                        float sy = state.PY + cap.Sy;
                        float sz = state.PZ + cap.Sx * sin + cap.Sz * cos;
                        float ex = state.PX + cap.Ex * cos - cap.Ez * sin;
                        float ey = state.PY + cap.Ey;
                        float ez = state.PZ + cap.Ex * sin + cap.Ez * cos;
                        entityList.Add(new SpellResolver.EntityData
                        {
                            Id = id,
                            PosX = sx, PosY = sy, PosZ = sz,
                            Radius = cap.Radius,
                            Shape = (sx != ex || sy != ey || sz != ez) ? HitboxShape.Capsule : HitboxShape.Sphere,
                            EndX = ex, EndY = ey, EndZ = ez,
                            Active = true,
                        });
                    }
                }
            }

            // ── Step 3: Resolve hitboxes ──
            var hits = SpellResolver.Tick(entityList);
            foreach (var hit in hits)
            {
                if (_states.TryGetValue(hit.TargetEntityId, out var targetState))
                {
                    float finalDamage = hit.Damage;
                    targetState.DamagePercent += (ushort)finalDamage;
                    if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;
                    Simulation.ApplyKnockback(ref targetState, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
                    targetState.HitstunTicks = hit.StunTicks;
                    _states[hit.TargetEntityId] = targetState;
                }
            }

            // ── Step 4: Void death check ──
            var deadIds = new List<ulong>();
            foreach (var kvp in _states)
                if (kvp.Value.PY < _arena.KillHeight)
                    deadIds.Add(kvp.Key);
            foreach (var id in deadIds)
            {
                var def = _defs[id];
                var reset = new CharacterState
                {
                    PX = _arena.SpawnPoints[0].X,
                    PY = _arena.SpawnPoints[0].Y,
                    PZ = _arena.SpawnPoints[0].Z,
                    FacingYaw = _arena.SpawnPoints[0].Yaw,
                    JumpsLeft = def.Movement.MaxJumps,
                    AirDodgesLeft = 1,
                    DamagePercent = 0,
                };
                _states[id] = reset;
            }
        }

        private static int FindAnimIndex(ServerSkeleton skel, string name)
        {
            for (int i = 0; i < (skel.Animations?.Length ?? 0); i++)
                if (skel.Animations[i].Name == name)
                    return i;
            return -1;
        }
    }
}
