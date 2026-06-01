#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Spell Book UI - full-screen overlay for browsing and assigning spells.
/// 
/// Layout:
/// - Left panel: Spell list grouped by role in a scrollable VBox (compact)
/// - Right panel: Action bar slots (1-4, A, E, Shift, R) in a 2x4 grid
/// - Preset buttons at the top
/// 
/// Drag & drop: click "Assign" on a spell, then click a slot to assign.
/// </summary>
public partial class SpellBookUI : Control
{
	// ==========================================
	// REFERENCES
	// ==========================================
	
	private PlayerController? _player;
	private SpellSystem? _spellSystem;
	
	// ==========================================
	// UI NODES
	// ==========================================
	
	private VBoxContainer? _spellListContainer;
	private GridContainer? _slotGrid;
	private HBoxContainer? _presetBar;
	private Button? _closeButton;
	private Label? _dragHintLabel;
	private Panel? _leftPanel;
	private Panel? _rightPanel;
	
	// ==========================================
	// CUSTOM TOOLTIP
	// ==========================================
	
	private Panel? _tooltipPanel;
	private Label? _tooltipLabel;
	private bool _isTooltipVisible = false;
	private string _currentTooltipText = "";
	
	// ==========================================
	// STATE
	// ==========================================
	
	private bool _isOpen = false;
	private int _draggedSpellId = -1;
	
	// ==========================================
	// COLORS PER ROLE
	// ==========================================
	
	private static readonly Dictionary<SpellRole, Color> RoleColors = new()
	{
		[SpellRole.Starter] = new Color(1f, 0.5f, 0f),    // Orange
		[SpellRole.Extender] = new Color(0f, 0.8f, 1f),   // Cyan
		[SpellRole.Finisher] = new Color(1f, 0.2f, 0.2f), // Red
		[SpellRole.Setup] = new Color(0.5f, 0f, 1f),      // Purple
		[SpellRole.Mobility] = new Color(0f, 1f, 0.5f),   // Green
	};
	
	// ==========================================
	// INIT
	// ==========================================
	
	public void Setup(PlayerController player, SpellSystem spellSystem)
	{
		_player = player;
		_spellSystem = spellSystem;
		
		BuildUI();
		
		Visible = false;
		_isOpen = false;
	}
	
	private void BuildUI()
	{
		// Full-screen size (force it)
		var viewportSize = GetViewportRect().Size;
		Position = Vector2.Zero;
		Size = viewportSize;
		
		// Semi-transparent black background
		var bg = new ColorRect();
		bg.Color = new Color(0f, 0f, 0f, 0.85f);
		bg.Position = Vector2.Zero;
		bg.Size = viewportSize;
		AddChild(bg);
		
		// Title
		var title = new Label();
		title.Text = "SPELL BOOK";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 32);
		title.Position = new Vector2(0f, 20f);
		title.Size = new Vector2(1920f, 50f);
		AddChild(title);
		
		// Preset bar at top
		_presetBar = new HBoxContainer();
		_presetBar.Position = new Vector2(100f, 80f);
		_presetBar.Size = new Vector2(1720f, 40f);
		AddChild(_presetBar);
		
		string[] presetNames = { "Starter", "Extender", "Finisher", "Setup", "Mobility" };
		foreach (var name in presetNames)
		{
			var btn = new Button();
			btn.Text = name;
			btn.CustomMinimumSize = new Vector2(300f, 40f);
			btn.Pressed += () => LoadPreset(name);
			_presetBar.AddChild(btn);
		}
		
		// ==========================================
		// MAIN CONTENT: two side-by-side panels
		// ==========================================
		
		// LEFT PANEL — Spell list (ScrollContainer)
		_leftPanel = new Panel();
		_leftPanel.Position = new Vector2(40f, 130f);
		_leftPanel.Size = new Vector2(900f, 700f);
		_leftPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		AddChild(_leftPanel);
		
		var scrollContainer = new ScrollContainer();
		scrollContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		_leftPanel.AddChild(scrollContainer);
		
		_spellListContainer = new VBoxContainer();
		_spellListContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollContainer.AddChild(_spellListContainer);
		
		// RIGHT PANEL — Slot grid
		_rightPanel = new Panel();
		_rightPanel.Position = new Vector2(980f, 130f);
		_rightPanel.Size = new Vector2(900f, 700f);
		_rightPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		AddChild(_rightPanel);
		
