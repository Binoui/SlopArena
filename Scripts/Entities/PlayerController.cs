#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena;

/// <summary>
/// Thin orchestrator: delegates movement to MovementComponent and animation to AnimationController.
/// Retains combat logic, NPC mode, input handling, and public API.
///
/// Movement is camera-relative: Z/S moves forward/backward relative to camera facing,
/// Q/D strafes left/right. Input snaps to 8 directions.
/// Dash works on ground and in air (1s duration, invincibility).
/// Uses Smash-style % system: damage increases %, knockback scales with %.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    // ==========================================
    // CLASS / CHARACTER DEFINITION
    // ==========================================

    private CharacterDefinition _charDef;
    private CharacterClass _playerClass = CharacterClass.Manki;
    private ArenaDefinition _arenaDef;
    private BakedAnimationData? _bakedData;

    public void SetClass(CharacterClass pc)
    {
        _playerClass = pc;
        _charDef = CharacterRegistry.Get(pc);
    }

    public new CharacterClass GetClass() => _playerClass;
    public CharacterDefinition CharDef => _charDef;

    // ==========================================
    // HURTBOX / %
    // ==========================================

    private Hurtbox? _hurtbox;

    public ushort GetDamagePercent() => _movementComponent.DamagePercent;

    // ==========================================
    // COMPONENTS
    // ==========================================

    private MovementComponent _movementComponent = null!;
    private AnimationController _animationController = null!;
    private StateMachine? _fsm;
    private InputController _inputCtrl = new();
    private CameraMount? _camera;
    private CombatComponent? _combatComponent;
    private TargetLockSystem? _targetLock;
    private PlayerModel? _playerModelHelper;
    private Vector3 _moveDirection = Vector3.Zero;
    private Node3D? _playerModel;
    private BoneHurtboxSetup? _boneHurtboxes;
    /// <summary>
    /// set by _UnhandledInput, consumed by BuildInputState
    /// </summary>
    public byte _pendingSlotPress;
    /// <summary>
    /// track animation changes during combo chain
    /// </summary>
    private byte _lastComboStage;

    // ── Ability system ──
    private Ability? _activeAbility;
    private float? _abilityAimYaw;
    private ushort? _abilityAimDistance;

    /// <summary>
    /// camera-relative (X=camRight, Y=camForward)
    /// </summary>
    private Vector2 _snappedInputDirection = Vector2.Zero;

    private GroundArrowIndicator? _groundArrowIndicator;
    private DebugOverlay? _debugOverlay;

    // ==========================================
    // RESPAWN STATE (Player + NPC)
    // ==========================================

    private bool _isPlayerControlled = true;
    private bool _isNPC = false;
    private float _respawnTimer = 0f;
    /// <summary>
    /// 20 seconds for both player and NPCs
    /// </summary>
    private const float RespawnDelay = 20.0f;
    /// <summary>
    /// Camera target during respawn
    /// </summary>
    private Vector3 _deathPosition = Vector3.Zero;
    private float _npcHitFlashTimer = 0f;
    private MeshInstance3D? _npcMesh;
    private float _npcOriginalEmission = 1.5f;
    /// <summary>
    /// Brief white flash on hit impact
    /// </summary>
    private float _hitFlashTimer = 0f;

    public void SetNPC(bool isNpc)
    {
        _isNPC = isNpc;
        _isPlayerControlled = !isNpc;
    }

    public bool IsNPC() => _isNPC;
    public bool IsAlive() => _respawnTimer <= 0f;
    /// <summary>
    /// Alias for external code
    /// </summary>
    /// <returns></returns>
    public bool IsNpcAlive() => IsAlive();
    public float GetRespawnTimeRemaining() => _respawnTimer;
    public Vector3 GetDeathPosition() => _deathPosition;

    // ==========================================
    // UI STATE
    // ==========================================

    public bool IsSpellBookOpen { get; set; } = false;
    public bool IsEscapeMenuOpen { get; set; } = false;

    // ==========================================
    // EVENTS
    // ==========================================

    public event Action<float, float, float, float, float>? OnStateUpdated;
    /// <summary>
    /// slotIndex, for HUD flash
    /// </summary>
    public event Action<int>? OnAbilityUsed;

    // ==========================================
    // PUBLIC GETTERS
    // ==========================================

    public float GetVelZ() => Velocity.Y;
    public CombatComponent? GetCombatComponent() => _combatComponent;
    public float GetDashCooldown() => _movementComponent.DashCooldownRemaining;
    public float GetSlotCooldown(int slotIndex)
    {
        var state = _movementComponent.State;
        return slotIndex switch
        {
            0 => state.Cooldown0,
            1 => state.Cooldown1,
            2 => state.Cooldown2,
            3 => state.Cooldown3,
            4 => state.Cooldown4,
            5 => state.Cooldown5,
            _ => 0
        };
    }
    public byte GetComboStage() => _movementComponent.State.ComboStage;
    public ushort GetComboTimerTicks() => _movementComponent.State.ComboTimerTicks;
    public Vector3 MoveDirection => _moveDirection;
    /// <summary>Camera yaw in radians (0 = looks along -Z). For abilities that need initial aim.</summary>
    public float GetCameraYaw() => _camera != null ? _camera.GetCameraYaw() : GlobalRotation.Y;
    /// <summary>Get the FSM state machine for animation/movement control.</summary>
    public StateMachine? GetFSM() => _fsm;

    public void SetupCombat(LocalServerBridge? simulation, ArenaDefinition arenaDef, ulong entityId, SpellVFXManager? spellVFX = null)
    {
        _arenaDef = arenaDef;

        // Create target lock system ONLY for player-controlled entities
        if (_isPlayerControlled)
        {
            _targetLock = new TargetLockSystem
            {
                Name = "TargetLockSystem",
                Camera = _camera?.GetCamera(),
                LockRange = 20f,
                LockAngle = 45f,
                UpdateInterval = 0.1f,
            };
            AddChild(_targetLock);
        }

        // Create combat component with target lock
        _combatComponent = new CombatComponent();
        _combatComponent.Name = "CombatComponent";
        _combatComponent.Setup(this, simulation!, entityId, spellVFX, _targetLock);
        _combatComponent.OnTakeDamage += OnCombatTakeDamage;
        AddChild(_combatComponent);
    }

    /// <summary>Visual feedback when this entity takes damage (hit flash + reaction).</summary>
    private void OnCombatTakeDamage(float damage, float kbX, float kbY, float kbZ)
    {
        // Hit flash
        _hitFlashTimer = 0.15f;
        if (_isNPC) _npcHitFlashTimer = 0.15f;

        // Hit reaction animation
        float kbMag = Mathf.Sqrt((kbX * kbX) + (kbY * kbY) + (kbZ * kbZ));
        string hitAnim = kbMag < 10f ? "hit_small" : kbMag < 20f ? "hit_medium" : "hit_hard";
        var hitState = _fsm?.GetState<HitReactionState>("hit_reaction");
        if (hitState != null)
        {
            hitState.HitAnimName = hitAnim;
            // Match server's hitstun duration so animation lock equals actual hitstun
            _movementComponent.State.AnimLockTicks = _movementComponent.State.HitstunTicks;
        }
        _fsm?.TransitionTo("hit_reaction");
    }

    // ==========================================
    // INITIALIZATION
    // ==========================================

    public override void _Ready()
    {
        _charDef = CharacterRegistry.Get(_playerClass);
        _arenaDef = ArenaRegistry.Get("pit");
        SetupInputMap();

        UpDirection = Vector3.Up;
        CollisionLayer = 1;
        CollisionMask = 1;
        FloorStopOnSlope = true;
        FloorMaxAngle = 45.0f;

        var capsule = new CapsuleShape3D { Radius = _charDef.CapsuleRadius, Height = _charDef.CapsuleHeight };
        AddChild(new CollisionShape3D { Shape = capsule });

        // Components
        _movementComponent = new MovementComponent(this);
        _movementComponent.Setup(_charDef, _arenaDef);

        _animationController = new AnimationController { Name = "AnimationController" };
        AddChild(_animationController);

        // Model + AnimationPlayer
        _playerModelHelper = new PlayerModel(this, _charDef, _playerClass, _bakedData, _isNPC);
        _playerModel = _playerModelHelper.Load();
        AnimationPlayer? animPlayer = null;
        Skeleton3D? skeleton = null;

        if (_playerModel != null)
        {
            skeleton = _animationController.FindSkeleton(_playerModel);

            // Bone-attached props (aerosol + lighter) for Manki
            if (_playerClass == CharacterClass.Manki && skeleton != null)
            {
                var weaponAttach = new MankiWeaponAttach { Name = "MankiWeaponAttach" };
                AddChild(weaponAttach);
                weaponAttach.Setup(skeleton);
            }

            // Check if model already has an AnimationPlayer (embedded anims in GLB)
            animPlayer = _animationController.FindAnimationPlayer(_playerModel);
            if (animPlayer != null)
            {
                // Fix RootNode: point to the GLB root (parent of armature), so track
                // paths like "Armature/Skeleton3D:mixamorig_Hips" resolve correctly.
                if (skeleton != null)
                {
                    Node? glbRoot = skeleton.GetParent()?.GetParent();
                    if (glbRoot != null)
                        animPlayer.RootNode = glbRoot.GetPath();
                }
            }
            else
            {
                // Create an AnimationPlayer for models without embedded anims (Knight.glb)
                animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
                _playerModel.AddChild(animPlayer);
                if (skeleton != null)
                {
                    Node? glbRoot = skeleton.GetParent()?.GetParent();
                    if (glbRoot != null)
                        animPlayer.RootNode = glbRoot.GetPath();
                }

                // Create "default" library
                var lib = new AnimationLibrary();
                animPlayer.AddAnimationLibrary("default", lib);
            }
        }

        if (animPlayer == null)
        {
            // Fallback: create our own AnimationPlayer
            animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
            (_playerModel ?? this).AddChild(animPlayer);
            if (skeleton != null)
            {
                Node? glbRoot = skeleton.GetParent()?.GetParent();
                if (glbRoot != null)
                    animPlayer.RootNode = glbRoot.GetPath();
            }
            var lib = new AnimationLibrary();
            animPlayer.AddAnimationLibrary("default", lib);
        }

        _animationController.Setup(animPlayer, skeleton);

        // Find AnimationTree (created in .tscn by user), build StateMachine from data
        if (_playerModel != null)
        {
            var animTree = _playerModel.GetNodeOrNull<AnimationTree>("AnimationTree");
            if (animTree != null)
            {
                if (animPlayer != null)
                    animTree.TreeRoot = AnimationTreeBuilder.Build(animPlayer, _charDef);
                _animationController.SetupAnimationTree(animTree);
            }

            // Find FSM in the model scene (manki.tscn)
            _fsm = _playerModel.GetNodeOrNull<StateMachine>("FSM");
            if (_fsm == null)
                GD.PrintErr($"{_playerClass}: No FSM node found — add StateMachine named 'FSM' to model scene");

            // Apply animation TimeScales: duration = bakedFrames / DurationTicks
            _playerModelHelper?.ApplyAnimationTimeScales(animTree, _charDef);
        }

        // Initialize StateMachine deferred (needs AnimationTree to settle in tree)
        Callable.From(() =>
        {
            _fsm?.Initialize(this, _movementComponent, _inputCtrl);
            _fsm?.TransitionTo("idle");
        }).CallDeferred();

        // Register programmatically-added states (not in .tscn)
        _fsm?.AddState(new JumpState());

        // Bone-attached hurtboxes (Smash-style — multiple spheres on skeleton bones)
        if (skeleton != null)
        {
            _boneHurtboxes = new BoneHurtboxSetup { Name = "BoneHurtboxes" };
            AddChild(_boneHurtboxes);
            _boneHurtboxes.Build(skeleton, BoneHurtboxSetup.DefaultHumanoid());
        }
        // Visual feedback on damage (hit flash + hit reaction)
        // Subscribed in SetupCombat via OnCombatTakeDamage

        // Ground arrow indicator
        _groundArrowIndicator = new GroundArrowIndicator { Name = "GroundArrow" };
        AddChild(_groundArrowIndicator);

        if (_isPlayerControlled)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        _debugOverlay = new DebugOverlay(this, Name == "Player");
        AddChild(_debugOverlay);
    }

    // ==========================================
    // ==========================================
    // INPUT MAP SETUP
    // ==========================================

    private void SetupInputMap()
    {
        void Add(string n, InputEventKey k) { if (!InputMap.HasAction(n)) InputMap.AddAction(n); InputMap.ActionAddEvent(n, k); }
        Add("move_forward", new InputEventKey { PhysicalKeycode = Key.W });
        Add("move_back", new InputEventKey { PhysicalKeycode = Key.S });
        Add("move_left", new InputEventKey { PhysicalKeycode = Key.A });
        Add("move_right", new InputEventKey { PhysicalKeycode = Key.D });
        Add("jump", new InputEventKey { PhysicalKeycode = Key.Space });
        Add("dash", new InputEventKey { PhysicalKeycode = Key.Shift });
        Add("crouch", new InputEventKey { PhysicalKeycode = Key.C });
        Add("ability_q", new InputEventKey { PhysicalKeycode = Key.Q });
        Add("ability_e", new InputEventKey { PhysicalKeycode = Key.E });
        Add("ability_r", new InputEventKey { PhysicalKeycode = Key.R });
        Add("ability_f", new InputEventKey { PhysicalKeycode = Key.F });
        Add("spellbook_toggle", new InputEventKey { Keycode = Key.B });
        Add("ui_cancel", new InputEventKey { Keycode = Key.Escape });
        Add("trinket", new InputEventKey { Keycode = Key.G });
        Add("tech", new InputEventKey { Keycode = Key.T });
        SettingsUI.LoadBindings();
    }

    // ════════════════════════════════════════
    // INPUT (everything in _Input — embedded editor eats events in _UnhandledInput)
    // ════════════════════════════════════════

    public override void _Input(InputEvent @event)
    {
        if (!_isPlayerControlled) return;

        // If escape menu is open, skip all game input
        if (IsEscapeMenuOpen)
            return;

        // Camera orbit
        if (@event is InputEventMouseMotion mm && _camera != null && _activeAbility == null)
            _camera.RotateCamera(mm.Relative);

        // Click to capture mouse
        if (@event is InputEventMouseButton cb && cb.Pressed && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;

        // Mouse wheel zoom
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { _camera?.ZoomCamera(-1f); return; }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _camera?.ZoomCamera(1f); return; }
        }

        // Attack clicks
        if (@event is InputEventMouseButton ib && ib.Pressed && _combatComponent != null)
        {
            if (ib.ButtonIndex == MouseButton.Left)
            {
                if (GetSlotCooldown(0) > 0) return;
                bool airborne = !_movementComponent.IsGrounded;
                ActivateAbility(AbilityFactory.Create(0, airborne, _charDef));
                GetViewport().SetInputAsHandled();
            }
            else if (ib.ButtonIndex == MouseButton.Right)
            {
                if (GetSlotCooldown(1) > 0) return;
                bool airborne = !_movementComponent.IsGrounded;
                ActivateAbility(AbilityFactory.Create(1, airborne, _charDef));
                GetViewport().SetInputAsHandled();
            }
        }

        // Forward input to active ability (mouse motion during aiming, etc.)
        if (_activeAbility != null)
        {
            _activeAbility.OnInput(@event);
            if (_activeAbility != null) return;
        }

        // Ability keys (Q, E, R, F)
        if (Input.IsActionJustPressed("ability_q"))
        {
            if (GetSlotCooldown(2) > 0) return;
            ActivateAbility(AbilityFactory.Create(2, false, _charDef));
            GetViewport().SetInputAsHandled(); return;
        }
        if (Input.IsActionJustPressed("ability_e"))
        {
            if (GetSlotCooldown(3) > 0) return;
            ActivateAbility(AbilityFactory.Create(3, false, _charDef));
        }
        if (Input.IsActionJustPressed("ability_r"))
        {
            if (GetSlotCooldown(4) > 0) return;
            ActivateAbility(AbilityFactory.Create(4, false, _charDef));
        }
        if (Input.IsActionJustPressed("ability_f"))
        {
            if (GetSlotCooldown(5) > 0) return;
            ActivateAbility(AbilityFactory.Create(5, false, _charDef));
        }
    }

    // Remove _UnhandledInput entirely — everything is in _Input now

    // ==========================================
    // PROCESS (timers, NPC)
    // ==========================================

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _inputCtrl.Poll();
        _debugOverlay?.UpdateDebug(_fsm?.CurrentStateName ?? "?", Velocity.Y, IsOnFloor());
        _debugOverlay?.UpdateDamage(_movementComponent.DamagePercent);

        // ── Centralized jump input ──
        // All jump transitions (ground jump, double jump after hitstun)
        // are handled here rather than in individual states.
        string currentState = _fsm?.CurrentStateName ?? "";
        if (_inputCtrl.JumpJustPressed && currentState is "idle" or "run" or "fall")
        {
            _fsm?.TransitionTo("jump");
        }

        // ── Active ability tick ──
        if (_activeAbility != null)
        {
            var result = _activeAbility.Tick(this, dt);
            if (result != null)
            {
                // Store aim data for BuildInputState
                _abilityAimYaw = result.Value.AimYaw;
                _abilityAimDistance = result.Value.AimDistance;

                if (result.Value.ActiveSlot.HasValue)
                {
                    // Ability fired — set the slot and deactivate
                    _pendingSlotPress = result.Value.ActiveSlot.Value;
                    OnAbilityUsed?.Invoke(result.Value.ActiveSlot.Value - 1);
                    DeactivateAbility();
                }
            }
            // null result means ability is still active (charging, aiming, etc.)
        }

        // Respawn timer (both player and NPCs)
        if (_respawnTimer > 0f)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0f)
            {
                DoRespawn();
            }
        }

        // Hit flash timer (ticked independently)
        if (_hitFlashTimer > 0f)
            _hitFlashTimer -= dt;

        // NPC emission flash (red glow on hit) — complements state color
        if (_isNPC)
        {
            if (_npcHitFlashTimer > 0f)
            {
                _npcHitFlashTimer -= dt;
                if (_npcMesh?.MaterialOverride is StandardMaterial3D m) m.EmissionEnergyMultiplier = 8f;
            }
            else if (_npcMesh?.MaterialOverride is StandardMaterial3D m && !Mathf.IsEqualApprox(m.EmissionEnergyMultiplier, _npcOriginalEmission))
                m.EmissionEnergyMultiplier = _npcOriginalEmission;

            // Hide while respawning
            Visible = _respawnTimer <= 0f;
        }
    }

    // ==========================================
    // PHYSICS PROCESS
    // ==========================================

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // NPCs: read injected AI input instead of keyboard
        if (!_isPlayerControlled)
        {
            var npcInput = BuildInputState();
            // Movement simulation handled by ServerSimulation (in MatchManager._PhysicsProcess)

            OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
            return;
        }

        var input = BuildInputState();

        var simState = _movementComponent.State.State;
        var slot = _movementComponent.State.AttackSlot;

        // React to sim's ActionState for FSM transitions
        if (_fsm != null)
        {
            var simComboStage = _movementComponent.State.ComboStage;

            // Combo chain: animation changed (same "attack" state, next stage)
            if (simState == ActionState.Attacking && _fsm.IsInState("attack") &&
                simComboStage != _lastComboStage && simComboStage > 0)
            {
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = ability.GetAnimationName(simComboStage);
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.ChainTo(animName);
                }
            }

            // First attack: transition FSM to "attack"
            if (simState == ActionState.Attacking && !_fsm.IsInState("attack"))
            {
                // Sim started an attack — play the FSM animation
                var ability = _charDef.GetSlotAbility(slot > 0 ? slot - 1 : 0, !_movementComponent.IsGrounded);
                string animName = ability.GetAnimationName(simComboStage);
                var attackState = _fsm.GetAttackState();
                if (attackState != null)
                {
                    attackState.NextAnimName = animName;
                    _fsm.TransitionTo("attack");
                }
            }

            // Track combo stage for animation chaining
            _lastComboStage = _movementComponent.State.ComboStage;
        }

        // Hitstun FSM transition (fallback if OnCombatTakeDamage event didn't fire)
        if (_fsm != null && simState == ActionState.Hitstun && !_fsm.IsInState("hit_reaction"))
        {
            _movementComponent.State.AnimLockTicks = _movementComponent.State.HitstunTicks;
            _fsm.TransitionTo("hit_reaction");
        }

        // Dash FSM transition
        if (_fsm != null && simState == ActionState.Dashing && !_fsm.IsInState("dash"))
        {
            var dashState = _fsm.GetState<DashState>("dash");
            if (dashState != null)
            {
                // Use the direction the simulation already computed (handles FacingYaw fallback)
                float dashDirX = _movementComponent.State.DashDirX;
                float dashDirZ = _movementComponent.State.DashDirZ;
                GD.Print($"[Dash] facingYaw={_movementComponent.State.FacingYaw:F3} moveDir=({_moveDirection.X:F2},{_moveDirection.Z:F2}) dashDir=({dashDirX:F2},{dashDirZ:F2})");
                dashState.SetDirection(dashDirX, dashDirZ);
                _fsm.TransitionTo("dash");
            }
        }

        bool wasKnocked = _movementComponent.IsInKnockback();
        // Movement simulation handled by ServerSimulation (in MatchManager._PhysicsProcess)
        // _movementComponent.Tick(input);

        // Tech roll on knockback landing
        if (wasKnocked && _movementComponent.IsGrounded && !_movementComponent.IsInKnockback() && Input.IsActionJustPressed("tech"))
            _movementComponent.DoTechRoll();

        // Update ground arrow
        _groundArrowIndicator?.Update(GlobalPosition, _snappedInputDirection);

        // Animation handled by FSM states (idle/run/jump/fall/landing)
        // FSM runs in _Process — no manual Travel calls needed here

        OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
    }

    // ==========================================
    // INPUT STATE BUILDER (camera-relative, 8-direction)
    // ==========================================

    private InputState BuildInputState()
    {
        var result = _inputCtrl.BuildInputState(
            camera: _camera,
            bodyYaw: GlobalRotation.Y,
            isNPC: _isNPC,
            isAiming: _activeAbility != null,
            pendingSlotPress: _pendingSlotPress,
            abilityAimYaw: _abilityAimYaw,
            abilityAimDistance: _abilityAimDistance,
            fsm: _fsm
        );

        _moveDirection = result.moveDirection;
        _snappedInputDirection = result.snappedInputDirection;

        // Clear consumed frame state owned by PlayerController
        _pendingSlotPress = 0;
        _abilityAimYaw = null;
        _abilityAimDistance = null;

        return result.input;
    }

    // ==========================================
    // KNOCKBACK
    // ==========================================

    public void ApplyKnockback(Vector3 force) => _movementComponent.ApplyKnockback(force.X, force.Y, force.Z);

    /// <summary>Called by CombatComponent.TakeDamage to increase damage percent.</summary>
    public void ApplyDamageToMovement(float damage) => _movementComponent.ApplyDamage(damage);

    /// <summary>Get all bone-attached hurtbox capsules for hit detection.</summary>
    public List<(Vector3 start, Vector3 end, float radius)> GetHurtboxShapes()
    {
        if (_boneHurtboxes != null && _boneHurtboxes.Count > 0)
            return _boneHurtboxes.GetWorldCapsules();
        // Fallback: single sphere (degenerate capsule)
        return new List<(Vector3, Vector3, float)> { (GlobalPosition, GlobalPosition, _charDef.HurtboxRadius) };
    }

    // ==========================================
    // ABILITY SYSTEM
    // ==========================================

    /// <summary>
    /// Activate an ability. Deactivates any currently active ability first.
    /// </summary>
    private void ActivateAbility(Ability ability)
    {
        _activeAbility?.OnDeactivate(this);
        _activeAbility = ability;
        _abilityAimYaw = null;
        _abilityAimDistance = null;
        ability.OnActivate(this);
    }

    /// <summary>
    /// Deactivate the currently active ability (if any).
    /// </summary>
    private void DeactivateAbility()
    {
        _activeAbility?.OnDeactivate(this);
        _activeAbility = null;
    }

    // ==========================================
    // VISUAL FEEDBACK (called by FSM states in Enter/Exit)
    // ==========================================

    /// <summary>When true, state colors show on the model (toggled by F3 debug).</summary>
    public bool DebugEmissionEnabled { get; set; }

    /// <summary>Set emission glow on the character model. Called by states in Enter().</summary>
    public void SetModelEmission(Color color, float energy = 1.5f)
    {
        if (!DebugEmissionEnabled) return;
        if (_playerModel != null)
            ApplyEmissionRecursive(_playerModel, color, energy);
    }

    /// <summary>Remove emission glow. Called by states in Exit().</summary>
    public void ClearModelEmission()
    {
        if (!DebugEmissionEnabled) return;
        if (_playerModel != null)
            ApplyEmissionRecursive(_playerModel, new Color(1, 1, 1), 0f, clear: true);
    }

    /// <summary>Hitstun emission gradient: Yellow (0%) → Red (150%+).</summary>
    public Color GetHitstunColor()
    {
        float t = Mathf.Clamp(_movementComponent.DamagePercent / 150f, 0f, 1f);
        return new Color(1f, 1f - (t * 0.9f), 0.2f - (t * 0.1f));
    }

    private static void ApplyEmissionRecursive(Node node, Color color, float energy, bool clear = false)
    {
        if (node is MeshInstance3D mesh)
        {
            if (clear)
            {
                mesh.MaterialOverride = null; // Restore original material
            }
            else
            {
                if (mesh.MaterialOverride is not StandardMaterial3D mat)
                {
                    mat = new StandardMaterial3D();
                    mesh.MaterialOverride = mat;
                }
                mat.EmissionEnabled = true;
                mat.Emission = color;
                mat.EmissionEnergyMultiplier = energy;
            }
        }
        foreach (var child in node.GetChildren())
            ApplyEmissionRecursive(child, color, energy, clear);
    }

    // ==========================================
    // DIRECTION HELPERS
    // ==========================================

    public void SetCamera(CameraMount cam)
    {
        _camera = cam;
        // Update target lock system with camera (if already created)
        if (_targetLock != null)
            _targetLock.SetCamera(cam.GetCamera());
    }
    public Vector3 GetPlayerForward() => (-Transform.Basis.Z with { Y = 0 }).Normalized();
    public Vector3 GetCameraForward() => _camera?.GetForwardDirection() ?? GetPlayerForward();
    public CameraMount? GetCamera() => _camera;
    public CharacterDefinition GetCharacterDef() => _charDef;

    /// <summary>Expose CharacterState for external code (e.g. FSM states).</summary>
    public ref CharacterState GetState() => ref _movementComponent.State;

    // ==========================================
    // MODEL LOADING
    // ==========================================

    // ==========================================
    // AI INPUT INJECTION (for NPCs)
    // ==========================================

    /// <summary>
    /// Inject synthetic input from AI controller (for NPCs).
    /// Must be called every frame before _Process() if AI-controlled.
    /// </summary>
    public void InjectInput(InputState input)
    {
        if (!_isNPC) return; // Only NPCs can have injected input
        _inputCtrl.InjectAI(input);
    }

    /// <summary>Return the current input state (read by server bridge).</summary>
    public InputState GetCurrentInput()
    {
        return BuildInputState();
    }

    /// <summary>Pass baked skeleton data for auto-computing visual model Y offset.</summary>
    public void SetBakedData(BakedAnimationData? baked) => _bakedData = baked;

    /// <summary>Log current Y positions for debug alignment check.</summary>
    public void DebugYPositions()
    {
        if (_bakedData == null) return;
        float py = _movementComponent.State.PY;
        float capsuleBottom = py - (_charDef.CapsuleHeight * 0.5f);
        float modelY = _playerModel?.Position.Y ?? 0f;
        // LeftToe_End world Y (index 10 in baked data)
        _bakedData.GetBonePosition("idle", 0, 10, out _, out float toeY, out _);
        float toeWorld = py + modelY + (toeY * _charDef.HurtboxBoneScale);
        // Hips world Y (bone index 2, always 0 in baked)
        float hipsWorld = py + modelY;
        // LeftFoot hurtbox world Y (bone index 6)
        _bakedData.GetBonePosition("idle", 0, 6, out _, out float footY, out _);
        float footWorld = py + modelY + (footY * _charDef.HurtboxBoneScale);
        GD.Print($"[Y] state.PY={py:F4} capsuleBottom={capsuleBottom:F4} floor={_arenaDef.FloorHeight} " +
                 $"modelOff={modelY:F4} sole={_charDef.ModelSoleOffset:F4} | " +
                 $"Hips_world={hipsWorld:F4} Foot_world={footWorld:F4} Toe_world={toeWorld:F4}");
    }

    /// <summary>Apply authoritative state from the simulation.</summary>
    public void ApplyServerState(CharacterState state)
    {
        // Sync position, velocity, grounded, state — sim is authority on everything now
        _movementComponent.State = state;

        // Apply to Godot body
        GlobalPosition = new Vector3(state.PX, state.PY, state.PZ);
        Velocity = new Vector3(state.VX, state.VY, state.VZ);
        GlobalRotation = new Vector3(0f, state.FacingYaw, 0f);
        MoveAndSlide();
    }

    /// <summary>
    /// Trigger an ability by slot index (for NPCs / BotController).
    /// Sets _pendingSlotPress so the sim picks it up on the next tick,
    /// same as player abilities.
    /// </summary>
    public void UseAbility(int slot)
    {
        if (!_isNPC) return;
        _pendingSlotPress = (byte)(slot + 1); // slot is 0-based, ActiveSlot is 1-based
    }

    // ==========================================
    // RESPAWN METHODS (Player + NPC)
    // ==========================================

    /// <summary>
    /// Called when knocked out of bounds (like Smash Bros).
    /// Triggers 20s respawn sequence for both player and NPCs.
    /// </summary>
    public void TriggerRespawn()
    {
        if (_respawnTimer > 0f) return; // Already respawning
        _respawnTimer = RespawnDelay;

        // Save death position for camera (so camera stays at blast zone point)
        _deathPosition = GlobalPosition;

        // Hide character and stop physics during respawn
        Visible = false;
        Velocity = Vector3.Zero;

        // Move way below the map so no collisions happen during respawn
        GlobalPosition = new Vector3(0f, -1000f, 0f);
    }

    /// <summary>
    /// Legacy method for NPC knockout (calls TriggerRespawn internally).
    /// </summary>
    public void NpcKnockOut() => TriggerRespawn();

    private void DoRespawn()
    {
        _respawnTimer = 0f;

        // Respawn at arena center, 20 units above ground (in air)
        if (_arenaDef.SpawnPoints.Length > 0)
        {
            // Use center spawn point if available, otherwise calculate center
            var centerSpawn = _arenaDef.SpawnPoints[_arenaDef.SpawnPoints.Length / 2];
            GlobalPosition = new Vector3(centerSpawn.X, centerSpawn.Y + 20f, centerSpawn.Z);
        }
        else
        {
            // Fallback: arena center
            float centerX = (_arenaDef.MinX + _arenaDef.MaxX) / 2f;
            float centerZ = (_arenaDef.MinZ + _arenaDef.MaxZ) / 2f;
            GlobalPosition = new Vector3(centerX, 20f, centerZ);
        }

        Velocity = Vector3.Zero;

        // Reset damage % on respawn (Smash-style stock system)
        _movementComponent.State.DamagePercent = 0;

        // Make visible again
        Visible = true;
    }

    // ==========================================
    // ATTACK EXECUTION HELPERS
    // ==========================================

    /// <summary>
    /// ==========================================
    /// CLICK TARGETING STATE
    /// ==========================================
    /// </summary>
    private Vector2 _storedMousePos = Vector2.Zero;
}
