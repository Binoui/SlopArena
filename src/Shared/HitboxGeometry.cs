using System;

namespace SlopArena.Shared;

/// <summary>
/// Pure hitbox geometry: resolves a HitboxEvent's world-space capsule endpoints from a
/// character state + pose. Shared by the simulation (ServerAbility.SpawnHitbox), the
/// Ability Lab preview tool, and tests — one implementation so previews cannot drift
/// from what the server resolves (spec #119).
/// </summary>
public static class HitboxGeometry
{
    /// <summary>
    /// Resolve a hitbox's world-space start/end positions.
    /// When evt.BoneName is set and baked data is available, positions at the bone's
    /// world position (plus BoneOff*) instead of the fixed OffX/Y/Z entity-relative
    /// offset. Mirrors the pre-extraction logic of ServerAbility.SpawnHitbox exactly.
    /// Bone names resolve against the baked skeleton's bone set — any baked bone is
    /// attachable (the bake can carry bones the hurtbox defs don't, e.g. toes).
    /// </summary>
    /// <param name="s">Character state: position, facing yaw, and AttackElapsedTicks for the baked-frame projection.</param>
    /// <param name="evt">The hitbox event to position.</param>
    /// <param name="baked">Baked skeleton data (nullable — bone path requires it).</param>
    /// <param name="def">Character definition (bone set + scale + duration lookup).</param>
    /// <param name="animationNames">Ability's AnimationNames[] (AnimIndex selects the pose animation).</param>
    /// <param name="animIndex">Current animation index into animationNames.</param>
    /// <param name="slot">0-based ability slot (for stage duration lookup).</param>
    public static void ResolvePositions(
        in CharacterState s, in HitboxEvent evt,
        BakedAnimationData? baked, CharacterDefinition? def,
        string[]? animationNames, byte animIndex, byte slot,
        out float wx, out float wy, out float wz,
        out float wex, out float wey, out float wez)
    {
        float cos = MathF.Cos(s.FacingYaw);
        float sin = MathF.Sin(s.FacingYaw);

        bool resolved = false;
        wx = wy = wz = 0f;

        // ── Bone-attached hitbox path (requires baked data) ──
        if (evt.BoneName != null && baked != null)
        {
            // The bake holds exactly the bones the baker wrote (SlopArenaBaker's curated
            // mixamorig list: head/spine2/hips/hands/feet/toes); GetBonePosition indexes
            // BoneNames directly. HurtboxBoneDefs lookup is kept as a legacy fallback
            // for defs whose bones aren't present in the bake.
            int bi = -1;
            if (baked.BoneNames != null)
            {
                for (int i = 0; i < baked.BoneNames.Length; i++)
                {
                    if (string.Equals(baked.BoneNames[i], evt.BoneName, StringComparison.Ordinal)) { bi = i; break; }
                }
            }
            if (bi < 0 && def?.HurtboxBoneDefs != null)
            {
                for (int i = 0; i < def.HurtboxBoneDefs.Length; i++)
                {
                    if (def.HurtboxBoneDefs[i].BoneName == evt.BoneName) { bi = i; break; }
                }
            }

            if (bi >= 0)
            {
                string targetAnim = animationNames != null && animationNames.Length > animIndex
                    ? animationNames[animIndex] : "idle";
                int animIdx = baked.FindAnimIndex(targetAnim);
                if (animIdx < 0) { targetAnim = "idle"; animIdx = baked.FindAnimIndex(targetAnim); }

                if (animIdx >= 0)
                {
                    int fc = baked.Animations[animIdx].FrameCount;
                    var spec = def.GetSlotAbility(slot, airborne: false);
                    int durationTicks = (spec != null && animIndex < spec.Stages.Length)
                        ? spec.Stages[animIndex].DurationTicks
                        : 60;
                    int bakedFrame = durationTicks > 0
                        ? Math.Min(s.AttackElapsedTicks * fc / durationTicks, fc - 1)
                        : Math.Min(s.AttackElapsedTicks, fc - 1);
                    if (bakedFrame >= fc) bakedFrame = fc - 1;

                    if (baked.GetBonePosition(targetAnim, bakedFrame, bi, out float bx, out float by, out float bz))
                    {
                        float scale = def.HurtboxBoneScale;
                        bx *= scale; by *= scale; bz *= scale;
                        wx = s.PX + ((bx * cos) + (bz * sin));
                        wy = def.BoneYToWorldY(s.PY, by);
                        wz = s.PZ + ((-bx * sin) + (bz * cos));
                        wx += (evt.BoneOffX * cos) + (evt.BoneOffZ * sin);
                        wy += evt.BoneOffY;
                        wz += (-evt.BoneOffX * sin) + (evt.BoneOffZ * cos);
                        resolved = true;
                    }
                }
            }
        }

        // ── Fallback: entity-relative offset (standard path) ──
        if (!resolved)
        {
            wx = s.PX + ((evt.OffX * cos) + (evt.OffZ * sin));
            wy = s.PY + evt.OffY;
            wz = s.PZ + ((-evt.OffX * sin) + (evt.OffZ * cos));
        }

        wex = wx + ((evt.EndOffX * cos) + (evt.EndOffZ * sin));
        wey = wy + evt.EndOffY;
        wez = wz + ((-evt.EndOffX * sin) + (evt.EndOffZ * cos));
    }
}
