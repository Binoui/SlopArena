using SlopArena.Client.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlopArena.EditorTools;

public sealed partial class AbilityLabWindow
{
    private IMGUIContainer? _movesViewport;
    private RenderTexture? _movesViewportTarget;
    private Label? _movesSceneStatus;
    private Toggle? _sceneHitboxes;
    private Toggle? _sceneHurtboxes;
    private Toggle? _sceneMotion;
    private EventCallback<ChangeEvent<bool>>? _hitboxesChanged;
    private EventCallback<ChangeEvent<bool>>? _hurtboxesChanged;
    private EventCallback<ChangeEvent<bool>>? _motionChanged;

    private void BindMovesViewport()
    {
        ReleaseMovesViewport();
        _movesViewport = _root.Q<IMGUIContainer>("move-scene-viewport");
        _movesSceneStatus = _root.Q<Label>("scene-status");
        _sceneHitboxes = _root.Q<Toggle>("scene-hitboxes");
        _sceneHurtboxes = _root.Q<Toggle>("scene-hurtboxes");
        _sceneMotion = _root.Q<Toggle>("scene-motion");
        if (_movesViewport == null || _movesSceneStatus == null || _sceneHitboxes == null ||
            _sceneHurtboxes == null || _sceneMotion == null)
        {
            if (_movesSceneStatus != null) _movesSceneStatus.text = "Scene viewport controls are unavailable.";
            return;
        }

        _movesViewport.onGUIHandler = DrawMovesViewport;
        _hitboxesChanged = evt => SetSceneOverlay(hitboxes: evt.newValue);
        _hurtboxesChanged = evt => SetSceneOverlay(hurtboxes: evt.newValue);
        _motionChanged = evt => SetSceneOverlay(motion: evt.newValue);
        _sceneHitboxes.RegisterValueChangedCallback(_hitboxesChanged);
        _sceneHurtboxes.RegisterValueChangedCallback(_hurtboxesChanged);
        _sceneMotion.RegisterValueChangedCallback(_motionChanged);
        RefreshMovesViewport();
    }

    private void RefreshMovesViewport()
    {
        if (_movesSceneStatus == null) return;
        if (!_workspace.HasPackage)
        {
            _movesSceneStatus.text = "Select a package to view the scene.";
        }
        else if (_lab == null)
        {
            _movesSceneStatus.text = "No Ability Lab rig or preview camera. Open a package preview to view the scene.";
        }
        else if (_workspace.LiveDraftInvalid)
        {
            _movesSceneStatus.text = "Draft invalid · scene preview unavailable.";
        }
        else if (_lab.PreviewCamera == null)
        {
            _movesSceneStatus.text = "Preview camera unavailable.";
        }
        else if (_lab.IsScenarioPreview)
        {
            _movesSceneStatus.text = $"Recorded Shared scenario · frame {_lab.ScenarioFrame}";
        }
        else if (_lab.PhasePreviewActive)
        {
            _movesSceneStatus.text = "Presentation-only phase preview";
        }
        else if (!_lab.IsPackagePreview)
        {
            string status = string.IsNullOrWhiteSpace(_lab.PreviewStatus)
                ? "No package preview is available."
                : $"No package preview · {_lab.PreviewStatus}";
            _movesSceneStatus.text = _lab.PreviewCamera == null
                ? $"{status} · Preview camera unavailable."
                : status;
        }
        else
        {
            _movesSceneStatus.text = _lab.PreviewStatus;
        }

        bool canDraw = _workspace.HasPackage && !_workspace.LiveDraftInvalid && _lab?.PreviewCamera != null &&
            (_lab.IsPackagePreview || _lab.IsScenarioPreview || _lab.PhasePreviewActive);
        _sceneHitboxes?.SetEnabled(canDraw && !_grabSelected);
        _sceneHurtboxes?.SetEnabled(canDraw);
        _sceneMotion?.SetEnabled(canDraw);
        if (_lab != null)
        {
            _sceneHitboxes?.SetValueWithoutNotify(_lab.ShowHitboxes);
            _sceneHurtboxes?.SetValueWithoutNotify(_lab.ShowHurtboxes);
            _sceneMotion?.SetValueWithoutNotify(_lab.ShowTrajectory);
        }
        _movesViewport?.MarkDirtyRepaint();
    }

    private void ReleaseMovesViewport()
    {
        if (_sceneHitboxes != null && _hitboxesChanged != null)
            _sceneHitboxes.UnregisterValueChangedCallback(_hitboxesChanged);
        if (_sceneHurtboxes != null && _hurtboxesChanged != null)
            _sceneHurtboxes.UnregisterValueChangedCallback(_hurtboxesChanged);
        if (_sceneMotion != null && _motionChanged != null)
            _sceneMotion.UnregisterValueChangedCallback(_motionChanged);
        if (_movesViewport != null) _movesViewport.onGUIHandler = null;
        ReleaseMovesViewportTarget();
        _movesViewport = null;
        _movesSceneStatus = null;
        _sceneHitboxes = null;
        _sceneHurtboxes = null;
        _sceneMotion = null;
        _hitboxesChanged = null;
        _hurtboxesChanged = null;
        _motionChanged = null;
    }

