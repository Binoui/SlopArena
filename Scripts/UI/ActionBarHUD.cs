#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Action Bar HUD — shows 6 class abilities with key icons, names, cooldowns,
/// input flash feedback, and combo chain indicators.
/// Uses Kenney input prompt icons + UI pack for a cohesive retro-futuristic look.
/// </summary>
public partial class ActionBarHUD : Control
{
    private PlayerController? _player;
    private Label? _classNameLabel;
    private readonly Panel?[] _slots = new Panel?[6];
    private readonly Label?[] _nameLabels = new Label?[6];
    private readonly ColorRect?[] _cdOverlays = new ColorRect?[6];
    private readonly Label?[] _cdTexts = new Label?[6];
    private readonly TextureRect?[] _keyIcons = new TextureRect?[6];
    private readonly StyleBoxFlat?[] _slotStyles = new StyleBoxFlat?[6];

    private static readonly string[] SlotNames = { "Slot0", "Slot1", "Slot2", "Slot3", "Slot4", "Slot5" };
    private static readonly Color ComboBorder = new Color(0.9f, 0.7f, 0.1f, 1f);

    /// <summary>
    /// Maps slot index to Kenney input prompt texture path
    /// </summary>
    private static readonly string[] KeyTexturePaths = new[]
    {
        "res://assets/ui/keys/mouse_left.png",
        "res://assets/ui/keys/mouse_right.png",
        "res://assets/ui/keys/keyboard_q.png",
        "res://assets/ui/keys/keyboard_e.png",
        "res://assets/ui/keys/keyboard_r.png",
        "res://assets/ui/keys/keyboard_f.png",
    };

    public override void _Ready()
    {
        WireNodes();
    }

    private void WireNodes()
    {
        _classNameLabel = GetNodeOrNull<Label>("ClassName");

        for (int i = 0; i < 6; i++)
        {
            _slots[i] = GetNodeOrNull<Panel>($"ActionBar/{SlotNames[i]}");
            _nameLabels[i] = GetNodeOrNull<Label>($"ActionBar/{SlotNames[i]}/NameLabel");
            _cdOverlays[i] = GetNodeOrNull<ColorRect>($"ActionBar/{SlotNames[i]}/CooldownOverlay");
            _cdTexts[i] = GetNodeOrNull<Label>($"ActionBar/{SlotNames[i]}/CooldownText");

            // Key icon (Kenney input prompt) — overlaps top of slot like a badge
            var icon = new TextureRect();
            icon.ExpandMode = TextureRect.ExpandModeEnum.FitHeightProportional;
            icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            icon.CustomMinimumSize = new Vector2(24f, 24f);
            icon.Position = new Vector2(58f, -12f);
            icon.Size = new Vector2(24f, 24f);
            icon.MouseFilter = MouseFilterEnum.Ignore;
            _slots[i]?.AddChild(icon);
            _keyIcons[i] = icon;

            // Panel background style (Kenney-inspired flat dark)
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.85f);
            style.SetCornerRadiusAll(4);
            style.BorderWidthLeft = 1;
            style.BorderWidthRight = 1;
            style.BorderWidthTop = 1;
            style.BorderWidthBottom = 1;
            style.BorderColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            _slots[i]?.AddThemeStyleboxOverride("panel", style);
            _slotStyles[i] = style;
        }

        LoadKeyTextures();
    }

    private void LoadKeyTextures()
    {
        for (int i = 0; i < 6; i++)
        {
            if (_keyIcons[i] == null) continue;
            var tex = GD.Load<Texture2D>(KeyTexturePaths[i]);
            if (tex != null)
                _keyIcons[i]!.Texture = tex;
        }
    }

    public void Setup(PlayerController player)
    {
        _player = player;
        UpdateAbilityNames();
    }

    private void UpdateAbilityNames()
    {
        if (_player == null) return;

        var def = _player.GetCharacterDef();

        if (_classNameLabel != null)
            _classNameLabel.Text = def.DisplayName.ToUpper();

        AbilityData[] abilities = { def.LMB, def.RMB, def.Q, def.E, def.R, def.F };

        for (int i = 0; i < 6; i++)
        {
            if (_nameLabels[i] != null)
                _nameLabels[i]!.Text = abilities[i].Name;
        }
    }

    public void OnClassChanged()
    {
        UpdateAbilityNames();
    }

    public void FlashSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 6) return;
        if (_slots[slotIndex] == null || !IsInstanceValid(_slots[slotIndex])) return;

        var flash = new ColorRect();
        flash.Color = new Color(1f, 1f, 1f, 0.4f);
        flash.MouseFilter = MouseFilterEnum.Ignore;
        flash.Size = _slots[slotIndex]!.Size;
        flash.Position = Vector2.Zero;
        _slots[slotIndex]!.AddChild(flash);

        var tween = CreateTween();
        tween.TweenProperty(flash, "modulate", new Color(1f, 1f, 1f, 0f), 0.12f);
        tween.Finished += () =>
        {
            if (IsInstanceValid(flash))
                flash.QueueFree();
        };
    }

    public override void _Process(double delta)
    {
        UpdateCooldowns();
        UpdateComboIndicators();
    }

    private void UpdateCooldowns()
    {
        if (_player == null) return;

        var def = _player.GetCharacterDef();
        AbilityData[] abilities = { def.LMB, def.RMB, def.Q, def.E, def.R, def.F };
        int[] slotIds = { 0, 1, 2, 3, 4, 5 };

        for (int i = 0; i < 6; i++)
        {
            float cdRemaining = _player.GetSlotCooldown(slotIds[i]);
            ushort cdMax = abilities[i].CooldownTicks;

            if (cdRemaining > 0 && cdMax > 0)
            {
                float seconds = cdRemaining / 60f;
                if (_cdOverlays[i] != null) _cdOverlays[i]!.Visible = true;
                if (_cdTexts[i] != null)
                {
                    _cdTexts[i]!.Visible = true;
                    _cdTexts[i]!.Text = seconds >= 1f ? $"{seconds:F1}s" : $"{Mathf.RoundToInt(seconds * 60)}f";
                }
            }
            else
            {
                if (_cdOverlays[i] != null) _cdOverlays[i]!.Visible = false;
                if (_cdTexts[i] != null) _cdTexts[i]!.Visible = false;
            }
        }
    }

    private void UpdateComboIndicators()
    {
        if (_player == null) return;

        byte comboStage = _player.GetComboStage();
        ushort comboTimer = _player.GetComboTimerTicks();
        bool inComboWindow = comboStage > 0 && comboTimer > 0;

        if (_slotStyles[0] == null) return;
        _slotStyles[0]!.BorderWidthLeft = inComboWindow ? 2 : 1;
        _slotStyles[0]!.BorderWidthRight = inComboWindow ? 2 : 1;
        _slotStyles[0]!.BorderWidthTop = inComboWindow ? 2 : 1;
        _slotStyles[0]!.BorderWidthBottom = inComboWindow ? 2 : 1;
        _slotStyles[0]!.BorderColor = inComboWindow ? ComboBorder : new Color(0.3f, 0.3f, 0.4f, 1f);
    }
}
