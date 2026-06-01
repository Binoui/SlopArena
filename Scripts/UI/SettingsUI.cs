#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Settings submenu — Graphics (placeholder) + Controls (key rebinding).
/// Shows a list of bindable actions, click a row to rebind.
/// </summary>
public partial class SettingsUI : Control
{
	public event Action? OnBack;
	
	// ==========================================
	// REBINDABLE ACTIONS
	// ==========================================
	
	private readonly List<(string actionName, string displayName)> _bindableActions = DefaultBindings;
	
	// ==========================================
	// STATE
	// ==========================================
	
	private bool _isListening = false;
	private string? _listeningAction = null;
	private int _listeningRowIndex = -1;
	private bool _built = false;
	
	// ==========================================
	// UI NODES
	// ==========================================
	
	private VBoxContainer? _controlList;
	private Label? _listeningHint;
	private Button? _backBtn;
	private Panel? _graphicsPanel;
	private Panel? _controlsPanel;
	private TabContainer? _tabContainer;
	
	// ==========================================
	// BUILD
	// ==========================================
	
	public void BuildUI()
	{
		if (_built) return;
		_built = true;
		
		var viewportSize = GetViewportRect().Size;
		Position = Vector2.Zero;
		Size = viewportSize;
		
		// Back button (top-left)
		_backBtn = new Button();
		_backBtn.Text = "< Back";
		_backBtn.Position = new Vector2(40f, 40f);
		_backBtn.Size = new Vector2(120f, 40f);
		_backBtn.AddThemeFontSizeOverride("font_size", 18);
		_backBtn.Pressed += () => OnBack?.Invoke();
		AddChild(_backBtn);
		
		// Tab container
		_tabContainer = new TabContainer();
		_tabContainer.Position = new Vector2(40f, 100f);
		_tabContainer.Size = new Vector2(900f, 700f);
		_tabContainer.AddThemeFontSizeOverride("font_size", 16);
		AddChild(_tabContainer);
		
		// === GRAPHICS TAB ===
		_graphicsPanel = new Panel();
		_graphicsPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_tabContainer.AddChild(_graphicsPanel);
		
		var gfxVBox = new VBoxContainer();
		gfxVBox.Position = new Vector2(20f, 20f);
		gfxVBox.Size = new Vector2(860f, 660f);
		_graphicsPanel.AddChild(gfxVBox);
		
		var gfxTitle = new Label();
		gfxTitle.Text = "Graphics Settings";
		gfxTitle.AddThemeFontSizeOverride("font_size", 24);
		gfxVBox.AddChild(gfxTitle);
		
		var gfxPlaceholder = new Label();
		gfxPlaceholder.Text = "\nComing soon:\n- Resolution\n- Fullscreen / Windowed\n- VSync\n- Quality Preset\n- Shadow Quality\n- Anti-aliasing";
		gfxPlaceholder.AddThemeFontSizeOverride("font_size", 16);
		gfxPlaceholder.Modulate = new Color(0.6f, 0.6f, 0.6f);
		gfxVBox.AddChild(gfxPlaceholder);
		
		// === CONTROLS TAB ===
		_controlsPanel = new Panel();
		_controlsPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_tabContainer.AddChild(_controlsPanel);
		
		// Listening hint (shown when rebinding)
		_listeningHint = new Label();
		_listeningHint.Text = "Press a key to bind... (Escape to cancel)";
		_listeningHint.Position = new Vector2(40f, 60f);
		_listeningHint.Size = new Vector2(860f, 30f);
		_listeningHint.AddThemeFontSizeOverride("font_size", 18);
		_listeningHint.Modulate = new Color(1f, 0.8f, 0.2f);
		_listeningHint.Visible = false;
		_controlsPanel.AddChild(_listeningHint);
		
		// Scrollable list of bindings
		var scrollContainer = new ScrollContainer();
		scrollContainer.Position = new Vector2(20f, 20f);
		scrollContainer.Size = new Vector2(860f, 640f);
		_controlsPanel.AddChild(scrollContainer);
		
		_controlList = new VBoxContainer();
		_controlList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_controlList.AddThemeConstantOverride("separation", 4);
		scrollContainer.AddChild(_controlList);
		
		RebuildBindingList();
	}
	
