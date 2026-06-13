#nullable enable
using Godot;

/// <summary>
/// Attaches aerosol can (right hand) + lighter (left hand) to Manki's bones.
/// Updates position each frame via skeleton bone global pose.
/// Props are hidden by default, shown during RMB attack.
/// </summary>
public partial class MankiWeaponAttach : Node
{
    private Skeleton3D? _skeleton;
    private int _rightHandIdx = -1;
    private int _leftHandIdx = -1;
    private Node3D? _rightHandNode;
    private Node3D? _leftHandNode;
    private StateMachine? _fsm;

    public void Setup(Skeleton3D skeleton)
    {
        _skeleton = skeleton;

        // Find FSM in the model tree (parent → PlayerModel → FSM)
        var parent = GetParentOrNull<Node3D>();
        if (parent != null)
        {
            var model = parent.GetNodeOrNull<Node3D>("PlayerModel");
            if (model != null)
                _fsm = model.GetNodeOrNull<StateMachine>("FSM");
        }

        // Find hand bones
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string name = skeleton.GetBoneName(i);
            if (name.Contains("RightHand"))
                _rightHandIdx = i;
            if (name.Contains("LeftHand"))
                _leftHandIdx = i;
        }

        if (_rightHandIdx >= 0)
        {
            _rightHandNode = new Node3D { Name = "MankiRightHand", Visible = false };
            AddChild(_rightHandNode);
            BuildAerosolCan(_rightHandNode);
        }

        if (_leftHandIdx >= 0)
        {
            _leftHandNode = new Node3D { Name = "MankiLeftHand", Visible = false };
            AddChild(_leftHandNode);
            BuildLighter(_leftHandNode);
        }
    }

    private static void BuildAerosolCan(Node3D parent)
    {
        // Can body
        var canMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.7f, 0.8f),
            Metallic = 0.6f,
            Roughness = 0.3f,
        };
        var canMesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.3f };
        canMesh.SurfaceSetMaterial(0, canMat);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = canMesh,
            Position = new Vector3(0f, 0f, 0.15f),
            Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
        });

        // Nozzle
        var nozzleMat = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.2f) };
        var nozzleMesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.06f };
        nozzleMesh.SurfaceSetMaterial(0, nozzleMat);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = nozzleMesh,
            Position = new Vector3(0f, 0f, 0.32f),
            Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f),
        });
    }

    private static void BuildLighter(Node3D parent)
    {
        // Lighter body
        var lighterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.6f, 0.1f),
            Metallic = 0.2f,
            Roughness = 0.7f,
        };
        var lighterMesh = new BoxMesh { Size = new Vector3(0.06f, 0.12f, 0.04f) };
        lighterMesh.SurfaceSetMaterial(0, lighterMat);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = lighterMesh,
            Position = new Vector3(0f, 0.02f, 0.08f),
            Rotation = new Vector3(0f, 0.3f, 0f),
        });

        // Flint spark
        var flintMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.8f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.7f, 0f),
            EmissionEnergyMultiplier = 3f,
        };
        var flintMesh = new SphereMesh { Radius = 0.015f, Height = 0.03f };
        flintMesh.SurfaceSetMaterial(0, flintMat);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = flintMesh,
            Position = new Vector3(0f, 0.1f, 0.12f),
        });
    }

    public override void _Process(double delta)
    {
        if (_skeleton == null) return;

        // Show props during charge or attack, hide otherwise
        bool visible = false;
        if (_fsm != null)
        {
            string state = _fsm.CurrentStateName;
            visible = state == "aimed_charge" || state == "attack";
        }
        if (_rightHandNode != null) _rightHandNode.Visible = visible;
        if (_leftHandNode != null) _leftHandNode.Visible = visible;

        if (_rightHandIdx >= 0 && _rightHandNode != null && visible)
        {
            _rightHandNode.GlobalPosition = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_rightHandIdx).Origin;
        }

        if (_leftHandIdx >= 0 && _leftHandNode != null && visible)
        {
            _leftHandNode.GlobalPosition = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_leftHandIdx).Origin;
        }
    }
}
