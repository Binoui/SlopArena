#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    public static class SlopArenaTrainingCommandsSelfTest
    {
        [MenuItem("Tools/SlopArena/Tests/Training Sequence Parser")]
        public static void Run()
        {
            Require(SlopArenaTrainingCommands.ToWireYaw(Mathf.PI * 0.5f) == 9000
                && SlopArenaTrainingCommands.ToWireYaw(-Mathf.PI * 0.5f) == -9000,
                "Radian facing yaw was not encoded as signed degrees × 100.");
            Require(SlopArenaTrainingCommands.ToWireYaw(Mathf.PI * 1.5f) == -9000,
                "Facing yaw was not canonicalized to the signed [-18000, 18000] wire range.");
            var slotMappings = new[]
            {
                ("1", AbilitySlots.Slot1), ("2", AbilitySlots.Slot2),
                ("3", AbilitySlots.Slot3), ("4", AbilitySlots.Slot4),
                ("A", AbilitySlots.A), ("E", AbilitySlots.E),
                ("R", AbilitySlots.R), ("F", AbilitySlots.F)
            };
            foreach (var mapping in slotMappings)
                Require(SlopArenaTrainingCommands.MapSlot(mapping.Item1) == mapping.Item2,
                    $"Canonical slot '{mapping.Item1}' did not map to its AbilitySlots wire constant.");

            var cookedSlots = new List<CookedSlotDefinition>();
            foreach (var address in CanonicalSlotProjection.All)
                cookedSlots.Add(new CookedSlotDefinition(
                    address.Ordinal, address.Id, address.IsAirborne, address.InputLabel,
                    "", "", default, default, 0, false, false, null));
            var definition = new CharacterDefinition { CookedSlots = cookedSlots };
            foreach (var mapping in slotMappings)
            {
                byte wire = SlopArenaTrainingCommands.MapSlot(mapping.Item1);
                Require(definition.GetCookedSlotAbility(wire, false)?.Id == "ground." + mapping.Item1
                    && definition.GetCookedSlotAbility(wire, true)?.Id == "air." + mapping.Item1,
                    $"Canonical slot '{mapping.Item1}' does not resolve to the matching cooked ground/air ability.");
            }

            var steps = SlopArenaTrainingCommands.ParseSteps(
                "[{\"ticks\":2,\"slot\":\"1\",\"jumpPressed\":true,\"jumpHeld\":true," +
                "\"downPressed\":true,\"downHeld\":true,\"shieldPressed\":true,\"shieldHeld\":true," +
                "\"grabPressed\":true,\"faceToCamera\":true,\"toggleLock\":true,\"retargetPressed\":true}]");
            var firstTick = SlopArenaTrainingCommands.BuildInput(steps[0], true);
            var secondTick = SlopArenaTrainingCommands.BuildInput(steps[0], false);
            Require(firstTick.ActiveSlot == AbilitySlots.Slot1 && firstTick.Jump && firstTick.JumpHeld
                && firstTick.DownPressed && firstTick.Down && firstTick.ShieldPressed && firstTick.ShieldHeld
                && firstTick.GrabPressed && firstTick.FaceToCamera && firstTick.ToggleLock && firstTick.RetargetPressed,
                "The first tick did not carry the requested edge and held inputs.");
            Require(secondTick.ActiveSlot == AbilitySlots.None && !secondTick.Jump && secondTick.JumpHeld
                && !secondTick.DownPressed && secondTick.Down && !secondTick.ShieldPressed && secondTick.ShieldHeld
                && !secondTick.GrabPressed && !secondTick.FaceToCamera && !secondTick.ToggleLock && !secondTick.RetargetPressed,
                "One-tick edges were repeated or held inputs were dropped after the step's first tick.");
            var nextStep = SlopArenaTrainingCommands.ParseSteps(
                "[{\"ticks\":2,\"jumpPressed\":true},{\"ticks\":1,\"jumpPressed\":true}]")[1];
            Require(SlopArenaTrainingCommands.BuildInput(nextStep, true).Jump
                && !SlopArenaTrainingCommands.BuildInput(nextStep, false).Jump,
                "A fresh step must emit its own edge once, even when the previous step also pressed it.");

            Require(!SlopArenaTrainingCommands.HasConsumedStepTicks(2, 1)
                && SlopArenaTrainingCommands.HasConsumedStepTicks(2, 2)
                && !SlopArenaTrainingCommands.HasConsumedStepTicks(2, 3),
                "A sequence step must transition only after exactly its requested number of simulation ticks.");
            Require(SlopArenaTrainingCommands.ParseSteps("[{\"ticks\":600}]")[0].Ticks == 600,
                "The 600-tick total boundary must be accepted.");
            ExpectInvalid("[]");
            ExpectInvalid("[{\"ticks\":0}]");
            ExpectInvalid("[{\"ticks\":-1}]");
            ExpectInvalid("[{\"ticks\":601}]");
            ExpectInvalid("[{\"ticks\":600},{\"ticks\":1}]");
            ExpectInvalid("[{\"ticks\":1.5}]");
            ExpectInvalid("[{\"ticks\":true}]");
            ExpectInvalid("[{\"ticks\":\"1\"}]");
            ExpectInvalid("[{\"ticks\":1,\"jumpPressed\":\"true\"}]");
            ExpectInvalid("[{\"ticks\":1,\"moveX\":\"0.5\"}]");
            ExpectInvalid("[{\"ticks\":1,\"aimYaw\":1.5}]");
            ExpectInvalid("[{\"ticks\":1,\"targetEntityId\":100}]");
            ExpectInvalid("[{\"ticks\":1,\"slot\":1}]");
            ExpectInvalid("[{\"ticks\":1,\"lockMode\":\"1\"}]");
            ExpectInvalid("[{\"ticks\":1,\"slot\":\"warp\"}]");
            ExpectInvalid("[{\"ticks\":1,\"burst\":true}]");
            ExpectInvalid("[{\"ticks\":1,\"warpTargetX\":0}]");
            ExpectInvalid("{\"ticks\":1}");

            Debug.Log("[SlopArenaTrainingCommandsSelfTest] Passed cooked slot/wire projection, radian yaw encoding, per-step edge/held boundaries, strict typed JSON fields, positive durations, 600-tick boundary and retired-control rejection.");
        }

        private static void ExpectInvalid(string json)
        {
            try
            {
                SlopArenaTrainingCommands.ParseSteps(json);
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new InvalidOperationException("Expected invalid Training sequence JSON to be rejected: " + json);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