	private void RebuildBindingList()
	{
		if (_controlList == null) return;
		
		foreach (var child in _controlList.GetChildren())
			child.QueueFree();
		
		for (int i = 0; i < _bindableActions.Count; i++)
		{
			var (actionName, displayName) = _bindableActions[i];
			int capturedIndex = i;
			
			var row = new HBoxContainer();
			row.CustomMinimumSize = new Vector2(0f, 36f);
			
			// Action name label
			var nameLabel = new Label();
			nameLabel.Text = displayName;
			nameLabel.CustomMinimumSize = new Vector2(300f, 36f);
			nameLabel.AddThemeFontSizeOverride("font_size", 18);
			row.AddChild(nameLabel);
			
			// Current key display
			var keyLabel = new Label();
			keyLabel.Text = GetKeyDisplayName(actionName);
			keyLabel.CustomMinimumSize = new Vector2(200f, 36f);
			keyLabel.AddThemeFontSizeOverride("font_size", 18);
			keyLabel.Modulate = new Color(0.8f, 0.8f, 0.3f);
			row.AddChild(keyLabel);
			
			// Rebind button
			var rebindBtn = new Button();
			rebindBtn.Text = "Rebind";
			rebindBtn.CustomMinimumSize = new Vector2(100f, 30f);
			rebindBtn.AddThemeFontSizeOverride("font_size", 14);
			rebindBtn.Pressed += () => StartListening(actionName, capturedIndex, keyLabel, rebindBtn);
			row.AddChild(rebindBtn);
			
			_controlList.AddChild(row);
		}
	}
	
	// ==========================================
	// KEY REBINDING
	// ==========================================
	
	private void StartListening(string actionName, int rowIndex, Label keyLabel, Button rebindBtn)
	{
		_isListening = true;
		_listeningAction = actionName;
		_listeningRowIndex = rowIndex;
		
		if (_listeningHint != null)
		{
			_listeningHint.Visible = true;
			_listeningHint.Text = $"Binding \"{GetActionDisplayName(actionName)}\" — press a key... (Escape to cancel)";
		}
		
		rebindBtn.Text = "...";
		rebindBtn.Disabled = true;
		keyLabel.Text = "[press a key]";
	}
	
	/// <summary>
	/// Handle Escape key: if listening for a rebind, cancel that first.
	/// If not listening, the parent (Main.cs) will close this via the escape menu toggle.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_isListening) return; // Let Main.cs handle Escape for closing
		
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			// Escape cancels rebinding
			if (keyEvent.Keycode == Key.Escape || keyEvent.PhysicalKeycode == Key.Escape)
			{
				CancelListening();
				GetViewport().SetInputAsHandled();
				return;
			}
			
			// Get the physical key (works with AZERTY/QWERTY)
			Key key = keyEvent.PhysicalKeycode != Key.None
				? keyEvent.PhysicalKeycode
				: keyEvent.Keycode;
			
			if (key != Key.None)
			{
				ApplyBinding(_listeningAction!, key);
				RebuildBindingList();
				_isListening = false;
				_listeningAction = null;
				
				if (_listeningHint != null)
					_listeningHint.Visible = false;
			}
			
