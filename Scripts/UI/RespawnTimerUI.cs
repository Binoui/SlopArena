#nullable enable
using Godot;

/// <summary>
/// Center-screen respawn timer display (Smash Bros-style).
/// Shows countdown when player is eliminated and waiting to respawn.
/// </summary>
public partial class RespawnTimerUI : Control
{
    private Label _timerLabel = null!;
    private Panel _bgPanel = null!;

    public override void _Ready()
    {
        // Center on screen
        SetAnchorsPreset(LayoutPreset.Center);
        Size = new Vector2(300f, 100f);
        Position = -Size / 2f; // Center around anchor

        // Background panel
        _bgPanel = new Panel();
        _bgPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        _bgPanel.Modulate = new Color(0.1f, 0.1f, 0.15f, 0.85f);
        AddChild(_bgPanel);

        // Timer text
        _timerLabel = new Label();
        _timerLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        _timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _timerLabel.VerticalAlignment = VerticalAlignment.Center;
        _timerLabel.AddThemeFontSizeOverride("font_size", 48);
        _timerLabel.Modulate = new Color(1.0f, 0.3f, 0.3f); // Red
        _timerLabel.Text = "20";
        AddChild(_timerLabel);

        // Start hidden
        Visible = false;
    }

    /// <summary>
    /// Update the timer display.
    /// Pass 0 or negative to hide.
    /// </summary>
    public void UpdateTimer(float remainingSeconds)
    {
        if (remainingSeconds <= 0f)
        {
            Visible = false;
            return;
        }

        Visible = true;
        int seconds = Mathf.CeilToInt(remainingSeconds);
        _timerLabel.Text = seconds.ToString();

        // Color fade from red to yellow as timer gets low
        if (seconds <= 5)
        {
            _timerLabel.Modulate = new Color(1.0f, 0.5f, 0.1f); // Orange
        }
        else if (seconds <= 10)
        {
            _timerLabel.Modulate = new Color(1.0f, 0.4f, 0.2f); // Red-orange
        }
        else
        {
            _timerLabel.Modulate = new Color(1.0f, 0.3f, 0.3f); // Red
        }
    }
}
