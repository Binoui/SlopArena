using System;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Abilities;

namespace SlopArena.Shared
{
	public class ServerSimulation
	{
		private readonly ArenaDefinition _arena;
		private readonly Dictionary<ulong, CharacterState> _states = new();
		private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
		private readonly Dictionary<ulong, BakedAnimationData> _bakedData = new();
		private readonly Dictionary<ulong, int> _animFrames = new();
		private readonly Dictionary<ulong, int> _prevAnimIndex = new();
		private List<SpellResolver.EntityData> _lastEntityList = new();
		public List<SpellResolver.HitResult> LastTickHits { get; } = new();
		private readonly SpellResolver _spellResolver = new();
		private readonly Dictionary<ulong, (float x, float y, float z)> _respawnPositions = new();

		// ── Ability pool ──
		private readonly Dictionary<ulong, ServerAbility> _activeAbilities = new();

		public ServerSimulation(ArenaDefinition arena) => _arena = arena;

		public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
		{
			_defs[id] = def;
			initialState.EntityId = id;
			_states[id] = initialState;
			if (baked != null) _bakedData[id] = baked;
			_animFrames[id] = 0;
			_prevAnimIndex[id] = -1;
		}

		public void SetRespawnPosition(ulong entityId, float x, float y, float z)
		{
			_respawnPositions[entityId] = (x, y, z);
		}

		public void RemoveEntity(ulong id)
		{
			_states.Remove(id);
			_defs.Remove(id);
			_bakedData.Remove(id);
			_animFrames.Remove(id);
			_prevAnimIndex.Remove(id);
			_activeAbilities.Remove(id);
			_respawnPositions.Remove(id);
		}

		public CharacterState GetState(ulong id) => _states.TryGetValue(id, out var s) ? s : default;
		public void SetState(ulong id, CharacterState state) => _states[id] = state;
		public Dictionary<ulong, CharacterState> GetAllStates() => _states;
		public List<SpellResolver.EntityData> GetLastEntityData() => _lastEntityList;
		public SpellResolver Resolver => _spellResolver;

		// ── Ability pool management ──

		/// <summary>
		/// Activate a server ability for an entity.
		/// Calls OnStart and registers the ability for per-tick updates.
		/// </summary>
		public void ActivateAbility(ulong entityId, ServerAbility ability, byte slot, CharacterDefinition def)
		{
			if (!_states.TryGetValue(entityId, out var state)) return;
			ability.Resolver = _spellResolver;
			ability.SimulationStates = _states;
			ability.BakedData = _bakedData.TryGetValue(entityId, out var b) ? b : null;
			ability.CharacterDef = def;
			ability.Slot = slot;
			ability.OnStart(ref state, def);
			state.AnimIndex = ability.AnimIndex;
			state.IsServerAbility = true;
			state.AttackSlot = (byte)(slot + 1);
			_states[entityId] = state;
			_activeAbilities[entityId] = ability;
		}


		/// <summary>
		/// Get the active ability for an entity, or null if none.
		/// </summary>
		public ServerAbility? GetActiveAbility(ulong entityId)
		{
			return _activeAbilities.TryGetValue(entityId, out var a) ? a : null;
		}

		/// <summary>
		/// Get cooldown ticks for a slot (1-6).
		/// </summary>
		private static ushort GetCooldown(CharacterState s, byte slot) => slot switch
		{
			1 => s.Cooldown0,
			2 => s.Cooldown1,
			3 => s.Cooldown2,
			4 => s.Cooldown3,
			5 => s.Cooldown4,
			6 => s.Cooldown5,
			_ => 0,
		};

		/// <summary>
		/// Set cooldown ticks for a slot (1-6).
		/// </summary>
		private static void SetCooldown(ref CharacterState s, byte slot, ushort ticks)
		{
			switch (slot)
			{
				case 1: s.Cooldown0 = ticks; break;
				case 2: s.Cooldown1 = ticks; break;
				case 3: s.Cooldown2 = ticks; break;
				case 4: s.Cooldown3 = ticks; break;
				case 5: s.Cooldown4 = ticks; break;
				case 6: s.Cooldown5 = ticks; break;
			}
		}

