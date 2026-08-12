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
		private readonly Dictionary<ulong, (float x, float y, float z, float yaw)> _respawnPositions = new();
		// Track pending attack slots for warp-in-progress entities
		private readonly Dictionary<ulong, byte> _pendingWarpAttacks = new();
		// ── Ability pool ──
		private readonly Dictionary<ulong, ServerAbility> _activeAbilities = new();
		private readonly IMatchRule _rule;
		/// <summary>Ticks of invincibility granted on respawn (60 = 1s at 60Hz). Issue #37.</summary>
		public ushort RespawnInvincibilityTicks { get; set; } = 60;

		/// <param name="rule">Win-condition rule (elimination + match end). Defaults to stock mode, 3 stocks.</param>
		public ServerSimulation(ArenaDefinition arena, IMatchRule? rule = null)
		{
			_arena = arena;
			_rule = rule ?? new StockMatchRule(3);
		}
		private const float WarpConeHalfAngleRad = 120f * MathF.PI / 180f / 2f; // 60° half-cone = 120° total facing cone

		// ── Hitstop tuning (ADR-0012). Game-wide defaults; per-ability overrides via
		// AbilitySpec.Params keys below. Tune from playtest. ──
		private const float HitstopBaseTicks = 1f;
		private const float HitstopPerDamageTicks = 1.5f;
		private const float HitstopMaxTicks = 12f;
		private const float HitstopLowDamageThreshold = 3f;
		private const float HitstopLowDamageMult = 2f;
		private const float HitstopMultihitMult = 0.5f;

		/// <summary>Freeze ticks for a connecting hit (ADR-0012): 2 + 2·damage, cap 24;
		/// damage under 3 ×2; hits beyond the first within an ability ×0.5 (applied after the cap,
		/// floored at 1). Per-ability overrides via spec.Params keys:
		/// hitstop_base_ticks, hitstop_per_damage_ticks, hitstop_cap_ticks,
		/// hitstop_low_damage_threshold, hitstop_low_damage_multiplier, hitstop_multihit_multiplier.
		/// Pass the ATTACKER's ability spec (the ability that lands the hit); null = defaults.</summary>
		public static ushort ComputeHitstopTicks(float damage, bool beyondFirst, AbilitySpec? spec)
		{
			float baseT = HitstopParam(spec, "hitstop_base_ticks", HitstopBaseTicks);
			float perDmg = HitstopParam(spec, "hitstop_per_damage_ticks", HitstopPerDamageTicks);
			float cap = HitstopParam(spec, "hitstop_cap_ticks", HitstopMaxTicks);
			float lowThresh = HitstopParam(spec, "hitstop_low_damage_threshold", HitstopLowDamageThreshold);
			float lowMult = HitstopParam(spec, "hitstop_low_damage_multiplier", HitstopLowDamageMult);
			float multihitMult = HitstopParam(spec, "hitstop_multihit_multiplier", HitstopMultihitMult);

			float raw = baseT + perDmg * damage;
			if (damage < lowThresh) raw *= lowMult;
			raw = Math.Min(raw, cap);
			if (beyondFirst) raw *= multihitMult;
			return (ushort)Math.Max(1f, raw);
		}

		private static float HitstopParam(AbilitySpec? spec, string key, float fallback)
			=> (spec?.Params != null && spec.Params.TryGetValue(key, out float v)) ? v : fallback;

		public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
		{
			_defs[id] = def;
			initialState.EntityId = id;
			_states[id] = initialState;
			if (baked != null) _bakedData[id] = baked;
			_animFrames[id] = 0;
			_prevAnimIndex[id] = -1;
	}

		public void SetRespawnPosition(ulong entityId, float x, float y, float z, float yaw = 0f)
		{
			_respawnPositions[entityId] = (x, y, z, yaw);
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
			ability.Arena = _arena;
			ability.Slot = slot;
			ability.OnStart(ref state, def);
			state.AnimIndex = ability.AnimIndex;
			state.AttackSlot = (byte)(slot + 1);
            // ADR-0015 / issue #115: momentum-preserve removed the blanket AirTime reset
            // and VY-cancel for every aerial ability. The FloatWindow now resets ONLY for
            // recovery-designated moves (AbilitySpec.IsRecoveryMove) — the Smash up-B analog.
            var spec = def.GetSlotAbility(slot, !state.IsGrounded);
            if (spec != null && spec.IsRecoveryMove)
                state.AirTimeTicks = 0;
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

				// Interrupt: if state left Attacking or Aiming (dash, idle, or other), deactivate without OnEnd.
				if (state.State != ActionState.Attacking && state.State != ActionState.Aiming)
				{
					ended.Add(id);
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[AbilityInterrupt] entity={id} slot={ability.Slot} state={state.State} — deactivated");
					continue;
				}

				var input = inputs.TryGetValue(id, out var i) ? i : default;

				// Hitstop pauses the attacker's ability (ADR-0012): timers pause, so recovery
				// extends symmetrically with the victim's lock. Do NOT interrupt — the ability
				// resumes when the freeze expires.
				if (state.HitstopTicks > 0)
				{
					_states[id] = state;
					continue;
				}

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
					// StartDash already cleared AttackSlot/AnimLockTicks, so
					// the clean-up below (cooldown, buffered slot, AnimLockTicks) is still correct.
					
					// Apply cooldown (all 11 slots — issue #117; the old < 6 gate skipped
					// slots 6-10 entirely, so Ki Shot on the Q slot would never cooldown)
					if (ability.Slot < AbilitySlots.Count)
					{
						state.SetCooldown((byte)(ability.Slot + 1), ability.Cooldown);
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

					_states[id] = state; // Persist cooldown + buffered slot clear
				}
				_activeAbilities.Remove(id);
			}
		}

		public static List<SpellResolver.EntityData> BuildEntitiesFromState(
			CharacterState state, CharacterDefinition def, BakedAnimationData baked,
			string targetAnim, int animFrame, ulong entityId = 0)
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
						// Per-def offset (Ability Lab authored, spec #119): applied in
						// sim-meter space, rotated by facing — matches the hitbox
						// BoneOff* convention. All shipped defs use zero offsets, so
						// this is behavior-preserving for existing characters.
						wx += (hbd.OffX * cos) + (hbd.OffZ * sin);
						wy += hbd.OffY;
						wz += (-hbd.OffX * sin) + (hbd.OffZ * cos);
						list.Add(new SpellResolver.EntityData
						{
							Id = entityId, PosX = wx, PosY = wy, PosZ = wz,
							Radius = hbd.Radius, Shape = HitboxShape.Sphere,
							EndX = wx, EndY = wy, EndZ = wz, Active = true,
						});
					}
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
					list.Add(new SpellResolver.EntityData
					{
						Id = entityId, PosX = sx, PosY = sy, PosZ = sz, Radius = cap.Radius,
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
            else if ((state.State is ActionState.Attacking or ActionState.Aiming) && state.AttackSlot > 0)
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
            if ((state.State is ActionState.Attacking or ActionState.Aiming) && state.AttackSlot > 0)
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

		/// <summary>
		/// IASA early-out (issue #124): true when the current attack's stage has passed its
		/// IasaTicks. From that tick on, ability inputs interrupt the recovery. 0 = none
		/// (full ADR-0014 lock — the pre-IASA behavior). Never true outside Attacking.
		/// </summary>
		private static bool IsIasaUnlocked(CharacterState state, CharacterDefinition def)
		{
			if (state.State != ActionState.Attacking || state.AttackSlot == 0) return false;
			var spec = def.GetSlotAbility(state.AttackSlot - 1, !state.IsGrounded);
			if (spec?.Stages is not { Length: > 0 }) return false;
			var stage = Simulation.ResolveStage(spec, state);
			if (stage.IasaTicks == 0) return false;

			return ElapsedInStage(state, spec) >= stage.IasaTicks;
		}

		/// <summary>
		/// Current stage's elapsed ticks for an attacking entity. AttackElapsedTicks counts
		/// ticks since the last stage reset; stage-driven moves (StageChainAbility) never
		/// reset it mid-attack, so subtracting prior stages' durations yields the current
		/// stage's elapsed. Charge abilities reset it at their mid-attack stage transition
		/// (ChargeAttackAbility/AimHoldAbility), which underflows the subtraction — fall back
		/// to the raw clock (elapsed since the transition). Shared by the IASA check
		/// (issue #124) and the landing-lag auto-cancel windows (issue #125).
		/// </summary>
		private static int ElapsedInStage(CharacterState state, AbilitySpec? spec)
		{
			if (spec?.Stages is not { Length: > 0 }) return 0;
			int stageIdx = Math.Min(state.ComboStage, spec.Stages.Length - 1);
			int elapsed = state.AttackElapsedTicks;
			for (int i = 0; i < stageIdx; i++)
				elapsed -= spec.Stages[i].DurationTicks;
			return elapsed < 0 ? state.AttackElapsedTicks : elapsed;
		}

		/// <summary>
		/// Landing lag (issue #125): when the character lands this tick while an air-started
		/// ability whose stage declares <c>LandingLagTicks</c> is still active, apply the
		/// lock — no input, no movement — unless the landing frame fell in an auto-cancel
		/// window (stage-elapsed <c>&lt;= AutoCancelBeforeTicks</c> or <c>&gt;=
		/// AutoCancelAfterTicks</c>), in which case the aerial ENDS on the landing frame and
		/// the player acts immediately (Melee's AC: no landing commitment at all). All-zero
		/// fields = current behavior, landing never locks.
		///
		/// Detection: airborne at tick start + grounded after SimulateTick = a landing.
		/// A ledge snap also flips IsGrounded but boosts VY (LedgeSnapUpwardBoost) — that is
		/// not a landing, so the <c>VY &lt;= 0</c> guard excludes it. The spec is resolved
		/// with <c>airborne: true</c>: the only air-started ability classes in the game
		/// (AirLmbCombo, AirChargeAttack) resolve their own stages from the airborne variant,
		/// so this reads the exact stages the running ability uses. Ground-started abilities
		/// that land mid-stage (KistuRisingSlash) resolve the slot's AIR spec here — they are
		/// unaffected while no air stage declares LandingLagTicks; a designer giving e.g.
		/// AirRMB (Collapse) a lag must know the declaration governs that slot's landings.
		///
		/// Known 1-frame limitation: the input gates (burst especially) run inside
		/// SimulateTick BEFORE this applies the lock, so a press on the landing frame itself
		/// is processed pre-lock — an offensive burst on the landing frame still cancels the
		/// aerial (pre-issue behavior). Every later locked tick is fully gated.
		/// </summary>
		private static void ApplyLandingLag(ref CharacterState state, CharacterDefinition def, bool wasGrounded)
		{
			if (wasGrounded || !state.IsGrounded) return; // no landing this tick
			if (state.VY > 0f) return;                    // ledge snap boost, not a landing
			if (state.State != ActionState.Attacking && state.State != ActionState.Aiming) return;
			if (state.AttackSlot == 0 || state.LandingLagTicks > 0) return;

			var spec = def.GetSlotAbility(state.AttackSlot - 1, airborne: true);
			if (spec?.Stages is not { Length: > 0 }) return;
			var stage = Simulation.ResolveStage(spec, state);
			if (stage.LandingLagTicks == 0) return;

			int elapsed = ElapsedInStage(state, spec);
			bool autoCancel = (stage.AutoCancelBeforeTicks > 0 && elapsed <= stage.AutoCancelBeforeTicks)
				|| (stage.AutoCancelAfterTicks > 0 && elapsed >= stage.AutoCancelAfterTicks);
			if (autoCancel)
			{
				// Auto-cancel landing: the move ends here — the player acts immediately
				// instead of riding out the move's ground recovery. Same interrupt semantics
				// as dash/IASA cancels: dropped without OnEnd, cooldown still applies
				// (TickAbilities' cleanup), stale buffer cleared.
				state.State = ActionState.Idle;
				state.AttackSlot = 0;
				state.ComboStage = 0;
				state.AttackElapsedTicks = 0;
				state.AnimLockTicks = 0;
				state.BufferedSlot = 0;
				return;
			}

			state.LandingLagTicks = stage.LandingLagTicks;
			// The lag is a hard no-input window: a press buffered mid-air must not fire
			// through it (Simulation also refuses to buffer new presses while it is live).
			state.BufferedSlot = 0;
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

				var def = _defs[id];
				// IASA early-out (issue #124): an attack stage that has passed its IasaTicks
				// releases the anim lock for ability inputs — the press interrupts the recovery.
				// IasaTicks = 0 (default) keeps the full ADR-0014 lock. Only the AnimLockTicks
				// term relaxes: hitstun, hitstop, burst recovery and landing lag (issue #125)
				// always block — attacker hitstop (FreezesOwner) keeps State == Attacking, so
				// relaxing those too would let a press cancel the attack mid-freeze (ADR-0012),
				// and landing lag is a hard no-input lock that IASA must not bypass.
				bool iasaUnlocked = IsIasaUnlocked(state, def);
				if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BurstRecoveryTicks > 0
					|| state.LandingLagTicks > 0
					|| (state.AnimLockTicks > 0 && !iasaUnlocked)) continue; // ADR-0014
				if (state.State != ActionState.Idle && state.State != ActionState.Attacking) continue;

				bool airborne = !state.IsGrounded;
				var spec = def.GetSlotAbility(input.ActiveSlot - 1, airborne);

				// Issue #117: no spec for this (slot, state) — grounded-only move pressed in
				// the air, or a data-less slot. Reject and consume (nothing to buffer); the
				// old code NRE'd on spec.Stages below for data-less slots 6-10.
				if (spec == null)
				{
					var rejected = input;
					rejected.ActiveSlot = 0;
					inputs[id] = rejected;
					continue;
				}

				ushort cooldown = state.GetCooldown(input.ActiveSlot);
				if (cooldown > 0)
				{
					if (Simulation.OnDebugLog != null)
						Simulation.OnDebugLog.Invoke(
							$"[Cooldown] BLOCKED slot={input.ActiveSlot} cooldown={cooldown} entity={id}");
					continue;
				}

				// ── Warp check: sprint to target if between WarpRange and AttackRange ──
				if (state.State == ActionState.Idle && spec.Stages != null && spec.Stages.Length > 0)
				{
					var firstStage = spec.Stages[0];
					if (firstStage.WarpRange > 0f)
					{
						ulong targetId = FindClosestEnemy(id, state.PX, state.PZ, firstStage.WarpRange, out _);
						if (targetId > 0)
						{
							var target = _states[targetId];
							float dx = target.PX - state.PX;
							float dz = target.PZ - state.PZ;
							float dist = MathF.Sqrt(dx * dx + dz * dz);
							if (dist > firstStage.AttackRange && dist <= firstStage.WarpRange)
							{
								// ── Facing cone check: only warp to enemies roughly in front ──
								float angleToEnemy = MathF.Atan2(dx, dz);
								float angleDiff = angleToEnemy - state.FacingYaw;
								while (angleDiff > MathF.PI) angleDiff -= 2f * MathF.PI;
								while (angleDiff < -MathF.PI) angleDiff += 2f * MathF.PI;
								if (MathF.Abs(angleDiff) > WarpConeHalfAngleRad)
								{
									if (Simulation.OnDebugLog != null)
										Simulation.OnDebugLog.Invoke(
											$"[WarpCone] SKIP entity={id} target={targetId} angleDiff={angleDiff:F3} rad (outside {WarpConeHalfAngleRad:F3} half-cone)");
									goto tryDirectAttack; // skip warp, fall through to normal attack activation
								}

								state.WarpTargetX = target.PX;
								state.WarpTargetZ = target.PZ;
								state.WarpAttackRange = firstStage.AttackRange;
								state.WarpSpeed = 1f;
								state.State = ActionState.Warping;
								_pendingWarpAttacks[id] = input.ActiveSlot;

								// Consume input (prevent SimulateTick from also starting attack)
								var ci = input;
								ci.ActiveSlot = 0;
								inputs[id] = ci;
								_states[id] = state;
								continue;
							}
						}
					}
				}

				tryDirectAttack:

                // Reject F (Overclock) reactivation while buff already active
                if (input.ActiveSlot == AbilitySlots.F && (state.BuffActiveFlags & (byte)SlopArena.Shared.BuffType.Overclock) != 0)
                    continue;

				// ── Charge-stock gate: abilities that declare a "max_charges" param are
				// limited by a refundable charge pool (Kistu Rising Slash) rather than a flat
				// cooldown. Blocked when the pool is exhausted. ──
				int maxCharges = (spec.Params != null && spec.Params.TryGetValue("max_charges", out var mc)) ? (int)mc : 0;
				if (maxCharges > 0 && state.ChargeStockSpent >= maxCharges)
				{
					// Consume the input so SimulateTick doesn't start a data-driven attack.
					var blockedInput = input;
					blockedInput.ActiveSlot = 0;
					inputs[id] = blockedInput;
					continue;
				}

				var ability = SlopArena.Shared.Abilities.AbilityFactory.CreateServer(def.Class, (byte)(input.ActiveSlot - 1), airborne);
				if (ability == null) continue; // unsupported character or slot

				// ── IASA interrupt ──
				// An active ability whose stage has passed its IasaTicks is dropped without
				// OnEnd (same semantics as hitstun/dash interrupts) so the new ability takes
				// over. Placed AFTER the activation gates (cooldown, charge stock, factory
				// support) so a blocked press never cancels the current attack. The move was
				// used — its cooldown still applies, mirroring the dash-cancel path in
				// TickAbilities. Attack state is cleared so the new ability's OnStart begins
				// clean and no stale buffered press double-fires when the new attack's lock
				// expires.
				if (_activeAbilities.TryGetValue(id, out var currentAbility))
				{
					if (!iasaUnlocked) continue;

					_activeAbilities.Remove(id);
					if (currentAbility.Slot < AbilitySlots.Count)
						state.SetCooldown((byte)(currentAbility.Slot + 1), currentAbility.Cooldown);
					state.AttackSlot = 0;
					state.ComboStage = 0;
					state.AttackElapsedTicks = 0;
					state.AnimLockTicks = 0;
					state.BufferedSlot = 0;
					_states[id] = state;
				}

				SlopArena.Shared.Abilities.AbilityFactory.InitFromSpec(ability, spec, (byte)(input.ActiveSlot - 1));
				ActivateAbility(id, ability, (byte)(input.ActiveSlot - 1), def);

				// Spend a charge from the pool (refunded on hit by the ability's OnHitEntity).
				if (maxCharges > 0 && _states.TryGetValue(id, out var afterState))
				{
					afterState.ChargeStockSpent++;
					ushort regenPeriod = (ushort)(spec.Params.TryGetValue("charge_regen_ticks", out var rg) ? rg : 180f);
					afterState.ChargeStockRegenPeriod = regenPeriod;
					if (afterState.ChargeStockRegenTicks == 0)
						afterState.ChargeStockRegenTicks = regenPeriod;
					_states[id] = afterState;
				}

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
				// Eliminated (0 stocks / rule) — frozen spectator, no physics (issue #37).
				if (_rule.IsEliminated(state)) continue;
				if (!_defs.TryGetValue(id, out var def)) continue; // state exists but no definition — invalid entity, skip (never simulate)
				var input = inputs.TryGetValue(id, out var i2) ? i2 : default;
				bool wasGrounded = state.IsGrounded;
				Simulation.SimulateTick(ref state, def, input, _arena);
				// Landing lag (issue #125): land mid-aerial → lock, unless the landing frame
				// falls in an auto-cancel window. Server-only — the lock is authority-enforced.
				ApplyLandingLag(ref state, def, wasGrounded);
				_states[id] = state;
			}

			// ── Step 1a: Burst side effects (ADR-0014): server-only — shove (defensive)
			// / hitbox (offensive). The user's own state changes already happened inside
			// SimulateTick. ──
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var burstState) || burstState.BurstPending == 0) continue;
				if (burstState.BurstPending == 1)
				{
					ulong attackerId = burstState.LastAttackerEntityId;
					if (attackerId != 0 && attackerId != id
					    && _states.TryGetValue(attackerId, out var attackerState))
					{
						float dx = attackerState.PX - burstState.PX;
						float dz = attackerState.PZ - burstState.PZ;
						float dist = MathF.Sqrt(dx * dx + dz * dz);
						if (dist > 0.001f) { dx /= dist; dz /= dist; }
						else { dx = MathF.Sin(burstState.FacingYaw); dz = MathF.Cos(burstState.FacingYaw); }
						// Small fixed shove, stun 0 → no hitstun, no punish. Interrupts a mid-attack
						// attacker via ApplyKnockback's State=Idle → TickAbilities drops their ability
						// (breaks the string — the point of the escape); they are free to act at once.
						Simulation.ApplyKnockback(ref attackerState, dx, dz,
							BurstConfig.AttackerPushAngle, BurstConfig.AttackerPushBaseKnockback, 0f, 0);
						_states[attackerId] = attackerState;
					}
					burstState.LastAttackerEntityId = 0;
				}
				else if (burstState.BurstPending == 2)
				{
					float cos = MathF.Cos(burstState.FacingYaw), sin = MathF.Sin(burstState.FacingYaw);
					float hx = burstState.PX + sin * BurstConfig.HitboxForwardOffset;
					float hy = burstState.PY + BurstConfig.HitboxHeightOffset;
					float hz = burstState.PZ + cos * BurstConfig.HitboxForwardOffset;
					_spellResolver.Spawn(new Hitbox
					{
						X = hx, Y = hy, Z = hz,
						EndX = hx, EndY = hy, EndZ = hz,           // sphere — End == start (BuildHurtboxList convention)
						Radius = BurstConfig.HitboxRadius, Shape = HitboxShape.Sphere,
						Damage = BurstConfig.HitboxDamage,
						BaseKnockback = BurstConfig.HitboxBaseKnockback,
						KnockbackGrowth = BurstConfig.HitboxKnockbackGrowth, // 0 → ApplyKnockback never scales by DamagePercent
						KnockbackAngle = BurstConfig.HitboxAngle,
						StunTicks = BurstConfig.HitboxStunTicks,
						DurationTicks = BurstConfig.HitboxDurationTicks,
						OwnerId = id,
						FreezesOwner = false,   // user is already in recovery; freezing them inside it would muddy the punish window
					});
				}
				burstState.BurstPending = 0;
				_states[id] = burstState;
			}

			// ── Step 1b: Tick server-side abilities (overrides movement, spawns hitboxes) ──
			TickAbilities(inputs);

			// ── Step 1c: Landing lag freeze (issue #125) ──
			// The lock is "no input, no movement": while it is live, the active ability's
			// per-tick velocity writes (StageChainAbility's lunge re-apply, AirChargeAttack's
			// MoveY) must not move the character. Zero velocity every lagged tick — the move
			// itself keeps running (stages advance, lingering hitboxes resolve), which is the
			// Melee landing commitment: the active window is the air cost, landing is the lock.
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var lagState) || lagState.LandingLagTicks == 0) continue;
				if (lagState.VX != 0f || lagState.VY != 0f || lagState.VZ != 0f)
				{
					lagState.VX = 0f; lagState.VY = 0f; lagState.VZ = 0f;
					_states[id] = lagState;
				}
			}
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
				// ── Warping: rotation tracking (no warp init — already set by PreTickAbilities) ──
				if (state.State == ActionState.Warping)
				{
					if (_pendingWarpAttacks.TryGetValue(id, out byte pendingSlot))
					{
						var def = _defs[id];
						bool airborne = !state.IsGrounded;
						var spec = def.GetSlotAbility(pendingSlot - 1, airborne);
						if (spec != null && spec.Stages != null && spec.Stages.Length > 0)
						{
							var stage = spec.Stages[0];
							var target = _states[targetId];
							float dx = target.PX - state.PX;
							float dz = target.PZ - state.PZ;
							float dist = MathF.Sqrt(dx * dx + dz * dz);

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
						}
					}
					_states[id] = state;
					continue;
				}

				// ── Attacking/Aiming behaviors (warp, rotation) ──
				if (state.State is not (ActionState.Attacking or ActionState.Aiming) || state.AttackSlot == 0)
				{
					_states[id] = state;
					continue;
				}

				// ── Hitstop (ADR-0012): keep TargetEntityId fresh but block warp-init and
				// face-toward-target rotation while the attacker is frozen. ──
				if (state.HitstopTicks > 0)
				{
					_states[id] = state;
					continue;
				}

				var attackDef = _defs[id];
				bool attackAirborne = !state.IsGrounded;
				var attackSpec = attackDef.GetSlotAbility(state.AttackSlot - 1, attackAirborne);
				if (attackSpec == null)
				{
					// Issue #117 backstop: an AttackSlot placeholder with no air spec
					// (grounded-only move buffered mid-air) must reset, not stick in
					// Attacking forever.
					state.State = ActionState.Idle;
					state.AttackSlot = 0;
					state.AnimLockTicks = 0;
					state.ComboStage = 0;
					_states[id] = state;
					continue;
				}

				if (attackSpec.Stages == null || attackSpec.Stages.Length == 0) { _states[id] = state; continue; }
				var attackStage = Simulation.ResolveStage(attackSpec, state);

				// Only process warp/rotation if target lock is enabled for this stage
				if (!attackStage.UseTargetLock) { _states[id] = state; continue; }

				var attackTarget = _states[targetId];
				float attackDx = attackTarget.PX - state.PX;
				float attackDz = attackTarget.PZ - state.PZ;
				float attackDist = MathF.Sqrt(attackDx * attackDx + attackDz * attackDz);

				// ── Warp toward target if within WarpRange but outside AttackRange ──
				// Only for initial engage: don't re-warp if a ServerAbility is active.
				// Without this guard, hitting a target knocks it back > AttackRange but ≤
				// WarpRange, and ProcessTargetLock re-triggers warp every tick ("follow").
				if (!_activeAbilities.ContainsKey(id) && state.WarpSpeed <= 0f
				    && attackStage.WarpRange > 0f
				    && attackDist > attackStage.AttackRange
				    && attackDist <= attackStage.WarpRange)
				{
					state.WarpTargetX = attackTarget.PX;
					state.WarpTargetZ = attackTarget.PZ;
					state.WarpAttackRange = attackStage.AttackRange;
					state.WarpSpeed = 1f;
				}

				// ── Rotate toward target each tick ──
				float attackRotRange = attackStage.WarpRange > 0f ? attackStage.WarpRange : attackStage.AttackRange;
				if (attackStage.RotateTowardTarget && attackStage.TrackingStrength > 0f && attackDist <= attackRotRange)
				{
					if (attackDx * attackDx + attackDz * attackDz > 0.001f)
					{
						float targetYaw = MathF.Atan2(attackDx, attackDz);
						float diff = targetYaw - state.FacingYaw;
						while (diff > MathF.PI) diff -= 2f * MathF.PI;
						while (diff < -MathF.PI) diff += 2f * MathF.PI;
						state.FacingYaw += diff * attackStage.TrackingStrength * Simulation.TickDt;
					}
				}

				_states[id] = state;
			}
		}

		private List<SpellResolver.EntityData> BuildHurtboxList()
		{
			// ── Step 2: Build entity list for hit detection ──
			// Unified pose resolution (spec #119): every entity's hurtboxes come from
			// BuildEntitiesFromState — the same function the Ability Lab preview uses —
			// so what the tool displays is exactly what collides.
			var entityList = new List<SpellResolver.EntityData>();
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = _defs[id];

				// Eliminated (0 stocks / rule) — untargetable spectator (issue #37).
				if (_rule.IsEliminated(state)) continue;

				if (ResolveBoneAnimFrame(id, state, def, out var baked, out var targetAnim, out var bakedFrame))
				{
					entityList.AddRange(BuildEntitiesFromState(state, def, baked, targetAnim, bakedFrame, id));
				}
				else if (def.HurtboxCapsules != null)
				{
					// No baked data / no bone defs → capsule fallback.
					entityList.AddRange(BuildEntitiesFromState(state, def, null!, "idle", 0, id));
				}
			}
			_lastEntityList = entityList;
			return entityList;
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
				bool attackerExists = _states.TryGetValue(hit.OwnerEntityId, out var attackerState);

				// Invincible (respawn grace, dash) — the hit is fully ignored (issue #37).
				if (targetState.InvincibilityTicks > 0) continue;


				// ── Counter interception (target-side): if the defender has an active
				// ability that counters this hit, it absorbs the hit and applies its own
				// riposte to the attacker. Skip normal damage/knockback for this hit. ──
				if (attackerExists && hit.OwnerEntityId != hit.TargetEntityId
				    && _activeAbilities.TryGetValue(hit.TargetEntityId, out var defenderAbility))
				{
					if (defenderAbility.TryCounter(ref targetState, ref attackerState, hit.Damage))
					{
						_states[hit.TargetEntityId] = targetState;
						_states[hit.OwnerEntityId] = attackerState;
						continue;
					}
				}

				// Knockback direction: from attacker to target (not hitbox to target).
				// The hitbox offset can place it past the target, inverting the direction.
				// Smash convention: always push away from the attacker.
				float dirX = hit.DirX;
				float dirZ = hit.DirZ;
				if (attackerExists)
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

				// Burst (ADR-0014): remember who hit us — consumed by the defensive shove.
				// Placed after the invincibility/counter continues, so ignored hits never mark.
				if (attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
					targetState.LastAttackerEntityId = hit.OwnerEntityId;

				float finalDamage = hit.Damage;
				targetState.DamagePercent += (ushort)finalDamage;
				if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;
				// Resolve hitstun animation tier from stun duration
				targetState.HitstunLevel = hit.StunTicks <= 30 ? (byte)0 :
				    hit.StunTicks <= 50 ? (byte)1 : (byte)2;

				// ── Hitstop (ADR-0012): freeze both (melee) or receiver only, defer the launch.
				// 'Beyond first' = attacker or victim already frozen — covers melee multihits
				// (attacker still frozen from the previous hit) and projectile/zone rehits
				// (victim still frozen). Same-ability signal per ADR-0012.
				bool beyondFirst = attackerState.HitstopTicks > 0 || targetState.HitstopTicks > 0;
				AbilitySpec? hitstopSpec = null;
				if (attackerExists && attackerState.AttackSlot > 0
				    && _defs.TryGetValue(hit.OwnerEntityId, out var hitOwnerDef))
					hitstopSpec = hitOwnerDef.GetSlotAbility(attackerState.AttackSlot - 1, !attackerState.IsGrounded);
				ushort freeze = ComputeHitstopTicks(finalDamage, beyondFirst, hitstopSpec);
				float kvBeforeOnHitX = targetState.KVX;
				float kvBeforeOnHitY = targetState.KVY;
				float kvBeforeOnHitZ = targetState.KVZ;
				if (freeze > 0)
				{
					targetState.HitstopTicks = freeze;
					targetState.QueuedKBDirX = dirX; targetState.QueuedKBDirZ = dirZ;
					targetState.QueuedKBAngle = hit.KnockbackAngle;
					targetState.QueuedKBBase = hit.BaseKnockback;
					targetState.QueuedKBGrowth = hit.KnockbackGrowth;
					targetState.QueuedKBStun = hit.StunTicks;
					// Only the LAST hit's queue applies at expiry — reset any previous
					// hit's override snapshot on this victim.
					targetState.QueuedKVOverride = false;
					targetState.QueuedKVX = 0f; targetState.QueuedKVY = 0f; targetState.QueuedKVZ = 0f;
					if (hit.FreezesOwner && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
						attackerState.HitstopTicks = freeze; // overwrite, not max: each hit pops fresh (discounted)
				}
				else
				{
					// Tuner zeroed the freeze for this ability — launch immediately (pre-hitstop behavior).
					Simulation.ApplyKnockback(ref targetState, dirX, dirZ,
					    hit.KnockbackAngle, hit.BaseKnockback, hit.KnockbackGrowth, hit.StunTicks);
					targetState.HitstunTicks = hit.StunTicks;
				}

				// Let the attacker's active ability apply hit effects (e.g., FightGuy R mark consumption)
				if (attackerExists
				    && _activeAbilities.TryGetValue(hit.OwnerEntityId, out var attackerAbility)
				    && _defs.TryGetValue(hit.OwnerEntityId, out var attackerDef))
				{
					float kbForce = hit.BaseKnockback + hit.KnockbackGrowth * (targetState.DamagePercent * 0.01f);
					attackerAbility.OnHitEntity(ref attackerState, ref targetState, attackerDef, ref finalDamage, ref kbForce);
				}

				// If the hit's OnHitEntity rewrote the launch at connect (NetherGrasp's yank —
				// the hitbox carries zero KB, the yank is applied here), snapshot the final
				// launch state so the freeze-expiry gate restores it exactly instead of
				// recomputing a zero-KB launch from the raw params.
				if (freeze > 0
				    && (targetState.KVX != kvBeforeOnHitX
				        || targetState.KVY != kvBeforeOnHitY
				        || targetState.KVZ != kvBeforeOnHitZ))
				{
					targetState.QueuedKVOverride = true;
					targetState.QueuedKVX = targetState.KVX;
					targetState.QueuedKVY = targetState.KVY;
					targetState.QueuedKVZ = targetState.KVZ;
					targetState.QueuedKBStun = targetState.HitstunTicks;
				}

				// Write the attacker's state back even when the owner has no active ability
				// (e.g. a projectile hitting after its ability ended) — the freeze must land.
				if (attackerExists) _states[hit.OwnerEntityId] = attackerState;
				_states[hit.TargetEntityId] = targetState;
			}
		}

		private void ProcessProjectileExplosions()
		{
			// ── Step 3b: Projectile explosions (entity hit + ground impact) ──
            // Ground collision for remaining active projectiles (samples heightmap per projectile)
            _spellResolver.CheckGroundCollision(_arena);

			// Spawn explosion hitboxes for all deactivated projectiles this tick
            // NOTE: The ProjectileExplosion config is baked at spawn time, so nothing here
            // applies Overclock buff bonuses — explosions are secondary effects detached
            // from the owner's state by the time they resolve. An ability MAY therefore bake
            // buffed values into the config itself before Resolver.Spawn: NilusVoidRift does
            // exactly that (its explosion IS the payload rift), while MankiBazooka and
            // MankiRoundBomb buff only the direct projectile hit and leave their ground-impact
            // explosions unbuffed. Buffing an explosion any later would need the owner's buff
            // flags propagated alongside the projectile and checked at explosion time.
			foreach (var (ex, ey, ez, explosion, ownerId) in _spellResolver.DrainPendingExplosions())
			{
				var (kbAngle, kbBase, kbGrowth) = explosion.Knockback.Resolve();
				_spellResolver.Spawn(new Hitbox
				{
					X = ex, Y = ey, Z = ez,
					Radius = explosion.Radius, Shape = HitboxShape.Sphere,
					EndX = ex, EndY = ey, EndZ = ez,
					Damage = explosion.Damage,
					BaseKnockback = kbBase,
					KnockbackGrowth = kbGrowth,
					KnockbackAngle = kbAngle,
					StunTicks = explosion.StunTicks,
					DurationTicks = explosion.DurationTicks,
					OwnerId = ownerId,
					CanHitOwner = explosion.CanHitOwner,
					RehitIntervalTicks = explosion.RehitIntervalTicks,
				});
			}
		}

		private void ProcessWarpArrivals()
		{
			foreach (var id in _pendingWarpAttacks.Keys.ToList())
			{
				if (!_states.TryGetValue(id, out var state))
				{
					_pendingWarpAttacks.Remove(id);
					continue;
				}

				// Clean up if entity left Warping state (interrupted by hitstun, etc.)
				if (state.State != ActionState.Warping)
				{
					_pendingWarpAttacks.Remove(id);
					continue;
				}

				// Warp still in progress
				if (state.WarpSpeed > 0f)
					continue;

				// Warp completed — activate the pending attack
				byte slot = _pendingWarpAttacks[id];
				_pendingWarpAttacks.Remove(id);

				var def = _defs[id];
				bool airborne = !state.IsGrounded;
				var spec = def.GetSlotAbility(slot - 1, airborne);
				if (spec == null)
				{
					state.State = ActionState.Idle;
					_states[id] = state;
					continue;
				}

				var ability = SlopArena.Shared.Abilities.AbilityFactory.CreateServer(def.Class, (byte)(slot - 1), airborne);
				if (ability == null)
				{
					state.State = ActionState.Idle;
					_states[id] = state;
					continue;
				}
				SlopArena.Shared.Abilities.AbilityFactory.InitFromSpec(ability, spec, (byte)(slot - 1));
				ActivateAbility(id, ability, (byte)(slot - 1), def);
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
				byte newDeaths = oldState.Deaths < byte.MaxValue ? (byte)(oldState.Deaths + 1) : oldState.Deaths;

				// Respawn point: per-entity override when set (MatchInstance/TrainingMatch
				// distribute spawn points), else deterministic by entity index so players
				// never all stack on SpawnPoints[0] (issue #37).
				float rpx, rpy, rpz, rpyaw;
				if (_respawnPositions.TryGetValue(id, out var rp))
				{
					rpx = rp.x; rpy = rp.y; rpz = rp.z; rpyaw = rp.yaw;
				}
				else
				{
					int idx = (int)((id - 1) % (ulong)Math.Max(1, _arena.SpawnPoints.Length));
					var sp = _arena.SpawnPoints[idx];
					rpx = sp.X; rpy = sp.Y; rpz = sp.Z; rpyaw = sp.Yaw;
				}

				var respawned = new CharacterState
				{
					PX = rpx, PY = rpy, PZ = rpz,
					FacingYaw = rpyaw,
					EntityId = id,
					State = ActionState.Idle,
					IsGrounded = true,
					JumpsLeft = d.Movement.MaxJumps, AirDodgesLeft = 1,
					Deaths = newDeaths, DamagePercent = 0,
				};
				// Burst cooldown persists through KO (issue #99 user story 12) — the ONLY
				// carry-over. Recovery is deliberately NOT carried: death clears the punish window.
				respawned.BurstCooldownTicks = oldState.BurstCooldownTicks;

				if (_rule.IsEliminated(respawned))
				{
					// Lost (0 stocks / rule) — spectator: frozen at its spawn point,
					// excluded from hurtboxes and physics (see BuildHurtboxList/
					// SimulateMovement), no input (see MatchInstance). Issue #37.
					respawned.InvincibilityTicks = 0;
				}
				else
				{
					// Still has stocks — respawn with brief invincibility (Smash convention).
					respawned.InvincibilityTicks = RespawnInvincibilityTicks;
				}
				_states[id] = respawned;
			}
		}

		public void Tick(Dictionary<ulong, InputState> inputs)
		{
			PreTickAbilities(inputs);

			ProcessTargetLock(inputs);

			SimulateMovement(inputs);

			// ── Warp arrival: activate pending attacks ──
			ProcessWarpArrivals();

			var entityList = BuildHurtboxList();

			ResolveHits(entityList);

			ProcessProjectileExplosions();

			CheckVoidDeaths();
		}
	}
}
