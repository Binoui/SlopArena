#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Standalone animation controller extracted from PlayerController.
/// Handles Mixamo FBX animation loading, path remapping, looping behavior,
/// state machine transitions (idle/walk/run/jump), and spell cast animations.
///
/// Usage: Create as child of PlayerController, call Setup() in _Ready(),
/// then call ProcessAnimation() each frame from PostMove().
/// </summary>
public partial class AnimationController : Node
{
	// ==========================================
	// REFERENCES (set by Setup)
	// ==========================================

	private AnimationPlayer? _animPlayer;
	private Skeleton3D? _skeleton;
	private Node3D? _playerModel;
	private AnimationLibrary? _animLib;

	// ==========================================
	// ANIMATION STATE
	// ==========================================

	private string _currentAnim = "";
	private float _castAnimTimer = 0f;

	// ==========================================
	// PUBLIC STATE STRUCT
	// ==========================================

	/// <summary>
	/// Snapshot of player state passed into ProcessAnimation each frame.
	/// </summary>
	public struct AnimationState
	{
		public bool IsOnFloor;
		public float HorizontalSpeed;
		public bool IsDashing;
		public bool IsAirDodging;
		public bool IsInKnockback;
		public float CastTimerRemaining;
	}

	// ==========================================
	// PUBLIC API
	// ==========================================

	/// <summary>
	/// Store references to the animation player, skeleton, and player model.
	/// Must be called before Initialize() and ProcessAnimation().
	/// </summary>
	public void Setup(AnimationPlayer animPlayer, Skeleton3D? skeleton, Node3D? playerModel)
	{
		_animPlayer = animPlayer;
		_skeleton = skeleton;
		_playerModel = playerModel;

		// Global blend time for smooth animation transitions
		_animPlayer.PlaybackDefaultBlendTime = 0.15f;
	}

	/// <summary>
	/// Load all animations from the Pro Magic Pack directory and play idle.
	/// Should be called from PlayerController._Ready() after Setup().
	/// </summary>
	public void Initialize()
	{
		if (_animPlayer == null)
		{
			GD.PrintErr("AnimationController.Initialize(): _animPlayer is null. Call Setup() first.");
			return;
		}

		_animLib = new AnimationLibrary();
		_animPlayer.AddAnimationLibrary("default", _animLib);

		// Load all animations from the Pro Magic Pack directory
		LoadAllAnimations(_animLib);

		// Log which animations we loaded
		var loadedAnims = _animLib.GetAnimationList();
		//GD.Print($"AnimationController: Loaded {loadedAnims.Count} animations");

		// Play idle if available
		if (_animLib.HasAnimation("idle"))
		{
			_animLib.GetAnimation("idle").LoopMode = Animation.LoopModeEnum.Linear;
			_animPlayer.Play("default/idle");
		}
		else if (loadedAnims.Count > 0)
		{
			string firstAnim = loadedAnims[0];
			GD.Print($"AnimationController: Playing first available animation: {firstAnim}");
			var first = _animLib.GetAnimation(firstAnim);
			if (first != null) first.LoopMode = Animation.LoopModeEnum.Linear;
			_animPlayer.Play("default/" + firstAnim);
		}
		else
		{
			GD.Print("AnimationController: WARNING — No animations loaded at all!");
		}
	}

	/// <summary>
	/// Called each physics frame from PlayerController.PostMove().
	/// Decides which animation to play based on movement state.
	/// Cast animations take priority over movement animations.
	/// </summary>
	public void ProcessAnimation(float dt, AnimationState state)
	{
		if (_animPlayer == null) return;

		// Cast animation timer (decays in _Process)
		if (_castAnimTimer > 0f)
			_castAnimTimer -= dt;

		// Cast animation takes priority over movement
		if (_castAnimTimer > 0f)
		{
			if (!_animPlayer.IsPlaying())
				_castAnimTimer = 0f;
			return;
		}

		string targetAnim;

		// Movement state derived from player values
		if (state.IsDashing)
		{
			targetAnim = "run";
		}
		else if (state.IsAirDodging)
		{
			targetAnim = "jump";
		}
		else
		{
			bool isGrounded = state.IsOnFloor;
			float hSpeed = state.HorizontalSpeed;
			bool isWalking = hSpeed > 1f && hSpeed < 11f;
			bool isRunning = hSpeed >= 11f;

			if (!isGrounded)
				targetAnim = "jump";
			else if (isRunning)
				targetAnim = "run";
			else if (isWalking)
				targetAnim = "walk";
			else
				targetAnim = "idle";
		}

		PlayAnimWithFallback(targetAnim);
	}

