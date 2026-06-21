#nullable enable
using Godot;

/// <summary>
/// Combines the on-screen debug label and the damage-percent Label3D into one Node.
/// The debug label (CanvasLayer + Label) is only created when isPlayer is true.
/// The damage Label3D is always created above the character.
/// </summary>
public partial class DebugOverlay : Node
{
    private Label? _debugLabel;
    private Label3D _damagePercentLabel;

    /// <summary>
    /// Creates the debug overlay.
    /// </summary>
    /// <param name="parent">Parent node (used for context; the overlay adds itself as a child externally).</param>
    /// <param name="isPlayer">If true, creates the CanvasLayer-based debug Label.</param>
    public DebugOverlay(Node3D parent, bool isPlayer)
    {
        Name = "DebugOverlay";

        if (isPlayer)
        {
            _debugLabel = new Label
            {
                Name = "DebugLabel",
                Position = new Vector2(10, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _debugLabel.AddThemeColorOverride("font_color", new Color(0, 1, 0));
            _debugLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
            _debugLabel.AddThemeConstantOverride("shadow_offset_x", 1);
            _debugLabel.AddThemeConstantOverride("shadow_offset_y", 1);

            var canvas = new CanvasLayer();
            canvas.AddChild(_debugLabel);
            AddChild(canvas);
        }

        _damagePercentLabel = new Label3D
        {
            Name = "DamagePercentLabel",
            Position = new Vector3(0f, 5f, 0f),
            PixelSize = 0.012f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 4,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            Modulate = Colors.White,
            Text = "0%",
        };
        AddChild(_damagePercentLabel);
    }

    /// <summary>
    /// Update the debug label text (only if it exists).
    /// </summary>
    public void UpdateDebug(string fsmState, float velocityY, bool isOnFloor)
    {
        if (_debugLabel == null) return;
        _debugLabel.Text = $"fsm: {fsmState}  Y: {velocityY:F1}  floor: {isOnFloor}";
    }

    /// <summary>
    /// Update the damage percent label text and color gradient.
    /// </summary>
    /// <param name="damagePercent">Current damage percent (0-~300).</param>
    public void UpdateDamage(ushort damagePercent)
    {
        _damagePercentLabel.Text = $"{damagePercent}%";
        float t = Mathf.Clamp(damagePercent / 150f, 0f, 1f);
        _damagePercentLabel.Modulate = new Color(1f, 1f - (t * 0.7f), 0.2f - (t * 0.1f));
    }
}