			GetViewport().SetInputAsHandled();
		}
	}
	
	private void CancelListening()
	{
		_isListening = false;
		_listeningAction = null;
		
		if (_listeningHint != null)
			_listeningHint.Visible = false;
		
		RebuildBindingList();
	}
	
	private void ApplyBinding(string actionName, Key key)
	{
		// Clear existing bindings for this action
		InputMap.ActionEraseEvents(actionName);
		
		// Add the new key binding (PhysicalKeycode for AZERTY compatibility)
		var inputEvent = new InputEventKey();
		inputEvent.PhysicalKeycode = key;
		InputMap.ActionAddEvent(actionName, inputEvent);
		
		// Persist to disk
		SaveBindings();
		
		GD.Print($"Rebound \"{actionName}\" to {key}");
	}
	
	// ==========================================
	// HELPERS
	// ==========================================
	
	private static string GetKeyDisplayName(string actionName)
	{
		var events = InputMap.ActionGetEvents(actionName);
		if (events.Count > 0 && events[0] is InputEventKey keyEvent)
		{
			Key key = keyEvent.PhysicalKeycode != Key.None
				? keyEvent.PhysicalKeycode
				: keyEvent.Keycode;
			return KeyToDisplayName(key);
		}
		return "(unbound)";
	}
	
	private static string KeyToDisplayName(Key key)
	{
		return key switch
		{
			Key.Space => "Space",
			Key.Shift => "Shift",
			Key.Ctrl => "Ctrl",
			Key.Alt => "Alt",
			Key.Tab => "Tab",
			Key.Escape => "Escape",
			Key.Enter => "Enter",
			Key.Backspace => "Backspace",
			Key.Key1 => "1",
			Key.Key2 => "2",
			Key.Key3 => "3",
			Key.Key4 => "4",
			Key.Key5 => "5",
			Key.Key6 => "6",
			Key.Key7 => "7",
			Key.Key8 => "8",
			Key.Key9 => "9",
			Key.Key0 => "0",
			Key.A => "A",
			Key.B => "B",
			Key.C => "C",
			Key.D => "D",
			Key.E => "E",
			Key.F => "F",
			Key.G => "G",
			Key.H => "H",
			Key.I => "I",
			Key.J => "J",
			Key.K => "K",
			Key.L => "L",
			Key.M => "M",
			Key.N => "N",
			Key.O => "O",
			Key.P => "P",
			Key.Q => "Q",
			Key.R => "R",
			Key.S => "S",
			Key.T => "T",
			Key.U => "U",
			Key.V => "V",
			Key.W => "W",
			Key.X => "X",
			Key.Y => "Y",
			Key.Z => "Z",
			_ => key.ToString()
		};
	}
	
	private static string GetActionDisplayName(string actionName)
	{
		return actionName switch
		{
			"move_forward" => "Move Forward",
			"move_back" => "Move Back",
			"move_left" => "Move Left",
			"move_right" => "Move Right",
			"jump" => "Jump",
			"dash" => "Dash",
			"crouch" => "Crouch",
			"spell_slot1" => "Spell Slot 1",
			"spell_slot2" => "Spell Slot 2",
			"spell_slot3" => "Spell Slot 3",
			"spell_slot4" => "Spell Slot 4",
			"spell_slotA" => "Spell Slot 5",
			"spell_slotE" => "Spell Slot 6",
			"spell_slotR" => "Spell Slot 8",
			"spellbook_toggle" => "Spellbook",
			"target_next" => "Target Next",
			"trinket" => "Trinket",
			"tech" => "Tech Roll",
			"fast_fall" => "Fast Fall",
			_ => actionName
		};
	}

	// ==========================================
	// PERSISTENCE
	// ==========================================

	private const string ConfigPath = "user://input.cfg";

	public static void SaveBindings()
	{
		var config = new ConfigFile();
		foreach (var (actionName, _) in DefaultBindings)
		{
			var events = InputMap.ActionGetEvents(actionName);
			if (events.Count > 0 && events[0] is InputEventKey keyEvent)
			{
				int keyCode = (int)keyEvent.PhysicalKeycode;
				if (keyCode != 0)
					config.SetValue("input", actionName, keyCode);
			}
		}
		config.Save(ConfigPath);
	}

	public static void LoadBindings()
	{
		var config = new ConfigFile();
		if (config.Load(ConfigPath) != Error.Ok)
			return;

		foreach (var (actionName, _) in DefaultBindings)
		{
			var keyCode = (Key)(int)config.GetValue("input", actionName, 0);
			if (keyCode == Key.None) continue;

			var events = InputMap.ActionGetEvents(actionName);
			for (int i = events.Count - 1; i >= 0; i--)
			{
				if (events[i] is InputEventKey keyEv)
				{
					InputMap.ActionEraseEvent(actionName, keyEv);
					break;
				}
			}
			var inputEvent = new InputEventKey();
			inputEvent.PhysicalKeycode = keyCode;
			InputMap.ActionAddEvent(actionName, inputEvent);
		}
	}

	private static readonly List<(string actionName, string displayName)> DefaultBindings = new()
	{
		("move_forward",  "Move Forward"),
		("move_back",     "Move Back"),
		("move_left",     "Move Left"),
		("move_right",    "Move Right"),
		("jump",          "Jump"),
		("dash",          "Dash"),
		("crouch",        "Crouch"),
		("spell_slot1",   "Spell Slot 1"),
		("spell_slot2",   "Spell Slot 2"),
		("spell_slot3",   "Spell Slot 3"),
		("spell_slot4",   "Spell Slot 4"),
		("spell_slotA",   "Spell Slot 5"),
		("spell_slotE",   "Spell Slot 6"),
		("spell_slotR",   "Spell Slot 8"),
		("spellbook_toggle", "Spellbook"),
		("target_next",   "Target Next"),
		("trinket",       "Trinket"),
		("tech",          "Tech Roll"),
		("fast_fall",     "Fast Fall"),
	};

	public void Close()
	{
		CancelListening();
	}
	
	public override void _Notification(int what)
	{
		if (what == NotificationResized)
		{
			var size = GetViewportRect().Size;
			Position = Vector2.Zero;
			Size = size;
		}
	}
}
