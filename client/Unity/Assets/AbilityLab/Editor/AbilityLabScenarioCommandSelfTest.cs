using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Tools;

namespace SlopArena.EditorTools;

public static class AbilityLabScenarioCommandSelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Ability Lab Scenario Commands")]
    public static void Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Ability Lab scenario command self-test requires Edit Mode.");

        AbilityLabWindow? window = AbilityLabWindow.FindExistingForCommand();
        if (window == null || !window.CommandWorkspace.HasPackage || window.CommandWorkspace.LiveDraftInvalid ||
            window.CommandWorkspace.Preview?.IsAvailable != true)
            throw new InvalidOperationException("Open a valid Character Package in Ability Lab before running this self-test.");
        AbilityLab? lab = window.CommandLab;
        if (lab == null || !lab.IsPackagePreview || lab.SelectedPackageId != window.CommandWorkspace.PackageId)
            throw new InvalidOperationException("Ability Lab package preview rig is unavailable.");

        var inspection = SlopArenaAbilityLabCommands.Inspect();
        string? action = inspection.Slots?.Find(slot => slot.Available)?.Id;
        if (action == null)
            throw new InvalidOperationException("The open package has no available canonical action to simulate.");

        var originalCursor = lab.CaptureTimelineCursor();
        var originalCamera = lab.CaptureCameraState();
        bool originalHurtboxes = lab.ShowHurtboxes;
        bool originalHitboxes = lab.ShowHitboxes;
        bool originalBakedBones = lab.ShowBakedBones;
        bool originalDummy = lab.ShowDummy;
        Camera? previewCamera = lab.PreviewCamera;
        Vector3 cameraPosition = previewCamera != null ? previewCamera.transform.position : default;
        Quaternion cameraRotation = previewCamera != null ? previewCamera.transform.rotation : default;
        RenderTexture? cameraTarget = previewCamera != null ? previewCamera.targetTexture : null;
        float cameraAspect = previewCamera != null ? previewCamera.aspect : 0f;
        bool hadCamera = previewCamera != null;
        string output = $".ability-lab-cache/scenario-command-selftest-{Guid.NewGuid():N}";
        string? outputDirectory = null;
        try
        {
            var run = SlopArenaAbilityLabCommands.Run(action, ticks: 24, distance: 0.5f,
                opponent: "idle", damage: 0, facing: 180f);

            AbilityLabCommandScenario runScenario = run.Scenario ?? throw new InvalidOperationException("Native run returned no scenario data.");
            if (!run.Success || runScenario.Action != action ||
                runScenario.Frames.Count != 25 || runScenario.Frames[0].FrameIndex != 0 ||
                runScenario.Frames[0].MatchTick != 1 || run.ScenarioFrame != 24)
                throw new InvalidOperationException("Native run did not return the actual frame-zero/MatchTick-one Shared scenario and leave preview at its last frame.");
            AbilityLabScenarioResult recorded = lab.Scenario ?? throw new InvalidOperationException("Native run was not retained by Ability Lab.");
            if (recorded.Frames.Count != runScenario.Frames.Count || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Native run result does not match the live recorded scenario.");
            if (recorded.Contacts.Any(contact => !contact.Hit.Blocked))
            {
                if (!runScenario.Contacts.Any(contact => !contact.Blocked && contact.Damage > 0f)
                    || !runScenario.Frames.Any(frame => frame.Opponent.Damage > 0))
                    throw new InvalidOperationException("Native hit verdict omitted contact or applied opponent damage.");
            }

            var beforeInvalid = lab.CaptureTimelineCursor();
            AbilityLabScenarioResult beforeInvalidScenario = lab.Scenario ?? throw new InvalidOperationException("Recorded scenario disappeared.");
            var invalid = SlopArenaAbilityLabCommands.Run(action, ticks: AbilityLabScenarioOptions.MaxFrame + 1);
            if (invalid.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Out-of-range scenario input succeeded or mutated the live scenario/cursor.");
            var invalidOpponent = SlopArenaAbilityLabCommands.Run(action, opponent: "other");
            if (invalidOpponent.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Invalid opponent input mutated the recorded scenario/cursor.");
            var invalidDistance = SlopArenaAbilityLabCommands.Run(action, distance: -0.01f);
            if (invalidDistance.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario) || lab.ScenarioFrame != 24)
                throw new InvalidOperationException("Invalid opponent distance mutated the recorded scenario/cursor.");
            lab.RestoreTimelineCursor(beforeInvalid);
            SlopArena.Shared.CanonicalSlotProjection.TryGet(action, out var address);
            int index = Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
            var spec = lab.Def.GetSlotAbility(AbilityLab.SlotIndices[index], address.IsAirborne);
            var originalNames = spec.AnimationNames;
            try
            {
                spec.AnimationNames = new[] { "selftest.missing.animation" };
                var missingBinding = SlopArenaAbilityLabCommands.Run(action, ticks: 12);
                if (missingBinding.Success || !ReferenceEquals(beforeInvalidScenario, lab.Scenario)
                    || lab.ScenarioFrame != 24)
                    throw new InvalidOperationException("A missing animation binding replaced the active run before failing.");
            }
            finally { spec.AnimationNames = originalNames; }

            var preview = SlopArenaAbilityLabCommands.Preview(action, 4);
            if (!preview.Success || lab.ScenarioFrame != 4 || preview.ScenarioFrame != 4)
                throw new InvalidOperationException("Native preview did not seek the recorded scenario frame.");

            if (lab.PreviewCamera == null)
                throw new InvalidOperationException("Ability Lab camera is unavailable for capture restoration coverage.");
            outputDirectory = SlopArenaAbilityLabCommands.TryResolveCaptureDirectory(output, out string resolved, out string error)
                ? resolved : throw new InvalidOperationException(error);
            lab.ShowHurtboxes = !originalHurtboxes;
            lab.ShowHitboxes = !originalHitboxes;
            lab.ShowBakedBones = !originalBakedBones;
            lab.ShowDummy = !originalDummy;
            var capture = SlopArenaAbilityLabCommands.Capture(action, "0,4", output, 320, 240);
            if (!capture.Success || capture.Captures?.Count != 2 || capture.Captures[0].Frame?.FrameIndex != 0 ||
                capture.Captures[0].MatchTick != 1 || capture.Captures[1].Frame?.FrameIndex != 4 ||
                !ReferenceEquals(recorded, lab.Scenario) || lab.ScenarioFrame != 4)
                throw new InvalidOperationException("Capture did not report recorded scenario frames or restore its scenario cursor.");
            if (lab.ShowHurtboxes != !originalHurtboxes || lab.ShowHitboxes != !originalHitboxes ||
                lab.ShowBakedBones != !originalBakedBones || lab.ShowDummy != !originalDummy)
                throw new InvalidOperationException("Capture changed Ability Lab visibility before caller-state restoration.");
            if (hadCamera && (lab.PreviewCamera == null || lab.PreviewCamera.transform.position != cameraPosition ||
                              lab.PreviewCamera.transform.rotation != cameraRotation ||
                              lab.PreviewCamera.targetTexture != cameraTarget || lab.PreviewCamera.aspect != cameraAspect))
                throw new InvalidOperationException("Capture did not restore the camera transform, target, and aspect.");
            AssertDistinctImages(capture.Captures[0].Path, capture.Captures[1].Path);
            var shortRun = SlopArenaAbilityLabCommands.Run(action, ticks: 12, distance: 9f,
                opponent: "shield", damage: 7, facing: 90f);
            if (!shortRun.Success) throw new InvalidOperationException("Cross-action fixture could not run.");
            var beforeCross = lab.Scenario;
            var cross = SlopArenaAbilityLabCommands.Capture("grab", "19", output + "/cross", 320, 240);
            if (!cross.Success || cross.Captures[0].Frame?.FrameIndex != 19
                || !ReferenceEquals(beforeCross, lab.Scenario) || lab.ScenarioFrame != 12)
                throw new InvalidOperationException("Default grab capture inherited another action's horizon or failed restoration.");
            var grabRun = SlopArenaAbilityLabCommands.Run("grab", ticks: 24, distance: 0.7f);
            if (!grabRun.Success || !grabRun.Scenario.Interactions.Any(observation => observation.Kind == "capture")
                || !grabRun.Scenario.Interactions.Any(observation => observation.Kind == "release")
                || !grabRun.Scenario.Frames.Any(frame => frame.Opponent.Damage > 0))
                throw new InvalidOperationException("Native grab verdict lost observed capture, release or damage.");
            Debug.Log("[AbilityLabScenarioCommandSelfTest] Passed native run/seek/capture, frame chronology, invalid-input non-mutation and complete restoration.");
        }
        finally
        {
            lab.RestoreTimelineCursor(originalCursor);
            lab.RestoreCameraState(originalCamera);
            lab.ShowHurtboxes = originalHurtboxes;
            lab.ShowHitboxes = originalHitboxes;
            lab.ShowBakedBones = originalBakedBones;
            lab.ShowDummy = originalDummy;
            window.RefreshScenarioControls();
            if (outputDirectory != null && Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, true);
        }
    }

    private static void AssertDistinctImages(string firstPath, string secondPath)
    {
        string root = Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", ".."));
        var first = new Texture2D(2, 2);
        var second = new Texture2D(2, 2);
        try
        {
            if (!first.LoadImage(File.ReadAllBytes(Path.Combine(root, firstPath)))
                || !second.LoadImage(File.ReadAllBytes(Path.Combine(root, secondPath))))
                throw new InvalidOperationException("Native capture did not produce decodable PNGs.");
            var a = first.GetPixels32();
            var b = second.GetPixels32();
            bool distinct = false;
            for (int i = 0; i < a.Length && !distinct; i++) distinct = !a[i].Equals(b[i]);
            if (!distinct) throw new InvalidOperationException("Distinct recorded poses produced identical captured pixels.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }
}
