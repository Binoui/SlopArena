#nullable enable
using Godot;
using System;

/// <summary>
/// Escape menu overlay — Resume, Settings, Exit Lobby, Exit Game.
/// Uses Kenney UI assets for a cohesive retro-futuristic look.
/// </summary>
public partial class EscapeMenuUI : Control
{
    public event Action? OnResumePressed;
    public event Action? OnExitLobby;
    public event Action? OnExitGame;

    private bool _isOpen = false;
    private bool _settingsOpen = false;

    private ColorRect? _bg;
    private VBoxContainer? _menuContainer;
    private SettingsUI? _settingsUI;
    private Label? _title;
    private Font? _kenneyFont;

    private static readonly Color BgColor = new Color(0.05f, 0.05f, 0.1f, 0.85f);
    private static readonly Color GoldColor = new Color(1f, 0.8f, 0.2f);
    private static readonly Color DangerColor = new Color(1f, 0.3f, 0.3f);

    public void Build()
    {
        _kenneyFont = GD.Load<Font>("res://assets/ui/font/Kenney Future.ttf");

        var viewportSize = GetViewportRect().Size;
        Position = Vector2.Zero;
        Size = viewportSize;
        MouseFilter = MouseFilterEnum.Stop;

        // Dark overlay background
        _bg = new ColorRect();
        _bg.Color = BgColor;
        _bg.Position = Vector2.Zero;
        _bg.Size = viewportSize;
        _bg.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_bg);

        // Title — "PAUSED"
        _title = new Label();
        _title.Text = "PAUSED";
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.VerticalAlignment = VerticalAlignment.Center;
        _title.Position = new Vector2(0f, 80f);
        _title.Size = new Vector2(1920f, 70f);
        if (_kenneyFont != null)
            _title.AddThemeFontOverride("font", _kenneyFont);
        _title.AddThemeFontSizeOverride("font_size", 56);
        _title.Modulate = GoldColor;
        AddChild(_title);

        // Menu buttons container
        _menuContainer = new VBoxContainer();
        _menuContainer.Position = new Vector2(760f, 200f);
        _menuContainer.Size = new Vector2(400f, 500f);
        _menuContainer.AddThemeConstantOverride("separation", 14);
        AddChild(_menuContainer);

        // Resume
        AddKenneyButton("Resume", () => { Close(); OnResumePressed?.Invoke(); }, null);

        // Settings
        AddKenneyButton("Settings", OpenSettings, null);

        // Exit Lobby
        AddKenneyButton("Exit Lobby", () => { Close(); OnExitLobby?.Invoke(); }, null);

        // Exit Game (red tint)
        AddKenneyButton("Exit Game", () => { Close(); OnExitGame?.Invoke(); }, DangerColor);

        // Escape hint
        var hint = new Label();
        hint.Text = "Press Escape to close";
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Position = new Vector2(0f, 700f);
        hint.Size = new Vector2(1920f, 30f);
        hint.Modulate = new Color(0.4f, 0.4f, 0.5f);
        hint.MouseFilter = MouseFilterEnum.Ignore;
        if (_kenneyFont != null)
            hint.AddThemeFontOverride("font", _kenneyFont);
        hint.AddThemeFontSizeOverride("font_size", 16);
        AddChild(hint);

        // Settings sub-menu (hidden initially)
        _settingsUI = new SettingsUI();
        _settingsUI.Name = "SettingsUI";
        _settingsUI.Visible = false;
        _settingsUI.OnBack += CloseSettings;
        AddChild(_settingsUI);

        Visible = false;
        _isOpen = false;
    }

    private void AddKenneyButton(string text, Action onPressed, Color? tint = null)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(400f, 54f);
        btn.Size = new Vector2(400f, 54f);

        // Kenney-style button background via StyleBoxFlat
        var style = new StyleBoxFlat();
        style.BgColor = tint ?? new Color(0.12f, 0.12f, 0.18f, 1f);
        style.SetCornerRadiusAll(6);
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthBottom = 2;
        style.BorderColor = tint.HasValue
            ? new Color(1f, 0.4f, 0.4f, 1f)
            : new Color(0.4f, 0.4f, 0.6f, 1f);
        btn.AddThemeStyleboxOverride("normal", style);

        // Hover: brighter
        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = tint.HasValue
            ? new Color(0.25f, 0.08f, 0.08f, 1f)
            : new Color(0.18f, 0.18f, 0.28f, 1f);
        hoverStyle.SetCornerRadiusAll(6);
        hoverStyle.BorderWidthLeft = 2;
        hoverStyle.BorderWidthRight = 2;
        hoverStyle.BorderWidthTop = 2;
        hoverStyle.BorderWidthBottom = 2;
        hoverStyle.BorderColor = tint.HasValue
            ? new Color(1f, 0.5f, 0.5f, 1f)
            : new Color(0.6f, 0.6f, 0.9f, 1f);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        // Font
        if (_kenneyFont != null)
            btn.AddThemeFontOverride("font", _kenneyFont);
        btn.AddThemeFontSizeOverride("font_size", 20);
        btn.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 1f));
        btn.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));

        btn.Pressed += onPressed;
        _menuContainer!.AddChild(btn);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            var size = GetViewportRect().Size;
            Position = Vector2.Zero;
            Size = size;
            if (_bg != null)
            {
                _bg.Position = Vector2.Zero;
                _bg.Size = size;
            }
        }
    }

    /// <summary>
    /// player — unused, kept for API consistency
    /// </summary>
    /// <param name="_"></param>
    public void Open(PlayerController _)
    {
        _isOpen = true;
        _settingsOpen = false;
        Visible = true;
        _menuContainer!.Visible = true;
        if (_settingsUI != null)
        {
            _settingsUI.Visible = false;
            _settingsUI.Close();
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        if (_settingsOpen)
        {
            CloseSettings();
            return;
        }
        _isOpen = false;
        _settingsOpen = false;
        Visible = false;
        _settingsUI?.Close();
    }

    public bool IsOpen() => _isOpen || _settingsOpen;

    public void Toggle(PlayerController player)
    {
        if (_isOpen || _settingsOpen)
            Close();
        else
            Open(player);
    }

    private void OpenSettings()
    {
        _settingsOpen = true;
        _menuContainer!.Visible = false;
        if (_title != null) _title.Text = "SETTINGS";
        if (_settingsUI != null)
        {
            _settingsUI.Visible = true;
            _settingsUI.BuildUI();
        }
    }

    private void CloseSettings()
    {
        _settingsOpen = false;
        _menuContainer!.Visible = true;
        if (_title != null) _title.Text = "PAUSED";
        if (_settingsUI != null) _settingsUI.Visible = false;
    }
}
