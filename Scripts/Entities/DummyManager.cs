using Godot;
using System;

/// <summary>
/// Enemy NPCs that are structurally identical to the player:
/// same model (characterMedium.fbx), same skin system, same AnimationPlayer,
/// same collision size (capsule 1.5x3), same hurtbox.
///
/// Physics: knockback pushes them around, friction slows them, gravity when airborne.
/// Respawn: 3s after death, back to spawn position with full HP.
/// </summary>
public partial class DummyManager : Node3D
{
    /// <summary>
    /// Spawn points (must match the arena layout).
    /// Offset Y by 1.5 so the capsule base sits on the floor (capsule height 3 → center at +1.5).
    /// </summary>
    public static readonly Vector3[] DummyPositions = new Vector3[]
    {
        new Vector3(60f, 1.5f, 60f),
        new Vector3(140f, 1.5f, 60f),
        new Vector3(100f, 1.5f, 100f),
        new Vector3(60f, 1.5f, 140f),
        new Vector3(140f, 1.5f, 140f),
    };

    private const int DummyCount = 5;
    private const int MaxHP = 300;
    private const float RespawnDelay = 3.0f;
    private const float GroundFriction = 10.0f;
    private const float Gravity = 50.0f;
    private const float VelocityMaxY = 50.0f;

    /// <summary>
    /// Skins cycled across dummies
    /// </summary>
    private static readonly string[] SkinPaths = new[]
    {
        "res://assets/characters/Skins/skaterMaleA.png",
        "res://assets/characters/Skins/skaterFemaleA.png",
        "res://assets/characters/Skins/criminalMaleA.png",
        "res://assets/characters/Skins/cyborgFemaleA.png",
    };

    /// <summary>
    /// ── Per-dummy state ────────────────────────────────────────
    /// </summary>
    private int[] _hp = new int[DummyCount];
    private float[] _respawnTimers = new float[DummyCount];
    private float[] _hitFlashTimers = new float[DummyCount];
    private Vector3[] _spawnPositions = new Vector3[DummyCount];

    private CharacterBody3D[] _bodies = new CharacterBody3D[DummyCount];
    private MeshInstance3D[] _meshes = new MeshInstance3D[DummyCount];
    private Hurtbox[] _hurtboxes = new Hurtbox[DummyCount];
    private AnimationPlayer[] _animPlayers = new AnimationPlayer[DummyCount];
    private float[] _originalEmissionEnergy = new float[DummyCount];

    public override void _Ready()
    {
        for (int i = 0; i < DummyCount; i++)
        {
            _hp[i] = MaxHP;
            _spawnPositions[i] = DummyPositions[i];
            CreateDummy(i);
        }
    }

