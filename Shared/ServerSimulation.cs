using System;
using System.Collections.Generic;

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
		private readonly Dictionary<ulong, bool> _prevAttack = new();
		private List<SpellResolver.EntityData> _lastEntityList = new();
		private readonly SpellResolver _spellResolver = new();

		public ServerSimulation(ArenaDefinition arena) => _arena = arena;

		public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
		{
			_defs[id] = def;
			_states[id] = initialState;
			if (baked != null) _bakedData[id] = baked;
			_animFrames[id] = 0;
			_prevAnimIndex[id] = -1;
		}

		public void RemoveEntity(ulong id)
		{
			_states.Remove(id);
			_defs.Remove(id);
			_bakedData.Remove(id);
			_animFrames.Remove(id);
			_prevAnimIndex.Remove(id);
		}

		public CharacterState GetState(ulong id) => _states.TryGetValue(id, out var s) ? s : default;
		public Dictionary<ulong, CharacterState> GetAllStates() => _states;
		public List<SpellResolver.EntityData> GetLastEntityData() => _lastEntityList;
		public SpellResolver Resolver => _spellResolver;

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
						float wy = py - def.CapsuleHeight * 0.5f + by;
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

			// ── Step 2: Build entity list for hit detection ──
			var entityList = new List<SpellResolver.EntityData>();
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = _defs[id];

				if (_bakedData.TryGetValue(id, out var baked) && def.HurtboxBoneDefs != null && def.HurtboxBoneDefs.Length > 0)
				{
					string targetAnim;
					if (state.State == ActionState.Dashing) targetAnim = "dash";
					else if (state.State == ActionState.Attacking && state.AttackSlot > 0)
					{
						bool airborne = !state.IsGrounded;
						var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
						int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
						targetAnim = (stageIdx >= 0 && stageIdx < ability.AnimationNames.Length) ? ability.AnimationNames[stageIdx] : "melee";
					}
					else if (state.State == ActionState.Hitstun) targetAnim = "small_hit";
					else if (!state.IsGrounded) targetAnim = state.VY > 0 ? "jump" : "fall";
					else if ((state.VX * state.VX) + (state.VZ * state.VZ) > 1f) targetAnim = "run";
					else targetAnim = "idle";

					int animIdx = baked.FindAnimIndex(targetAnim);
					if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }
					if (animIdx >= 0)
					{
						int fc = baked.Animations[animIdx].FrameCount;
						int prevAnim = _prevAnimIndex.TryGetValue(id, out var p) ? p : -1;
						int frame = _animFrames.TryGetValue(id, out var f) ? f : 0;
						if (prevAnim != animIdx) { frame = 0; _prevAnimIndex[id] = animIdx; }
						int nextFrame = frame + 1;
						if (nextFrame >= fc) nextFrame = 0;
						_animFrames[id] = nextFrame;

						int bakedFrame = frame;
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
							float wy = py - def.CapsuleHeight * 0.5f + by;
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
						entityList.Add(new SpellResolver.EntityData
						{
							Id = id, PosX = sx, PosY = sy, PosZ = sz, Radius = cap.Radius,
							Shape = (sx != ex || sy != ey || sz != ez) ? HitboxShape.Capsule : HitboxShape.Sphere,
							EndX = ex, EndY = ey, EndZ = ez, Active = true,
						});
					}
				}
			}

			// ── Spawn hitbox events for attacking entities ──
			foreach (var kvp in _states)
			{
				ulong id = kvp.Key;
				var state = kvp.Value;
				var def = _defs[id];
				if (state.State != ActionState.Attacking || state.AttackSlot == 0) continue;

				bool airborne = !state.IsGrounded;
				var ability = def.GetSlotAbility(state.AttackSlot - 1, airborne);
				int stageIdx = Math.Min(state.ComboStage, (byte)(ability.Stages.Length - 1));
				if (stageIdx < 0 || stageIdx >= ability.Stages.Length) continue;

				var stage = ability.Stages[stageIdx];
				if (stage.HitboxEvents == null) continue;

				float cos = MathF.Cos(state.FacingYaw);
				float sin = MathF.Sin(state.FacingYaw);
				foreach (var evt in stage.HitboxEvents)
				{
					if (state.AttackElapsedTicks != evt.TriggerTick) continue;

					if (ability is RoundBombSpec roundBomb)
					{
						// ── Targeted projectile (velocity from aim) ──
						var pc = roundBomb.ProjectileConfig;
						float D = Math.Clamp(state.AimTargetDistance, 0.5f, pc.MaxRange);
						float launchRad = pc.LaunchAngleDeg * (MathF.PI / 180f);
						float g = pc.Gravity;
						float dY = -def.CapsuleHeight * 0.5f - pc.LaunchOffsetY;

						CombatMath.ComputeProjectileLaunch(D, launchRad, g, dY,
							out float _, out float hSpeed, out float vSpeed);

						float aimCos = MathF.Cos(state.AimYaw);
						float aimSin = MathF.Sin(state.AimYaw);
						float cosθ = MathF.Cos(launchRad);

						_spellResolver.Spawn(new Hitbox
						{
							X = state.PX, Y = state.PY + pc.LaunchOffsetY, Z = state.PZ,
							VX = hSpeed * aimSin, VY = vSpeed, VZ = hSpeed * aimCos,
							Radius = pc.HitboxRadius, Shape = HitboxShape.Sphere,
							EndX = state.PX, EndY = state.PY, EndZ = state.PZ,
							Damage = pc.Damage, KnockbackForce = pc.KnockbackForce,
							KnockbackUpward = pc.KnockbackUpward, StunTicks = pc.StunTicks,
							DurationTicks = pc.MaxFlightTicks, OwnerId = id, Gravity = g,
							Explosion = pc.Explosion,
						});
					}
					else
					{
						// ── Melee/static hitbox ──
						float hx = state.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
						float hy = state.PY + evt.OffY;
						float hz = state.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));
						float hex = hx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
						float hey = hy + evt.EndOffY;
						float hez = hz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));
						_spellResolver.Spawn(new Hitbox
						{
							X = hx, Y = hy, Z = hz, Radius = evt.Radius, Shape = evt.Shape,
							EndX = hex, EndY = hey, EndZ = hez, Damage = evt.Damage,
							KnockbackForce = evt.KnockbackForce, KnockbackUpward = evt.KnockbackUpward,
							StunTicks = evt.StunTicks, DurationTicks = evt.DurationTicks, OwnerId = id,
						});
					}
				}
			}

			// ── Step 3: Resolve hitboxes ──
			_lastEntityList = entityList;
			foreach (var hit in _spellResolver.Tick(entityList))
			{
				if (!_states.TryGetValue(hit.TargetEntityId, out var targetState)) continue;
				float finalDamage = hit.Damage;
				targetState.DamagePercent += (ushort)finalDamage;
				if (targetState.DamagePercent > 999) targetState.DamagePercent = 999;
				Simulation.ApplyKnockback(ref targetState, hit.KnockbackX, hit.KnockbackY, hit.KnockbackZ);
				targetState.HitstunTicks = hit.StunTicks;
				_states[hit.TargetEntityId] = targetState;
			}

			// ── Step 3b: Projectile explosions (entity hit + ground impact) ──
			// Ground collision for remaining active projectiles
			_spellResolver.CheckGroundCollision(_arena.FloorHeight);

			// Spawn explosion hitboxes for all deactivated projectiles this tick
			foreach (var (ex, ey, ez, explosion, ownerId) in _spellResolver.DrainPendingExplosions())
			{
				_spellResolver.Spawn(new Hitbox
				{
					X = ex, Y = ey, Z = ez,
					Radius = explosion.Radius, Shape = HitboxShape.Sphere,
					EndX = ex, EndY = ey, EndZ = ez,
					Damage = explosion.Damage,
					KnockbackForce = explosion.KnockbackForce,
					KnockbackUpward = explosion.KnockbackUpward,
					StunTicks = explosion.StunTicks,
					DurationTicks = explosion.DurationTicks,
					OwnerId = ownerId,
				});
			}

			// ── Step 4: Void death check ──
			var deadIds = new List<ulong>();
			foreach (var kvp in _states)
				if (kvp.Value.PY < _arena.KillHeight) deadIds.Add(kvp.Key);
			foreach (var id in deadIds)
			{
				var d = _defs[id];
				var oldState = _states[id];
				_states[id] = new CharacterState
				{
					PX = _arena.SpawnPoints[0].X, PY = _arena.SpawnPoints[0].Y, PZ = _arena.SpawnPoints[0].Z,
					FacingYaw = _arena.SpawnPoints[0].Yaw,
					JumpsLeft = d.Movement.MaxJumps, AirDodgesLeft = 1,
					Deaths = (byte)(oldState.Deaths + 1), DamagePercent = 0,
				};
			}
		}
	}
}