	/// <summary>
	/// Update animation state each physics frame.
	/// Transition between idle, walk/run (sprint), jump, fall, dash, air-dodge, knockback.
	/// </summary>
	
	/// <summary>
	/// Access the AnimationPlayer so PlayerController can set RootNode, etc.
	/// </summary>
	public AnimationPlayer? GetAnimPlayer() => _animPlayer;

	/// <summary>
	/// Access the internal _castAnimTimer for PlayerController to check.
	/// </summary>
	public float GetCastTimer() => _castAnimTimer;

	// ==========================================
	// ANIMATION LOADING
	// ==========================================

	/// <summary>
	/// Load ALL FBX animation files from the Pro Magic Pack directory.
	/// Each FBX file contains one animation clip, named by a standardized key.
	/// </summary>
	private void LoadAllAnimations(AnimationLibrary animLib)
	{
		string animDir = "res://assets/characters/ProMagicPack/";

		var dir = DirAccess.Open(animDir);
		if (dir == null)
		{
			GD.PrintErr($"AnimationController: Cannot open animation directory: {animDir}");
			return;
		}

		dir.ListDirBegin();
		int loadedCount = 0;

		while (true)
		{
			string fileName = dir.GetNext();
			if (string.IsNullOrEmpty(fileName))
				break;

			// Only process FBX files (skip .import, directories, etc.)
			if (!fileName.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
				continue;

			if (fileName.Equals("characterMedium.fbx", StringComparison.OrdinalIgnoreCase))
				continue; // Skip the model file itself

			string fbxPath = animDir + fileName;
			string animKey = NormalizeAnimationName(fileName);

			if (LoadSingleAnimation(animLib, fbxPath, animKey))
				loadedCount++;
		}

		dir.ListDirEnd();
		//GD.Print($"AnimationController: Loaded {loadedCount} animations from {animDir}");
	}

	/// <summary>
	/// Convert a filename like "Standing Run Forward.fbx" to "run_forward"
	/// then apply specific renames to standard keys like "run", "idle", etc.
	/// </summary>
	private string NormalizeAnimationName(string fileName)
	{
		// Remove .fbx extension
		string name = fileName;
		int extIdx = name.LastIndexOf(".fbx", StringComparison.OrdinalIgnoreCase);
		if (extIdx > 0)
			name = name.Substring(0, extIdx);

		// Remove common prefixes
		if (name.StartsWith("Standing ", StringComparison.OrdinalIgnoreCase))
			name = name.Substring("Standing ".Length);
		else if (name.StartsWith("standing ", StringComparison.OrdinalIgnoreCase))
			name = name.Substring("standing ".Length);
		else if (name.StartsWith("Crouch ", StringComparison.OrdinalIgnoreCase))
			name = "crouch_" + name.Substring("Crouch ".Length);

		// Replace spaces with underscores, lowercase
		name = name.Replace(" ", "_").ToLower();

		// Remove leading whitespace only (not digits — "1h" and "2h" matter!)
		name = name.TrimStart();

		// Specific renames for common animations
		var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Idle variants → single "idle"
			{ "idle", "idle" },
			{ "idle_02", "idle" },
			{ "idle_03", "idle" },
			{ "idle_04", "idle" },

			// Movement
			{ "run_forward", "run" },
			{ "walk_forward", "walk" },
			{ "sprint_forward", "sprint" },
			{ "jump", "jump" },
			{ "jump_running", "jump_run" },
			{ "land_to_standing_idle", "land" },
			{ "jump_running_landing", "land_run" },
			{ "idle_to_crouch", "crouch_transition" },
			{ "crouch_idle", "crouch_idle" },
			{ "crouch_walk_forward", "crouch_walk" },
			{ "crouch_to_standing_idle", "stand_transition" },

			// Cast animations
			{ "1h_cast_spell_01", "cast_1h" },
			{ "2h_cast_spell_01", "cast_2h" },

			// Attack animations
			{ "1h_magic_attack_01", "attack_1h" },
			{ "1h_magic_attack_02", "attack_1h_b" },
			{ "1h_magic_attack_03", "attack_1h_c" },
			{ "2h_magic_attack_01", "attack_2h" },
			{ "2h_magic_attack_02", "attack_2h_b" },
			{ "2h_magic_attack_03", "attack_2h_c" },
			{ "2h_magic_attack_04", "attack_2h_d" },
			{ "2h_magic_attack_05", "attack_2h_e" },
			{ "2h_magic_area_attack_01", "attack_area_2h" },
			{ "2h_magic_area_attack_02", "attack_area_2h_b" },

			// Block
			{ "block_idle", "block_idle" },
			{ "block_start", "block_start" },
			{ "block_end", "block_end" },
			{ "block_react_large", "block_react" },

			// Hit reactions
			{ "react_small_from_front", "hit_small_front" },
			{ "react_small_from_back", "hit_small_back" },
			{ "react_small_from_left", "hit_small_left" },
			{ "react_small_from_right", "hit_small_right" },
			{ "react_large_from_front", "hit_large_front" },
			{ "react_large_from_back", "hit_large_back" },
			{ "react_large_from_left", "hit_large_left" },
			{ "react_large_from_right", "hit_large_right" },

			// Death
			{ "react_death_forward", "death_forward" },
			{ "react_death_backward", "death_backward" },
			{ "react_death_left", "death_left" },
			{ "react_death_right", "death_right" },

			// Turn
			{ "turn_left_90", "turn_left" },
			{ "turn_right_90", "turn_right" },
		};

		if (renames.ContainsKey(name))
			return renames[name];

		return name;
	}