    private void ReleaseMovesViewportTarget()
    {
        if (_movesViewportTarget == null) return;
        _movesViewportTarget.Release();
        DestroyImmediate(_movesViewportTarget);
        _movesViewportTarget = null;
    }

    private void SetSceneOverlay(bool? hitboxes = null, bool? hurtboxes = null, bool? motion = null)
    {
        if (_lab == null) return;
        if (hitboxes.HasValue) _lab.ShowHitboxes = hitboxes.Value;
        if (hurtboxes.HasValue) _lab.ShowHurtboxes = hurtboxes.Value;
        if (motion.HasValue) _lab.ShowTrajectory = motion.Value;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        _movesViewport?.MarkDirtyRepaint();
    }

    private void DrawMovesViewport()
    {
        if (Event.current.type != EventType.Repaint || _movesViewport == null) return;
        var rect = _movesViewport.contentRect;
        if (rect.width <= 0f || rect.height <= 0f || !_workspace.HasPackage || _workspace.LiveDraftInvalid || _lab == null ||
            (!_lab.IsPackagePreview && !_lab.IsScenarioPreview && !_lab.PhasePreviewActive)) return;
        var camera = _lab.PreviewCamera;
        if (camera == null) return;
        int width = Mathf.Max(1, Mathf.CeilToInt(rect.width * EditorGUIUtility.pixelsPerPoint));
        int height = Mathf.Max(1, Mathf.CeilToInt(rect.height * EditorGUIUtility.pixelsPerPoint));
        if (_movesViewportTarget == null || _movesViewportTarget.width != width || _movesViewportTarget.height != height)
        {
            ReleaseMovesViewportTarget();
            _movesViewportTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "AbilityLabMovesViewport",
                hideFlags = HideFlags.HideAndDontSave,
            };
            _movesViewportTarget.Create();
        }

        var target = camera.targetTexture;
        float aspect = camera.aspect;
        Rect cameraRect = camera.rect;
        Rect pixelRect = camera.pixelRect;
        var active = RenderTexture.active;
        try
        {
            // The native Normal path renders into RenderTexture.active. Isolate that clear,
            // then let IMGUI composite under its own clip instead of painting over the toolbar.
            camera.targetTexture = _movesViewportTarget;
            camera.aspect = width / (float)height;
            RenderTexture.active = _movesViewportTarget;
            Handles.DrawCamera(rect, camera, DrawCameraMode.Normal);
        }
        finally
        {
            RenderTexture.active = active;
            camera.targetTexture = target;
            camera.aspect = aspect;
            camera.rect = cameraRect;
            camera.pixelRect = pixelRect;
        }
        GUI.DrawTexture(rect, _movesViewportTarget, ScaleMode.StretchToFill, false);
        if (_lab.IsScenarioPreview && _lab.Scenario is { } scenario &&
            SlopArena.Shared.CanonicalSlotProjection.TryGet(scenario.Options.Action, out var address))
        {
            int slotIndex = System.Array.IndexOf(AbilityLab.SlotNames, address.InputLabel);
            if (slotIndex >= 0)
            {
                var cooked = _lab.Def.GetCookedSlotAbility(
                    (byte)(AbilityLab.SlotIndices[slotIndex] + 1), address.IsAirborne);
                SlopArena.Shared.CookedChargedDirectionalDashCapabilityParameters? charge = null;
                if (cooked != null)
                    foreach (var stage in cooked.Timeline.Stages)
                    foreach (var operation in stage.Operations)
                        if (operation is SlopArena.Shared.CookedStartCapabilityOperation
                            { Parameters: SlopArena.Shared.CookedChargedDirectionalDashCapabilityParameters parameters })
                        {
                            charge = parameters;
                            break;
                        }
                if (charge != null)
                {
                    ushort ticks = scenario.Frames[_lab.ScenarioFrame].Actor.ChargeTicks;
                    byte tier = charge.GetChargeTier(ticks);
                    Color color = tier switch
                    {
                        2 => new Color(1f, 0.3f, 0.12f),
                        1 => new Color(1f, 0.8f, 0.15f),
                        _ => new Color(0.35f, 0.85f, 1f),
                    };
                    var badge = new Rect(rect.x + 12f, rect.y + 12f, 210f, 34f);
                    EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.82f));
                    var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = color }, fontSize = 16 };
                    GUI.Label(badge, $"CHARGE {ticks} · TIER {tier + 1}", style);
                }
            }
        }
    }
}
