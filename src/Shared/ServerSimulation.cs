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
		private readonly Dictionary<ulong, byte> _kos = new();
		private readonly Dictionary<ulong, (ulong attackerId, uint tick)> _lastHitCredits = new();
		private uint _tick;
		public void SetTick(uint tick) => _tick = tick;
		private readonly List<TimelinePresentationEvent> _presentationEvents = new();
		private const uint KillCreditWindowTicks = 180;

		private readonly Dictionary<ulong, BakedAnimationData> _bakedData = new();
		private readonly Dictionary<ulong, int> _animFrames = new();
		private readonly Dictionary<ulong, int> _prevAnimIndex = new();
		private List<SpellResolver.EntityData> _lastEntityList = new();
		public List<SpellResolver.HitResult> LastTickHits { get; } = new();
		private readonly SpellResolver _spellResolver = new();
		/// <summary>Authoritative KOs credited during the current match.</summary>
		public byte GetKOs(ulong entityId) => _kos.TryGetValue(entityId, out var kos) ? kos : (byte)0;

		private readonly Dictionary<ulong, (float x, float y, float z, float yaw)> _respawnPositions = new();
		// Track pending attack slots for warp-in-progress entities
		private readonly Dictionary<ulong, byte> _pendingWarpAttacks = new();
		// ── Ability pool ──
		private readonly Dictionary<ulong, ServerAbility> _activeAbilities = new();
		private readonly IMatchRule _rule;
		private readonly ArenaCollision.BlastLines _blastLines;
		/// <summary>Ticks of invincibility granted on respawn (60 = 1s at 60Hz). Issue #37.</summary>
		public ushort RespawnInvincibilityTicks { get; set; } = 60;

		/// <param name="rule">Win-condition rule (elimination + match end). Defaults to stock mode, 3 stocks.</param>
		public ServerSimulation(ArenaDefinition arena, IMatchRule? rule = null)
		{
			_arena = arena;
			_blastLines = ArenaCollision.ResolveBlastLines(in arena);
			_rule = rule ?? new StockMatchRule(3);
		}
		private const float WarpConeHalfAngleRad = 120f * MathF.PI / 180f / 2f; // 60° half-cone = 120° total facing cone

		// ── Hitstop tuning (ADR-0012). Game-wide defaults; per-ability overrides via
		/// <summary>Freeze ticks for a connecting hit (ADR-0019, issue #143):
		/// min(12, (int)((damage/3 + 6) · multiplier)) — jabs ~7, mediums ~8, kills ~10.
		/// Cap 12 is a never-biting safety (kit max 16 dmg → 11). The ADR-0012 extras
		/// (low-damage ×2, beyond-first ×0.5) and the six hitstop_* param keys are dropped;
		/// a single per-ability override remains: `hitstop_multiplier` (default 1.0).
		/// Pass the ATTACKER's ability spec (the ability that lands the hit); null = defaults.</summary>
		public static ushort ComputeHitstopTicks(float damage, AbilitySpec? spec)
		{
			float mul = HitstopParam(spec, "hitstop_multiplier", 1f);
			float raw = (int)((damage / 3f + 6f) * mul);
			return (ushort)Math.Max(1f, Math.Min(12f, raw));
		}

		private static float HitstopParam(AbilitySpec? spec, string key, float fallback)
			=> (spec?.Params != null && spec.Params.TryGetValue(key, out float v)) ? v : fallback;

		public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
		{
			_defs[id] = def;
			initialState.EntityId = id;
			_states[id] = initialState;
			_kos[id] = 0;
			_lastHitCredits.Remove(id);

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
			if (_activeAbilities.TryGetValue(id, out var ability)
			    && _states.TryGetValue(id, out var state))
			{
				ability.OnCancel(ref state);
			}
			_states.Remove(id);
			_defs.Remove(id);
			_bakedData.Remove(id);
			_animFrames.Remove(id);
			_prevAnimIndex.Remove(id);
			_activeAbilities.Remove(id);
			_respawnPositions.Remove(id);
			_kos.Remove(id);
			_lastHitCredits.Remove(id);

		}

		public CharacterState GetState(ulong id) => _states.TryGetValue(id, out var s) ? s : default;
		public void SetState(ulong id, CharacterState state) => _states[id] = state;
		public Dictionary<ulong, CharacterState> GetAllStates() => _states;
		public List<SpellResolver.EntityData> GetLastEntityData() => _lastEntityList;
		public SpellResolver Resolver => _spellResolver;
		public IReadOnlyList<TimelinePresentationEvent> GetPresentationEvents(bool clear = false)
		{
			var snapshot = new List<TimelinePresentationEvent>(_presentationEvents);
			if (clear) _presentationEvents.Clear();
			return snapshot;
		}

		public void ClearPresentationEvents() => _presentationEvents.Clear();

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
			ability.PresentationSink = e => _presentationEvents.Add(e with { MatchTick = _tick });
			ability.Slot = slot;
			ability.AirborneAtStart = !state.IsGrounded;
            var spec = def.GetSlotAbility(slot, !state.IsGrounded);
            var cookedSlot = def.GetCookedSlotAbility((byte)(slot + 1), !state.IsGrounded);
            bool preserveMomentum = cookedSlot?.PreserveMomentumOnStart ?? spec?.PreserveMomentumOnStart ?? false;
            // ADR-0015 §2 refinement: grounded activations stop incoming momentum unless
            // the move explicitly preserves it. The move's own OnStart velocity follows.
            if (state.IsGrounded && !preserveMomentum)
            {
                state.VX = 0f;
                state.VZ = 0f;
            }
            // Ability refresh (ADR-0020): activating any ability refills the Rush window.
            state.RushTicks = def.Movement.RushTicks;
			// Acting ends the post-hitstun flight regime.
			state.InPostHitstunFlight = false;
			ability.OnStart(ref state, def);
			state.AnimIndex = ability.AnimIndex;
			if (state.State != ActionState.Attacking && state.State != ActionState.Aiming)
			{
				if (ability.Slot < AbilitySlots.Count)
					state.SetCooldown((byte)(ability.Slot + 1), ability.Cooldown);
				_states[entityId] = state;
				return;
			}
			state.AttackSlot = (byte)(slot + 1);
            // ADR-0015 / issue #115: recovery-designated moves reset the float window.
            if (cookedSlot?.IsRecoveryMove == true || (cookedSlot == null && spec?.IsRecoveryMove == true))
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

				if (state.State != ActionState.Attacking && state.State != ActionState.Aiming)
				{
					ability.OnCancel(ref state);
					_states[id] = state;
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
                int stageIdx = ability != null ? Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1)) : 0;
                targetAnim = ability != null && stageIdx >= 0 && stageIdx < ability.AnimationNames.Length ? ability.AnimationNames[stageIdx] : "melee";
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
                var cooked = def.GetCookedSlotAbility(state.AttackSlot, airborne);
                if (cooked != null)
                {
                    int stageIdx = Math.Min(state.ComboStage, (byte)(cooked.Timeline.Stages.Count - 1));
                    int durationTicks = cooked.Timeline.Stages[stageIdx].DurationTicks;
                    if (durationTicks > 0) bakedFrame = Math.Min(frame * fc / durationTicks, fc - 1);
                }
                else
                {
                    var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
                    if (ability != null)
                    {
                        int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
                        if (stageIdx >= 0 && stageIdx < ability.Stages.Length)
                        {
                            int durationTicks = ability.Stages[stageIdx].DurationTicks;
                            if (durationTicks > 0) bakedFrame = Math.Min(frame * fc / durationTicks, fc - 1);
                        }
                    }
                }
            }

            return true;
        }

		/// <summary>
		/// Landing lag (issue #125 / ADR-0021 §3) + aerial landing termination (drift fix):
		/// when an AIR-STARTED ability (<see cref="ServerAbility.AirborneAtStart"/>) is still
		/// active when the character lands, the aerial ENDS on the landing frame and — unless
		/// the landing fell in an auto-cancel window — a no-input/no-movement lock applies for
		/// the stage's <c>LandingLagTicks</c>. The termination is unconditional for aerials
		/// (LandingLagTicks == 0 still ENDS the move, just with no lock): otherwise a grounded
		/// aerial keeps the character in Attacking on the floor, which skips
		/// ProcessNormalMovement's friction and lets a lunge move (Cyclone) drive the
		/// character across the stage with only dash to stop it.
		///
		/// Detection: airborne at tick start + grounded after SimulateTick = a landing.
		/// A ledge snap also flips IsGrounded but boosts VY (LedgeSnapUpwardBoost) — that is
		/// not a landing, so the <c>VY &lt;= 0</c> guard excludes it. The spec is resolved with
		/// the SAME airborne flag the move started with (<c>AirborneAtStart</c>): only
		/// genuinely air-started moves read their airborne variant's landing lag. A ground move
		/// that is launched and lands mid-move (e.g. a mutual LMB trade) keeps its GROUND
		/// spec — its landing carries no lag, because the ground spec declares none
		/// (ADR-0021 §3: landing lag belongs to aerials, not to ground normals).
		///
		/// Landing frame is pre-lock for committal escapes — by design (ADR-0021 §3): the
		/// input gates (burst especially) run inside SimulateTick BEFORE this applies the lock,
		/// so a press on the landing frame itself is processed pre-lock. That lets a burst
		/// (offensive cancel — costs the ~60 s committal cooldown, ADR-0014) or an air-jump
		/// (costs a double jump) cancel on the landing frame; dash and abilities stay blocked
		/// by their own gates, so there is no free cancel. Every later locked tick is fully gated.
		/// </summary>
		private static void ApplyLandingLag(ref CharacterState state, CharacterDefinition def, bool wasGrounded, ServerAbility? activeAbility)
		{
			if (wasGrounded || !state.IsGrounded) return; // no landing this tick
			if (state.VY > 0f) return;                    // ledge snap boost, not a landing
			if (state.State != ActionState.Attacking && state.State != ActionState.Aiming) return;
			if (state.AttackSlot == 0 || state.LandingLagTicks > 0) return;
			// Only AIR-started moves terminate on landing (drift fix). A ground move launched
			// and landed mid-move keeps its ground behavior — no termination.
			if (activeAbility == null || !activeAbility.AirborneAtStart) return;

			var cooked = def.GetCookedSlotAbility(state.AttackSlot, airborne: true);
			ushort landingLagTicks;
			ushort autoCancelBeforeTicks;
			ushort autoCancelAfterTicks;
			int elapsed;
			if (cooked != null)
			{
				var stage = cooked.Timeline.Stages[Math.Min(state.ComboStage, (byte)(cooked.Timeline.Stages.Count - 1))];
				landingLagTicks = stage.LandingLagTicks;
				autoCancelBeforeTicks = stage.AutoCancelBeforeTicks;
				autoCancelAfterTicks = stage.AutoCancelAfterTicks;
				elapsed = state.AttackElapsedTicks;
				for (var i = 0; i < state.ComboStage && i < cooked.Timeline.Stages.Count; i++)
					elapsed -= cooked.Timeline.Stages[i].DurationTicks;
			}
			else
			{
				var spec = def.GetSlotAbility(state.AttackSlot - 1, airborne: true);
				if (spec?.Stages is not { Length: > 0 }) return;
				var stage = Simulation.ResolveStage(spec, state);
				landingLagTicks = stage.LandingLagTicks;
				autoCancelBeforeTicks = stage.AutoCancelBeforeTicks;
				autoCancelAfterTicks = stage.AutoCancelAfterTicks;
				elapsed = Simulation.ElapsedInStage(state, spec);
			}
			bool autoCancel = (autoCancelBeforeTicks > 0 && elapsed <= autoCancelBeforeTicks)
				|| (autoCancelAfterTicks > 0 && elapsed >= autoCancelAfterTicks);

			// The aerial always ENDS on landing. Auto-cancel: end with no lock at all (Melee's
			// AC: no landing commitment). Otherwise the landing lag (possibly 0) is the ONLY
			// cost — no riding out the stage's remaining recovery + IASA.
			state.State = ActionState.Idle;
			state.AttackSlot = 0;
			state.ComboStage = 0;
			state.AttackElapsedTicks = 0;
			state.AnimLockTicks = 0;
			state.BufferedSlot = 0;
			if (autoCancel)
				return;
			state.LandingLagTicks = landingLagTicks;
        }

        /// <summary>Occupancy-aware ledge grab (ADR-0020): an off-grid, non-hitstun entity
        /// within grab range of a ledge enters LedgeHang — unless another entity already
        /// hangs that ledge (edge sample point within 0.2 m), in which case it falls past.</summary>
        private void TryLedgeGrab(ulong id, ref CharacterState state, CharacterDefinition def)
        {
            if (state.IsGrounded || state.State == ActionState.Hitstun || state.State == ActionState.LedgeHang
                || state.State == ActionState.JumpSquat || state.State == ActionState.Attacking || state.State == ActionState.Aiming
                || state.HitstunTicks != 0 || state.VY >= 0f
                || Simulation.HasKnockback(state) || state.LedgeRegrabLockTicks > 0) return;
            float capsuleHalf = def.CapsuleHeight * 0.5f;
            if (!Simulation.FindLedge(state, _arena, capsuleHalf, out float surfaceY, out _, out _, out float edgeX, out float edgeZ)) return;

            foreach (var kvp in _states)
            {
                if (kvp.Key == id) continue;
                var other = kvp.Value;
                if (other.State != ActionState.LedgeHang) continue;
                if (!Simulation.FindLedge(other, _arena, _defs[kvp.Key].CapsuleHeight * 0.5f, out _, out _, out _, out float ox, out float oz)) continue;
                float dxx = ox - edgeX, dzz = oz - edgeZ;
                if (dxx * dxx + dzz * dzz < 0.2f * 0.2f) return;   // occupied — fall past
            }

            // Grab
            state.State = ActionState.LedgeHang;
            state.IsGrounded = false;
            state.VX = state.VY = state.VZ = 0f;
            state.KVX = state.KVY = state.KVZ = 0f;
            state.InvincibilityTicks = Simulation.LedgeGrabInvincibilityTicks;
            state.JumpsLeft = def.Movement.MaxJumps;
            state.AirTimeTicks = 0;
            state.PY = surfaceY + capsuleHalf;
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
				bool iasaUnlocked = Simulation.IsIasaUnlocked(state, def);
				if (state.HitstunTicks > 0 || state.HitstopTicks > 0 || state.BurstRecoveryTicks > 0
					|| state.LandingLagTicks > 0
					|| (state.AnimLockTicks > 0 && !iasaUnlocked)) continue; // ADR-0014
				if (state.State != ActionState.Idle && state.State != ActionState.Attacking && state.State != ActionState.Run) continue;

				bool airborne = !state.IsGrounded;
				var cookedSlot = def.GetCookedSlotAbility(input.ActiveSlot, airborne);
				var spec = def.GetSlotAbility(input.ActiveSlot - 1, airborne);

				// Issue #117: reject slots with no cooked or legacy definition.
				if (cookedSlot == null && spec == null)
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
				if ((state.State == ActionState.Idle || state.State == ActionState.Run) && spec?.Stages is { Length: > 0 })
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
				int maxCharges = cookedSlot == null && spec!.Params != null && spec.Params.TryGetValue("max_charges", out var mc) ? (int)mc : 0;
				if (maxCharges > 0 && state.ChargeStockSpent >= maxCharges)
				{
					// Consume the input so SimulateTick doesn't start a data-driven attack.
					var blockedInput = input;
					blockedInput.ActiveSlot = 0;
					inputs[id] = blockedInput;
					continue;
				}

                var ability = cookedSlot != null
                    ? new CookedTimelineAbility(cookedSlot, cookedSlot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray())
                    : AbilityFactory.CreateServer(def.Class, (byte)(input.ActiveSlot - 1), airborne);
                if (ability == null) continue;

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

					currentAbility.OnCancel(ref state);
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

				if (cookedSlot != null)
				{
					ability.Cooldown = cookedSlot.CooldownTicks;
                    ability.AnimationNames = cookedSlot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray();
				}
				else
				{
					AbilityFactory.InitFromSpec(ability, spec!, (byte)(input.ActiveSlot - 1));
				}
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
                TryLedgeGrab(id, ref state, def);
				// Landing lag (issue #125 / ADR-0021 §3): land mid-aerial → lock, unless the
				// landing frame falls in an auto-cancel window. Only air-started moves resolve
				// their airborne variant's landing lag (ground moves keep their ground spec).
				_activeAbilities.TryGetValue(id, out var activeAbility);
				ApplyLandingLag(ref state, def, wasGrounded, activeAbility);
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
                        // Fixed defensive shove — NOT a damage launch: bypasses KbScaleFactor
                        // (the escape tool must not shrink with the hit-knockback balance).
                        Simulation.ApplyKnockbackForce(ref attackerState, dx, dz,
                            BurstConfig.AttackerPushAngle, BurstConfig.AttackerPushBaseKnockback, 0);

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

			// ── Step 1c: Landing lag freeze (issue #125 / ADR-0021 §3) ──
			// The lock is "no input, no movement": the aerial has already ENDED on the landing
			// frame (ApplyLandingLag), but the residual lunge/air drift is still live. Zero
			// velocity every lagged tick so the character plants and stays pinned — the lock
			// is the only cost, not an overlay on the move's remaining recovery.
			foreach (var id in simIds)
			{
				if (!_states.TryGetValue(id, out var lagState) || lagState.LandingLagTicks == 0) continue;
				if (lagState.VX != 0f || lagState.VY != 0f || lagState.VZ != 0f)
				{
					lagState.VX = 0f; lagState.VY = 0f; lagState.VZ = 0f;
					_states[id] = lagState;
				}
			}
			ResolvePushboxes(simIds);
		}

		/// <summary>
		/// Resolve stable body pushboxes after all movement and ability movement for this tick.
		/// Pushboxes are horizontal cylinders derived from each character definition; they are
		/// deliberately separate from animation-driven attack hurtboxes.
		/// </summary>
		private void ResolvePushboxes(ulong[] simIds)
		{
			Array.Sort(simIds);
			for (int i = 0; i < simIds.Length; i++)
			{
				ulong firstId = simIds[i];
				if (!_states.TryGetValue(firstId, out var first)
				    || !_defs.TryGetValue(firstId, out var firstDef)
				    || _rule.IsEliminated(first))
					continue;

				for (int j = i + 1; j < simIds.Length; j++)
				{
					ulong secondId = simIds[j];
					if (!_states.TryGetValue(secondId, out var second)
					    || !_defs.TryGetValue(secondId, out var secondDef)
					    || _rule.IsEliminated(second))
						continue;

					float verticalReach = (firstDef.CapsuleHeight + secondDef.CapsuleHeight) * 0.5f;
					if (MathF.Abs(first.PY - second.PY) >= verticalReach)
						continue;

					float dx = second.PX - first.PX;
					float dz = second.PZ - first.PZ;
					float distanceSquared = dx * dx + dz * dz;
					float radiusSum = firstDef.CapsuleRadius + secondDef.CapsuleRadius;
					if (distanceSquared >= radiusSum * radiusSum)
						continue;

					float distance;
					if (distanceSquared > 0.000001f)
					{
						distance = MathF.Sqrt(distanceSquared);
						dx /= distance;
						dz /= distance;
					}
					else
					{
						distance = 0f;
						float angle = ((firstId ^ secondId) & 1UL) == 0UL ? 0f : MathF.PI * 0.5f;
						dx = MathF.Sin(angle);
						dz = MathF.Cos(angle);
					}

					float penetration = radiusSum - distance;
					bool firstWarping = first.WarpSpeed > 0f;
					bool secondWarping = second.WarpSpeed > 0f;
					if (firstWarping && secondWarping)
						continue;

					float firstCorrection = secondWarping ? 1f : firstWarping ? 0f : 0.5f;
					float secondCorrection = firstWarping ? 1f : secondWarping ? 0f : 0.5f;
					first.PX -= dx * penetration * firstCorrection;
					first.PZ -= dz * penetration * firstCorrection;
					second.PX += dx * penetration * secondCorrection;
					second.PZ += dz * penetration * secondCorrection;

					float relativeVelocity = (second.VX - first.VX) * dx + (second.VZ - first.VZ) * dz;
					if (relativeVelocity < 0f)
					{
						if (!firstWarping && !secondWarping)
						{
							float impulse = relativeVelocity * 0.5f;
							first.VX += dx * impulse;
							first.VZ += dz * impulse;
							second.VX -= dx * impulse;
							second.VZ -= dz * impulse;
						}
						else if (!firstWarping)
						{
							first.VX += dx * relativeVelocity;
							first.VZ += dz * relativeVelocity;
						}
						else if (!secondWarping)
						{
							second.VX -= dx * relativeVelocity;
							second.VZ -= dz * relativeVelocity;
						}
					}

					_states[firstId] = first;
					_states[secondId] = second;
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
		/// Persistent target-lock range (meters, ADR-0018 / issue #127): beyond this the
		/// lock disengages. The soft-lock resolver still scans 20m — the indicator keeps
		/// tracking, but facing control returns to the manual rules.
		/// </summary>
		private const float LockRangeMeters = 10f;

		/// <summary>
		/// Compute soft-lock target for every entity each tick.
		/// Prefers client-provided target (from screen-center) when input.TargetEntityId > 0,
		/// otherwise brute-force scans for nearest enemy within 20m.
		/// Stores the result in state.TargetEntityId for abilities, camera, and indicator to query.
		///
		/// When the entity is attacking with UseTargetLock=true, also processes warp
		/// (auto-dash toward target) and rotation (face toward target). When LockOn
		/// (ADR-0018), also snaps facing toward the resolved target every tick.
		/// </summary>
		private void ProcessTargetLock(Dictionary<ulong, InputState> inputs)
		{
			ulong[] ids = new ulong[_states.Count];
			_states.Keys.CopyTo(ids, 0);
			foreach (var id in ids)
			{
				if (!_states.TryGetValue(id, out var state)) continue;
				bool hasInput = inputs.TryGetValue(id, out var input);
				if (hasInput && input.ToggleLock)
					state.LockOn = !state.LockOn;

				ulong targetId = 0;
				if (hasInput && input.TargetEntityId > 0 && _states.ContainsKey(input.TargetEntityId))
					targetId = input.TargetEntityId;
				if (targetId == 0)
					targetId = FindClosestEnemy(id, state.PX, state.PZ, 20f, out _);
				state.TargetEntityId = targetId;

				if (state.LockOn)
				{
					if (targetId == 0)
						state.LockOn = false;
					else
					{
						var lockTarget = _states[targetId];
						float lockDx = lockTarget.PX - state.PX;
						float lockDz = lockTarget.PZ - state.PZ;
						if (lockDx * lockDx + lockDz * lockDz > LockRangeMeters * LockRangeMeters)
							state.LockOn = false;
					}
				}

				if (targetId == 0)
				{
					_states[id] = state;
					continue;
				}

				if (state.State == ActionState.Warping)
				{
					if (_pendingWarpAttacks.TryGetValue(id, out byte pendingSlot))
					{
						var def = _defs[id];
						var spec = def.GetSlotAbility(pendingSlot - 1, !state.IsGrounded);
						if (spec?.Stages is { Length: > 0 })
						{
							var stage = spec.Stages[0];
							var target = _states[targetId];
							float dx = target.PX - state.PX;
							float dz = target.PZ - state.PZ;
							if (stage.RotateTowardTarget && dx * dx + dz * dz > 0.001f)
							{
								float targetYaw = MathF.Atan2(dx, dz);
								if (state.LockOn)
									state.FacingYaw = targetYaw;
								else if (stage.TrackingStrength > 0f)
								{
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

				if (state.State is not (ActionState.Attacking or ActionState.Aiming) || state.AttackSlot == 0)
				{
					_states[id] = state;
					continue;
				}
				if (state.HitstopTicks > 0)
				{
					_states[id] = state;
					continue;
				}

				var attackDef = _defs[id];
				bool attackAirborne = !state.IsGrounded;
				var attackSpec = attackDef.GetSlotAbility(state.AttackSlot - 1, attackAirborne);
				var attackCooked = attackDef.GetCookedSlotAbility(state.AttackSlot, attackAirborne);
				if (attackSpec == null)
				{
					if (attackCooked != null)
					{
						_states[id] = state;
						continue;
					}
					state.State = ActionState.Idle;
					state.AttackSlot = 0;
					state.AnimLockTicks = 0;
					state.ComboStage = 0;
					_states[id] = state;
					continue;
				}
				if (attackSpec.Stages == null || attackSpec.Stages.Length == 0)
				{
					_states[id] = state;
					continue;
				}
				var attackStage = Simulation.ResolveStage(attackSpec, state);
				if (!attackStage.UseTargetLock)
				{
					_states[id] = state;
					continue;
				}

				var attackTarget = _states[targetId];
				float attackDx = attackTarget.PX - state.PX;
				float attackDz = attackTarget.PZ - state.PZ;
				float attackDist = MathF.Sqrt(attackDx * attackDx + attackDz * attackDz);
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
				if (attackStage.RotateTowardTarget && attackDx * attackDx + attackDz * attackDz > 0.001f)
				{
					float targetYaw = MathF.Atan2(attackDx, attackDz);
					if (state.LockOn)
						state.FacingYaw = targetYaw;
					else if (attackStage.TrackingStrength > 0f)
					{
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


        private void ResolveHits(List<SpellResolver.EntityData> entityList, Dictionary<ulong, InputState> inputs)
		{
			// Bone-tracked hitboxes sweep with their limb: re-resolve positions from
			// the owners' current (post-movement) states before the collision pass.
			_spellResolver.UpdateBoneHitboxes(_states);

			// ── Step 3: Resolve hitboxes ──
			var hits = _spellResolver.Tick(entityList);
			LastTickHits.Clear();
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
					if (defenderAbility.TryCounter(ref targetState, ref attackerState,
					    _defs[hit.OwnerEntityId], hit.Damage))
					{
						_lastHitCredits[hit.OwnerEntityId] = (hit.TargetEntityId, _tick);
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

				// Hit-reaction facing: the victim turns to face the attacker (the direction the
				// hit came from — opposite the launch). Persists through the hitstun flight;
				// ProcessNormalMovement re-faces on the next input/land.
				targetState.FacingYaw = MathF.Atan2(-dirX, -dirZ);

				// Burst (ADR-0014): remember who hit us — consumed by the defensive shove.
				// Placed after the invincibility/counter continues, so ignored hits never mark.
				if (attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
					targetState.LastAttackerEntityId = hit.OwnerEntityId;
				if (attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
					_lastHitCredits[hit.TargetEntityId] = (hit.OwnerEntityId, _tick);


				float finalDamage = hit.Damage;
				targetState.DamagePercent += (ushort)finalDamage;
				if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;


				// ── Hitstop (ADR-0019): freeze both (melee) or receiver only, defer the launch.
				AbilitySpec? hitstopSpec = null;
				if (attackerExists && attackerState.AttackSlot > 0
				    && _defs.TryGetValue(hit.OwnerEntityId, out var hitOwnerDef))
					hitstopSpec = hitOwnerDef.GetSlotAbility(attackerState.AttackSlot - 1, !attackerState.IsGrounded);
                ushort freeze = 0;
                float kvBeforeOnHitX = targetState.KVX;
                float kvBeforeOnHitY = targetState.KVY;
                float kvBeforeOnHitZ = targetState.KVZ;
                // Capture the default once: the hook check must compare kbForce to this exact
            // stored value — re-computing the expression at the check is NOT bit-identical
            // (the editor JIT evaluates in 80-bit x87 precision, so round32(expr) !=
            // expr80 — every normal hit wrongly took the force path, bypassing KbScaleFactor).
            float kbForceDefault = hit.BaseKnockback + hit.KnockbackGrowth * (targetState.DamagePercent * 0.01f);
            float kbForce = kbForceDefault;
                bool hookSuppliedForce = false;
                bool hookZeroForce = false;
                if (attackerExists
                    && _activeAbilities.TryGetValue(hit.OwnerEntityId, out var attackerAbility)
                    && _defs.TryGetValue(hit.OwnerEntityId, out var attackerDef))
                {
                    attackerAbility.OnHitEntity(ref attackerState, ref targetState,
                        attackerDef, _defs[hit.TargetEntityId], ref finalDamage, ref kbForce);
                    hookSuppliedForce = kbForce != kbForceDefault;
                    hookZeroForce = hookSuppliedForce && kbForce <= 0f;
                }
                float launchBase = hookSuppliedForce ? 0f : hit.BaseKnockback;
                float launchGrowth = hookSuppliedForce ? 0f : hit.KnockbackGrowth;
                freeze = ComputeHitstopTicks(finalDamage, hitstopSpec);
				if (freeze > 0)
				{
					targetState.HitstopTicks = freeze;
                    targetState.QueuedKBDirX = dirX; targetState.QueuedKBDirZ = dirZ;
                    targetState.QueuedKBAngle = hit.KnockbackAngle;
                    targetState.QueuedKBBase = launchBase;
                    targetState.QueuedKBGrowth = launchGrowth;
                    targetState.QueuedKBForce = kbForce;
                    targetState.QueuedKBResolvedForce = hookSuppliedForce;
                    targetState.QueuedKBZero = hookZeroForce;
                    targetState.QueuedKBDamage = finalDamage;
                    targetState.QueuedKBStun = hit.StunTicks;

                    // Only the LAST hit's queue applies at expiry — reset any previous
                    // hit's override snapshot on this victim.
                    targetState.QueuedKVOverride = false;
                    targetState.QueuedKVX = 0f; targetState.QueuedKVY = 0f; targetState.QueuedKVZ = 0f;
                    if (hit.FreezesOwner && attackerExists && hit.OwnerEntityId != hit.TargetEntityId)
                        attackerState.HitstopTicks = freeze;
                }
                else
                {
                    // Tuner zeroed the freeze for this ability — launch immediately.
                    bool hookAppliedLaunch = targetState.KVX != kvBeforeOnHitX
                        || targetState.KVY != kvBeforeOnHitY
                        || targetState.KVZ != kvBeforeOnHitZ;
                    if (hookAppliedLaunch)
                    {
                    }
                    else if (hookSuppliedForce)
                    {
                        Simulation.ApplyKnockbackForce(ref targetState, dirX, dirZ,
                            hit.KnockbackAngle, kbForce, hit.StunTicks);
                    }
                    else if (hookZeroForce)
                    {
                        targetState.KVX = targetState.KVY = targetState.KVZ = 0f;
                        targetState.HitstunTicks = 0;
                        targetState.HitstunLevel = 0;
                        targetState.State = ActionState.Idle;
                    }
                    else
                    {
                        Simulation.ApplyKnockback(ref targetState, dirX, dirZ,
                            hit.KnockbackAngle, launchBase, launchGrowth,
                            finalDamage, hit.StunTicks, _defs[hit.TargetEntityId].Weight);
                    }
                    if (inputs.TryGetValue(hit.TargetEntityId, out var targetInput)
                        && (targetInput.MoveX != 0f || targetInput.MoveY != 0f))
                    {
                        targetState.DIX = targetInput.MoveX;
                        targetState.DIY = targetInput.MoveY;
                        Simulation.ApplySdi(ref targetState, targetState.DIX, targetState.DIY);
                        Simulation.ApplyDirectionalInfluence(ref targetState);
                        targetState.DIX = targetState.DIY = 0f;
                    }
                }



				// Write the attacker's state back even when the owner has no active ability
				// (e.g. a projectile hitting after its ability ended) — the freeze must land.
				if (attackerExists) _states[hit.OwnerEntityId] = attackerState;
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

				float impactForce;
				if (targetState.QueuedKVOverride)
				{
					impactForce = MathF.Sqrt(
						targetState.QueuedKVX * targetState.QueuedKVX
						+ targetState.QueuedKVY * targetState.QueuedKVY
						+ targetState.QueuedKVZ * targetState.QueuedKVZ);
				}
				else if (hookSuppliedForce)
				{
					impactForce = MathF.Max(0f, kbForce);
				}
				else
				{
					float mass = MathF.Max(0.01f, _defs[hit.TargetEntityId].Weight + 100f);
					impactForce = (launchBase
						+ launchGrowth * (targetState.DamagePercent * 0.01f + 1f)
						+ finalDamage * 0.1f) * 200f / mass * Simulation.KbScaleFactor;
				}

				var resolvedHit = hit;
				resolvedHit.Damage = finalDamage;
				resolvedHit.DirX = dirX;
				resolvedHit.DirZ = dirZ;
				resolvedHit.ImpactForce = impactForce;
				resolvedHit.HitstopTicks = freeze;

				_states[hit.TargetEntityId] = targetState;
				LastTickHits.Add(resolvedHit);
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
				var cookedSlot = def.GetCookedSlotAbility(slot, airborne);
				var spec = def.GetSlotAbility(slot - 1, airborne);
				if (cookedSlot == null && spec == null)
				{
					state.State = ActionState.Idle;
					_states[id] = state;
					continue;
				}

                var ability = cookedSlot != null
                    ? new CookedTimelineAbility(cookedSlot, cookedSlot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray())
                    : AbilityFactory.CreateServer(def.Class, (byte)(slot - 1), airborne);
				if (ability == null)
				{
					state.State = ActionState.Idle;
					_states[id] = state;
					continue;
				}
				if (cookedSlot != null)
				{
					ability.Cooldown = cookedSlot.CooldownTicks;
					ability.AnimationNames = cookedSlot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray();
				}
				else
				{
					AbilityFactory.InitFromSpec(ability, spec!, (byte)(slot - 1));
				}
				ActivateAbility(id, ability, (byte)(slot - 1), def);
			}
		}

		private void CheckBlastDeaths()
		{
			// ── Step 4: Blast zone death check (void + side + top; inactive planes are ±inf) ──
			var deadIds = new List<ulong>();
			foreach (var kvp in _states)
			{
				var s = kvp.Value;
				if (s.PY < _blastLines.KillHeight || s.PY > _blastLines.KillTop
					|| s.PX < _blastLines.KillMinX || s.PX > _blastLines.KillMaxX
					|| s.PZ < _blastLines.KillMinZ || s.PZ > _blastLines.KillMaxZ)
					deadIds.Add(kvp.Key);
			}
			foreach (var id in deadIds)
			{
				var d = _defs[id];
				var oldState = _states[id];
				if (_activeAbilities.TryGetValue(id, out var deadAbility))
				{
					deadAbility.OnCancel(ref oldState);
					_activeAbilities.Remove(id);
				}
				if (_lastHitCredits.TryGetValue(id, out var credit)
				    && _tick - credit.tick <= KillCreditWindowTicks
				    && credit.attackerId != id
				    && _kos.ContainsKey(credit.attackerId))
				{
					if (_kos[credit.attackerId] < byte.MaxValue)
						_kos[credit.attackerId]++;
				}
				_lastHitCredits.Remove(id);
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
			_tick++;
			PreTickAbilities(inputs);

			ProcessTargetLock(inputs);

			SimulateMovement(inputs);

			// ── Warp arrival: activate pending attacks ──
			ProcessWarpArrivals();

			var entityList = BuildHurtboxList();

            ResolveHits(entityList, inputs);

			ProcessProjectileExplosions();

			CheckBlastDeaths();
		}
	}
}
