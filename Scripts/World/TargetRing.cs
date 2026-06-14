#nullable enable
using Godot;
using System;

/// <summary>
/// Visual targeting ring that follows the targeted entity.
/// Shared by TrainingMatch and PvPMatch.
/// </summary>
public partial class TargetRing : MeshInstance3D
{
    private PlayerController? _player;
    private PlayerController? _opponent;
    private PlayerController[]? _npcs;
    private int _npcCount;
    private ulong _opponentEntityId;

    private ulong _targetId;
    public ulong CurrentTarget => _targetId;
    public bool HasTarget => _targetId > 0;
    public event Action<ulong>? OnTargetChanged;

    public TargetRing()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float innerR = 0.8f, outerR = 2.2f;
        const int segs = 32;
        for (int i = 0; i < segs; i++)
        {
            float a1 = (float)i / segs * Mathf.Tau;
            float a2 = (float)(i + 1) / segs * Mathf.Tau;
            float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
            float c2 = Mathf.Cos(a2), s2 = Mathf.Sin(a2);
            var in1 = new Vector3(c1 * innerR, 0, s1 * innerR);
            var in2 = new Vector3(c2 * innerR, 0, s2 * innerR);
            var out1 = new Vector3(c1 * outerR, 0, s1 * outerR);
            var out2 = new Vector3(c2 * outerR, 0, s2 * outerR);
            st.AddVertex(in1); st.AddVertex(out1); st.AddVertex(in2);
            st.AddVertex(in2); st.AddVertex(out1); st.AddVertex(out2);
        }
        st.GenerateNormals();
        Mesh = st.Commit();
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.2f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.8f, 0.1f),
            EmissionEnergyMultiplier = 3f,
        };
        Visible = false;
    }

    public void Setup(PlayerController player, PlayerController? opponent, ulong opponentEntityId,
        PlayerController[] npcs, int npcCount)
    {
        _player = player;
        _opponent = opponent;
        _opponentEntityId = opponentEntityId;
        _npcs = npcs;
        _npcCount = npcCount;
    }

    public void SetTarget(ulong entityId)
    {
        _targetId = entityId;
        bool valid = entityId == _opponentEntityId || (entityId >= 100 && entityId < (ulong)(100 + _npcCount));
        if (valid)
        {
            Vector3 pos;
            if (entityId == _opponentEntityId && _opponent != null)
                pos = _opponent.GlobalPosition;
            else
            {
                int idx = (int)(entityId - 100);
                pos = idx >= 0 && idx < _npcCount && _npcs != null && _npcs[idx] != null
                    ? _npcs[idx]!.GlobalPosition : Vector3.Zero;
            }
            pos.Y = 0.1f;
            Position = pos;
            Visible = true;
        }
        else Visible = false;
        OnTargetChanged?.Invoke(entityId);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_targetId == _opponentEntityId && _opponent != null)
        {
            var pos = _opponent.GlobalPosition;
            pos.Y = 0.1f;
            Position = pos;
        }
        else if (_targetId >= 100 && _targetId < (ulong)(100 + _npcCount))
        {
            int idx = (int)(_targetId - 100);
            if (idx >= 0 && idx < _npcCount && _npcs != null && _npcs[idx] != null)
            {
                var pos = _npcs[idx]!.GlobalPosition;
                pos.Y = 0.1f;
                Position = pos;
            }
        }
    }
}
