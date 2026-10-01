using System;
using System.Linq;
using SlopArena.Client.Tools;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools;

public static class AbilityLabScenarioSelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Ability Lab Scenario Outcomes")]
    public static void Run()
    {
        var definition = Fixture();
        var controller = new AbilityLabSimulationController();
        Vector3 origin = new(0f, definition.CapsuleHeight * 0.5f, 0f);
        AbilityLabScenarioResult Sim(string action, float distance, AbilityLabOpponentBehavior behavior = AbilityLabOpponentBehavior.Idle)
            => controller.RunScenario(definition, null, new AbilityLabScenarioOptions(action, 48, distance, behavior), origin, 0f);

        var hit = Sim("ground.1", 1.2f);
        Require(hit.Contacts.Count == 1 && !hit.Contacts[0].Hit.Blocked, "Cooked move did not produce one accepted contact.");
        Require(hit.Frames.Any(frame => frame.Opponent.DamagePercent == 8 && frame.Opponent.State == ActionState.Hitstun),
            "Accepted hit did not apply fixture damage and Hitstun.");
        Require(hit.Frames.Any(frame => frame.Opponent.KVZ > 0f || frame.Opponent.VZ > 0f), "Recorded run lost the actual post-Hitstop launch.");
        Require(hit.Frames[0].FrameIndex == 0 && hit.Frames[0].MatchTick == 1
            && hit.Contacts[0].Hit.MatchTick == hit.Contacts[0].FrameIndex + 1, "Frame and Shared MatchTick are ambiguous.");
        for (int index = 1; index < hit.Frames.Count; index++)
            if ((hit.Frames[index].Opponent.HitstopTicks > 0 || hit.Frames[index - 1].Opponent.HitstopTicks > 0)
                && hit.Frames[index - 1].Opponent.State == ActionState.Hitstun)
                Require(hit.Frames[index].OpponentPoseTicks == hit.Frames[index - 1].OpponentPoseTicks, "Hitstop advanced the recorded pose clock.");

        var miss = Sim("ground.1", 12f);
        Require(miss.Contacts.Count == 0 && miss.Frames.All(frame => frame.Opponent.DamagePercent == 0), "Out-of-reach target was hit.");
        var block = Sim("ground.1", 1.2f, AbilityLabOpponentBehavior.Shield);
        Require(block.Contacts.Count == 1 && block.Contacts[0].Hit.Blocked && block.Contacts[0].Hit.Damage == 0f,
            "Held shield did not produce an authoritative zero-damage block.");
        Require(block.Frames.All(frame => frame.Opponent.DamagePercent == 0)
            && block.Frames.Any(frame => frame.Opponent.BlockStunTicks > 0), "Block damaged the opponent or lost Blockstun.");
        var collisionFrame = block.Frames[block.Contacts[0].FrameIndex];
        Require(collisionFrame.Hurtboxes.Any(shape => shape.Id == 2 && shape.ShieldSurface)
            && !collisionFrame.Hurtboxes.Any(shape => shape.Id == 2 && !shape.ShieldSurface),
            "Recorded blocked contact showed raw body hurtboxes instead of Shared's shield surface.");

        var captured = Sim("grab", 1.2f);
        Require(captured.Interactions.Count == 2 && captured.Interactions[0].Kind == "capture"
            && captured.Interactions[1].Kind == "release", "Grab did not record paired capture and automatic release.");
        var captureFrame = captured.Frames[captured.Interactions[0].FrameIndex];
        Require(captureFrame.Actor.State == ActionState.Throwing && captureFrame.Opponent.State == ActionState.Grabbed
            && captureFrame.Actor.InteractionId == captureFrame.Opponent.InteractionId, "Capture introduced a hold phase or lost linkage.");
        var releaseFrame = captured.Frames[captured.Interactions[1].FrameIndex];
        Require(releaseFrame.Actor.InteractionId == 0 && releaseFrame.Opponent.InteractionId == 0
            && releaseFrame.Opponent.DamagePercent > captureFrame.Opponent.DamagePercent
            && releaseFrame.Opponent.State == ActionState.Hitstun, "Automatic release lost terminal linkage, damage or launch state.");
        Require(captured.Interactions[1].FrameIndex - captured.Interactions[0].FrameIndex == DefenseConfig.ThrowReleaseTicks,
            "Scenario orchestration changed Shared release timing.");
        Require(captured.Contacts.Count == 0, "Grab invented a hitbox contact record.");
        var shieldGrab = Sim("grab", 1.2f, AbilityLabOpponentBehavior.Shield);
        Require(shieldGrab.Interactions.Any(observation => observation.Kind == "capture"), "Shield incorrectly blocked grab.");
        var whiff = Sim("grab", 12f);
        Require(whiff.Interactions.Count == 0 && whiff.Frames.All(frame => frame.Opponent.DamagePercent == 0)
            && whiff.Frames[whiff.Frames.Count - 1].Actor.State == ActionState.Idle, "Distant grab did not whiff and recover.");

        var replay = Sim("grab", 1.2f);
        for (int index = captured.Frames.Count - 1; index >= 0; index--)
            Require(captured.Frames[index].Actor.Equals(replay.Frames[index].Actor)
                && captured.Frames[index].Opponent.Equals(replay.Frames[index].Opponent), "Re-recording changed a prior Shared frame.");
        ExpectInvalid(new AbilityLabScenarioOptions("grab", AbilityLabScenarioOptions.MaxFrame + 1));
        ExpectInvalid(new AbilityLabScenarioOptions("unknown"));
        ExpectInvalid(new AbilityLabScenarioOptions("grab", distance: float.NaN));
        Debug.Log("[AbilityLabScenarioSelfTest] Passed cooked hit/miss/block, grab capture/shield/whiff/release, deterministic replay, pose clocks and invalid requests.");
    }

    private static CharacterDefinition Fixture()
    {
        var hitbox = new CookedHitbox(AuthoringHitboxShape.Sphere, 0.45f,
            0f, 0f, 1f, 0f, 0f, 0f, null, null, 8f, 45f, 20f, 0f, 12, 1, true, 0);
        var slot = new CookedSlotDefinition(0, "ground.1", false, "Scenario fixture", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(12, 0, 0, 0, 0, Array.Empty<string>(),
                    new CookedTimelineOperation[] { new CookedSpawnHitboxOperation(2, AuthoringUnit.Meters, hitbox) }),
            }));
        return new CharacterDefinition
        {
            Class = CharacterClass.None,
            CapsuleHeight = 1.7f,
            CapsuleRadius = 0.35f,
            ShieldRadius = 1.05f,
            Weight = 100f,
            HurtboxCapsules = new[] { new HurtboxCapsule(0f, -0.6f, 0f, 0f, 0.6f, 0f, 0.35f) },
            CaptureGeometry = new CookedCaptureGeometry(1.5f, 1f, 1.5f, 0f,
                new CaptureAnchor(0f, 0f, 0.2f), new CaptureAnchor(0f, 0f, 0.65f)),
            CookedSlots = new[] { slot },
            // Cooked execution and public metadata are both present in runtime definitions.
            Slot1 = new AbilitySpec
            {
                AnimationNames = Array.Empty<string>(),
                Stages = new[] { new AttackStage { DurationTicks = 12 } },
            },
        };
    }

    private static void ExpectInvalid(AbilityLabScenarioOptions options)
    {
        try { options.Validate(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid scenario options were accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
