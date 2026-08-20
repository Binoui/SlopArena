using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlopArena.Shared;
using SlopArena.Client.Tools;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Ability Lab control panel (spec #119): character/ability selection, frame
    /// scrubbing, hitbox timeline, and the hitbox editor (add / remove / move / scale)
    /// for the AbilityLab rig in the current scene. Scene-view handles on the active
    /// hitboxes are wired through SceneView.duringSceneGui; the wireframe display is
    /// drawn by the rig itself (OnRenderObject), so it shows in the Game view too.
    ///
    /// Usage: open Tools → SlopArena → Ability Lab, click "Create Lab Rig", press Play.
    /// </summary>
    public class AbilityLabWindow : EditorWindow
    {
        private static readonly string[] SpeedOptions = { "0.25×", "0.5×", "1×", "2×" };
        private static readonly string[] ShapeOptions = { "Sphere", "Capsule" };

        private AbilityLab _lab;
        private Vector2 _scroll;
        private int _selectedHitbox = -1;
        private bool _showTimeline = true;

        [MenuItem("Tools/SlopArena/Ability Lab")]
        public static void Open() => GetWindow<AbilityLabWindow>("Ability Lab");

        private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
        private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        private AbilityLab FindLab() => AbilityLab.Instance != null ? AbilityLab.Instance : FindObjectOfType<AbilityLab>();

        private void OnGUI()
        {
            _lab = FindLab();

            if (_lab == null)
            {
                EditorGUILayout.HelpBox(
                    "No Ability Lab rig in the scene. Create one, then press Play — the tool " +
                    "previews poses and hitboxes in play mode.", MessageType.Info);
                if (GUILayout.Button("Create Lab Rig"))
                {
                    var go = new GameObject("AbilityLab");
                    var rig = go.AddComponent<AbilityLab>();
                    rig.EnsureCamera();
                    Selection.activeGameObject = go;
                    _lab = rig;
                }
                return;
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Rig ready. Camera already works in edit mode — right-drag orbit, " +
                    "middle-drag pan, scroll zoom. Press Play to load a character, scrub, " +
                    "and see boxes.", MessageType.Info);
                if (GUILayout.Button("Enter Play Mode"))
                    EditorApplication.EnterPlaymode();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                DrawCharacterSelect();
                DrawScrubber();
                DrawDisplayToggles();
                if (_showTimeline) DrawTimeline();
                DrawActiveHitboxes();
                DrawHitboxEditor();
                DrawPersistence();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            // Keep the window live while the rig plays — scrub/timeline advance per tick.
            if (_lab.Playing) Repaint();
        }

        // ── Sections ──

        private void DrawCharacterSelect()
        {
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);
            // BuildRegistry()[0] is `default` (null placeholder for CharacterClass.None) —
            // filter it out of the dropdown or DisplayName NREs on draw.
            var defs = Array.FindAll(CharacterRegistry.All, d => d != null);
            var names = new string[defs.Length];
            for (int i = 0; i < defs.Length; i++) names[i] = defs[i].DisplayName;
            int current = Array.FindIndex(defs, d => d.Class == _lab.Character);
            int pick = EditorGUILayout.Popup("Character", Mathf.Max(0, current), names);
            if (pick != current) _lab.LoadCharacter(defs[pick].Class);

            bool air = EditorGUILayout.Toggle("Airborne variant", _lab.Airborne);
            if (air != _lab.Airborne) _lab.SetAirborne(air);

            int slot = EditorGUILayout.Popup("Ability", _lab.SlotIndex, AbilityLab.SlotNames);
            if (slot != _lab.SlotIndex) _lab.SetSlot(slot);

            var spec = _lab.CurrentSpec();
            int stageCount = spec?.Stages != null ? spec.Stages.Length : 0;
            int stage = EditorGUILayout.Popup("Stage", _lab.StageIndex, StageNames(stageCount));
            if (stage != _lab.StageIndex) _lab.SetStage(stage);
        }

        private static string[] StageNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = $"Stage {i + 1}";
            return names;
        }

        private void DrawScrubber()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scrub", EditorStyles.boldLabel);
            bool hasStage = _lab.TryGetStage(out var stage);
            int duration = hasStage ? Mathf.Max(1, (int)stage.DurationTicks) : 1;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⏮", GUILayout.Width(28))) _lab.SetTick(0);
            if (GUILayout.Button(_lab.Playing ? "⏸" : "▶", GUILayout.Width(32)))
            {
                _lab.Playing = !_lab.Playing;
                if (!_lab.Playing) _lab.RefreshPose();
            }
            if (GUILayout.Button("-1", GUILayout.Width(32)))
                _lab.SetTick((ushort)Mathf.Max(0, _lab.Tick - 1));
            if (GUILayout.Button("+1", GUILayout.Width(32)))
                _lab.SetTick((ushort)Mathf.Min(duration - 1, _lab.Tick + 1));
            int speed = Mathf.Clamp(Array.IndexOf(SpeedOptions, _lab.PlaySpeed switch
            {
                0.25f => "0.25×", 0.5f => "0.5×", 2f => "2×", _ => "1×",
            }), 0, SpeedOptions.Length - 1);
            int newSpeed = EditorGUILayout.Popup(speed, SpeedOptions, GUILayout.Width(64));
            if (newSpeed != speed) _lab.PlaySpeed = newSpeed switch { 0 => 0.25f, 1 => 0.5f, 2 => 1f, _ => 2f };
            EditorGUILayout.EndHorizontal();

            int tick = EditorGUILayout.IntSlider("Tick", _lab.Tick, 0, duration - 1);
            if (tick != _lab.Tick) _lab.SetTick((ushort)tick);
            EditorGUILayout.LabelField($"Frame {_lab.Tick}/{duration - 1}  ({(float)_lab.Tick / AbilityLab.TickRate:0.00}s)");

            float yaw = EditorGUILayout.Slider("Facing yaw", _lab.FacingYaw, 0f, Mathf.PI * 2f);
            if (Mathf.Abs(yaw - _lab.FacingYaw) > 0.0001f) { _lab.FacingYaw = yaw; _lab.RefreshPose(); }
        }

        private void DrawDisplayToggles()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            _lab.ShowHurtboxes = EditorGUILayout.Toggle("Hurtboxes (green)", _lab.ShowHurtboxes);
            _lab.ShowHitboxes = EditorGUILayout.Toggle("Hitboxes (orange)", _lab.ShowHitboxes);
            _lab.ShowBakedBones = EditorGUILayout.Toggle("Baked bones (cyan; weapon tip magenta)", _lab.ShowBakedBones);
            _lab.ShowDummy = EditorGUILayout.Toggle("Dummy opponent (red)", _lab.ShowDummy);
            if (_lab.ShowDummy)
            {
                float dist = EditorGUILayout.Slider("Dummy distance", _lab.DummyDistance, 0.5f, 8f);
                if (Mathf.Abs(dist - _lab.DummyDistance) > 0.0001f) { _lab.DummyDistance = dist; _lab.RefreshPose(); }
            }
            bool traj = EditorGUILayout.Toggle("Knockback trajectory (cyan→blue)", _lab.ShowTrajectory);
            if (traj != _lab.ShowTrajectory) _lab.ShowTrajectory = traj;
            if (_lab.ShowTrajectory)
            {
                float pct = EditorGUILayout.Slider("Victim %", _lab.TrajectoryPercent, 0f, 200f);
                if (Mathf.Abs(pct - _lab.TrajectoryPercent) > 0.001f) _lab.TrajectoryPercent = pct;
                var events = _lab.CurrentWorkingEvents();
                if (events.Length > 0)
                {
                    int idx = Mathf.Clamp(_lab.PreviewHitboxIndex, 0, events.Length - 1);
                    int pick = EditorGUILayout.Popup("Preview hitbox", idx,
                        Enumerable.Range(0, events.Length).Select(i => $"Hitbox {i}").ToArray());
                    if (pick != idx) _lab.PreviewHitboxIndex = pick;
                }
                EditorGUILayout.HelpBox(
                    "Arc = victim launched by the previewed hitbox's knockback at the chosen %; " +
                    "cyan = hitstun, blue = flight, white = apex, red = landing.",
                    MessageType.None);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset camera", GUILayout.Width(100))) _lab.ResetCameraView();
            EditorGUILayout.LabelField("drag right: orbit · drag middle: pan · scroll: zoom");
            EditorGUILayout.EndHorizontal();
            _showTimeline = EditorGUILayout.Toggle("Show hitbox timeline", _showTimeline);
        }

        private void DrawTimeline()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hitbox timeline", EditorStyles.boldLabel);
            var events = _lab.CurrentWorkingEvents();
            if (!_lab.TryGetStage(out var stage) || events.Length == 0)
            {
                EditorGUILayout.LabelField("(no hitboxes in this stage)");
                return;
            }
            int duration = Mathf.Max(1, (int)stage.DurationTicks);
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                int start = Mathf.Min(evt.TriggerTick, duration);
                int end = Mathf.Min(evt.TriggerTick + evt.DurationTicks, duration);
                Rect bar = GUILayoutUtility.GetRect(140, 14);
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, bar.height), new Color(0.2f, 0.2f, 0.22f));
                EditorGUI.DrawRect(new Rect(
                    bar.x + bar.width * start / duration,
                    bar.y,
                    bar.width * Mathf.Max(0, end - start) / duration,
                    bar.height), new Color(1f, 0.35f, 0f, 0.9f));
                EditorGUI.DrawRect(new Rect(
                    bar.x + bar.width * _lab.Tick / duration - 1f, bar.y, 2f, bar.height), Color.white);
                string shape = evt.Shape == HitboxShape.Capsule ? "capsule" : "sphere";
                string bone = evt.BoneName ?? "entity";
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Jump", GUILayout.Width(44)))
                    _lab.SetTick(evt.TriggerTick);
                EditorGUILayout.LabelField(
                    $"t{evt.TriggerTick}-{evt.TriggerTick + evt.DurationTicks}  {shape} r={evt.Radius:0.00}  {bone}  dmg={evt.Damage:0.#}");
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawActiveHitboxes()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Active hitboxes @ tick {_lab.Tick}", EditorStyles.boldLabel);
            var active = _lab.ResolveHitboxes();
            if (active.Count == 0)
            {
                EditorGUILayout.LabelField("(none active)");
                return;
            }
            foreach (var (index, evt, start, end) in active)
            {
                string shape = evt.Shape == HitboxShape.Capsule ? "capsule" : "sphere";
                string pos = evt.Shape == HitboxShape.Capsule
                    ? $"{start:0.00} → {end:0.00}"
                    : start.ToString("0.00");
                EditorGUILayout.LabelField($"#{index} {shape} r={evt.Radius:0.00}  @ {pos}");
            }
        }

        private void DrawHitboxEditor()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hitbox editor", EditorStyles.boldLabel);
            var events = _lab.CurrentWorkingEvents();
            if (events.Length == 0)
            {
                EditorGUILayout.LabelField("(this stage has no hitboxes)");
            }
            else
            {
                for (int i = 0; i < events.Length; i++)
                {
                    var evt = events[i];
                    EditorGUILayout.BeginHorizontal();
                    bool selected = EditorGUILayout.Toggle(_selectedHitbox == i, GUILayout.Width(16));
                    if (selected != (_selectedHitbox == i)) _selectedHitbox = selected ? i : -1;
                    EditorGUILayout.LabelField($"Hitbox {i}", EditorStyles.boldLabel);
                    if (GUILayout.Button("Remove", GUILayout.Width(64)))
                    {
                        _lab.RemoveWorkingEvent(i);
                        _selectedHitbox = -1;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Trigger", GUILayout.Width(70));
                    int trigger = EditorGUILayout.IntField(evt.TriggerTick);
                    if (trigger != evt.TriggerTick)
                    {
                        var n = evt; n.TriggerTick = (ushort)Mathf.Clamp(trigger, 1, 999);
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.LabelField("Duration", GUILayout.Width(64));
                    int dur = EditorGUILayout.IntField(evt.DurationTicks);
                    if (dur != evt.DurationTicks)
                    {
                        var n = evt; n.DurationTicks = (ushort)Mathf.Clamp(dur, 1, 999);
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.EndHorizontal();

                    int shape = EditorGUILayout.Popup("Shape", evt.Shape == HitboxShape.Capsule ? 1 : 0, ShapeOptions);
                    if (shape != (evt.Shape == HitboxShape.Capsule ? 1 : 0))
                    {
                        var n = evt; n.Shape = shape == 1 ? HitboxShape.Capsule : HitboxShape.Sphere;
                        _lab.SetWorkingEvent(i, n);
                    }
                    float radius = EditorGUILayout.FloatField("Radius", evt.Radius);
                    if (radius != evt.Radius)
                    {
                        var n = evt; n.Radius = Mathf.Max(0.01f, radius);
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.BeginHorizontal();
                    float ox = EditorGUILayout.FloatField("Off X", evt.OffX);
                    float oy = EditorGUILayout.FloatField("Y", evt.OffY);
                    float oz = EditorGUILayout.FloatField("Z", evt.OffZ);
                    if (ox != evt.OffX || oy != evt.OffY || oz != evt.OffZ)
                    {
                        var n = evt; n.OffX = ox; n.OffY = oy; n.OffZ = oz;
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.EndHorizontal();
                    // Bone dropdowns: "entity (origin)" + every baked skeleton bone.
                    // Hoisted above the capsule block so both dropdowns share one list.
                    string currentBone = evt.BoneName ?? "";
                    string currentEndBone = evt.EndBoneName ?? "";
                    string[] bakedBones = _lab.BakedBoneNames;
                    var boneOptions = new System.Collections.Generic.List<string> { "entity (origin)" };
                    boneOptions.AddRange(bakedBones);
                    if (currentBone.Length > 0 && Array.IndexOf(bakedBones, currentBone) < 0)
                        boneOptions.Insert(1, currentBone);
                    if (currentEndBone.Length > 0 && Array.IndexOf(bakedBones, currentEndBone) < 0
                        && boneOptions.IndexOf(currentEndBone) < 0)
                        boneOptions.Insert(1, currentEndBone);
                    if (evt.Shape == HitboxShape.Capsule)
                    {
                        // End Bone dropdown — anchors the capsule's end at a second baked
                        // point (e.g. _weapon_tip). End X/Y/Z below are a facing-rotated
                        // DELTA on top of that anchor (or on top of the start when no end
                        // bone is set) — keep them editable so the capsule can be nudged
                        // past/around the anchor.
                        int endSel = Mathf.Max(0, currentEndBone.Length > 0 ? boneOptions.IndexOf(currentEndBone) : 0);
                        int endPick = EditorGUILayout.Popup("End Bone", endSel, boneOptions.ToArray());
                        string pickedEndBone = endPick == 0 ? null : boneOptions[endPick];
                        if (pickedEndBone != evt.EndBoneName)
                        {
                            var n = evt; n.EndBoneName = pickedEndBone;
                            _lab.SetWorkingEvent(i, n);
                        }
                        EditorGUILayout.BeginHorizontal();
                        float ex = EditorGUILayout.FloatField("End X", evt.EndOffX);
                        float ey = EditorGUILayout.FloatField("Y", evt.EndOffY);
                        float ez = EditorGUILayout.FloatField("Z", evt.EndOffZ);
                        if (ex != evt.EndOffX || ey != evt.EndOffY || ez != evt.EndOffZ)
                        {
                            var n = evt; n.EndOffX = ex; n.EndOffY = ey; n.EndOffZ = ez;
                            _lab.SetWorkingEvent(i, n);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    int boneSel = Mathf.Max(0, currentBone.Length > 0 ? boneOptions.IndexOf(currentBone) : 0);
                    int bonePick = EditorGUILayout.Popup("Bone", boneSel, boneOptions.ToArray());
                    string pickedBone = bonePick == 0 ? null : boneOptions[bonePick];
                    if (pickedBone != evt.BoneName)
                    {
                        var n = evt; n.BoneName = pickedBone;
                        _lab.SetWorkingEvent(i, n);
                    }
                    // Damage / Stun (editable)
                    EditorGUILayout.BeginHorizontal();
                    float dmg = EditorGUILayout.FloatField("Damage", evt.Damage);
                    if (Mathf.Abs(dmg - evt.Damage) > 0.001f) { var n = evt; n.Damage = Mathf.Max(0f, dmg); _lab.SetWorkingEvent(i, n); }
                    int stun = EditorGUILayout.IntField("Stun", evt.StunTicks);
                    if (stun != evt.StunTicks) { var n = evt; n.StunTicks = (ushort)Mathf.Clamp(stun, 0, 999); _lab.SetWorkingEvent(i, n); }
                    EditorGUILayout.EndHorizontal();
                    // Knockback (editable): angle + base + growth — the shape the trajectory preview draws.
                    bool customKb = evt.Knockback.Profile == KnockbackProfile.Custom;
                    EditorGUILayout.BeginHorizontal();
                    if (customKb)
                    {
                        int angle = EditorGUILayout.IntField("Angle", evt.Knockback.Angle);
                        if (angle != evt.Knockback.Angle)
                        {
                            var n = evt; var kb = n.Knockback; kb.Angle = (sbyte)Mathf.Clamp(angle, -180, 180); n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"Angle: {evt.Knockback.Profile} (fixed)");
                    }
                    EditorGUILayout.EndHorizontal();
                    if (customKb)
                    {
                        EditorGUILayout.BeginHorizontal();
                        float baseKb = EditorGUILayout.FloatField("Base KB", evt.Knockback.BaseKnockback);
                        if (Mathf.Abs(baseKb - evt.Knockback.BaseKnockback) > 0.001f)
                        {
                            var n = evt; var kb = n.Knockback; kb.BaseKnockback = baseKb; n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                        float growth = EditorGUILayout.FloatField("Growth", evt.Knockback.KnockbackGrowth);
                        if (Mathf.Abs(growth - evt.Knockback.KnockbackGrowth) > 0.001f)
                        {
                            var n = evt; var kb = n.Knockback; kb.KnockbackGrowth = growth; n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.LabelField(
                        $"stun gate {evt.StunTicks} · kb {evt.Knockback.Profile}" +
                        (evt.Interruptible ? " · interruptible" : " · armor"));
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add hitbox"))
            {
                _lab.AddWorkingEvent();
                _selectedHitbox = events.Length; // select the new event
            }
            using (new EditorGUI.DisabledScope(!_lab.CanUndo))
                if (GUILayout.Button("Undo")) { _lab.UndoEvents(); SceneView.RepaintAll(); }
            using (new EditorGUI.DisabledScope(!_lab.CanRedo))
                if (GUILayout.Button("Redo")) { _lab.RedoEvents(); SceneView.RepaintAll(); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Scene handles: drag a sphere to move it (capsule: drag either end), drag the " +
                "ring to scale radius. Scrub into a hitbox's trigger window to grab its handles.",
                MessageType.None);
        }

        private void DrawPersistence()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Persistence", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save to source (C#)"))
            {
                _lab.SaveToSource();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Revert edits"))
            {
                if (EditorUtility.DisplayDialog("Ability Lab", "Discard all unsaved hitbox edits?", "Revert", "Cancel"))
                {
                    _lab.RevertEdits();
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (_lab.SourceFilePath != null)
                EditorGUILayout.HelpBox(
                    $"Writes to: {_lab.SourceFilePath}\n" +
                    "Then run `dotnet build src/Shared/` (auto-copies the DLL to Unity) " +
                    "and restart the match — the edit is in the real character data.", MessageType.None);
        }

        // ── Scene handles ──

        private void OnSceneGUI(SceneView sv)
        {
            var lab = FindLab();
            if (lab == null || !Application.isPlaying || lab.Def == null) return;
            if (!lab.ShowHitboxes) return;

            float yaw = lab.FacingYaw;
            float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);

            foreach (var (index, evt, start, end) in lab.ResolveHitboxes())
            {
                float size = Mathf.Max(evt.Radius * 2f, 0.05f);

                if (index != _selectedHitbox)
                {
                    if (Handles.Button(start, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    {
                        _selectedHitbox = index;
                        Repaint();
                    }
                    continue;
                }

                // Drag the start point → edit Off* (inverse-facing rotation).
                Vector3 basePos = start - new Vector3(
                    evt.OffX * cos + evt.OffZ * sin, evt.OffY, -evt.OffX * sin + evt.OffZ * cos);
                EditorGUI.BeginChangeCheck();
                Vector3 newStart = Handles.FreeMoveHandle(start, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 d = newStart - basePos;
                    var n = evt;
                    n.OffX = d.x * cos - d.z * sin;
                    n.OffY = d.y;
                    n.OffZ = d.x * sin + d.z * cos;
                    lab.SetWorkingEvent(index, n);
                    sv.Repaint();
                }

                if (evt.Shape == HitboxShape.Capsule)
                {
                    // Capsule far end drag → edit EndOff* (relative to the current start).
                    EditorGUI.BeginChangeCheck();
                    Vector3 newEnd = Handles.FreeMoveHandle(end, size * 0.8f, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 d = newEnd - start;
                        var n = evt;
                        n.EndOffX = d.x * cos - d.z * sin;
                        n.EndOffY = d.y;
                        n.EndOffZ = d.x * sin + d.z * cos;
                        lab.SetWorkingEvent(index, n);
                        sv.Repaint();
                    }
                }

                EditorGUI.BeginChangeCheck();
                float newRadius = Handles.RadiusHandle(Quaternion.identity, start, evt.Radius);
                if (EditorGUI.EndChangeCheck())
                {
                    var n = evt;
                    n.Radius = Mathf.Max(0.01f, newRadius);
                    lab.SetWorkingEvent(index, n);
                    sv.Repaint();
                }
            }
        }
    }
}
