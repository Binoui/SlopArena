#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Handles loading and setting up the 3D character model (.tscn scene).
/// Shared by PlayerController and DummyManager — eliminates copy-paste.
/// </summary>
public class PlayerModel
{
    private readonly Node3D _owner;
    private readonly CharacterDefinition _charDef;
    private readonly CharacterClass _playerClass;
    private readonly BakedAnimationData? _bakedData;
    private readonly bool _isNPC;

    public Node3D? ModelNode { get; private set; }
    public MeshInstance3D? FirstMesh { get; private set; }

    public PlayerModel(Node3D owner, CharacterDefinition charDef, CharacterClass playerClass,
        BakedAnimationData? bakedData, bool isNPC = false)
    {
        _owner = owner;
        _charDef = charDef;
        _playerClass = playerClass;
        _bakedData = bakedData;
        _isNPC = isNPC;
    }

    /// <summary>
    /// Load the 3D model .tscn, apply scale/position/skin, add to owner node.
    /// Returns the model node, or null if loading failed (fallback mesh used).
    /// </summary>
    public Node3D? Load()
    {
        string modelPath;
        Vector3 scale;

        switch (_playerClass)
        {
            case CharacterClass.Manki:
                modelPath = "res://assets/characters/manki/manki.tscn";
                scale = Vector3.One;
                break;
            case CharacterClass.Bunny:
                modelPath = "res://assets/characters/bunny/bunny.tscn";
                scale = new Vector3(0.017f, 0.017f, 0.017f);
                break;
            default:
                modelPath = "res://assets/characters/manki/manki.tscn";
                scale = Vector3.One;
                break;
        }

        if (!ResourceLoader.Exists(modelPath))
        {
            CreateFallbackMesh();
            return null;
        }

        var pm = GD.Load<PackedScene>(modelPath)?.Instantiate<Node3D>();
        if (pm == null)
        {
            CreateFallbackMesh();
            return null;
        }

        GD.Print($"[Model] Loading {_playerClass}: path={modelPath} scale={scale} " +
                 $"capsule=({_charDef.CapsuleRadius},{_charDef.CapsuleHeight})");

        pm.Name = "PlayerModel";
        pm.Scale = scale;
        float modelYOffset = ComputeModelYOffset();
        pm.Position = new Vector3(0, modelYOffset, 0);
        GD.Print($"[Model] Y offset={modelYOffset:F4} " +
                 $"(auto={_charDef.AutoModelYOffset}, sole={_charDef.ModelSoleOffset})");
        _owner.AddChild(pm);

        ModelNode = pm;
        return pm;
    }

    /// <summary>
    /// Apply a texture to all MeshInstance3D children recursively.
    /// If tex is null, applies a default grey material.
    /// </summary>
    public void ApplySkinRecursive(Node node, Texture2D? tex)
    {
        if (node is MeshInstance3D mi)
        {
            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = false;
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear;
            if (tex != null)
                mat.AlbedoTexture = tex;
            else
                mat.AlbedoColor = new Color(0.7f, 0.7f, 0.7f);
            mi.MaterialOverride = mat;

            if (_isNPC && FirstMesh == null)
            {
                FirstMesh = mi;
                mat.EmissionEnabled = true;
                mat.Emission = new Color(0.8f, 0f, 0f);
            }
        }

        foreach (var c in node.GetChildren())
            ApplySkinRecursive(c, tex);
    }

    /// <summary>
    /// Compute the Y offset that aligns the visual model's feet with the capsule bottom.
    /// Uses baked skeleton data (lowest bone at idle frame 0) if available.
    /// Falls back to CharacterDefinition.ModelYOffset.
    /// </summary>
    private float ComputeModelYOffset()
    {
        if (_charDef.AutoModelYOffset && _bakedData != null)
        {
            float lowestY = float.MaxValue;
            string lowestBone = "";
            int found = 0;

            for (int bi = 0; bi < _bakedData.BoneNames.Length; bi++)
            {
                if (_bakedData.GetBonePosition("idle", 0, bi, out _, out float by, out _))
                {
                    if (by < lowestY) { lowestY = by; lowestBone = _bakedData.BoneNames[bi]; }
                    found++;
                }
            }

            if (found > 0 && lowestY < float.MaxValue)
            {
                float footWorldY = lowestY * _charDef.HurtboxBoneScale;
                float result = -(footWorldY + (_charDef.CapsuleHeight * 0.5f) + _charDef.ModelSoleOffset);
                GD.Print($"[ModelY] Auto: lowest={lowestY:F4} (bone={lowestBone}) " +
                         $"scale={_charDef.HurtboxBoneScale} footY={footWorldY:F4} " +
                         $"capsuleHalf={_charDef.CapsuleHeight * 0.5f} sole={_charDef.ModelSoleOffset} => offset={result:F4}");
                return result;
            }

            GD.Print("[ModelY] No 'idle' animation or bones in baked data, using fallback");
        }

        GD.Print($"[ModelY] Fallback: ModelYOffset={_charDef.ModelYOffset}");
        return _charDef.ModelYOffset;
    }

    private void CreateFallbackMesh()
    {
        var cm = new CapsuleMesh { Radius = 1.5f, Height = 3f };
        cm.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = new Color(0f, 0.75f, 1f),
            EmissionEnabled = true,
            Emission = new Color(0f, 0.5f, 0.8f),
            EmissionEnergyMultiplier = 2f
        });
        _owner.AddChild(new MeshInstance3D { Mesh = cm });
    }
}