    /// <summary>
    /// ── Physics ────────────────────────────────────────────────
    /// </summary>
    /// <param name="delta"></param>
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < DummyCount; i++)
        {
            var body = _bodies[i];
            if (body == null) continue;

            if (_respawnTimers[i] > 0)
            {
                body.Velocity = Vector3.Zero;
                continue;
            }

            Vector3 vel = body.Velocity;

            // Horizontal friction
            vel.X = Mathf.MoveToward(vel.X, 0f, GroundFriction * dt);
            vel.Z = Mathf.MoveToward(vel.Z, 0f, GroundFriction * dt);

            // Gravity
            if (!body.IsOnFloor())
            {
                vel.Y -= Gravity * dt;
                vel.Y = Mathf.Max(vel.Y, -VelocityMaxY);
            }
            else if (vel.Y < 0f)
            {
                vel.Y = 0f;
            }

            body.Velocity = vel;
            body.MoveAndSlide();

            // Fallen off the world
            if (body.GlobalPosition.Y < -20f)
            {
                body.GlobalPosition = _spawnPositions[i];
                body.Velocity = Vector3.Zero;
            }
        }
    }

    /// <summary>
    /// ── Visual flash + respawn timer ───────────────────────────
    /// </summary>
    /// <param name="delta"></param>
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < DummyCount; i++)
        {
            // Hit flash
            if (_hitFlashTimers[i] > 0)
            {
                _hitFlashTimers[i] -= dt;
                if (_meshes[i] != null && _meshes[i].MaterialOverride is StandardMaterial3D mat)
                    mat.EmissionEnergyMultiplier = 8.0f;
            }
            else
            {
                if (_meshes[i]?.MaterialOverride is StandardMaterial3D mat
                    && !Mathf.IsEqualApprox(mat.EmissionEnergyMultiplier, _originalEmissionEnergy[i]))
                {
                    mat.EmissionEnergyMultiplier = _originalEmissionEnergy[i];
                }
            }

            // Respawn timer
            if (_respawnTimers[i] > 0)
            {
                _respawnTimers[i] -= dt;
                if (_respawnTimers[i] <= 0)
                    RespawnDummy(i);
            }

            // Visibility
            bool alive = _respawnTimers[i] <= 0;
            if (_bodies[i] != null) _bodies[i].Visible = alive;
            if (_hurtboxes[i] != null) _hurtboxes[i].Visible = alive;
        }
    }

    /// <summary>
    /// ── Create one dummy ───────────────────────────────────────
    /// </summary>
    /// <param name="index"></param>
    private void CreateDummy(int index)
    {
        Vector3 pos = _spawnPositions[index];

        // ── CharacterBody3D ──
        var body = new CharacterBody3D();
        body.Name = $"DummyBody_{index}";
        body.CollisionLayer = 2;
        body.CollisionMask = 1; // collide with walls/floor (layer 1)
        body.UpDirection = Vector3.Up;
        body.FloorStopOnSlope = true;
        body.FloorMaxAngle = 45.0f;
        body.Position = pos;
        body.AddToGroup("enemies");  // For target lock system
        AddChild(body);
        _bodies[index] = body;

        // ── Collision shape (same as player: capsule 1.5×3) ──
        var colShape = new CollisionShape3D();
        var capsule = new CapsuleShape3D();
        capsule.Radius = 1.5f;
        capsule.Height = 3f;
        colShape.Shape = capsule;
        body.AddChild(colShape);

        // ── Hurtbox (sphere radius 2, same as player) ──
        var hurtbox = new Hurtbox();
        hurtbox.Name = $"DummyHurtbox_{index}";
        hurtbox.OwnerEntity = this;
        int capturedIndex = index;

        var hbShape = new CollisionShape3D();
        var hbSphere = new SphereShape3D();
        hbSphere.Radius = 2.0f;
        hbShape.Shape = hbSphere;
        hurtbox.AddChild(hbShape);
        body.AddChild(hurtbox);
        _hurtboxes[index] = hurtbox;

        hurtbox.OnHit += (Vector3 _, float damage, Vector3 knockbackForce) => DamageDummy(capturedIndex, (int)damage, knockbackForce);

        // ── Model (characterMedium.fbx) ──
        var model = LoadModel(index);
        if (model != null)
        {
            body.AddChild(model);
        }
        else
        {
            // Fallback: colored capsule
            var mesh = new MeshInstance3D();
            var capMesh = new CapsuleMesh();
            capMesh.Radius = 1.5f;
            capMesh.Height = 3f;
            var mat = new StandardMaterial3D
            {
                AlbedoColor = GetSkinColor(index),
                EmissionEnabled = true,
                Emission = GetSkinColor(index) * 0.6f,
                EmissionEnergyMultiplier = 1.5f,
            };
            mesh.Mesh = capMesh;
            mesh.MaterialOverride = mat;
            mesh.Position = new Vector3(0f, 0f, 0f);
            body.AddChild(mesh);
            _meshes[index] = mesh;
            _originalEmissionEnergy[index] = 1.5f;
        }

        // ── AnimationPlayer (skinned to model skeleton) ──
        var animPlayer = new AnimationPlayer();
        animPlayer.Name = "AnimationPlayer";

        if (model != null)
        {
            var skeleton = FindSkeleton(model);
            if (skeleton != null)
                animPlayer.RootNode = skeleton.GetPath();
            model.AddChild(animPlayer);
        }
        else
        {
            body.AddChild(animPlayer);
        }

        var animLib = new AnimationLibrary();
        animPlayer.AddAnimationLibrary("default", animLib);
        LoadAllAnimations(animLib);

        // Play idle
        if (animLib.HasAnimation("idle"))
        {
            animLib.GetAnimation("idle").LoopMode = Animation.LoopModeEnum.Linear;
            animPlayer.Play("default/idle");
        }
        else if (animLib.GetAnimationList().Count > 0)
        {
            string first = animLib.GetAnimationList()[0];
            var anim = animLib.GetAnimation(first);
            if (anim != null) anim.LoopMode = Animation.LoopModeEnum.Linear;
            animPlayer.Play("default/" + first);
        }

        _animPlayers[index] = animPlayer;
    }

    /// <summary>
    /// ── Model loading (mirrors PlayerController.LoadPlayerModel) ──
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    private Node3D LoadModel(int index)
    {
        const string modelPath = "res://assets/characters/Model/characterMedium.fbx";
        if (!ResourceLoader.Exists(modelPath))
            return null;

        var scene = GD.Load<PackedScene>(modelPath);
        if (scene == null) return null;

        var instance = scene.Instantiate<Node3D>();
        instance.Name = $"DummyModel_{index}";

        // Apply skin (cycle through the 4 available skins)
        string skinPath = SkinPaths[index % SkinPaths.Length];
        var skinTex = GD.Load<Texture2D>(skinPath);
        ApplySkinRecursive(instance, skinTex);

        // Mixamo fix: rotate 180° so character faces -Z
        instance.RotateY(Mathf.Pi);
        instance.Scale = new Vector3(0.5f, 0.5f, 0.5f);
        instance.Position = new Vector3(0f, -1.5f, 0f);

        // Store the first MeshInstance3D found for flash effects
        StoreFirstMesh(instance, index);

        return instance;
    }

    private void ApplySkinRecursive(Node node, Texture2D skinTex)
    {
        if (node is MeshInstance3D mi)
        {
            if (skinTex != null)
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoTexture = skinTex;
                mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
                mi.MaterialOverride = mat;
            }
            else
            {
                var mat = new StandardMaterial3D();
                mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
                mi.MaterialOverride = mat;
            }
        }
        foreach (var child in node.GetChildren())
            ApplySkinRecursive(child, skinTex);
    }

    private void StoreFirstMesh(Node node, int index)
    {
        if (node is MeshInstance3D mi && _meshes[index] == null)
        {
            _meshes[index] = mi;
            // Extract or set emission for flash effect
            if (mi.MaterialOverride is StandardMaterial3D mat)
            {
                mat.EmissionEnabled = true;
                mat.Emission = new Color(0.8f, 0f, 0f);
                mat.EmissionEnergyMultiplier = 1.5f;
                _originalEmissionEnergy[index] = 1.5f;
            }
        }
        foreach (var child in node.GetChildren())
            StoreFirstMesh(child, index);
    }

    private Skeleton3D FindSkeleton(Node node)
    {
        if (node is Skeleton3D skel) return skel;
        foreach (var child in node.GetChildren())
        {
            var result = FindSkeleton(child);
            if (result != null) return result;
        }
        return null;
    }

    private AnimationPlayer FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var result = FindAnimationPlayer(child);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// ── Color for fallback capsules ──
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    private static Color GetSkinColor(int index)
    {
        return index switch
        {
            0 => new Color(1f, 0.2f, 0.2f),   // red
            1 => new Color(0.2f, 0.6f, 1f),   // blue
            2 => new Color(0.2f, 1f, 0.3f),   // green
            3 => new Color(1f, 0.8f, 0.2f),   // yellow
            _ => new Color(1f, 0.4f, 0.8f),   // pink
        };
    }

    /// <summary>
    /// ── Damage & knockback ──
    /// </summary>
    /// <param name="index"></param>
    /// <param name="damage"></param>
    /// <param name="knockbackForce"></param>
    /// <returns></returns>
    public bool DamageDummy(int index, int damage, Vector3 knockbackForce)
    {
        if (index < 0 || index >= DummyCount) return false;
        if (_respawnTimers[index] > 0) return false;

        _hp[index] -= damage;
        FlashDummy(index);

        if (_bodies[index] != null)
            _bodies[index].Velocity = knockbackForce;

        if (_hp[index] <= 0)
        {
            _hp[index] = 0;
            _respawnTimers[index] = RespawnDelay;
            GD.Print($"Dummy {index + 1} DEFEATED! Respawning in {RespawnDelay}s");
            return true;
        }

        GD.Print($"Dummy {index + 1} took {damage} damage! HP: {_hp[index]}/{MaxHP}");
        return true;
    }

    public bool DamageDummy(int index, int damage)
        => DamageDummy(index, damage, Vector3.Zero);

    public int GetHP(int index)
        => index >= 0 && index < DummyCount ? _hp[index] : 0;

    public int GetMaxHP() => MaxHP;

    public bool IsAlive(int index)
        => index >= 0 && index < DummyCount && _respawnTimers[index] <= 0 && _hp[index] > 0;

    private void RespawnDummy(int index)
    {
        _hp[index] = MaxHP;
        _respawnTimers[index] = 0;
        if (_bodies[index] != null)
        {
            _bodies[index].GlobalPosition = _spawnPositions[index];
            _bodies[index].Velocity = Vector3.Zero;
        }
        GD.Print($"Dummy {index + 1} respawned!");
    }

    public void FlashDummy(int index, float duration = 0.3f)
    {
        if (index >= 0 && index < DummyCount)
            _hitFlashTimers[index] = duration;
    }

    /// <summary>
    /// Animation loading (copied from PlayerController)
    /// </summary>
    private void LoadAllAnimations(AnimationLibrary animLib)
    {
        const string animDir = "res://assets/characters/ProMagicPack/";
        var dir = DirAccess.Open(animDir);
        if (dir == null)
        {
            GD.PrintErr($"Cannot open animation directory: {animDir}");
            return;
        }

        dir.ListDirBegin();
        while (true)
        {
            string fileName = dir.GetNext();
            if (string.IsNullOrEmpty(fileName)) break;
            if (!fileName.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            if (fileName.Equals("characterMedium.fbx", StringComparison.OrdinalIgnoreCase)) continue;

            string path = animDir + fileName;
            string key = NormalizeAnimationName(fileName);
            LoadSingleAnimation(animLib, path, key);
        }
        dir.ListDirEnd();
    }

    private string NormalizeAnimationName(string fileName)
    {
        string name = fileName;
        int ext = name.LastIndexOf(".fbx", StringComparison.OrdinalIgnoreCase);
        if (ext > 0) name = name.Substring(0, ext);

        if (name.StartsWith("Standing ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("Standing ".Length);
        else if (name.StartsWith("standing ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("standing ".Length);

        // Convert to snake_case
        System.Text.StringBuilder sb = new();
        bool lastWasSpace = true;
        foreach (char c in name)
        {
            if (c == ' ' || c == '-')
            {
                lastWasSpace = true;
            }
            else
            {
                if (lastWasSpace)
                    sb.Append(char.ToLowerInvariant(c));
                else
                    sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private bool LoadSingleAnimation(AnimationLibrary lib, string path, string key)
    {
        try
        {
            // Try loading as Animation resource first
            var anim = ResourceLoader.Load(path, "Animation") as Animation;
            if (anim != null)
            {
                lib.AddAnimation(key, anim);
                return true;
            }

            // If that fails, try as PackedScene (FBX imported as scene)
            var scene = ResourceLoader.Load<PackedScene>(path);
            if (scene != null)
            {
                var temp = scene.Instantiate<Node>();
                var ap = FindAnimationPlayer(temp);
                if (ap != null)
                {
                    foreach (var libName in ap.GetAnimationLibraryList())
                    {
                        var animLib = ap.GetAnimationLibrary(libName);
                        if (animLib == null) continue;
                        foreach (var animName in animLib.GetAnimationList())
                        {
                            var extracted = animLib.GetAnimation(animName);
                            if (extracted != null)
                            {
                                lib.AddAnimation(key, extracted);
                                temp.QueueFree();
                                return true;
                            }
                        }
                    }
                }
                temp.QueueFree();
            }
            return false;
        }
        catch (Exception)
        {
            return false; // Silently skip incompatible files
        }
    }
}