		/// <summary>
		/// Tick all active abilities. Called after simulation each frame.
		/// Abilities that set AttackSlot=0 (via EndAbility) are auto-deactivated.
		/// Abilities are also interrupted (without calling OnEnd) when the state
		/// is no longer Attacking — e.g. dash cancelling an attack, or idle.
		/// </summary>
		public void TickAbilities(Dictionary<ulong, InputState> inputs)
		{
			// Collect entities whose ability ended this tick (can't modify dict during iteration)
			var ended = new List<ulong>();

			foreach (var kvp in _activeAbilities)
			{
				ulong id = kvp.Key;
				var ability = kvp.Value;
				if (!_states.TryGetValue(id, out var state)) continue;
				if (!_defs.TryGetValue(id, out var def)) continue;

				// Interrupt: if state left Attacking (dash, idle, or other), deactivate without OnEnd.
				// StartDash clears AttackSlot/AnimLockTicks/IsServerAbility, so the ability
				// won't see stale attack fields — just remove it cleanly.
				if (state.State != ActionState.Attacking)
				{
					ended.Add(id);
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[AbilityInterrupt] entity={id} slot={ability.Slot} state={state.State} — deactivated");
					continue;
				}

				var input = inputs.TryGetValue(id, out var i) ? i : default;

				ability.Tick(ref state, ref input, def);
				state.AnimIndex = ability.AnimIndex;

				// Check if ability ended itself (EndAbility set AttackSlot=0)
				if (state.AttackSlot == 0)
				{
					ended.Add(id);
					_states[id] = state; // Persist EndAbility changes (State=Idle, AttackSlot=0)
				}
				else
				{
					_states[id] = state;
				}
			}

			// Deactivate ended abilities (cooldown still needs applying)
			foreach (var id in ended)
			{
				if (_activeAbilities.TryGetValue(id, out var ability)
				    && _states.TryGetValue(id, out var state))
				{
					// OnEnd already called by EndAbility, skip the duplicate.
					// For interrupted abilities (dash/interrupt): OnEnd was NOT called — but
					// StartDash already cleared AttackSlot/AnimLockTicks/IsServerAbility, so
					// the clean-up below (cooldown, buffered slot, AnimLockTicks) is still correct.
					
					// Apply cooldown
					if (ability.Slot < 6)
					{
						SetCooldown(ref state, (byte)(ability.Slot + 1), ability.Cooldown);
						if (Simulation.OnDebugLog != null)
							Simulation.OnDebugLog.Invoke(
								$"[Cooldown] Set slot={(byte)(ability.Slot + 1)} cooldown={ability.Cooldown} entity={id}");
					}

					// Clear buffered slot to prevent data-driven re-trigger.
					// Without this, a LMB press during the last stage gets buffered by
					// SimulateTick's input buffer (line 268) before the ability expires.
					// On the next tick, the buffered slot creates a data-driven attack
					// with no ServerAbility — the character appears stuck in Attacking
					// with no animation for the full stage duration.
					if (state.BufferedSlot > 0)
					{
						state.BufferedSlot = 0;
						if (Simulation.OnDebugLog != null)
							Simulation.OnDebugLog.Invoke(
								$"[AbilityEnd] entity={id} cleared BufferedSlot — prevented data-driven re-trigger");
					}

					state.AnimLockTicks = 0; // Unlock input for next data-driven attack
					state.IsServerAbility = false; // Allow data-driven attacks to work after ServerAbility ends
					_states[id] = state; // Persist cooldown + buffered slot clear
				}
				_activeAbilities.Remove(id);
			}
		}

