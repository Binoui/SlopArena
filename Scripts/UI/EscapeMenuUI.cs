#nullable enable
using Godot;
using System;

/// <summary>
/// Escape menu overlay — Resume, Spellbook, Settings, Exit Lobby, Exit Game.
/// Opens/closes with Escape. Blocks camera input while open.
/// </summary>
public partial class EscapeMenuUI : Control
{
	// ==========================================
	// SIGNALS (wired by Main.cs)
	// ==========================================
	
	public event Action? OnResumePressed;
	public event Action? OnSettingsRequested;
	public event Action? OnExitLobby;
	public event Action? OnExitGame;
	
	// ==========================================
	// STATE
	// ==========================================
	
	private bool _isOpen = false;
	private bool _settingsOpen = false;
	
	// ==========================================
	// UI NODES
	// ==========================================
	
	private ColorRect? _bg;
	private VBoxContainer? _menuContainer;
	private SettingsUI? _settingsUI;
	private Button? _resumeBtn;
	private Button? _spellbookBtn;
	private Button? _settingsBtn;
	private Button? _exitLobbyBtn;
	private Button? _exitGameBtn;
	private Label? _title;
	
	// ==========================================
	// BUILD
	// ==========================================
	
	public void Build()
	{
		var viewportSize = GetViewportRect().Size;
		Position = Vector2.Zero;
		Size = viewportSize;
		MouseFilter = MouseFilterEnum.Stop;
		
		// Background overlay
		_bg = new ColorRect();
		_bg.Color = new Color(0f, 0f, 0f, 0.7f);
		_bg.Position = Vector2.Zero;
		_bg.Size = viewportSize;
		_bg.MouseFilter = MouseFilterEnum.Stop;
		AddChild(_bg);
		
		// Title
		_title = new Label();
		_title.Text = "PAUSED";
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		_title.AddThemeFontSizeOverride("font_size", 48);
		_title.Position = new Vector2(0f, 80f);
		_title.Size = new Vector2(1920f, 60f);
		_title.Modulate = new Color(1f, 0.8f, 0.2f);
		AddChild(_title);
		
		// Menu container (vertical center)
		_menuContainer = new VBoxContainer();
		_menuContainer.Position = new Vector2(760f, 200f);
		_menuContainer.Size = new Vector2(400f, 500f);
		_menuContainer.AddThemeConstantOverride("separation", 12);
		AddChild(_menuContainer);
		
		// Resume
		_resumeBtn = MakeMenuButton("Resume");
		_resumeBtn.Pressed += () => { Close(); OnResumePressed?.Invoke(); };
		_menuContainer.AddChild(_resumeBtn);
		
		// Settings
		_settingsBtn = MakeMenuButton("Settings");
		_settingsBtn.Pressed += () => OpenSettings();
		_menuContainer.AddChild(_settingsBtn);
		
		// Exit Lobby
		_exitLobbyBtn = MakeMenuButton("Exit Lobby");
		_exitLobbyBtn.Pressed += () => { Close(); OnExitLobby?.Invoke(); };
		_menuContainer.AddChild(_exitLobbyBtn);
		
		// Exit Game
		_exitGameBtn = MakeMenuButton("Exit Game");
		_exitGameBtn.Modulate = new Color(1f, 0.3f, 0.3f);
		_exitGameBtn.Pressed += () => { Close(); OnExitGame?.Invoke(); };
		_menuContainer.AddChild(_exitGameBtn);
		
		// Escape hint at bottom
		var hint = new Label();
		hint.Text = "Press Escape to close";
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.Position = new Vector2(0f, 700f);
		hint.Size = new Vector2(1920f, 30f);
		hint.Modulate = new Color(0.5f, 0.5f, 0.5f);
		hint.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(hint);
		
		// Settings UI (hidden initially)
		_settingsUI = new SettingsUI();
		_settingsUI.Name = "SettingsUI";
		_settingsUI.Visible = false;
		_settingsUI.OnBack += CloseSettings;
		AddChild(_settingsUI);
		
		// Start hidden
		Visible = false;
		_isOpen = false;
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
	
	private Button MakeMenuButton(string text)
	{
		var btn = new Button();
		btn.Text = text;
		btn.CustomMinimumSize = new Vector2(400f, 50f);
		btn.AddThemeFontSizeOverride("font_size", 22);
		btn.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
		return btn;
	}
	
	// ==========================================
	// OPEN / CLOSE
	// ==========================================
	
	public void Open(PlayerController player)
	{
		_isOpen = true;
		_settingsOpen = false;
		Visible = true;
		_menuContainer!.Visible = true;
		_settingsUI!.Visible = false;
		_settingsUI.Close();
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
	
	// ==========================================
	// SETTINGS SUB-MENU
	// ==========================================
	
	private void OpenSettings()
	{
		_settingsOpen = true;
		_menuContainer!.Visible = false;
		_title!.Text = "SETTINGS";
		_settingsUI!.Visible = true;
		_settingsUI.BuildUI();
	}
	
	private void CloseSettings()
	{
		_settingsOpen = false;
		_menuContainer!.Visible = true;
		_title!.Text = "PAUSED";
		_settingsUI!.Visible = false;
	}
}