		_slotGrid = new GridContainer();
		_slotGrid.Columns = 2;
		_slotGrid.Position = new Vector2(10f, 10f);
		_slotGrid.Size = new Vector2(880f, 680f);
		_slotGrid.AddThemeConstantOverride("v_separation", 10);
		_slotGrid.AddThemeConstantOverride("h_separation", 10);
		_rightPanel.AddChild(_slotGrid);
		
		// Drag hint label below the panels
		_dragHintLabel = new Label();
		_dragHintLabel.Position = new Vector2(40f, 840f);
		_dragHintLabel.Size = new Vector2(1840f, 30f);
		_dragHintLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_dragHintLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
		AddChild(_dragHintLabel);
		
		// Populate content
		PopulateSpellList();
		PopulateSlotGrid();
		UpdateDragHint();
		
		// Custom tooltip (hidden off-screen)
		_tooltipPanel = new Panel();
		_tooltipPanel.Position = new Vector2(-1000f, -1000f);
		_tooltipPanel.Size = new Vector2(400f, 120f);
		_tooltipPanel.Visible = false;
		_tooltipPanel.MouseFilter = MouseFilterEnum.Ignore;
		_tooltipPanel.AddThemeColorOverride("panel", new Color(0.15f, 0.15f, 0.2f, 0.95f));
		AddChild(_tooltipPanel);
		
		_tooltipLabel = new Label();
		_tooltipLabel.Position = new Vector2(10f, 8f);
		_tooltipLabel.Size = new Vector2(380f, 104f);
		_tooltipLabel.AddThemeFontSizeOverride("font_size", 13);
		_tooltipLabel.Modulate = new Color(0.95f, 0.95f, 0.95f);
		_tooltipLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		_tooltipPanel.AddChild(_tooltipLabel);
		