		public static List<SpellResolver.EntityData> BuildEntitiesFromState(
			CharacterState state, CharacterDefinition def, BakedAnimationData baked,
			string targetAnim, int animFrame)
		{
			var list = new List<SpellResolver.EntityData>();
			if (baked != null && def.HurtboxBoneDefs != null && def.HurtboxBoneDefs.Length > 0)
			{
				int animIdx = baked.FindAnimIndex(targetAnim);
				if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
				if (animIdx >= 0)
				{
					int fc = baked.Animations[animIdx].FrameCount;
					if (animFrame >= fc) animFrame = fc - 1;
					float px = state.PX, py = state.PY, pz = state.PZ;
					float yaw = state.FacingYaw;
					float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
					float scale = def.HurtboxBoneScale;
					for (int bi = 0; bi < def.HurtboxBoneDefs.Length; bi++)
					{
						var hbd = def.HurtboxBoneDefs[bi];
						if (!baked.GetBonePosition(targetAnim, animFrame, bi, out float bx, out float by, out float bz)) continue;
						bx *= scale; by *= scale; bz *= scale;
						float wx = px + ((bx * cos) + (bz * sin));
						float wy = def.BoneYToWorldY(py, by);
						float wz = pz + ((-bx * sin) + (bz * cos));
						list.Add(new SpellResolver.EntityData
						{
							Id = 0, PosX = wx, PosY = wy, PosZ = wz,
							Radius = hbd.Radius, Shape = HitboxShape.Sphere,
							EndX = wx, EndY = wy, EndZ = wz, Active = true,
						});
					}
				}
			}
			else
			{
				float cos = MathF.Cos(state.FacingYaw);
				float sin = MathF.Sin(state.FacingYaw);
				foreach (var cap in def.HurtboxCapsules)
				{
					float sx = state.PX + (cap.Sx * cos) + (cap.Sz * sin);
					float sy = state.PY + cap.Sy;
					float sz = state.PZ + ((-cap.Sx * sin) + (cap.Sz * cos));
					float ex = state.PX + (cap.Ex * cos) + (cap.Ez * sin);
					float ey = state.PY + cap.Ey;
					float ez = state.PZ + ((-cap.Ex * sin) + (cap.Ez * cos));
					list.Add(new SpellResolver.EntityData
					{
						PosX = sx, PosY = sy, PosZ = sz, Radius = cap.Radius,
						Shape = (sx != ex || sy != ey || sz != ez) ? HitboxShape.Capsule : HitboxShape.Sphere,
						EndX = ex, EndY = ey, EndZ = ez, Active = true,
					});
				}
			}
			return list;
		}

        /// <summary>
        /// Resolve the animation name and baked frame for hitbox/hurtbox bone lookup.
        /// Returns false when there's no valid baked data for this entity or animation index is invalid.
        /// Side effects: advances _animFrames and _prevAnimIndex for the entity.
        /// </summary>
        private bool ResolveBoneAnimFrame(ulong id, CharacterState state, CharacterDefinition def,
            out BakedAnimationData baked, out string targetAnim, out int bakedFrame)
        {
            baked = null!;
            targetAnim = null!;
            bakedFrame = 0;

            if (!_bakedData.TryGetValue(id, out baked!) || def.HurtboxBoneDefs == null || def.HurtboxBoneDefs.Length == 0)
                return false;

            // Resolve animation name based on current state
            if (state.State == ActionState.Dashing) targetAnim = "dash";
            else if (state.State == ActionState.Attacking && state.AttackSlot > 0)
            {
                bool airborne = !state.IsGrounded;
                var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
                int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
                targetAnim = (stageIdx >= 0 && stageIdx < ability.AnimationNames.Length) ? ability.AnimationNames[stageIdx] : "melee";
            }
            else if (state.State == ActionState.Hitstun) targetAnim = state.HitstunLevel switch
            {
                1 => def.HitMediumAnim,
                2 => def.HitHardAnim,
                _ => def.HitSmallAnim,
            };
            else if (!state.IsGrounded) targetAnim = state.VY > 0 ? "jump" : "fall";
            else if ((state.VX * state.VX) + (state.VZ * state.VZ) > 1f) targetAnim = "run";
            else targetAnim = "idle";

            int animIdx = baked.FindAnimIndex(targetAnim);
            if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
            if (animIdx < 0) return false;

            int fc = baked.Animations[animIdx].FrameCount;
            int prevAnim = _prevAnimIndex.TryGetValue(id, out var p) ? p : -1;
            int frame = _animFrames.TryGetValue(id, out var f) ? f : 0;
            if (prevAnim != animIdx) { frame = 0; _prevAnimIndex[id] = animIdx; }
            int nextFrame = frame + 1;
            if (nextFrame >= fc) nextFrame = 0;
            _animFrames[id] = nextFrame;

            bakedFrame = frame;
            if (state.State == ActionState.Attacking && state.AttackSlot > 0)
            {
                bool airborne = !state.IsGrounded;
                var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
                int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
                if (stageIdx >= 0 && stageIdx < ability.Stages.Length)
                {
                    int durationTicks = ability.Stages[stageIdx].DurationTicks;
                    if (durationTicks > 0) bakedFrame = Math.Min(frame * fc / durationTicks, fc - 1);
                }
            }

            return true;
        }

