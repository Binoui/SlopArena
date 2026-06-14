using Godot;
using SlopArena.Shared;
using System;
using System.Collections.Generic;

namespace SlopArena.Tools
{
	/// <summary>
	/// Attach this to a Node in a test scene, set the paths, and run (F6).
	/// It bakes the skeleton's bone positions per animation frame into a .bin file.
	/// </summary>
	[Tool]
	public partial class BakeSkeletonTool : Node
	{
		[Export] public NodePath SkeletonPath;
		[Export] public NodePath AnimPlayerPath;
		[Export] public Godot.Collections.Array<string> BoneNames = new();
		[Export] public string OutputPath = "res://data/manki_skeleton.bin";

		private bool _hasBaked;

		public override void _Ready()
		{
			if (Engine.IsEditorHint() && !_hasBaked)
			{
				Bake();
			}
		}

		public void Bake()
		{
			_hasBaked = true;

			var skel = GetNode<Skeleton3D>(SkeletonPath);
			var animPlayer = GetNode<AnimationPlayer>(AnimPlayerPath);

			if (skel == null || animPlayer == null)
			{
				GD.PrintErr("BakeSkeleton: Skeleton or AnimationPlayer not found");
				return;
			}

			if (BoneNames.Count == 0)
			{
				for (int i = 0; i < skel.GetBoneCount(); i++)
					BoneNames.Add(skel.GetBoneName(i));
			}

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
				GD.PrintErr("BakeSkeleton: No bones found");
				return;
			}

			int boneCount = boneIndices.Count;
			var animList = animPlayer.GetAnimationList();

			GD.Print($"BakeSkeleton: {boneCount} bones, {animList.Length} animations");

			using var f = Godot.FileAccess.Open(OutputPath, Godot.FileAccess.ModeFlags.Write);
			if (f == null)
			{
				GD.PrintErr($"BakeSkeleton: Cannot open output: {OutputPath}");
				return;
			}

			const float FPS = 60f;

			// Header: magic + version + boneCount + animCount
			f.Store32(0x4C454B53); // "SKEL"
			f.Store32(1);          // version
			f.Store32((uint)boneCount);
			f.Store32((uint)animList.Length);

			// Bone names
			foreach (var name in BoneNames)
			{
				var bytes = System.Text.Encoding.UTF8.GetBytes(name);
				f.Store32((uint)bytes.Length);
				f.StoreBuffer(bytes);
			}

			// Each animation
			foreach (var animName in animList)
			{
				var anim = animPlayer.GetAnimation(animName);
				float duration = anim.Length;
				int frameCount = (int)Math.Ceiling(duration * FPS) + 1;

				GD.Print($"BakeSkeleton: '{animName}' duration={duration:F2}s frames={frameCount}");

				// Header
				var nameBytes = System.Text.Encoding.UTF8.GetBytes(animName);
				f.Store32((uint)nameBytes.Length);
				f.StoreBuffer(nameBytes);
				f.Store32((uint)frameCount);

				// Sample each frame
				for (int frame = 0; frame < frameCount; frame++)
				{
					float time = Math.Min(frame / FPS, duration);
					animPlayer.Seek(time, true);
					skel.ForceUpdateAllBoneTransforms();

					foreach (int boneIdx in boneIndices)
					{
						var pose = skel.GetBoneGlobalPose(boneIdx);
						f.StoreFloat(pose.Origin.X);
						f.StoreFloat(pose.Origin.Y);
						f.StoreFloat(pose.Origin.Z);
					}
				}
			}

			f.Close();
			GD.Print($"BakeSkeleton: Done -> {OutputPath}");
		}
	}
}