	/// <summary>
	/// Load a single animation from an FBX file and add it to the library.
	/// Handles both scene-based FBX (with AnimationPlayer) and direct Animation resources.
	/// </summary>
	private bool LoadSingleAnimation(AnimationLibrary animLib, string fbxPath, string animKey)
	{
		if (!ResourceLoader.Exists(fbxPath))
		{
			GD.Print($"AnimationController: File not found: {fbxPath}");
			return false;
		}

		// Try loading as PackedScene first (most common for FBX animations)
		var scene = ResourceLoader.Load<PackedScene>(fbxPath);
		if (scene != null)
		{
			var tempInstance = scene.Instantiate<Node>();
			if (tempInstance == null) return false;

			var animPlayerInScene = FindAnimationPlayer(tempInstance);
			if (animPlayerInScene != null)
			{
				foreach (var libName in animPlayerInScene.GetAnimationLibraryList())
				{
					var lib = animPlayerInScene.GetAnimationLibrary(libName);
					if (lib == null) continue;

					foreach (var animNameInLib in lib.GetAnimationList())
					{
						var anim = lib.GetAnimation(animNameInLib);
						if (anim == null) continue;

						// Remap bone paths: handle both "Root|" (Kenney) and bare bones (Mixamo)
						anim = RemapAnimationPaths(anim);
						if (anim != null && !animLib.HasAnimation(animKey))
						{
							animLib.AddAnimation(animKey, anim);
						}
					}
				}
			}
			else
			{
				// Try loading as direct Animation resource
				LoadDirectAnimation(animLib, fbxPath, animKey);
			}

			tempInstance.QueueFree();
			return true;
		}

		// Fallback: try direct Animation resource
		return LoadDirectAnimation(animLib, fbxPath, animKey);
	}