		private void PreTickAbilities(Dictionary<ulong, InputState> inputs)
		{
			// ── Pre-sim: Activate server abilities from inputs ──
			// Snapshot keys to avoid collection-modified during ActivateAbility writes
			ulong[] entityIds = new ulong[_states.Count];
			_states.Keys.CopyTo(entityIds, 0);
			foreach (var id in entityIds)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				var input = inputs.TryGetValue(id, out var i) ? i : default;
				if (input.ActiveSlot == 0) continue;
				if (state.AnimLockTicks > 0 || state.HitstunTicks > 0) continue;
				if (state.State != ActionState.Idle && state.State != ActionState.Attacking) continue;

				var def = _defs[id];
				bool airborne = !state.IsGrounded;
				var spec = def.GetSlotAbility(input.ActiveSlot - 1, airborne);

				ushort cooldown = GetCooldown(state, input.ActiveSlot);
				if (cooldown > 0)
				{
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[Cooldown] BLOCKED slot={input.ActiveSlot} cooldown={cooldown} entity={id}");
					continue;
				}

                // Reject F (Overclock) reactivation while buff already active
                if (input.ActiveSlot == 6 && (state.BuffActiveFlags & (byte)SlopArena.Shared.BuffType.Overclock) != 0)
                    continue;

				// Server-side ability: try to create via slot mapping
				if (_activeAbilities.ContainsKey(id)) continue;

				var ability = SlopArena.Shared.Abilities.AbilityFactory.CreateServer(def.Class, (byte)(input.ActiveSlot - 1), airborne);
				if (ability == null)
				{
				    // Data-driven attack (no ServerAbility). When state is already Attacking,
				    // consume the input to prevent SimulateTick from immediately re-triggering
				    // the attack on the expiry tick (SimulateTick line 246).
				    // Without this, the Idle state set by expiry is immediately overwritten
				    // and the client never receives an Idle state packet.
				    if (state.State == ActionState.Attacking)
				    {
				        var consumed = input;
				        consumed.ActiveSlot = 0;
				        inputs[id] = consumed;
				    }
				    continue;
				}
				SlopArena.Shared.Abilities.AbilityFactory.InitFromSpec(ability, spec, (byte)(input.ActiveSlot - 1));
				ActivateAbility(id, ability, (byte)(input.ActiveSlot - 1), def);
				// Consume input so SimulateTick doesn't also try to start an attack
				var consumedInput = input;
				consumedInput.ActiveSlot = 0;
				inputs[id] = consumedInput;
			}
		}

		private void SimulateMovement(Dictionary<ulong, InputState> inputs)
		{
			// ── Step 1: Simulate each entity ──
			// Snapshot keys to avoid collection-modified when writing _states[id] = state
			ulong[] simIds = new ulong[_states.Count];
			_states.Keys.CopyTo(simIds, 0);
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				var def = _defs[id];
				var input = inputs.TryGetValue(id, out var i2) ? i2 : default;
				Simulation.SimulateTick(ref state, def, input, _arena);
				_states[id] = state;
			}

