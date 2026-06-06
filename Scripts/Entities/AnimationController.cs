#nullable enable
using Godot;
using System;

/// <summary>
/// Animation controller that drives AnimationTree parameters.
/// With AnimationTree active, the AnimationPlayer is "owned" by the tree
/// — never call animPlayer.Play() directly, always use the tree parameters.
///
/// The user's AnimationTree has:
///   [final] Blend2 (0=loco, 1=action)
///         /            \
///   [loco Blend2]  [action StateMachine]
///   idle ↔ run      idle → jump → fall → End
///                       └── LMB combo (external)
///
/// Setup:
///   _animController.Setup(animPlayer, skeleton);
///   _animController.SetAnimationTree(animTree);
///
/// Usage:
///   _animController.ProcessLocomotion(speed01);  // idle↔run blend
///   _animController.StartAction("jump");          // action via StateMachine
///   _animController.EndAction();                  // back to locomotion
/// </summary>
public partial class AnimationController : Node
{
	private AnimationPlayer? _animPlayer;
	private Skeleton3D? _skeleton;
	private AnimationTree? _animTree;
	private bool _isInAction = false;

	private static bool _tracksFixed = false;

	// AnimationTree parameter paths
	private const string LocoBlendParam = "parameters/locomotion/blend_amount";
	private const string FinalBlendParam = "parameters/final/blend_amount";
	private const string ActionPlaybackPath = "parameters/action/playback";
	private const string LMBPlaybackPath = "parameters/action/LMB/playback";

	private AnimationNodeStateMachinePlayback? GetActionPlayback()
	{
		if (_animTree == null) return null;
		var val = _animTree.Get(ActionPlaybackPath);
		if (val.Obj is AnimationNodeStateMachinePlayback pb)
			return pb;
		return null;
	}

	private AnimationNodeStateMachinePlayback? GetLMBPlayback()
	{
		if (_animTree == null) return null;
		var val = _animTree.Get(LMBPlaybackPath);
		if (val.Obj is AnimationNodeStateMachinePlayback pb)
			return pb;
		return null;
	}

	public void Setup(AnimationPlayer animPlayer, Skeleton3D? skeleton)
	{
		_animPlayer = animPlayer;
		_skeleton = skeleton;
		if (_animPlayer != null)
			_animPlayer.PlaybackDefaultBlendTime = 0.15f;
		if (_animPlayer != null && _skeleton != null)
			FixAnimationTracks();
	}

	public void SetupAnimationTree(AnimationTree animTree)
	{
		_animTree = animTree;
	}

	// ── PUBLIC API ──

	/// <summary>Drive the idle↔run blend. 0=idle, 1=run. Ignored during actions.</summary>
	public void ProcessLocomotion(float speed01)
	{
		if (_animTree == null) return;
		if (_isInAction) return;
		_animTree.Set(LocoBlendParam, Math.Clamp(speed01, 0f, 1f));
	}

	/// <summary>
	/// Start an action via StateMachine Travel.
	/// stateName must match a state in the action StateMachine (e.g. "jump", "LMB").
	/// </summary>
	public void StartAction(string stateName)
	{
		var pb = GetActionPlayback();
		if (pb == null) return;

		GD.Print($"[AnimController] Travel → {stateName}");
		_animTree?.Set(FinalBlendParam, 1.0f);
		pb.Travel(stateName);
		_isInAction = true;
	}

	/// <summary>End current action and return to idle.</summary>
	public void EndAction()
	{
		_isInAction = false;
		var pb = GetActionPlayback();
		pb?.Travel("idle");
		_animTree?.Set(FinalBlendParam, 0.0f);
	}

	/// <summary>
	/// Travel within the LMB sub-machine for combo chaining.
	/// </summary>
	public void RequestSubAction(string subMachine, string targetState)
	{
		var pb = subMachine == "LMB" ? GetLMBPlayback() : null;
		if (pb == null) return;

		GD.Print($"[AnimController] sub Travel → {targetState}");
		pb.Travel(targetState);
		_isInAction = true;
	}

	public bool IsActionActive() => _isInAction;

	/// <summary>No-op — timer removed, StateMachine handles lifecycle.</summary>
	public void ProcessActionTimer(float _) { }

	// ── INTERNAL ──

	private void FixAnimationTracks()
	{
		if (_animPlayer == null || _skeleton == null) return;

		_animPlayer.RootNode = _skeleton.GetPath();
		if (_tracksFixed) return;

		int fixedCount = 0, totalTracks = 0;

		foreach (var animName in _animPlayer.GetAnimationList())
		{
			var anim = _animPlayer.GetAnimation(animName);
			if (anim == null) continue;

			for (int i = 0; i < anim.GetTrackCount(); i++)
			{
				totalTracks++;
				var oldPath = anim.TrackGetPath(i);
				string pathStr = oldPath.ToString();
				int colonIdx = pathStr.LastIndexOf(':');
				if (colonIdx < 0) continue;
				string boneSubname = pathStr.Substring(colonIdx);
				var newPath = new NodePath(boneSubname);
				if (newPath != oldPath)
				{
					anim.TrackSetPath(i, newPath);
					fixedCount++;
				}
			}
		}

		_tracksFixed = true;
		GD.Print($"[AnimController] Fixed {fixedCount}/{totalTracks} tracks");
	}

	// ── SCENE HELPERS ──

	public Skeleton3D? FindSkeleton(Node node)
	{
		if (node is Skeleton3D sk) return sk;
		foreach (var c in node.GetChildren())
		{
			var r = FindSkeleton(c);
			if (r != null) return r;
		}
		return null;
	}

	public AnimationPlayer? FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer ap) return ap;
		foreach (var c in node.GetChildren())
		{
			var r = FindAnimationPlayer(c);
			if (r != null) return r;
		}
		return null;
	}
}