	/// <summary>
	/// Try loading an FBX file directly as an Animation resource (fallback).
	/// </summary>
	private bool LoadDirectAnimation(AnimationLibrary animLib, string fbxPath, string animKey)
	{
		try
		{
			var directAnim = ResourceLoader.Load<Animation>(fbxPath);
			if (directAnim != null)
			{
				directAnim = RemapAnimationPaths(directAnim);
				if (directAnim != null)
				{
					animLib.AddAnimation(animKey, directAnim);
					//GD.Print($"  Loaded direct: {animKey} <- {fbxPath}");
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			GD.Print($"AnimationController: Could not load animation from {fbxPath}: {ex.Message}");
		}
		return false;
	}

	// ==========================================
	// MIXAMO PATH REMAPPING
	// ==========================================

	/// <summary>
	/// Remap bone paths in animation tracks to match our skeleton.
	/// Our AnimationPlayer's RootNode is set to the skeleton node.
	/// Mixamo FBX animations have tracks referencing "Root/Skeleton3D" or "Root/Skeleton"
	/// which need to be stripped so paths resolve relative to the skeleton.
	/// Kenney animations use "Root|" prefix which also gets stripped.
	/// </summary>
	private Animation RemapAnimationPaths(Animation anim)
	{
		var animCopy = (Animation)anim.Duplicate();
		int trackCount = animCopy.GetTrackCount();

		// Detect the skeleton path prefix used in this animation
		// Mixamo: "Root/Skeleton3D" or "Root/Skeleton"
		// Kenney: "Root|"
		string? prefixToStrip = null;

		for (int i = 0; i < trackCount && prefixToStrip == null; i++)
		{
			string path = animCopy.TrackGetPath(i);
			if (path.Contains("Root/Skeleton3D"))
				prefixToStrip = "Root/Skeleton3D";
			else if (path.Contains("Root/Skeleton"))
				prefixToStrip = "Root/Skeleton";
			else if (path.Contains("Root|"))
				prefixToStrip = "Root|";
		}

		if (prefixToStrip == null)
			return animCopy; // No remapping needed

		//GD.Print($"  Remapping paths: stripping '{prefixToStrip}' prefix");

		for (int i = 0; i < trackCount; i++)
		{
			string trackPath = animCopy.TrackGetPath(i);

			if (trackPath.Contains(prefixToStrip))
			{
				string newPath = trackPath.Replace(prefixToStrip, "");
				animCopy.TrackSetPath(i, new NodePath(newPath));
			}
		}

		return animCopy;
	}

	// ==========================================
	// ANIMATION PLAYBACK
	// ==========================================

	/// <summary>
	/// Play an animation by key, with fallback to similar names if the exact key
	/// doesn't exist in the library. Handles crossfade and loop mode.
	/// </summary>
	private void PlayAnimWithFallback(string animName)
	{
		if (_animPlayer == null) return;
		string fullPath = "default/" + animName;

		if (_animPlayer.HasAnimation(fullPath) && _currentAnim != fullPath)
		{
			SetLoopMode(fullPath, animName);
			_animPlayer.Play(fullPath);
			_currentAnim = fullPath;
		}
		else if (!_animPlayer.HasAnimation(fullPath))
		{
			// Fallback: find any animation that starts with the same prefix
			foreach (var available in _animPlayer.GetAnimationList())
			{
				if (available.StartsWith(animName) || animName.StartsWith(available))
				{
					string fallbackPath = "default/" + available;
					if (_currentAnim != fallbackPath)
					{
						SetLoopMode(fallbackPath, available);
						_animPlayer.Play(fallbackPath);
						_currentAnim = fallbackPath;
						return;
					}
				}
			}
		}
	}

	/// <summary>
	/// Set loop mode based on animation type.
	/// Looping: idle, run, walk, crouch (and their variants).
	/// Non-looping: attacks, casts, hit reactions, death.
	/// </summary>
	private void SetLoopMode(string fullPath, string animName)
	{
		if (_animPlayer == null) return;
		var anim = _animPlayer.GetAnimation(fullPath);
		if (anim == null) return;

		// Loop movement / idle animations, not attacks/casts/reacts
		bool shouldLoop = animName == "idle" || animName == "run" || animName == "walk"
			|| animName.StartsWith("idle") || animName.StartsWith("run") || animName.StartsWith("walk")
			|| animName.StartsWith("crouch");
		anim.LoopMode = shouldLoop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
	}

	// ==========================================
	// UTILITY: TREE TRAVERSAL
	// ==========================================

	/// <summary>
	/// Recursively find a Skeleton3D in the given node tree.
	/// Used during setup to set the AnimationPlayer's RootNode.
	/// </summary>
	public Skeleton3D? FindSkeleton(Node node)
	{
		if (node is Skeleton3D sk)
			return sk;
		foreach (var child in node.GetChildren())
		{
			var result = FindSkeleton(child);
			if (result != null)
				return result;
		}
		return null;
	}

	/// <summary>
	/// Recursively find an AnimationPlayer in the given node tree.
	/// Used internally when loading FBX scenes that contain embedded anim players.
	/// </summary>
	private AnimationPlayer? FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer ap)
			return ap;
		foreach (var child in node.GetChildren())
		{
			var result = FindAnimationPlayer(child);
			if (result != null)
				return result;
		}
		return null;
	}
}