			// ── Step 1b: Tick server-side abilities (overrides movement, spawns hitboxes) ──
			TickAbilities(inputs);
		}

		/// <summary>
		/// Find the closest enemy entity ID for target lock.
		/// Scans all registered entities, skipping self.
		/// </summary>
		private ulong FindClosestEnemy(ulong selfId, float selfX, float selfZ, float maxRange, out float outDist)
		{
			ulong closest = 0;
			float best = maxRange * maxRange;
			foreach (var kvp in _states)
			{
				if (kvp.Key == selfId) continue;
				float dx = kvp.Value.PX - selfX;
				float dz = kvp.Value.PZ - selfZ;
				float distSq = dx * dx + dz * dz;
				if (distSq < best) { best = distSq; closest = kvp.Key; }
			}
			outDist = MathF.Sqrt(best);
			return closest;
		}

		/// <summary>
		/// Compute soft-lock target for every entity each tick.
		/// Prefers client-provided target (from screen-center) when input.TargetEntityId > 0,
		/// otherwise brute-force scans for nearest enemy within 20m.
		/// Stores the result in state.TargetEntityId for abilities, camera, and indicator to query.
		///
		/// When the entity is attacking with UseTargetLock=true, also processes warp
		/// (auto-dash toward target) and rotation (face toward target).
		/// </summary>
		private void ProcessTargetLock(Dictionary<ulong, InputState> inputs)
		{
			// Snapshot keys to avoid InvalidOperationException when writing _states[id] = state
			ulong[] ids = new ulong[_states.Count];
			_states.Keys.CopyTo(ids, 0);
			foreach (var id in ids)
			{
				if (!_states.TryGetValue(id, out var state)) continue;

				// ── Find target ──
				// Check if client provided a target (screen-center override)
				ulong targetId = 0;
				if (inputs.TryGetValue(id, out var input) && input.TargetEntityId > 0)
				{
					ulong candidateId = input.TargetEntityId;
					if (_states.ContainsKey(candidateId))
						targetId = candidateId;
				}

				// Fall back to nearest scan if no client target
				if (targetId == 0)
				{
					float searchRange = 20f;
					targetId = FindClosestEnemy(id, state.PX, state.PZ, searchRange, out _);
				}

				state.TargetEntityId = targetId;
				if (targetId == 0) { _states[id] = state; continue; }

				// ── Attacking-only behaviors (warp, rotation) ──
				if (state.State != ActionState.Attacking || state.AttackSlot == 0)
				{
					_states[id] = state;
					continue;
				}

				var def = _defs[id];
				bool airborne = !state.IsGrounded;
				var spec = def.GetSlotAbility(state.AttackSlot - 1, airborne);
				if (spec == null) { _states[id] = state; continue; }

				if (spec.Stages == null || spec.Stages.Length == 0) { _states[id] = state; continue; }
				var stage = Simulation.ResolveStage(spec, state);

				// Only process warp/rotation if target lock is enabled for this stage
				if (!stage.UseTargetLock) { _states[id] = state; continue; }

				var target = _states[targetId];
				float dx = target.PX - state.PX;
				float dz = target.PZ - state.PZ;
				float dist = MathF.Sqrt(dx * dx + dz * dz);

				// ── Warp toward target if within WarpRange but outside AttackRange ──
				if (stage.WarpRange > 0f && dist > stage.AttackRange && dist <= stage.WarpRange)
				{
					state.WarpTargetX = target.PX;
					state.WarpTargetZ = target.PZ;
					state.WarpAttackRange = stage.AttackRange;
					state.WarpSpeed = 0.3f; // 30% of remaining distance per tick
				}

				// ── Rotate toward target each tick ──
				float rotRange = stage.WarpRange > 0f ? stage.WarpRange : stage.AttackRange;
				if (stage.RotateTowardTarget && stage.TrackingStrength > 0f && dist <= rotRange)
				{
					if (dx * dx + dz * dz > 0.001f)
					{
						float targetYaw = MathF.Atan2(dx, dz);
						float diff = targetYaw - state.FacingYaw;
						while (diff > MathF.PI) diff -= 2f * MathF.PI;
						while (diff < -MathF.PI) diff += 2f * MathF.PI;
						state.FacingYaw += diff * stage.TrackingStrength * Simulation.TickDt;
					}
				}

				_states[id] = state;
			}
		}

		private List<SpellResolver.EntityData> BuildHurtboxList()
		{
			// ── Step 2: Build entity list for hit detection ──
			var entityList = new List<SpellResolver.EntityData>();
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = _defs[id];

				if (ResolveBoneAnimFrame(id, state, def, out var baked, out var targetAnim, out var bakedFrame))
				{

						float px = state.PX, py = state.PY, pz = state.PZ;
						float yaw = state.FacingYaw;
						float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);

						for (int bi = 0; bi < def.HurtboxBoneDefs.Length; bi++)
						{
							var hbd = def.HurtboxBoneDefs[bi];
							if (!baked.GetBonePosition(targetAnim, bakedFrame, bi, out float bx, out float by, out float bz)) continue;
							float scale = def.HurtboxBoneScale;
							bx *= scale; by *= scale; bz *= scale;
							float wx = px + ((bx * cos) + (bz * sin));
							float wy = def.BoneYToWorldY(py, by);
							float wz = pz + ((-bx * sin) + (bz * cos));
							wx += hbd.OffX; wy += hbd.OffY; wz += hbd.OffZ;
							entityList.Add(new SpellResolver.EntityData
							{
								Id = id, PosX = wx, PosY = wy, PosZ = wz,
								Radius = hbd.Radius, Shape = HitboxShape.Sphere,
								EndX = wx, EndY = wy, EndZ = wz, Active = true,
							});
						}
				}
				else if (def.HurtboxCapsules != null)
				{
					float cos = MathF.Cos(state.FacingYaw);
					float sin = MathF.Sin(state.FacingYaw);
					foreach (var cap in def.HurtboxCapsules)
					{
						float sx = state.PX + (cap.Sx * cos) + (cap.Sz * sin);
						float sy = state.PY + cap.Sy;
						float sz = state.PZ + ((-cap.Sx * sin) + (cap.Sz * cos));
						float ex = state.PX + (cap.Ex * cos) + (cap.Ez * sin);
						float ey = state.PY + cap.Ey;
						float ez = state.PZ + ((-cap.Ex * sin) + (cap.Ez * cos));
						entityList.Add(new SpellResolver.EntityData
						{
							Id = id, PosX = sx, PosY = sy, PosZ = sz, Radius = cap.Radius,
							Shape = (sx != ex || sy != ey || sz != ez) ? HitboxShape.Capsule : HitboxShape.Sphere,
							EndX = ex, EndY = ey, EndZ = ez, Active = true,
						});
					}
				}
			}
			_lastEntityList = entityList;
			return entityList;
		}

		private void SpawnHitboxEvents()
		{
			// ── Spawn hitbox events for attacking entities ──
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = _defs[id];
				if (state.State != ActionState.Attacking || state.AttackSlot == 0 || state.IsServerAbility) continue;

				bool airborne = !state.IsGrounded;
				var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
				if (ability == null) continue;
				var stage = Simulation.ResolveStage(ability, state);
				if (stage.HitboxEvents == null) continue;

				float cos = MathF.Cos(state.FacingYaw);
				float sin = MathF.Sin(state.FacingYaw);
				foreach (var evt in stage.HitboxEvents)
				{
					if (state.AttackElapsedTicks != evt.TriggerTick) continue;
					// ── Bone-attached hitbox path ──
					if (evt.BoneName != null)
					{
						if (!_bakedData.TryGetValue(id, out var baked) || def.HurtboxBoneDefs == null)
						{
							Simulation.OnDebugLog?.Invoke($"[SpawnHitboxEvents] Bone hitbox for '{evt.BoneName}' skipped — no baked data for entity {id}");
							continue;
						}

						// Find bone def index by name
						int bi = -1;
						for (int i = 0; i < def.HurtboxBoneDefs.Length; i++)
						{
							if (def.HurtboxBoneDefs[i].BoneName == evt.BoneName) { bi = i; break; }
						}
						if (bi < 0)
						{
							Simulation.OnDebugLog?.Invoke($"[SpawnHitboxEvents] Bone '{evt.BoneName}' not found in HurtboxBoneDefs for entity {id}");
							continue;
						}

						// Resolve animation name (same mapping as BuildHurtboxList — read-only, no frame advance)
						string targetAnim;
						if (state.State == ActionState.Dashing) targetAnim = "dash";
						else if (state.State == ActionState.Attacking && state.AttackSlot > 0)
						{
							bool airborneBone = !state.IsGrounded;
							var boneAbility = def.GetSlotAbility(state.AttackSlot - 1, airborneBone);
							int boneStageIdx = Math.Min(state.ComboStage, (byte)(boneAbility.Stages.Length - 1));
							targetAnim = (boneStageIdx >= 0 && boneStageIdx < boneAbility.AnimationNames.Length) ? boneAbility.AnimationNames[boneStageIdx] : "melee";
						}
						else if (state.State == ActionState.Hitstun) targetAnim = state.HitstunLevel switch
						{
						    1 => def.HitMediumAnim,
						    2 => def.HitHardAnim,
						    _ => def.HitSmallAnim,
						};
						else if (!state.IsGrounded) targetAnim = state.VY > 0 ? "jump" : "fall";
						else if ((state.VX * state.VX) + (state.VZ * state.VZ) > 1f) targetAnim = "run";
						else targetAnim = "idle";

						int animIdx = baked.FindAnimIndex(targetAnim);
						if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
						if (animIdx < 0) continue;

						// Compute baked frame from attack elapsed ticks (no anim frame side effects)
						int fc = baked.Animations[animIdx].FrameCount;
						int bakedFrame = 0;
						if (state.State == ActionState.Attacking && state.AttackSlot > 0)
						{
							bool airborneBone2 = !state.IsGrounded;
							var boneAbility2 = def.GetSlotAbility(state.AttackSlot - 1, airborneBone2);
							int boneStageIdx2 = Math.Min(state.ComboStage, (byte)(boneAbility2.Stages.Length - 1));
							if (boneStageIdx2 >= 0 && boneStageIdx2 < boneAbility2.Stages.Length)
							{
								int durationTicks = boneAbility2.Stages[boneStageIdx2].DurationTicks;
								if (durationTicks > 0) bakedFrame = Math.Min(state.AttackElapsedTicks * fc / durationTicks, fc - 1);
							}
						}
						if (bakedFrame >= fc) bakedFrame = fc - 1;

						// Resolve bone position in world space
						if (!baked.GetBonePosition(targetAnim, bakedFrame, bi, out float bx, out float by, out float bz)) continue;
						float boneScale = def.HurtboxBoneScale;
						bx *= boneScale; by *= boneScale; bz *= boneScale;
						float wx = state.PX + ((bx * cos) + (bz * sin));
						float wy = def.BoneYToWorldY(state.PY, by);
						float wz = state.PZ + ((-bx * sin) + (bz * cos));
						wx += (evt.BoneOffX * cos) + (evt.BoneOffZ * sin); wy += evt.BoneOffY; wz += (-evt.BoneOffX * sin) + (evt.BoneOffZ * cos);

						float boneDamage = evt.Damage;
						float boneRadius = evt.Radius;
						ServerAbility.ApplyBuffBonuses(ref state, ref boneDamage, ref boneRadius);

						_spellResolver.Spawn(new Hitbox
						{
							X = wx, Y = wy, Z = wz, Radius = boneRadius, Shape = evt.Shape,
							EndX = wx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin)),
							EndY = wy + evt.EndOffY,
							EndZ = wz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos)),
							Damage = boneDamage,
							BaseKnockback = evt.BaseKnockback, KnockbackGrowth = evt.KnockbackGrowth, KnockbackUpward = evt.KnockbackUpward,
							StunTicks = evt.StunTicks, DurationTicks = evt.DurationTicks, OwnerId = id,
						});

						continue; // Skip ability.SpawnHitbox and default melee path for this evt
					}

					// Let the AbilitySpec decide: true = handled, false = use default melee
					if (!ability.SpawnHitbox(evt, state, def, _spellResolver, id))
					{
						// ── Default melee/static hitbox ──
						float hx = state.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
						float hy = state.PY + evt.OffY;
						float hz = state.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));
						float hex = hx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
						float hey = hy + evt.EndOffY;
						float hez = hz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));
                        float damage = evt.Damage;
                        float radius = evt.Radius;
                        ServerAbility.ApplyBuffBonuses(ref state, ref damage, ref radius);
                        _spellResolver.Spawn(new Hitbox
                        {
                            X = hx, Y = hy, Z = hz, Radius = radius, Shape = evt.Shape,
                            EndX = hex, EndY = hey, EndZ = hez, Damage = damage,
                            BaseKnockback = evt.BaseKnockback, KnockbackGrowth = evt.KnockbackGrowth, KnockbackUpward = evt.KnockbackUpward,
                            StunTicks = evt.StunTicks, DurationTicks = evt.DurationTicks, OwnerId = id,
                        });
					}
				}
			}
		}

		private void ResolveHits(List<SpellResolver.EntityData> entityList)
		{
			// ── Step 3: Resolve hitboxes ──
			var hits = _spellResolver.Tick(entityList);
			LastTickHits.Clear();
			LastTickHits.AddRange(hits);
			foreach (var hit in hits)
			{
				if (!_states.TryGetValue(hit.TargetEntityId, out var targetState)) continue;

				// Knockback direction: from attacker to target (not hitbox to target).
				// The hitbox offset can place it past the target, inverting the direction.
				// Smash convention: always push away from the attacker.
				float dirX = hit.DirX;
				float dirZ = hit.DirZ;
				if (_states.TryGetValue(hit.OwnerEntityId, out var attackerState))
				{
					float aDx = targetState.PX - attackerState.PX;
					float aDz = targetState.PZ - attackerState.PZ;
					float aDist = MathF.Sqrt(aDx * aDx + aDz * aDz);
					if (aDist > 0.001f)
					{
						dirX = aDx / aDist;
						dirZ = aDz / aDist;
					}
				}

				float finalDamage = hit.Damage;
				targetState.DamagePercent += (ushort)finalDamage;
				if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;
				// Resolve hitstun animation tier from damage
				targetState.HitstunLevel = finalDamage < 5f ? (byte)0 :
				    finalDamage < 15f ? (byte)1 : (byte)2;
				Simulation.ApplyKnockback(ref targetState, dirX, dirZ,
				    hit.KnockbackY, hit.BaseKnockback, hit.KnockbackGrowth, hit.StunTicks);
				targetState.HitstunTicks = hit.StunTicks;

				// Let the attacker's active ability apply hit effects (e.g., FightGuy R mark consumption)
				if (_activeAbilities.TryGetValue(hit.OwnerEntityId, out var attackerAbility)
				    && _defs.TryGetValue(hit.OwnerEntityId, out var attackerDef))
				{
					float kbForce = hit.BaseKnockback + hit.KnockbackGrowth * (targetState.DamagePercent * 0.01f);
					attackerAbility.OnHitEntity(ref attackerState, ref targetState, attackerDef, ref finalDamage, ref kbForce);
					_states[hit.OwnerEntityId] = attackerState;
				}

				_states[hit.TargetEntityId] = targetState;
			}
		}

		private void ProcessProjectileExplosions()
		{
			// ── Step 3b: Projectile explosions (entity hit + ground impact) ──
            // Ground collision for remaining active projectiles (samples heightmap per projectile)
            _spellResolver.CheckGroundCollision(_arena);

			// Spawn explosion hitboxes for all deactivated projectiles this tick
            // NOTE: Explosion damage/radius from projectiles (bazooka/roundbomb ground impact)
            // does NOT get Overclock buff bonuses. Explosions are secondary effects detached
            // from the owner's state — the ProjectileExplosion config is baked at spawn time.
            // Direct projectile hits DO get the bonus (applied in MankiBazooka/MankiRoundBomb
            // before Resolver.Spawn). If explosion buffs are desired, propagate owner buff
            // flags alongside projectile data and check at explosion time.
			foreach (var (ex, ey, ez, explosion, ownerId) in _spellResolver.DrainPendingExplosions())
			{
				_spellResolver.Spawn(new Hitbox
				{
					X = ex, Y = ey, Z = ez,
					Radius = explosion.Radius, Shape = HitboxShape.Sphere,
					EndX = ex, EndY = ey, EndZ = ez,
					Damage = explosion.Damage,
					BaseKnockback = explosion.BaseKnockback,
					KnockbackGrowth = explosion.KnockbackGrowth,
					KnockbackUpward = explosion.KnockbackUpward,
					StunTicks = explosion.StunTicks,
					DurationTicks = explosion.DurationTicks,
					OwnerId = ownerId,
					CanHitOwner = explosion.CanHitOwner,
				});
			}
		}

		private void CheckVoidDeaths()
		{
			// ── Step 4: Void death check ──
			var deadIds = new List<ulong>();
			foreach (var kvp in _states)
				if (kvp.Value.PY < _arena.KillHeight) deadIds.Add(kvp.Key);
			foreach (var id in deadIds)
			{
				var d = _defs[id];
				var oldState = _states[id];
				var (rpx, rpy, rpz) = _respawnPositions.TryGetValue(id, out var rp) ? rp :
					(_arena.SpawnPoints[0].X, _arena.SpawnPoints[0].Y, _arena.SpawnPoints[0].Z);
				_states[id] = new CharacterState
				{
					PX = rpx, PY = rpy, PZ = rpz,
					FacingYaw = _arena.SpawnPoints[0].Yaw,
                    EntityId = id,
					JumpsLeft = d.Movement.MaxJumps, AirDodgesLeft = 1,
					Deaths = (byte)(oldState.Deaths + 1), DamagePercent = 0,
				};
			}
		}

		public void Tick(Dictionary<ulong, InputState> inputs)
		{
			PreTickAbilities(inputs);

			ProcessTargetLock(inputs);

			SimulateMovement(inputs);

			var entityList = BuildHurtboxList();
			SpawnHitboxEvents();

			ResolveHits(entityList);

			ProcessProjectileExplosions();

			CheckVoidDeaths();
		}
	}
}
