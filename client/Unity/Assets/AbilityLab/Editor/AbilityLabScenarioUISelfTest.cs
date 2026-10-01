using System;
using System.Reflection;
using SlopArena.Client.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public static class AbilityLabScenarioUISelfTest
{
    [MenuItem("Tools/SlopArena/Tests/Ability Lab Scenario Controls")]
    public static void Run()
    {
        var window = AbilityLabWindow.FindExistingForCommand();
        if (window == null || !window.EnsureCommandUi() || window.CommandLab?.CanPreviewGrab != true)
            throw new InvalidOperationException("Open a valid package in Ability Lab first.");
        var lab = window.CommandLab;
        var cursor = lab.CaptureTimelineCursor();
        var draft = window.CommandWorkspace.Draft;
        bool dirty = window.CommandWorkspace.IsDirty;
        var root = window.rootVisualElement;
        try
        {
            Click(root.Q<Button>("selected-grab"));
            if (lab.Scenario?.Options.Action != "grab" || lab.ScenarioFrame != 0)
                throw new InvalidOperationException("Grab selection did not enter recorded Shared playback.");
            var slider = root.Q<SliderInt>("timeline-slider");
            if (!slider.enabledSelf) throw new InvalidOperationException("Grab scenario is not scrubbable.");
            slider.value = 7;
            var expected = lab.Scenario.Frames[7];
            if (lab.ScenarioFrame != 7 || lab.Renderer.CurrentActionState != expected.Actor.State
                || lab.DummyRenderer.CurrentActionState != expected.Opponent.State)
                throw new InvalidOperationException("UI scrubbing left a fighter in another frame's action state.");
            slider.value = 0;
            if (lab.ScenarioFrame != 0 || lab.Renderer.CurrentActionState != SlopArena.Shared.ActionState.GrabAttempt)
                throw new InvalidOperationException("Backward scrubbing did not rewind actual grab presentation.");
            Click(root.Q<Button>("scenario-exit"));
            if (lab.IsScenarioPreview || !root.Q<Button>("selected-ground-1").enabledSelf)
                throw new InvalidOperationException("Exit did not restore canonical authoring controls.");
            if (!ReferenceEquals(draft, window.CommandWorkspace.Draft) || window.CommandWorkspace.IsDirty != dirty)
                throw new InvalidOperationException("Scenario controls changed the draft or dirty state.");
            lab.RunScenario(new AbilityLabScenarioOptions("grab", 24, 0.7f, AbilityLabOpponentBehavior.Shield));
            lab.SeekScenario(0);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") == null)
                throw new InvalidOperationException("Recorded Shielding state has no actual guard visual.");
            lab.SeekScenario(7);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") != null)
                throw new InvalidOperationException("Grabbed state retained the prior shield visual.");
            lab.SeekScenario(0);
            if (lab.DummyRenderer.transform.Find("ShieldGuard") == null)
                throw new InvalidOperationException("Backward replay did not restore the actual shield visual.");
            var observed = lab.Scenario;
            var outcomeLabel = root.Q<Label>("scenario-outcomes");
            window.RefreshScenarioControls();
            foreach (var observation in observed.Interactions)
                if (!outcomeLabel.text.Contains($"{observation.Kind} frame {observation.FrameIndex}", StringComparison.Ordinal))
                    throw new InvalidOperationException("Scenario panel omitted an observed coupled-interaction outcome.");
            Debug.Log("[AbilityLabScenarioUISelfTest] Passed actual Grab selection, fighter-state scrubbing, rewind, authoring exit and draft preservation.");
        }
        finally
        {
            lab.RestoreTimelineCursor(cursor);
            window.RefreshScenarioControls();
        }
    }

    private static void Click(Button button)
    {
        if (button == null || !button.enabledSelf) throw new InvalidOperationException("Required scenario control is unavailable.");
        typeof(Clickable).GetMethod("SimulateSingleClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(button.clickable, new object[] { null, 0 });
    }
}