		// Close button
		_closeButton = new Button();
		_closeButton.Text = "Close (Esc)";
		_closeButton.Position = new Vector2(1700f, 20f);
		_closeButton.Size = new Vector2(200f, 50f);
		_closeButton.Pressed += Close;
		AddChild(_closeButton);
	}
	
	private void PopulateSpellList()
	{
		if (_spellListContainer == null) return;
		
		foreach (var child in _spellListContainer.GetChildren())
			child.QueueFree();
		
		var allSpells = SpellSystem.GetAllSpells();
		var grouped = new Dictionary<SpellRole, List<SpellData>>();
		
		foreach (var kvp in allSpells)
		{
			var def = SpellCatalog.GetSpell((ushort)kvp.Key);
			if (!grouped.ContainsKey(def.Role))
				grouped[def.Role] = new List<SpellData>();
			grouped[def.Role].Add(kvp.Value);
		}
		
		// Trim padding inside VBox
		_spellListContainer.AddThemeConstantOverride("separation", 2);
		
		foreach (SpellRole role in Enum.GetValues<SpellRole>())
		{
			if (!grouped.ContainsKey(role)) continue;
			
			// Role header (compact)
			var header = new Label();
			header.Text = $"-- {role} --";
			header.AddThemeFontSizeOverride("font_size", 14);
			header.Modulate = RoleColors.GetValueOrDefault(role, Colors.White);
			header.CustomMinimumSize = new Vector2(0f, 20f);
			_spellListContainer.AddChild(header);
			
			// Compact spell rows
			foreach (var spell in grouped[role])
			{
				var row = new HBoxContainer();
				row.CustomMinimumSize = new Vector2(0f, 24f);
				row.MouseFilter = MouseFilterEnum.Stop;
				
				// Tooltip with spell details
				var spellDef = SpellCatalog.GetSpell((ushort)spell.SpellID);
				string castType = spellDef.Shape switch
				{
					SpellShape.MeleeCone => "Melee",
					SpellShape.FastProjectile => "Fast shot",
					SpellShape.SlowProjectile => "Slow shot",
					SpellShape.Beam => "Beam",
					SpellShape.Trap => "Trap",
					SpellShape.DelayedAoE => "Delayed AoE",
					_ => "?"
				};
				string tooltip = spellDef.Name + "\n" + spellDef.Description + "\n\nCD: " + spellDef.Cooldown + "s | Cast: " + spellDef.CastTime.ToString("0.00") + "s | Dmg: " + spellDef.Damage + " | Stun: " + spellDef.StunDuration.ToString("0.00") + "s\n" + castType + " / " + spellDef.Role;
				
				var nameLabel = new Label();
				nameLabel.Text = "#" + spell.SpellID + " " + spell.Name;
				nameLabel.CustomMinimumSize = new Vector2(200f, 22f);
				nameLabel.AddThemeFontSizeOverride("font_size", 12);
				nameLabel.MouseFilter = MouseFilterEnum.Stop;
				nameLabel.TooltipText = tooltip;
				string capturedTooltip = tooltip;
				nameLabel.MouseEntered += () => ShowTooltip(capturedTooltip);
				nameLabel.MouseExited += HideTooltip;
				row.AddChild(nameLabel);
				
				var assignBtn = new Button();
				assignBtn.Text = "Assign";
				assignBtn.CustomMinimumSize = new Vector2(80f, 22f);
				assignBtn.AddThemeFontSizeOverride("font_size", 11);
				assignBtn.Modulate = RoleColors.GetValueOrDefault(role, Colors.White);
				assignBtn.TooltipText = tooltip;
				assignBtn.MouseEntered += () => ShowTooltip(capturedTooltip);
				assignBtn.MouseExited += HideTooltip;
				int capturedId = spell.SpellID;
				assignBtn.Pressed += () => StartDrag(capturedId);
				row.AddChild(assignBtn);
				
				_spellListContainer.AddChild(row);
			}
		}
	}
	
	private void PopulateSlotGrid()
	{
		if (_slotGrid == null || _spellSystem == null) return;
		
		foreach (var child in _slotGrid.GetChildren())
			child.QueueFree();
		
		foreach (SlotType slot in Enum.GetValues<SlotType>())
		{
			var slotPanel = new Panel();
			slotPanel.CustomMinimumSize = new Vector2(430f, 155f);
			
			var vbox = new VBoxContainer();
			vbox.Position = new Vector2(8f, 5f);
			vbox.Size = new Vector2(414f, 145f);
			slotPanel.AddChild(vbox);
			
			// Slot key label
			var slotLabel = new Label();
			slotLabel.Text = "[" + GetSlotKeyName(slot) + "]";
			slotLabel.AddThemeFontSizeOverride("font_size", 16);
			slotLabel.Modulate = new Color(0.8f, 0.8f, 0.8f);
			vbox.AddChild(slotLabel);
			
			// Spell info (if assigned)
			var spellInSlot = _spellSystem.GetSpellInSlot(slot);
			if (spellInSlot != null)
			{
				var spellLabel = new Label();
				spellLabel.Text = spellInSlot.Name;
				spellLabel.AddThemeFontSizeOverride("font_size", 18);
				spellLabel.Modulate = Colors.Yellow;
				vbox.AddChild(spellLabel);
				
				var cdLabel = new Label();
				cdLabel.Text = "CD:" + spellInSlot.CooldownMax + "s  Cast:" + spellInSlot.CastTime.ToString("0.00") + "s";
				cdLabel.AddThemeFontSizeOverride("font_size", 12);
				vbox.AddChild(cdLabel);
				
				var clearBtn = new Button();
				clearBtn.Text = "X Clear";
				clearBtn.CustomMinimumSize = new Vector2(100f, 26f);
				SlotType capturedSlot = slot;
				clearBtn.Pressed += () => ClearSlot(capturedSlot);
				vbox.AddChild(clearBtn);
			}
			else
			{
				var emptyLabel = new Label();
				emptyLabel.Text = "(empty)";
				emptyLabel.AddThemeFontSizeOverride("font_size", 16);
				emptyLabel.Modulate = new Color(0.5f, 0.5f, 0.5f);
				vbox.AddChild(emptyLabel);
			}
			
			// "Assign here" button when dragging
			if (_draggedSpellId > 0)
			{
				var castingSpell = SpellSystem.GetSpellByID(_draggedSpellId);
				var assignBtn = new Button();
				assignBtn.Text = "<-- Assign \"" + (castingSpell?.Name ?? "#" + _draggedSpellId) + "\"";
				assignBtn.CustomMinimumSize = new Vector2(350f, 28f);
				assignBtn.Modulate = Colors.Green;
				SlotType capturedSlot = slot;
				int capturedId = _draggedSpellId;
				assignBtn.Pressed += () =>
				{
					AssignToSlot(capturedSlot, capturedId);
					_draggedSpellId = -1;
					PopulateSlotGrid();
					UpdateDragHint();
				};
				vbox.AddChild(assignBtn);
			}
			
			_slotGrid.AddChild(slotPanel);
		}
	}
	
	// ==========================================
	// ACTIONS
	// ==========================================
	
	private void StartDrag(int spellId)
	{
		_draggedSpellId = spellId;
		PopulateSlotGrid();
		UpdateDragHint();
		GD.Print("Drag started: spell #" + spellId);
	}
	
	private void AssignToSlot(SlotType slot, int spellId)
	{
		_spellSystem?.AssignSpellToSlot(slot, spellId);
		GD.Print("Assigned spell #" + spellId + " to slot " + slot);
	}
	
	private void ClearSlot(SlotType slot)
	{
		_spellSystem?.ClearSlot(slot);
		PopulateSlotGrid();
	}
	
	private void LoadPreset(string name)
	{
		_spellSystem?.LoadPreset(name);
		_draggedSpellId = -1;
		PopulateSlotGrid();
		UpdateDragHint();
		GD.Print("Loaded preset: " + name);
	}
	
	private void UpdateDragHint()
	{
		if (_dragHintLabel == null) return;
		
		if (_draggedSpellId > 0)
		{
			var spell = SpellSystem.GetSpellByID(_draggedSpellId);
			string spellName = spell?.Name ?? "#" + _draggedSpellId;
			_dragHintLabel.Text = "Assigning: " + spellName + "  --  click a slot on the right to place it.  (Right-click or press Esc to cancel)";
			_dragHintLabel.Modulate = Colors.Yellow;
		}
		else
		{
			_dragHintLabel.Text = "Click \"Assign\" on a spell, then click a slot on the right to set it.";
			_dragHintLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
		}
	}
	
	// ==========================================
	// OPEN / CLOSE
	// ==========================================
	
	public void Open()
	{
		_isOpen = true;
		Visible = true;
		_draggedSpellId = -1;
		PopulateSlotGrid();
		UpdateDragHint();
		
		if (_player != null)
			_player.IsSpellBookOpen = true;
		
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}
	
	public void Close()
	{
		_isOpen = false;
		Visible = false;
		_draggedSpellId = -1;
		
		if (_player != null)
			_player.IsSpellBookOpen = false;
	}
	
	public bool IsOpen() => _isOpen;
	
	public void Toggle()
	{
		if (_isOpen)
			Close();
		else
			Open();
	}
	
	// ==========================================
	// HELPERS
	// ==========================================
	
	private static string GetSlotKeyName(SlotType slot)
	{
		return slot switch
		{
			SlotType.Slot1 => "1",
			SlotType.Slot2 => "2",
			SlotType.Slot3 => "3",
			SlotType.Slot4 => "4",
			SlotType.Slot5 => "5",
			SlotType.Slot6 => "6",
			SlotType.Slot7 => "7",
			SlotType.Slot8 => "8",
			_ => "?"
		};
	}
	
	// ==========================================
	// CUSTOM TOOLTIP
	// ==========================================
	
	private void ShowTooltip(string text)
	{
		if (_tooltipPanel == null || _tooltipLabel == null) return;
		_currentTooltipText = text;
		_tooltipLabel.Text = text;
		_isTooltipVisible = true;
		_tooltipPanel.Visible = true;
		UpdateTooltipPosition();
	}
	
	private void HideTooltip()
	{
		if (_tooltipPanel == null) return;
		_isTooltipVisible = false;
		_tooltipPanel.Visible = false;
	}
	
	private void UpdateTooltipPosition()
	{
		if (_tooltipPanel == null) return;
		Vector2 mousePos = GetGlobalMousePosition();
		float offsetX = 20f;
		float panelW = _tooltipPanel.Size.X;
		float panelH = _tooltipPanel.Size.Y;
		float screenW = 1920f;
		float screenH = 1080f;
		float x = Math.Clamp(mousePos.X + offsetX, 0f, screenW - panelW);
		float y;
		if (mousePos.Y - panelH - 10 > 0f)
		{
			y = mousePos.Y - panelH - 10f; // Above cursor
		}
		else
		{
			y = mousePos.Y + 30f; // Below cursor if not enough room above
		}
		y = Math.Clamp(y, 10f, screenH - panelH - 10f);
		_tooltipPanel.Position = new Vector2(x, y);
	}
	
	public override void _Process(double delta)
	{
		base._Process(delta);
		if (_isTooltipVisible)
		{
			UpdateTooltipPosition();
		}
	}
}
