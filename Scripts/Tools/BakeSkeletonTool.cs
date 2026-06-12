using Godot;
using SlopArena.Shared;
using System;
using System.Collections.Generic;

namespace SlopArena.Tools
{
    /// <summary>
    /// Run this scene (F6 or headless) to bake Manki's skeleton bone positions.
    /// No setup needed — it loads manki.tscn and finds Skeleton3D + AnimationPlayer automatically.
    /// </summary>
    [Tool]
    public partial class BakeSkeletonTool : Node
    {
        [Export] public string CharacterScenePath = "res://assets/characters/manki/manki.tscn";
        [Export] public string OutputPath = "res://data/manki_skeleton.bin";
        [Export] public bool TriggerBake
        {
            get => _triggerBake;
            set
            {
                if (value)
                {
                    Bake();
                    _triggerBake = false;
                }
            }
        }
        private bool _triggerBake;

        // Actual bone names from Godot's Skeleton3D (underscore format)
        private static readonly string[] BoneNames = new[]
        {
            "mixamorig_Head",
            "mixamorig_Spine2",
            "mixamorig_Hips",
            "mixamorig_RightHand",
            "mixamorig_LeftHand",
            "mixamorig_RightFoot",
            "mixamorig_LeftFoot",
        };

        public override void _Ready()
        {
            // Don't auto-bake on _Ready — use TriggerBake checkbox instead
        }

        public void Bake()
        {
            GD.Print("BakeSkeleton: Loading character scene...");

            var scene = GD.Load<PackedScene>(CharacterScenePath);
            if (scene == null)
            {
                GD.PrintErr($"BakeSkeleton: Cannot load scene: {CharacterScenePath}");
                QuitGame();
                return;
            }

            var instance = scene.Instantiate<Node3D>();
            AddChild(instance);

            // Find Skeleton3D and AnimationPlayer anywhere in the scene tree
            Skeleton3D skel = null;
            AnimationPlayer animPlayer = null;

            FindNodes(instance, ref skel, ref animPlayer);

            if (skel == null)
            {
                GD.PrintErr("BakeSkeleton: No Skeleton3D found in scene");
                QuitGame();
                return;
            }
            if (animPlayer == null)
            {
                GD.PrintErr("BakeSkeleton: No AnimationPlayer found in scene");
                QuitGame();
                return;
            }

            GD.Print($"BakeSkeleton: Found Skeleton3D ({skel.GetBoneCount()} bones) + AnimationPlayer");

            // Build bone index map
            var boneIndices = new List<int>();
            foreach (var name in BoneNames)
            {
                int idx = skel.FindBone(name);
                if (idx >= 0)
                    boneIndices.Add(idx);
                else
                    GD.PrintErr($"BakeSkeleton: Bone not found: {name}");
            }

            if (boneIndices.Count == 0)
            {
                string allBones = "";
                for (int i = 0; i < skel.GetBoneCount(); i++)
                    allBones += (i > 0 ? ", " : "") + skel.GetBoneName(i);
                GD.PrintErr($"BakeSkeleton: All bones: {allBones}");
                GD.PrintErr($"BakeSkeleton: No bones matched");
                QuitGame();
                return;
            }

            int boneCount = boneIndices.Count;
            var animList = animPlayer.GetAnimationList();

            GD.Print($"BakeSkeleton: {boneCount} bones, {animList.Length} animations");

            using var f = FileAccess.Open(OutputPath, FileAccess.ModeFlags.Write);
            if (f == null)
            {
                GD.PrintErr($"BakeSkeleton: Cannot open output: {OutputPath}");
                QuitGame();
                return;
            }

            const float FPS = 60f;

            // Header
            f.Store32(0x4C454B53); // "SKEL"
            f.Store32(1);
            f.Store32((uint)boneCount);
            f.Store32((uint)animList.Length);

            // Bone names (using the actual Skeleton3D names, not our input names)
            foreach (int bi in boneIndices)
            {
                string realName = skel.GetBoneName(bi);
                var bytes = System.Text.Encoding.UTF8.GetBytes(realName);
                f.Store32((uint)bytes.Length);
                f.StoreBuffer(bytes);
            }

            // Rest pose Hips position for reference (used for frame-relative normalization later)
            int hipsBoneIdx = skel.FindBone("mixamorig_Hips");

            GD.Print($"BakeSkeleton: Rest Hips index={hipsBoneIdx}");

            // Each animation
            foreach (var animName in animList)
            {
                var anim = animPlayer.GetAnimation(animName);
                float duration = anim.Length;
                int frameCount = (int)Math.Ceiling(duration * FPS) + 1;

                GD.Print($"BakeSkeleton: '{animName}' duration={duration:F2}s frames={frameCount}");

                var nameBytes = System.Text.Encoding.UTF8.GetBytes(animName);
                f.Store32((uint)nameBytes.Length);
                f.StoreBuffer(nameBytes);
                f.Store32((uint)frameCount);

                // Assign animation explicitly before seeking (robustness)
                animPlayer.AssignedAnimation = animName;

                for (int frame = 0; frame < frameCount; frame++)
                {
                    float time = Math.Min(frame / FPS, duration);
                    animPlayer.Seek(time, true);

                    // Transform bones into Hips' local coordinate system using AffineInverse.
                    // This gives correct positions in character-local space:
                    //   X = local right, Y = local up, Z = local forward
                    // so the runtime rotation formula (FacingYaw) works correctly.
                    Transform3D hipsInv = Transform3D.Identity;
                    if (hipsBoneIdx >= 0)
                        hipsInv = skel.GetBoneGlobalPose(hipsBoneIdx).AffineInverse();

                    foreach (int boneIdx in boneIndices)
                    {
                        var pose = skel.GetBoneGlobalPose(boneIdx);
                        var localPos = hipsInv * pose.Origin;
                        f.StoreFloat(localPos.X);
                        f.StoreFloat(localPos.Y);
                        f.StoreFloat(localPos.Z);
                    }
                }
            }

            f.Close();
            GD.Print($"BakeSkeleton: Done -> {OutputPath}");

            QuitGame();
        }

        private static void FindNodes(Node parent, ref Skeleton3D skel, ref AnimationPlayer animPlayer)
        {
            if (skel != null && animPlayer != null) return;

            if (parent is Skeleton3D s) skel = s;
            if (parent is AnimationPlayer a) animPlayer = a;

            int childCount = parent.GetChildCount();
            for (int i = 0; i < childCount; i++)
                FindNodes(parent.GetChild(i), ref skel, ref animPlayer);
        }

        private void QuitGame()
        {
            if (!Engine.IsEditorHint())
                GetTree().Quit();
        }
    }
}
