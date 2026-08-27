using System;
using System.Linq;
using System.Collections.Generic;
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
        private AbilityLabPackageWorkspace _workspace = new();
        private string _newPackageId = "new-character";
        private string _newDisplayName = "New Character";
        private string _renameOldId = "";
        private string _renameNewId = "";
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                _lab = FindLab();
                DrawPackageWorkspace();

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

                DrawCharacterSelect();
                DrawScrubber();
                DrawDisplayToggles();
                if (_showTimeline) DrawTimeline();
                DrawActiveHitboxes();
                DrawMoveProperties();
                DrawHitboxEditor();
                DrawPersistence();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            // Keep the window live while the rig plays — scrub/timeline advance per tick.
            if (_lab != null && _lab.Playing) Repaint();
        }

        // ── Sections ──

        private void DrawPackageWorkspace()
        {
            EditorGUILayout.LabelField("Character Package", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _newPackageId = EditorGUILayout.TextField("New package ID", _newPackageId);
            _newDisplayName = EditorGUILayout.TextField("Display name", _newDisplayName);
            if (GUILayout.Button("New Package", GUILayout.Width(110)))
            {
                if (!_workspace.NewPackage(_newPackageId, _newDisplayName))
                    ShowDiagnostics(_workspace.Diagnostics);
            }
            if (GUILayout.Button("Open Package", GUILayout.Width(110)))
            {
                string selected = EditorUtility.OpenFolderPanel("Open Character Package", "Assets/CharacterPackages", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    string project = UnityCharacterAssetCooker.ProjectRoot().Replace('\\', '/') + "/";
                    string relative = selected.Replace('\\', '/');
                    if (relative.StartsWith(project, StringComparison.Ordinal)) relative = relative.Substring(project.Length);
                    if (!_workspace.OpenPackage(relative)) ShowDiagnostics(_workspace.Diagnostics);
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!_workspace.HasPackage) return;
            EditorGUILayout.LabelField("Package", _workspace.PackageRoot);
            EditorGUILayout.LabelField("Status", _workspace.Status);
            var manifest = _workspace.Manifest;
            string creator = EditorGUILayout.TextField("Creator", manifest.Creator);
            string license = EditorGUILayout.TextField("License", manifest.License);
            string attribution = EditorGUILayout.TextField("Attribution", manifest.Attribution);
            if (creator != manifest.Creator || license != manifest.License || attribution != manifest.Attribution)
                _workspace.SetManifest(manifest with { Creator = creator, License = license, Attribution = attribution });
            EditorGUILayout.LabelField("Loaded source hash", _workspace.LoadedDiskHash);
            EditorGUILayout.LabelField("Cooked source hash", _workspace.CookedSourceHash);
            EditorGUILayout.LabelField("Cooked content hash", _workspace.CookedContentHash);
            EditorGUILayout.LabelField("Package hash", _workspace.PackageHash);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload Package")) _workspace.ReloadPackage();
            if (GUILayout.Button("Save Package")) { _workspace.SavePackage(); Repaint(); }
            if (GUILayout.Button("Revert Draft")) _workspace.RevertDraft();
            _renameOldId = EditorGUILayout.TextField("Rename semantic ID", _renameOldId);
            _renameNewId = EditorGUILayout.TextField("New semantic ID", _renameNewId);
            if (GUILayout.Button("Rename ID") && !_workspace.RenameSemanticId(_renameOldId, _renameNewId))
                ShowDiagnostics(_workspace.Diagnostics);
            if (GUILayout.Button("Migrate Schema")) EditorUtility.DisplayDialog("Schema migration", "No authoring migration is registered for this schema.", "OK");
            EditorGUILayout.EndHorizontal();
            if (_workspace.Status == "Failed")
                EditorGUILayout.HelpBox("Non-authoritative draft / Stale Cook. The last valid cooked preview remains active.", MessageType.Warning);
            ShowDiagnostics(_workspace.Diagnostics);
            DrawSourceEditor();
        }

        private static void ShowDiagnostics(IReadOnlyList<CharacterDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics ?? Array.Empty<CharacterDiagnostic>())
                EditorGUILayout.HelpBox($"{diagnostic.Code} {diagnostic.Path}: {diagnostic.Message}", diagnostic.Severity == CharacterDiagnosticSeverity.Error ? MessageType.Error : MessageType.Warning);
        }

        private static CharacterMovementSource DrawMovement(CharacterMovementSource m)
        {
            EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
            float runSpeed = EditorGUILayout.FloatField("Run speed (m/s)", m.RunSpeed);
            float runAccelerationA = EditorGUILayout.FloatField("Run acceleration A", m.RunAccelerationA);
            float runAccelerationB = EditorGUILayout.FloatField("Run acceleration B", m.RunAccelerationB);
            float dashSpeed = EditorGUILayout.FloatField("Dash speed", m.DashSpeed);
            float airSpeedMax = EditorGUILayout.FloatField("Air speed max", m.AirSpeedMax);
            float airAccelStick = EditorGUILayout.FloatField("Air accel stick", m.AirAccelStick);
            float airAccelBase = EditorGUILayout.FloatField("Air accel base", m.AirAccelBase);
            float jumpForce = EditorGUILayout.FloatField("Jump force", m.JumpForce);
            float shortHopForce = EditorGUILayout.FloatField("Short-hop force", m.ShortHopForce);
            float airJumpVMultiplier = EditorGUILayout.FloatField("Air jump V multiplier", m.AirJumpVMultiplier);
            float airJumpHMultiplier = EditorGUILayout.FloatField("Air jump H multiplier", m.AirJumpHMultiplier);
            float gravity = EditorGUILayout.FloatField("Gravity", m.Gravity);
            float airFloatGravity = EditorGUILayout.FloatField("Air float gravity", m.AirFloatGravity);
            ushort dashDuration = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Dash duration (ticks)", m.DashDurationTicks), 0, ushort.MaxValue);
            ushort dashCooldown = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Dash cooldown (ticks)", m.DashCooldownTicks), 0, ushort.MaxValue);
            float groundFriction = EditorGUILayout.FloatField("Ground friction", m.GroundFriction);
            float airFriction = EditorGUILayout.FloatField("Air friction", m.AirFriction);
            float maxFallSpeed = EditorGUILayout.FloatField("Max fall speed", m.MaxFallSpeed);
            float fastFallSpeed = EditorGUILayout.FloatField("Fast fall speed", m.FastFallSpeed);
            byte maxJumps = (byte)Mathf.Clamp(EditorGUILayout.IntField("Max jumps", m.MaxJumps), 0, byte.MaxValue);
            ushort jumpSquat = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Jump squat (ticks)", m.JumpSquatTicks), 0, ushort.MaxValue);
            ushort floatWindow = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Float window (ticks)", m.FloatWindowTicks), 0, ushort.MaxValue);
            ushort rush = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Rush (ticks)", m.RushTicks), 0, ushort.MaxValue);
            return new CharacterMovementSource(runSpeed, runAccelerationA, runAccelerationB, dashSpeed, airSpeedMax, airAccelStick, airAccelBase, jumpForce, shortHopForce, airJumpVMultiplier, airJumpHMultiplier, gravity, airFloatGravity, dashDuration, dashCooldown, groundFriction, airFriction, maxFallSpeed, fastFallSpeed, maxJumps, jumpSquat, floatWindow, rush);
        }
        private static CharacterPresentationSource DrawPresentation(CharacterPresentationSource p)
        {
            EditorGUILayout.LabelField("Presentation semantic IDs", EditorStyles.boldLabel);
            return new CharacterPresentationSource(
                EditorGUILayout.TextField("Idle", p.Idle), EditorGUILayout.TextField("Run", p.Run), EditorGUILayout.TextField("Dash", p.Dash),
                EditorGUILayout.TextField("Jump", p.Jump), EditorGUILayout.TextField("Fall", p.Fall), EditorGUILayout.TextField("Hit small", p.HitSmall),
                EditorGUILayout.TextField("Hit medium", p.HitMedium), EditorGUILayout.TextField("Hit hard", p.HitHard),
                EditorGUILayout.FloatField("Land start offset (s)", p.LandStartOffsetSeconds));
        }
        private int _sourceSlot;

        private void DrawSourceEditor()
        {
            if (!_workspace.HasPackage) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Authoring Document", EditorStyles.boldLabel);
            var draft = _workspace.Draft;
            string displayName = EditorGUILayout.TextField("Display name", draft.DisplayName);
            float weight = EditorGUILayout.FloatField("Weight", draft.Weight);
            float capsuleRadius = EditorGUILayout.FloatField("Capsule radius", draft.CapsuleRadius);
            float capsuleHeight = EditorGUILayout.FloatField("Capsule height", draft.CapsuleHeight);
            float hipHeight = EditorGUILayout.FloatField("Hip height", draft.HipHeight);
            float hurtboxRadius = EditorGUILayout.FloatField("Hurtbox radius", draft.HurtboxRadius);
            CharacterMovementSource movement = DrawMovement(draft.Movement);
            CharacterPresentationSource presentation = DrawPresentation(draft.Presentation);
            if (displayName != draft.DisplayName || !Mathf.Approximately(weight, draft.Weight) || !Mathf.Approximately(capsuleRadius, draft.CapsuleRadius) || !Mathf.Approximately(capsuleHeight, draft.CapsuleHeight) || !Mathf.Approximately(hipHeight, draft.HipHeight) || !Mathf.Approximately(hurtboxRadius, draft.HurtboxRadius) || !movement.Equals(draft.Movement) || !presentation.Equals(draft.Presentation))
                _workspace.SetDraft(draft with { DisplayName = displayName, Weight = weight, CapsuleRadius = capsuleRadius, CapsuleHeight = capsuleHeight, HipHeight = hipHeight, HurtboxRadius = hurtboxRadius, Movement = movement, Presentation = presentation });
            string[] slotIds = _workspace.Draft.Slots.Select(x => x.Id).ToArray();
            _sourceSlot = Mathf.Clamp(EditorGUILayout.Popup("Source slot", _sourceSlot, slotIds), 0, Math.Max(0, slotIds.Length - 1));
            if (slotIds.Length == 0) return;
            var slot = _workspace.Draft.Slots[_sourceSlot];
            EditorGUILayout.LabelField(slot.Id, slot.Name);
            string slotName = EditorGUILayout.TextField("Name", slot.Name);
            string description = EditorGUILayout.TextField("Description", slot.Description);
            string iconId = EditorGUILayout.TextField("Icon ID", slot.IconId);
            var behavior = (AuthoringAbilityBehavior)EditorGUILayout.EnumPopup("Behavior", slot.Behavior);
            var aimMode = (AuthoringAimMode)EditorGUILayout.EnumPopup("Aim mode", slot.AimMode);
            ushort cooldown = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Cooldown (ticks)", slot.CooldownTicks), 0, ushort.MaxValue);
            bool recovery = EditorGUILayout.Toggle("Recovery move", slot.IsRecoveryMove);
            bool momentum = EditorGUILayout.Toggle("Preserve momentum", slot.PreserveMomentumOnStart);
            if (slotName != slot.Name || description != slot.Description || iconId != slot.IconId || behavior != slot.Behavior || aimMode != slot.AimMode || cooldown != slot.CooldownTicks || recovery != slot.IsRecoveryMove || momentum != slot.PreserveMomentumOnStart)
            {
                slot = slot with { Name = slotName, Description = description, IconId = iconId, Behavior = behavior, AimMode = aimMode, CooldownTicks = cooldown, IsRecoveryMove = recovery, PreserveMomentumOnStart = momentum };
                _workspace.SetDraft(_workspace.Draft with { Slots = _workspace.Draft.Slots.Select((x, i) => i == _sourceSlot ? slot : x).ToArray() });
            }
            for (int stageIndex = 0; stageIndex < slot.Timeline.Stages.Count; stageIndex++)
            {
                var stage = slot.Timeline.Stages[stageIndex];
                EditorGUILayout.LabelField($"Stage {stageIndex}", EditorStyles.boldLabel);
                ushort duration = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Duration (ticks)", stage.DurationTicks), 0, ushort.MaxValue);
                ushort iasa = (ushort)Mathf.Clamp(EditorGUILayout.IntField("IASA (ticks)", stage.IasaTicks), 0, ushort.MaxValue);
                ushort landingLag = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Landing lag (ticks)", stage.LandingLagTicks), 0, ushort.MaxValue);
                ushort autoBefore = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Auto-cancel before", stage.AutoCancelBeforeTicks), 0, ushort.MaxValue);
                ushort autoAfter = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Auto-cancel after", stage.AutoCancelAfterTicks), 0, ushort.MaxValue);
                string animationText = EditorGUILayout.TextField("Animation IDs (ordered)", string.Join(", ", stage.AnimationIds));
                var animationIds = animationText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                if (duration != stage.DurationTicks || iasa != stage.IasaTicks || landingLag != stage.LandingLagTicks || autoBefore != stage.AutoCancelBeforeTicks || autoAfter != stage.AutoCancelAfterTicks || !animationIds.SequenceEqual(stage.AnimationIds, StringComparer.Ordinal))
                    _workspace.ReplaceStage(_sourceSlot, stageIndex, stage with { DurationTicks = duration, IasaTicks = iasa, LandingLagTicks = landingLag, AutoCancelBeforeTicks = autoBefore, AutoCancelAfterTicks = autoAfter, AnimationIds = animationIds });
                for (int operationIndex = 0; operationIndex < stage.Operations.Count; operationIndex++)
                {
                    var operation = stage.Operations[operationIndex];
                    EditorGUILayout.LabelField($"{operationIndex}: {operation.GetType().Name} @ {operation.Tick} {operation.Unit}");
                    if (operation is SpawnHitboxOperationSource hitbox)
                    {
                        var h = hitbox.Hitbox;
                        h = h with
                        {
                            Shape = (AuthoringHitboxShape)EditorGUILayout.EnumPopup("Shape", h.Shape),
                            Radius = EditorGUILayout.FloatField("Radius", h.Radius),
                            OffsetX = EditorGUILayout.FloatField("Offset X", h.OffsetX),
                            OffsetY = EditorGUILayout.FloatField("Offset Y", h.OffsetY),
                            OffsetZ = EditorGUILayout.FloatField("Offset Z", h.OffsetZ),
                            EndOffsetX = EditorGUILayout.FloatField("End X", h.EndOffsetX),
                            EndOffsetY = EditorGUILayout.FloatField("End Y", h.EndOffsetY),
                            EndOffsetZ = EditorGUILayout.FloatField("End Z", h.EndOffsetZ),
                            StartBoneId = EditorGUILayout.TextField("Start bone ID", h.StartBoneId ?? ""),
                            EndBoneId = EditorGUILayout.TextField("End bone ID", h.EndBoneId ?? ""),
                            Damage = EditorGUILayout.FloatField("Damage", h.Damage),
                            Angle = EditorGUILayout.FloatField("Angle", h.Angle),
                            BaseKnockback = EditorGUILayout.FloatField("Base knockback", h.BaseKnockback),
                            KnockbackGrowth = EditorGUILayout.FloatField("Knockback growth", h.KnockbackGrowth),
                            StunTicks = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Stun ticks", h.StunTicks), 0, ushort.MaxValue),
                            DurationTicks = (ushort)Mathf.Clamp(EditorGUILayout.IntField("Duration ticks", h.DurationTicks), 0, ushort.MaxValue),
                            Interruptible = EditorGUILayout.Toggle("Interruptible", h.Interruptible),
                            HitGroup = (byte)Mathf.Clamp(EditorGUILayout.IntField("Hit group", h.HitGroup), 0, byte.MaxValue),
                        };
                        h = h with { StartBoneId = string.IsNullOrEmpty(h.StartBoneId) ? null : h.StartBoneId, EndBoneId = string.IsNullOrEmpty(h.EndBoneId) ? null : h.EndBoneId };
                        if (!h.Equals(hitbox.Hitbox))
                            _workspace.ReplaceOperation(_sourceSlot, stageIndex, operationIndex, hitbox with { Hitbox = h });
                    }
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Up") && operationIndex > 0) _workspace.MoveOperation(_sourceSlot, stageIndex, operationIndex, operationIndex - 1);
                    if (GUILayout.Button("Down") && operationIndex + 1 < stage.Operations.Count) _workspace.MoveOperation(_sourceSlot, stageIndex, operationIndex, operationIndex + 1);
                    if (GUILayout.Button("Remove")) _workspace.RemoveOperation(_sourceSlot, stageIndex, operationIndex);
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("Add Operation"))
                    _workspace.AddOperation(_sourceSlot, stageIndex, new SpawnHitboxOperationSource(1, AuthoringUnit.Meters, new HitboxSource(AuthoringHitboxShape.Sphere, 0.25f, 0f, 0f, 1f, 0f, 0f, 0f, null, null, 1f, 45f, 10f, 1f, 10, 1, true, 0)));
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Stage")) _workspace.AddStage(_sourceSlot, new CharacterStageSource(1, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<CharacterTimelineOperationSource>()));
            if (GUILayout.Button("Remove Stage") && slot.Timeline.Stages.Count > 0) _workspace.RemoveStage(_sourceSlot, slot.Timeline.Stages.Count - 1);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Migrated hurtboxes (read-only)", EditorStyles.boldLabel);
            foreach (var capsule in _workspace.Draft.HurtboxCapsules) EditorGUILayout.LabelField($"Capsule ({capsule.StartX}, {capsule.StartY}, {capsule.StartZ}) → ({capsule.EndX}, {capsule.EndY}, {capsule.EndZ}) r={capsule.Radius}");
            foreach (var bone in _workspace.Draft.HurtboxBoneDefs) EditorGUILayout.LabelField($"Bone {bone.BoneId} offset=({bone.OffsetX}, {bone.OffsetY}, {bone.OffsetZ}) r={bone.Radius}");
        }

        private void DrawCharacterSelect()
        {
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);
            BuiltInRosterManifest roster;
            try
            {
                string path = System.IO.Path.Combine("content-cooked", "roster", "manifest.json");
                roster = BuiltInRosterManifestCodec.Load(path);
            }
            catch (Exception ex)
            {
                EditorGUILayout.HelpBox($"Built-in roster unavailable: {ex.Message}", MessageType.Error);
                return;
            }
            var entries = roster.Entries;
            var names = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) names[i] = entries[i].Selector.ToString();
            int current = -1;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Selector == _lab.Character) current = i;
            int pick = EditorGUILayout.Popup("Character", Mathf.Max(0, current), names);
            if (pick != current && pick >= 0 && pick < entries.Count) _lab.LoadCharacter(entries[pick].Selector);

            bool air = EditorGUILayout.Toggle("Airborne variant", _lab.Airborne);
            if (air != _lab.Airborne) _lab.SetAirborne(air);

            int[] slotIndices = AbilityLab.SlotIndices;
            int currentSlot = Array.IndexOf(slotIndices, _lab.SlotIndex);
            int pickSlot = EditorGUILayout.Popup(
                "Ability", Mathf.Max(0, currentSlot), AbilityLab.SlotNames);
            if (pickSlot != currentSlot && pickSlot >= 0 && pickSlot < slotIndices.Length)
                _lab.SetSlot(slotIndices[pickSlot]);

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
                    $"active [{evt.TriggerTick}, {evt.TriggerTick + evt.DurationTicks})  {shape} r={evt.Radius:0.00}  {bone}  dmg={evt.Damage:0.#}");
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
        private void DrawMoveProperties()
        {
            var spec = _lab.CurrentSpec();
            if (spec == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Move properties", EditorStyles.boldLabel);
            float hitstop = EditorGUILayout.FloatField(
                "Hitstop multiplier (override)", _lab.CurrentHitstopMultiplier);
            if (!Mathf.Approximately(hitstop, _lab.CurrentHitstopMultiplier))
                _lab.SetHitstopMultiplier(hitstop);

            var events = _lab.CurrentWorkingEvents();
            if (events.Length == 0) return;
            int index = Mathf.Clamp(_selectedHitbox, 0, events.Length - 1);
            var selected = events[index];
            ushort authoredTicks = ServerSimulation.ComputeHitstopTicks(selected.Damage, spec);
            ushort previewTicks = (ushort)Mathf.Clamp(
                (int)((selected.Damage / 3f + 6f) * _lab.CurrentHitstopMultiplier), 1, 12);
            EditorGUILayout.LabelField(
                $"Hitstop ticks (selected damage): {previewTicks} current / {authoredTicks} authored");
        }


        private void DrawHitboxEditor()
        {
            if (_workspace.HasPackage)
            {
                EditorGUILayout.HelpBox("Package hitboxes are edited in the typed Authoring Document section above.", MessageType.Info);
                return;
            }
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
                    EditorGUILayout.LabelField("Active start", GUILayout.Width(82));
                    int trigger = EditorGUILayout.IntField(evt.TriggerTick);
                    if (trigger != evt.TriggerTick)
                    {
                        var n = evt; n.TriggerTick = (ushort)Mathf.Clamp(trigger, 1, 999);
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.LabelField("Active duration", GUILayout.Width(98));
                    int dur = EditorGUILayout.IntField(evt.DurationTicks);
                    if (dur != evt.DurationTicks)
                    {
                        var n = evt; n.DurationTicks = (ushort)Mathf.Clamp(dur, 1, 999);
                        _lab.SetWorkingEvent(i, n);
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.LabelField($"active range [{evt.TriggerTick}, {evt.TriggerTick + evt.DurationTicks})");

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
                    // Damage / hitstun gate (editable)
                    EditorGUILayout.BeginHorizontal();
                    float dmg = EditorGUILayout.FloatField("Damage", evt.Damage);
                    if (Mathf.Abs(dmg - evt.Damage) > 0.001f) { var n = evt; n.Damage = Mathf.Max(0f, dmg); _lab.SetWorkingEvent(i, n); }
                    int stun = EditorGUILayout.IntField("Hitstun gate", evt.StunTicks);
                    if (stun != evt.StunTicks) { var n = evt; n.StunTicks = (ushort)Mathf.Clamp(stun, 0, 999); _lab.SetWorkingEvent(i, n); }
                    EditorGUILayout.EndHorizontal();
                    // Knockback (editable): angle + base + growth — the shape the trajectory preview draws.
                    bool customKb = evt.Knockback.Profile == KnockbackProfile.Custom;
                    EditorGUILayout.BeginHorizontal();
                    if (customKb)
                    {
                        int angle = EditorGUILayout.IntField("Launch angle", evt.Knockback.Angle);
                        if (angle != evt.Knockback.Angle)
                        {
                            var n = evt; var kb = n.Knockback; kb.Angle = (sbyte)Mathf.Clamp(angle, -90, 90); n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                    }
                    else
                    {
                        var resolvedKb = evt.Knockback.Resolve();
                        EditorGUILayout.LabelField(
                            $"Launch angle: {resolvedKb.angle}° ({evt.Knockback.Profile}, fixed)");
                        EditorGUILayout.LabelField(
                            $"Base knockback: {resolvedKb.baseKB:0.##} · Knockback growth: {resolvedKb.growthKB:0.##} (fixed)");
                    }
                    EditorGUILayout.EndHorizontal();
                    if (customKb)
                    {
                        EditorGUILayout.BeginHorizontal();
                        float baseKb = EditorGUILayout.FloatField("Base knockback", evt.Knockback.BaseKnockback);
                        if (Mathf.Abs(baseKb - evt.Knockback.BaseKnockback) > 0.001f)
                        {
                            var n = evt; var kb = n.Knockback; kb.BaseKnockback = Mathf.Max(0f, baseKb); n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                        float growth = EditorGUILayout.FloatField("Knockback growth", evt.Knockback.KnockbackGrowth);
                        if (Mathf.Abs(growth - evt.Knockback.KnockbackGrowth) > 0.001f)
                        {
                            var n = evt; var kb = n.Knockback; kb.KnockbackGrowth = Mathf.Max(0f, growth); n.Knockback = kb;
                            _lab.SetWorkingEvent(i, n);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.LabelField(
                        $"hitstun gate {evt.StunTicks} · kb {evt.Knockback.Profile}" +
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
            using (new EditorGUI.DisabledScope(!_workspace.CanUndo))
                if (GUILayout.Button("Undo")) { _workspace.Undo(); SceneView.RepaintAll(); }
            using (new EditorGUI.DisabledScope(!_workspace.CanRedo))
                if (GUILayout.Button("Redo")) { _workspace.Redo(); SceneView.RepaintAll(); }
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
            if (!_workspace.HasPackage)
            {
                EditorGUILayout.HelpBox("Open or create a Character Package to persist source edits. Legacy previews are read-only.", MessageType.Info);
                return;
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Package")) { _workspace.SavePackage(); SceneView.RepaintAll(); }
            if (GUILayout.Button("Revert Draft"))
            {
                if (EditorUtility.DisplayDialog("Ability Lab", "Discard all unsaved source edits?", "Revert", "Cancel"))
                {
                    _workspace.RevertDraft();
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Scene handles ──

        private void OnSceneGUI(SceneView sv)
        {
            var lab = FindLab();
            if (lab == null || !Application.isPlaying || lab.Def == null) return;
            if (_workspace.HasPackage) return;
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
