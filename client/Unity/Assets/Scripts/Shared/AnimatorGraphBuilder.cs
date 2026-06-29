using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

public static class AnimatorGraphBuilder
{
    public static AnimatorGraph Build(CharacterDefinition def)
    {
        var states = new List<GraphState>();
        var directs = new List<GraphDirectTransition>();
        var anys = new List<GraphAnyTransition>();

        // ── 1. Default states ──
        states.Add(new GraphState { Name = "Movement", MotionName = "MovementBlend", IsBlendTree = true,
            WriteDefaultValues = false, IsDefault = true, PositionX = 250, PositionY = 100, AutoExit = false });
        states.Add(new GraphState { Name = "Jump", MotionName = "jump", PositionX = 250, PositionY = 250, AutoExit = false });
        states.Add(new GraphState { Name = "Fall", MotionName = "fall", PositionX = 400, PositionY = 250, AutoExit = false });
        states.Add(new GraphState { Name = "Dash", MotionName = "dash", PositionX = 250, PositionY = 350, AutoExit = true });
        states.Add(new GraphState { Name = "Hitstun", MotionName = "hit_small", PositionX = 250, PositionY = 450, AutoExit = true });

        // Default direct transitions
        directs.Add(new GraphDirectTransition { FromState = "Movement", ToState = "Jump",
            Conditions = new[] { new GraphCondition { Parameter = "Jump", Mode = AnimConditionMode.If } }, Duration = 0.05f });
        directs.Add(new GraphDirectTransition { FromState = "Movement", ToState = "Fall",
            Conditions = new[] { new GraphCondition { Parameter = "IsGrounded", Mode = AnimConditionMode.IfNot } }, Duration = 0.1f });
        directs.Add(new GraphDirectTransition { FromState = "Jump", ToState = "Fall",
            Conditions = new[] { new GraphCondition { Parameter = "IsGrounded", Mode = AnimConditionMode.IfNot } },
            Duration = 0.1f, HasExitTime = true, ExitTime = 0.3f });
        directs.Add(new GraphDirectTransition { FromState = "Fall", ToState = "Movement",
            Conditions = new[] { new GraphCondition { Parameter = "IsGrounded", Mode = AnimConditionMode.If } }, Duration = 0.1f });

        // Default AnyState transitions
        anys.Add(new GraphAnyTransition { ToState = "Dash",
            Conditions = new[] { new GraphCondition { Parameter = "Dash", Mode = AnimConditionMode.If } },
            Duration = 0f, InterruptionSource = true });
        anys.Add(new GraphAnyTransition { ToState = "Hitstun",
            Conditions = new[] { new GraphCondition { Parameter = "Hitstun", Mode = AnimConditionMode.If } },
            Duration = 0f, InterruptionSource = true });
        anys.Add(new GraphAnyTransition { ToState = "Movement",
            Conditions = new[] { new GraphCondition { Parameter = "Idle", Mode = AnimConditionMode.If } },
            Duration = 0f, InterruptionSource = true });
        var directSeen = new HashSet<(string From, string To)>();

        // ── 2. Ability states ──
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ax = 500, ay = 50;
        const int stepY = 40;

        for (int slot = 0; slot < 6; slot++)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var spec = def.GetSlotAbility(slot, airborne);
                if (spec?.AnimationNames == null) continue;
                bool hasHold = spec.ChargeHoldTicks > 0 && spec.AnimationNames.Length >= 3;
                for (int stage = 0; stage < spec.AnimationNames.Length; stage++)
                {
                    string animName = spec.AnimationNames[stage];
                    if (string.IsNullOrEmpty(animName) || !seen.Add(animName)) continue;

                    bool isHoldLoop = hasHold && stage == 1;
                    bool autoExit = !hasHold || stage >= 2;

                    states.Add(new GraphState
                    {
                        Name = animName,
                        MotionName = animName,
                        PositionX = ax,
                        PositionY = ay,
                        AutoExit = autoExit,
                        IsHoldLoop = isHoldLoop,
                    });
                    ay += stepY;
                }
            }
        }

        // ── 3. Hold-phase direct transitions ──
        for (int slot = 0; slot < 6; slot++)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var spec = def.GetSlotAbility(slot, airborne);
                if (spec?.AnimationNames == null || spec.AnimationNames.Length < 3) continue;
                if (spec.ChargeHoldTicks <= 0) continue;
                if (!directSeen.Add((spec.AnimationNames[0], spec.AnimationNames[1]))) continue;
                directs.Add(new GraphDirectTransition
                {
                    FromState = spec.AnimationNames[0],
                    ToState = spec.AnimationNames[1],
                    Conditions = Array.Empty<GraphCondition>(),
                    Duration = 0.15f,
                    HasExitTime = true,
                    ExitTime = 0.9f,
                });

                // loop → throw (ComboStage == 1)
                if (!directSeen.Add((spec.AnimationNames[1], spec.AnimationNames[2]))) continue;
                directs.Add(new GraphDirectTransition
                {
                    FromState = spec.AnimationNames[1],
                    ToState = spec.AnimationNames[2],
                    Conditions = new[] { new GraphCondition { Parameter = "ComboStage", Mode = AnimConditionMode.Equals, Threshold = 1 } },
                    Duration = 0.05f,
                });
            }
        }

        // ── 4. Combo chain direct transitions (non-hold only) ──
        for (int slot = 0; slot < 6; slot++)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var spec = def.GetSlotAbility(slot, airborne);
                if (spec?.AnimationNames == null || spec.AnimationNames.Length < 2) continue;
                if (spec.ChargeHoldTicks > 0 && spec.AnimationNames.Length >= 3) continue; // hold-phase: skip

                for (int stage = 0; stage < spec.AnimationNames.Length - 1; stage++)
                {
                    string fromName = spec.AnimationNames[stage];
                    string toName = spec.AnimationNames[stage + 1];
                    if (fromName == toName) continue;
                    if (!directSeen.Add((fromName, toName))) continue;

                    directs.Add(new GraphDirectTransition
                    {
                        FromState = fromName,
                        ToState = toName,
                        Conditions = new[] { new GraphCondition { Parameter = "ComboStage", Mode = AnimConditionMode.Equals, Threshold = stage + 1 } },
                        Duration = 0f,
                    });
                }
            }
        }

        // ── 5. Ability AnyState transitions ──
        var seen2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int slot = 0; slot < 6; slot++)
        {
            foreach (bool airborne in new[] { false, true })
            {
                var spec = def.GetSlotAbility(slot, airborne);
                if (spec?.AnimationNames == null) continue;
                bool hasHold = spec.ChargeHoldTicks > 0 && spec.AnimationNames.Length >= 3;
                for (int stage = 0; stage < spec.AnimationNames.Length; stage++)
                {
                    // Skip hold-loop states (reached via direct transition, not AnyState)
                    if (hasHold && stage == 1) continue;

                    string animName = spec.AnimationNames[stage];
                    if (string.IsNullOrEmpty(animName) || !seen2.Add(animName)) continue;

                    int effectiveStage = (hasHold && stage >= 2) ? 1 : stage;

                    anys.Add(new GraphAnyTransition
                    {
                        ToState = animName,
                        Conditions = new[]
                        {
                            new GraphCondition { Parameter = "Attack", Mode = AnimConditionMode.If },
                            new GraphCondition { Parameter = "AttackSlot", Mode = AnimConditionMode.Equals, Threshold = slot },
                            new GraphCondition { Parameter = "ComboStage", Mode = AnimConditionMode.Equals, Threshold = effectiveStage },
                        },
                        Duration = 0f,
                        InterruptionSource = true,
                    });
                }
            }
        }

        // ── 6. Clip overrides (if any) ──
        if (def.ClipOverrides != null)
        {
            foreach (var ov in def.ClipOverrides)
            {
                if (string.IsNullOrEmpty(ov.Name) || !seen.Add(ov.Name)) continue;
                states.Add(new GraphState
                {
                    Name = ov.Name,
                    MotionName = ov.Name,
                    PositionX = ax,
                    PositionY = ay,
                    AutoExit = true,
                });
                ay += stepY;
            }
        }

        return new AnimatorGraph
        {
            DefaultStateName = "Movement",
            States = states,
            DirectTransitions = directs,
            AnyTransitions = anys,
        };
    }
}
