#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

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

    public void SetClass(CharacterClass pc)
    {
        _playerClass = pc;
        _charDef = CharacterRegistry.Get(pc);
    }

    public new CharacterClass GetClass() => _playerClass;

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
    private MeshInstance3D? _firstMesh;
    private Vector3 _moveDirection = Vector3.Zero;
    private Node3D? _playerModel;
    private BoneHurtboxSetup? _boneHurtboxes;
    private bool _heavyHeld = false;

    /// <summary>
    /// Persistent damage % label above the character (Smash-style)
    /// </summary>
    private Label3D? _damagePercentLabel;

    /// <summary>
    /// Ground arrow indicator
    /// </summary>
    private MeshInstance3D? _groundArrow;
    /// <summary>
    /// camera-relative (X=camRight, Y=camForward)
    /// </summary>
    private Vector2 _snappedInputDirection = Vector2.Zero;

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
    public event Action? OnTargetNextPressed;
    public event Action<ulong>? OnLeftClickEntity;
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

    public void SetupCombat(LocalServerBridge simulation, ArenaDefinition arenaDef, ulong entityId, SpellVFXManager? spellVFX = null)
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
        _combatComponent.Setup(this, simulation, entityId, spellVFX, _targetLock);
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
            ref var s = ref _movementComponent.State;
            s.AnimLockTicks = 30;
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
        _playerModel = LoadPlayerModel();
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
                // Fix RootNode to point to our PlayerModel (paths are relative to this)
                if (skeleton != null)
                    animPlayer.RootNode = _playerModel.GetPath();
            }
            else
            {
                // Create an AnimationPlayer for models without embedded anims (Knight.glb)
                animPlayer = new AnimationPlayer { Name = "AnimationPlayer" };
                _playerModel.AddChild(animPlayer);
                if (skeleton != null)
                    animPlayer.RootNode = _playerModel.GetPath();

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
            if (skeleton != null) animPlayer.RootNode = _playerModel?.GetPath();
            var lib = new AnimationLibrary();
            animPlayer.AddAnimationLibrary("default", lib);
        }

        _animationController.Setup(animPlayer, skeleton);

        // Find AnimationTree (created in manki.tscn by the user)
        if (_playerModel != null)
        {
            var animTree = _playerModel.GetNodeOrNull<AnimationTree>("AnimationTree");
            if (animTree != null)
                _animationController.SetupAnimationTree(animTree);

            // Find FSM in the model scene (manki.tscn)
            _fsm = _playerModel.GetNodeOrNull<StateMachine>("FSM");
            if (_fsm == null)
                GD.PrintErr($"{_playerClass}: No FSM node found — add StateMachine named 'FSM' to model scene");
        }

        // Initialize StateMachine deferred (needs AnimationTree to settle in tree)
        Callable.From(() =>
        {
            _fsm?.Initialize(this, _movementComponent, _inputCtrl);
            _fsm?.TransitionTo("idle");
        }).CallDeferred();

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
        _groundArrow = CreateGroundArrow();
        AddChild(_groundArrow);

        if (_isPlayerControlled)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        SetupDebugLabel();
        SetupDamageLabel();
    }

    // ── DEBUG ──

    private Label? _debugLabel;

    private void SetupDebugLabel()
    {
        // Only show debug for the real player (Name set before _Ready)
        if (Name != "Player") return;

        _debugLabel = new Label();
        _debugLabel.Name = "DebugLabel";
        _debugLabel.Position = new Vector2(10, 10);
        _debugLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _debugLabel.AddThemeColorOverride("font_color", new Color(0, 1, 0));
        _debugLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _debugLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _debugLabel.AddThemeConstantOverride("shadow_offset_y", 1);

        var canvas = new CanvasLayer();
        canvas.AddChild(_debugLabel);
        AddChild(canvas);
    }

    private void UpdateDebugLabel()
    {
        if (_debugLabel == null) return;

        string fsmState = _fsm?.CurrentStateName ?? "?";
        var animTree = _playerModel?.GetNodeOrNull<AnimationTree>("AnimationTree");
        float airBlend = animTree?.Get("parameters/air/blend_position").AsSingle() ?? 0f;
        _debugLabel.Text = $"fsm: {fsmState}  Y: {Velocity.Y:F1}  air: {airBlend:F2}  floor: {IsOnFloor()}";
    }

    // ── DAMAGE % LABEL (Smash-style, above everyone) ──

    private void SetupDamageLabel()
    {
        _damagePercentLabel = new Label3D();
        _damagePercentLabel.Name = "DamagePercentLabel";
        _damagePercentLabel.PixelSize = 0.012f;
        _damagePercentLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        _damagePercentLabel.OutlineSize = 4;
        _damagePercentLabel.OutlineModulate = new Color(0f, 0f, 0f, 0.9f);
        _damagePercentLabel.Modulate = Colors.White;
        _damagePercentLabel.Position = new Vector3(0f, 5f, 0f);
        _damagePercentLabel.Text = "0%";
        AddChild(_damagePercentLabel);
    }

    private void UpdateDamageLabel()
    {
        if (_damagePercentLabel == null) return;
        ushort pct = _movementComponent.DamagePercent;
        _damagePercentLabel.Text = $"{pct}%";
        float t = Mathf.Clamp(pct / 150f, 0f, 1f);
        _damagePercentLabel.Modulate = new Color(1f, 1f - (t * 0.7f), 0.2f - (t * 0.1f));
    }

    // ==========================================
    // GROUND ARROW (indicates input direction)
    // ==========================================

    private MeshInstance3D CreateGroundArrow()
    {
        var arrow = new MeshInstance3D();
        arrow.Name = "GroundArrow";

        // Build a chevron/triangle pointing in +Z (forward)
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Arrow shape: triangle pointing forward (Z+)
        const float size = 0.8f;

        // Tip
        st.AddVertex(new Vector3(0f, 0f, size));
        // Left
        st.AddVertex(new Vector3(-size * 0.5f, 0f, 0f));
        // Right
        st.AddVertex(new Vector3(size * 0.5f, 0f, 0f));

        st.GenerateNormals();
        arrow.Mesh = st.Commit();

        // Semi-transparent white material
        arrow.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.6f),
            EmissionEnabled = true,
            Emission = new Color(0.8f, 0.8f, 1f),
            EmissionEnergyMultiplier = 1.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither,
            DistanceFadeMaxDistance = 50f,
        };

        arrow.Visible = false;
        return arrow;
    }

    private void UpdateGroundArrow(bool hasInput)
    {
        if (_groundArrow == null) return;

        if (!hasInput || _isNPC)
        {
            _groundArrow.Visible = false;
            return;
        }

        _groundArrow.Visible = true;

        // Position at character's feet on the ground
        Vector3 pos = GlobalPosition;
        pos.Y = 0.05f;
        _groundArrow.Position = pos;

        // Rotate to face the input direction (camera-relative snapped direction)
        if (_snappedInputDirection.LengthSquared() > 0.001f)
        {
            float angle = MathF.Atan2(_snappedInputDirection.X, _snappedInputDirection.Y);
            _groundArrow.Rotation = new Vector3(0f, angle, 0f);
        }
    }

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

    // ==========================================
    // UNHANDLED INPUT
    // ==========================================

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isPlayerControlled) return;
        if (IsEscapeMenuOpen) { if (Input.IsActionJustPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible; return; }

        if (@event is InputEventMouseButton mb)
        {
            if (mb.Pressed)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp) { _camera?.ZoomCamera(-1f); return; }
                if (mb.ButtonIndex == MouseButton.WheelDown) { _camera?.ZoomCamera(1f); return; }
                if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
            }

            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed && _combatComponent != null)
            {
                ExecuteSlot(0, false, !_movementComponent.IsGrounded);
                GetViewport().SetInputAsHandled(); return;
            }

            if (mb.ButtonIndex == MouseButton.Right && _combatComponent != null)
            {
                if (mb.Pressed)
                {
                    if (_movementComponent.IsGrounded)
                    {
                        var ability = _charDef.GetSlotAbility(1, false);
                        if (ability.AimedCharge.HasValue && _fsm != null)
                        {
                            // Fire in character's current facing direction, not camera-relative
                            float aimYaw = GlobalRotation.Y;
                            var chargeState = _fsm.GetState<AimedChargeState>("aimed_charge");
                            if (chargeState != null)
                            {
                                chargeState.Configure(ability.AimedCharge.Value, 1, false, aimYaw);
                                _fsm.TransitionTo("aimed_charge");
                                GetViewport().SetInputAsHandled(); return;
                            }
                        }
                        // Legacy charge (no AimedCharge config)
                        _heavyHoldTimer = 0f; _heavyHeld = false;
                    }
                    else
                    {
                        // Air RMB: direct attack
                        ExecuteSlot(1, false, true);
                        GetViewport().SetInputAsHandled(); return;
                    }
                }
                else if (!_heavyHeld && (_fsm == null || !_fsm.IsInState("aimed_charge")))
                {
                    ExecuteSlot(1, false, !_movementComponent.IsGrounded);
                    GetViewport().SetInputAsHandled(); return;
                }
            }
        }

        if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured
            && (_fsm == null || !_fsm.IsInState("aimed_charge")))
            _camera?.RotateCamera(mm.Relative);

        if (Input.IsActionJustPressed("ability_q")) ExecuteSlot(2, false, false);
        if (Input.IsActionJustPressed("ability_e")) ExecuteSlot(3, false, false);
        if (Input.IsActionJustPressed("ability_r")) ExecuteSlot(4, false, false);
        if (Input.IsActionJustPressed("ability_f")) ExecuteSlot(5, false, false);
        if (Input.IsActionJustPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    // ==========================================
    // PROCESS (timers, NPC)
    // ==========================================

    private float _trinketCooldownTimer = 0f;
    [Export] public float TrinketCooldown = 120f;
    [Export] public float TrinketPushSpeed = 18.0f;
    private float _heavyHoldTimer = 0f;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _inputCtrl.Poll();
        UpdateDebugLabel();
        UpdateDamageLabel();
        if (_trinketCooldownTimer > 0f) _trinketCooldownTimer -= dt;

        // Legacy hold timer for RMB charge (only if not using AimedCharge system)
        if ((_fsm == null || !_fsm.IsInState("aimed_charge")) &&
            Input.IsMouseButtonPressed(MouseButton.Right) && _combatComponent != null)
        {
            _heavyHoldTimer += dt;
            if (_heavyHoldTimer > 0.3f && !_heavyHeld)
            {
                _heavyHeld = true;
                ExecuteSlot(1, true, !_movementComponent.IsGrounded);
            }
        }
        else
        {
            _heavyHoldTimer = 0f;
        }

        // Consume buffered LMB chain when lock expires (queue-based, like souls-like FSM)
        if (_movementComponent.State.BufferedChain > 0 &&
            _movementComponent.State.AnimLockTicks == 0)
        {
            ref var s = ref _movementComponent.State;
            s.BufferedChain--;
            ExecuteSlot(0, false, !_movementComponent.IsGrounded);
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

        // Dash (ground OR air)
        if (input.Dash && _movementComponent.State.AnimLockTicks <= 0)
        {
            var dashState = _fsm?.GetState<DashState>("dash");
            if (dashState != null)
            {
                dashState.SetDirection(_moveDirection.X, _moveDirection.Z);
                _fsm?.TransitionTo("dash");
            }
        }

        bool wasKnocked = _movementComponent.IsInKnockback();
        // Movement simulation handled by ServerSimulation (in MatchManager._PhysicsProcess)
        // _movementComponent.Tick(input);

        // Resolve pending attack stages after startup delay
        if (_fsm != null && _fsm.IsInState("attack"))
        {
            var attackState = _fsm.GetAttackState();
            if (attackState != null && attackState.HasPendingResolve && attackState.TickStartup())
            {
                var stages = attackState.PendingStages;
                if (stages != null)
                {
                    var ability = _charDef.GetSlotAbility(attackState.PendingSlotIndex, attackState.PendingAirborne);
                    ResolveAbilityStages(stages, attackState.PendingSlotIndex, attackState.PendingAirborne);

                    // Special effects (only for stage 0, not combo chains)
                    if (attackState.PendingSlotIndex != 0 && ability.SpecialEffectKeys != null)
                    {
                        foreach (var key in ability.SpecialEffectKeys)
                            AbilityRegistry.Execute(key, _combatComponent!);
                    }
                }
                attackState.ClearPendingResolve();
            }
        }

        // Tech roll on knockback landing
        if (wasKnocked && _movementComponent.IsGrounded && !_movementComponent.IsInKnockback() && Input.IsActionJustPressed("tech"))
            _movementComponent.DoTechRoll();

        // Update ground arrow
        UpdateGroundArrow(_snappedInputDirection.LengthSquared() > 0.001f);

        // Animation handled by FSM states (idle/run/jump/fall/landing)
        // FSM runs in _Process — no manual Travel calls needed here

        OnStateUpdated?.Invoke(GlobalPosition.X, GlobalPosition.Z, GlobalPosition.Y, Velocity.X, Velocity.Z);
    }

    // ==========================================
    // INPUT STATE BUILDER (camera-relative, 8-direction)
    // ==========================================

    private InputState BuildInputState()
    {
        var input = new InputState();

        // If NPC with AI-injected input, use that directly
        if (_isNPC && _inputCtrl.IsAIControlled())
        {
            var (moveX, moveY) = _inputCtrl.GetMovement();
            input.MoveX = moveX;
            input.MoveY = moveY;
            input.Up = moveY < -0.3f;
            input.Down = moveY > 0.3f;
            input.Left = moveX < -0.3f;
            input.Right = moveX > 0.3f;
            input.Jump = _inputCtrl.JumpJustPressed;
            input.Dash = _inputCtrl.DashJustPressed;
            input.Attack = _inputCtrl.IsAttackPressed();
            input.Crouch = false; // NPCs don't crouch for now

            // Set move direction for animations
            _moveDirection = new Vector3(moveX, 0f, moveY).Normalized();
            _snappedInputDirection = new Vector2(moveX, moveY);

            return input;
        }

        // Human player: read from Godot Input
        // Get camera-relative forward/right (default to world if no camera)
        Vector3 camForward = Vector3.Forward;
        Vector3 camRight = Vector3.Right;
        if (_camera != null)
        {
            // Z = direction où la caméra regarde = -Basis.Z = vers le centre de l'écran
            camForward = _camera.GetForwardDirection();
            camRight = _camera.GetRightDirection();
        }

        // Build raw camera-relative direction
        Vector3 rawDir = Vector3.Zero;
        if (Input.IsActionPressed("move_forward")) rawDir += camForward;
        if (Input.IsActionPressed("move_back")) rawDir -= camForward;
        if (Input.IsActionPressed("move_left")) rawDir -= camRight;
        if (Input.IsActionPressed("move_right")) rawDir += camRight;

        // 8-direction snap
        _moveDirection = Vector3.Zero;
        _snappedInputDirection = Vector2.Zero;

        if (rawDir.LengthSquared() > 0.001f)
        {
            // Get angle in camera-relative space (camRight=X, camForward=Z)
            // Convert to camera-relative 2D coordinates
            float rawForward = rawDir.Dot(camForward);
            float rawRight = rawDir.Dot(camRight);

            // Snap to 8 directions: compute angle and round to nearest 45°
            float angle = MathF.Atan2(rawRight, rawForward);
            const float snapStep = MathF.PI / 4f; // 45°
            float snappedAngle = MathF.Round(angle / snapStep) * snapStep;

            // Convert snapped angle back to camera-relative direction
            float fwd = MathF.Cos(snappedAngle);
            float rgt = MathF.Sin(snappedAngle);

            // Store in snapped direction (for ground arrow)
            _snappedInputDirection = new Vector2(rgt, fwd);

            // Convert from camera-relative to world space
            _moveDirection = (camForward * fwd) + (camRight * rgt);
            _moveDirection = _moveDirection.Normalized();
        }

        input.MoveX = _moveDirection.X;
        input.MoveY = _moveDirection.Z;
        input.Up = _moveDirection.Z < -0.3f;      // forward in world +Z
        input.Down = _moveDirection.Z > 0.3f;     // backward
        input.Left = _moveDirection.X < -0.3f;    // left
        input.Right = _moveDirection.X > 0.3f;    // right
        input.Jump = Input.IsActionJustPressed("jump");
        input.Dash = Input.IsActionJustPressed("dash");
        input.Crouch = Input.IsActionPressed("crouch");
        input.Attack = Input.IsMouseButtonPressed(MouseButton.Left);

        // Zero movement if FSM state disallows it (e.g., aimed charge, attack)
        if (_fsm != null && !_fsm.CanMove())
        {
            input.MoveX = 0f;
            input.MoveY = 0f;
            input.Jump = false;
            input.Dash = false;
            _moveDirection = Vector3.Zero;
            _snappedInputDirection = Vector2.Zero;
        }

        return input;
    }

    // ==========================================
    // COMBAT: unified ability execution
    // ==========================================

    /// <summary>
    /// Execute any ability slot (0-5) uniformly.
    /// </summary>
    public void ExecuteSlot(int slotIndex, bool charged, bool airborne)
    {
        if (_combatComponent == null) return;
        if (_movementComponent.IsInKnockback()) return;
        if (_movementComponent.State.AnimLockTicks > 0)
        {
            // Non-LMB slots can't be used during lock
            if (slotIndex != 0) return;

            // Buffer LMB input in queue (max 2, like souls-like FSM queue)
            if (_movementComponent.State.BufferedChain < 2)
            {
                ref var s = ref _movementComponent.State;
                s.BufferedChain++;
            }
            return; // consumed when lock expires
        }

        // Slot 0 (LMB) chains into next combo stage.
        // Other slots are blocked during an active attack.
        if (slotIndex != 0 && _fsm != null && _fsm.IsInState("attack")) return;

        var ability = _charDef.GetSlotAbility(slotIndex, airborne);

        // Cooldown check
        ushort slotCd = slotIndex switch
        {
            0 => _movementComponent.State.Cooldown0,
            1 => _movementComponent.State.Cooldown1,
            2 => _movementComponent.State.Cooldown2,
            3 => _movementComponent.State.Cooldown3,
            4 => _movementComponent.State.Cooldown4,
            5 => _movementComponent.State.Cooldown5,
            _ => 0
        };
        if (slotCd > 0) return;
        OnAbilityUsed?.Invoke(slotIndex);
        var stages = charged && ability.ChargedStages != null ? ability.ChargedStages : ability.Stages;

        // Play attack animation via AnimationTree StateMachine
        int animStage = (stages != null && stages.Length > 0)
            ? Math.Clamp(_movementComponent.State.ComboStage, 0, stages.Length - 1)
            : 0;
        string? animName = ability.AnimationNames != null && animStage < ability.AnimationNames.Length
            ? ability.AnimationNames[animStage]
            : null;
        // If this ability uses AimedCharge, use the attack anim (not the charge loop)
        if (animName != null && ability.AimedCharge.HasValue)
            animName = ability.AimedCharge.Value.AttackAnimName;
        if (animName != null && _fsm != null)
        {
            var attackState = _fsm.GetAttackState();
            if (attackState != null)
            {
                if (_fsm.IsInState("attack"))
                {
                    // Combo chain
                    if (stages != null && stages.Length > 0)
                    {
                        int stageIdx = Math.Clamp(_movementComponent.State.ComboStage, 0, stages.Length - 1);
                        ExecuteAttackStage(stages[stageIdx], stages, slotIndex, charged, airborne, isComboChain: true, startup: 0);
                    }
                    attackState.ChainTo(animName);
                }
                else
                {
                    // First attack
                    ushort startup = (stages != null && stages.Length > 0) ? stages[animStage].StartupTicks : (ushort)0;
                    if (stages != null && stages.Length > 0)
                    {
                        ExecuteAttackStage(stages[animStage], stages, slotIndex, charged, airborne, isComboChain: false, startup);
                    }
                    attackState.NextAnimName = animName;
                    _fsm.TransitionTo("attack");
                }
            }
        }

        // ── Step 2: Special effects ──
        // Only fire immediately if no startup delay (otherwise fired after delay in _PhysicsProcess)
        if (ability.SpecialEffectKeys != null)
        {
            ushort startup = (stages != null && stages.Length > 0) ? stages[0].StartupTicks : (ushort)0;
            if (startup == 0)
            {
                foreach (var key in ability.SpecialEffectKeys)
                    AbilityRegistry.Execute(key, _combatComponent);
            }
        }

        // ── Step 3: Set cooldown ──
        if (ability.CooldownTicks > 0)
        {
            ref var s = ref _movementComponent.State;
            switch (slotIndex)
            {
                case 0: s.Cooldown0 = ability.CooldownTicks; break;
                case 1: s.Cooldown1 = ability.CooldownTicks; break;
                case 2: s.Cooldown2 = ability.CooldownTicks; break;
                case 3: s.Cooldown3 = ability.CooldownTicks; break;
                case 4: s.Cooldown4 = ability.CooldownTicks; break;
                case 5: s.Cooldown5 = ability.CooldownTicks; break;
            }
        }
    }

    /// <summary>
    /// Resolve attack stages against simulation entities via SpellResolver.
    /// </summary>
    private void ResolveAbilityStages(AttackStage[] stages, int slotIndex, bool airborne)
    {
        var sim = _combatComponent?.GetSimulation();
        if (sim == null || _combatComponent == null) return;
        ulong pid = _combatComponent.GetEntityId();

        // LMB combo stage tracking via CharacterState (ground only; air uses stage 0)
        int stageIndex;
        byte newComboStage = _movementComponent.State.ComboStage;
        if (slotIndex == 0 && !airborne)
        {
            if (_movementComponent.State.ComboStage == 0)
                newComboStage = 1;
            else if (_movementComponent.State.ComboStage < stages.Length)
                newComboStage++;
            else
            {
                ref var s = ref _movementComponent.State;
                s.BufferedChain = 0; // combo maxed, clear queue
                return;
            }
            stageIndex = newComboStage - 1;
        }
        else
        {
            stageIndex = 0;
        }

        var stage = stages[stageIndex];
        // Look up full ability data for aimed-charge / cone detection
        var ability = _charDef.GetSlotAbility(slotIndex, airborne);
        bool isConeAttack = ability.AimedCharge.HasValue;
        // Use camera-relative input direction for lunge, fall back to character facing
        Vector3 fwd = (-Transform.Basis.Z with { Y = 0 }).Normalized();
        Vector3 lungeDir = _moveDirection.LengthSquared() > 0.001f ? _moveDirection : fwd;
        Vector3 pos = GlobalPosition;

        // Lunge in camera-relative input direction (not character facing)
        if (stage.LungeForce > 0f)
        {
            float upBoost = Velocity.Y + 2f;
            Velocity = new Vector3(lungeDir.X * stage.LungeForce, upBoost, lungeDir.Z * stage.LungeForce);
        }

        // Spawn melee hitbox in attack direction
        Vector3 hitDir = GetAttackDirection(stage);
        Vector3 handPos = pos + (Vector3.Up * 1.2f); // Hand height
        Vector3 hitPos = pos + (hitDir * 2.0f) + (Vector3.Up * 1.0f);

        Hitbox hb;
        if (isConeAttack)
        {
            // Capsule (tube) hitbox for flamethrower-like abilities
            float maxRange = ability.AimedCharge!.Value.ConeRange;
            // Scale range by charge: 10m (uncharged) → 15m (full)
            ushort ct = _movementComponent.State.ChargeTicks;
            ushort maxCt = ability.AimedCharge!.Value.MaxChargeTicks;
            float t = maxCt > 0 ? Math.Clamp((float)ct / maxCt, 0f, 1f) : 0f;
            float coneRange = 10f + (t * (maxRange - 10f));
            // TEMP: negate direction — GLB model faces +Z (Mixamo), not Godot -Z
            Vector3 tipPos = handPos + (-hitDir * coneRange);
            hb = new Hitbox
            {
                X = handPos.X, Y = handPos.Y, Z = handPos.Z,
                EndX = tipPos.X, EndY = tipPos.Y, EndZ = tipPos.Z,
                Shape = HitboxShape.Capsule,
                Radius = 0.6f,  // Narrow tube
                DurationTicks = 12,  // ~200ms burst
                Damage = stage.Damage,
                KnockbackForce = stage.KnockbackForce,
                KnockbackUpward = stage.KnockbackUpward,
                StunTicks = stage.StunTicks,
                OwnerId = pid,
            };
        }
        else
        {
            // Default sphere hitbox for melee attacks
            hb = new Hitbox
            {
                X = hitPos.X, Y = hitPos.Y, Z = hitPos.Z,
                Radius = 2.5f,
                DurationTicks = 15,
                Damage = stage.Damage,
                KnockbackForce = stage.KnockbackForce,
                KnockbackUpward = stage.KnockbackUpward,
                StunTicks = stage.StunTicks,
                OwnerId = pid,
            };
        }
        SpellResolver.Spawn(hb);
        if (isConeAttack)
            GD.Print($"[HITBOX] Capsule R={hb.Radius:F1} RNG={ability.AimedCharge!.Value.ConeRange} DMG={stage.Damage} KB={stage.KnockbackForce}");
        else
            GD.Print($"[HITBOX] Sphere at ({hitPos.X:F1},{hitPos.Y:F1},{hitPos.Z:F1}) R={hb.Radius:F1} " +
                     $"DMG={stage.Damage} KB={stage.KnockbackForce} Dir=({hitDir.X:F1},{hitDir.Z:F1})");

        // Update CharacterState combo/animation ticks
        _movementComponent.SetComboState(newComboStage, stage.ChainWindowTicks, stage.SelfLockTicks);
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

    private void CreateFallbackMesh()
    {
        var cm = new CapsuleMesh { Radius = 1.5f, Height = 3f };
        cm.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = new Color(0f, 0.75f, 1f), EmissionEnabled = true, Emission = new Color(0f, 0.5f, 0.8f), EmissionEnergyMultiplier = 2f });
        AddChild(new MeshInstance3D { Mesh = cm });
    }

    private Node3D? LoadPlayerModel()
    {
        string modelPath;
        Vector3 scale;
        Vector3 position;

        switch (_playerClass)
        {
            case CharacterClass.Manki:
                modelPath = "res://assets/characters/manki/manki.tscn";
                scale = Vector3.One;
                position = new Vector3(0, 0, 0);
                break;
            default:
                modelPath = "res://assets/characters/manki/manki.tscn";
                scale = Vector3.One;
                position = new Vector3(0, 0, 0);
                break;
        }

        if (!ResourceLoader.Exists(modelPath))
        {
            CreateFallbackMesh();
            return null;
        }

        var pm = GD.Load<PackedScene>(modelPath)?.Instantiate<Node3D>();
        if (pm == null) { CreateFallbackMesh(); return null; }

        pm.Name = "PlayerModel";
        AddChild(pm);
        pm.Scale = scale;
        pm.Position = position;

        return pm;
    }

    private void ApplySkinRecursive(Node node, Texture2D? tex)
    {
        if (node is MeshInstance3D mi)
        {
            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = false;
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
            if (tex != null) { mat.AlbedoTexture = tex; }
            else mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
            mi.MaterialOverride = mat;
            if (_isNPC && _firstMesh == null) { _firstMesh = mi; mat.EmissionEnabled = true; mat.Emission = new Color(0.8f, 0f, 0f); _npcOriginalEmission = mat.EmissionEnergyMultiplier; }
        }
        foreach (var c in node.GetChildren()) ApplySkinRecursive(c, tex);
    }

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

    /// <summary>Apply authoritative state from the server simulation.</summary>
    public void ApplyServerState(CharacterState state)
    {
        // Sync movement component state (struct copy)
        _movementComponent.State = state;

        // Apply to Godot body (authoritative position from server)
        GlobalPosition = new Vector3(state.PX, state.PY, state.PZ);
        Velocity = new Vector3(state.VX, state.VY, state.VZ);
        GlobalRotation = new Vector3(0f, state.FacingYaw, 0f);

        // MoveAndSlide collides with actual arena geometry
        MoveAndSlide();
    }

    /// <summary>
    /// Trigger an ability by slot index (for NPCs).
    /// 0 = LMB, 1-5 = Q/E/R/F/etc.
    /// </summary>
    public void UseAbility(int slot)
    {
        if (!_isNPC) return; // Only NPCs can have direct ability calls
        ExecuteSlot(slot, charged: false, airborne: !_movementComponent.IsGrounded);
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
    /// Execute an attack stage with optional warp and startup delay.
    /// Factorizes logic shared between first attack and combo chains.
    /// </summary>
    private void ExecuteAttackStage(AttackStage stage, AttackStage[] stages, int slotIndex, bool charged, bool airborne, bool isComboChain, ushort startup)
    {
        if (_combatComponent == null) return;

        if (stage.UseTargetLock)
        {
            _combatComponent.ExecuteAttackWithWarp(stage, _charDef.Movement.SprintSpeed, () =>
            {
                if (isComboChain || startup == 0)
                {
                    ResolveAbilityStages(stages, slotIndex, airborne);
                }
                else
                {
                    var attackState = _fsm?.GetAttackState();
                    attackState?.SetPendingResolve(stages, slotIndex, charged, airborne, startup);
                }
            });
        }
        else
        {
            if (isComboChain || startup == 0)
            {
                ResolveAbilityStages(stages, slotIndex, airborne);
            }
            else
            {
                var attackState = _fsm?.GetAttackState();
                attackState?.SetPendingResolve(stages, slotIndex, charged, airborne, startup);
            }
        }
    }

    /// <summary>
    /// Get attack direction with priority:
    /// 1. Cached warp direction (if we just warped)
    /// 2. Active target lock (if target still locked)
    /// 3. Input direction (if moving)
    /// 4. Camera forward (fallback)
    /// </summary>
    private Vector3 GetAttackDirection(AttackStage stage)
    {
        // Priority 1: Cached warp direction
        Vector3 warpDir = _combatComponent?.GetFinalWarpDirection() ?? Vector3.Zero;
        if (stage.UseTargetLock && warpDir.LengthSquared() > 0.001f)
        {
            warpDir.Y = 0;
            return warpDir.Normalized();
        }

        // Priority 2: Active target lock
        if (stage.UseTargetLock && _targetLock?.CurrentTarget != null)
        {
            Vector3 dir = _targetLock.GetDirectionToTarget();
            dir.Y = 0;
            return dir.Normalized();
        }

        // Priority 3: Input direction
        if (_moveDirection.LengthSquared() > 0.001f)
        {
            return _moveDirection;
        }

        // Priority 4: Player forward (character facing, not camera — camera looks at player)
        return GetPlayerForward();
    }

    /// <summary>
    /// ==========================================
    /// CLICK TARGETING STATE
    /// ==========================================
    /// </summary>
    private Vector2 _storedMousePos = Vector2.Zero;
}
