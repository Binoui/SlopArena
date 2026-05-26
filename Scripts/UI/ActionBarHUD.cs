#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using SlopArena.Shared;

/// <summary>
/// Action Bar HUD - bottom-of-screen bar showing 8 spell slots with cooldown overlays.
/// 
/// Layout:
/// - 8 slots in a horizontal row
/// - Each slot shows: keybind label, spell name, cooldown overlay (dark + timer text)
/// - Cooldowns update every frame via _Process
/// - Clicking a slot triggers the spell (same as pressing the key)
/// 
/// Connected to PlayerController and SpellSystem.
/// </summary>
public partial class ActionBarHUD : Control
{
	// ==========================================
	// REFERENCES
	// ==========================================
	
	private PlayerController? _player;
	private SpellSystem? _spellSystem;
	
	// ==========================================
	// SLOT UI NODES
	// ==========================================
	
	private class SlotUI
	{
		public Panel Panel = null!;
		public Label KeyLabel = null!;
		public Label NameLabel = null!;
		public ColorRect CooldownOverlay = null!;
		public Label CooldownText = null!;
		public SlotType SlotType;
	}
	
	private List<SlotUI> _slots = new();
	private HBoxContainer? _hbox;
	
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
	}
	
	private void BuildUI()
	{
		// Fill the entire screen so anchors work
		SetAnchorsPreset(LayoutPreset.FullRect);
		
		// Main container (centered at bottom)
		_hbox = new HBoxContainer();
		_hbox.Alignment = BoxContainer.AlignmentMode.Center;
		AddChild(_hbox);
		
		// Create 8 slots
		foreach (SlotType slot in Enum.GetValues<SlotType>())
		{
			var slotUI = new SlotUI();
			slotUI.SlotType = slot;
			
			// Panel
			slotUI.Panel = new Panel();
			slotUI.Panel.CustomMinimumSize = new Vector2(130f, 110f);
			
			var vbox = new VBoxContainer();
			vbox.Position = new Vector2(5f, 5f);
			vbox.Size = new Vector2(120f, 100f);
			slotUI.Panel.AddChild(vbox);
			
			// Key label
			slotUI.KeyLabel = new Label();
			slotUI.KeyLabel.Text = GetSlotKeyName(slot);
			slotUI.KeyLabel.AddThemeFontSizeOverride("font_size", 14);
			slotUI.KeyLabel.Modulate = new Color(0.7f, 0.7f, 0.7f);
			vbox.AddChild(slotUI.KeyLabel);
			
			// Spell name
			slotUI.NameLabel = new Label();
			slotUI.NameLabel.AddThemeFontSizeOverride("font_size", 12);
			slotUI.NameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
			slotUI.NameLabel.MaxLinesVisible = 2;
			vbox.AddChild(slotUI.NameLabel);
			
			// Cooldown overlay (dark overlay)
			slotUI.CooldownOverlay = new ColorRect();
			slotUI.CooldownOverlay.Color = new Color(0f, 0f, 0f, 0.6f);
			slotUI.CooldownOverlay.Size = new Vector2(130f, 110f);
			slotUI.CooldownOverlay.Visible = false;
			slotUI.Panel.AddChild(slotUI.CooldownOverlay);
			
			// Cooldown text (on top of overlay)
			slotUI.CooldownText = new Label();
			slotUI.CooldownText.AddThemeFontSizeOverride("font_size", 24);
			slotUI.CooldownText.HorizontalAlignment = HorizontalAlignment.Center;
			slotUI.CooldownText.VerticalAlignment = VerticalAlignment.Center;
			slotUI.CooldownText.Size = new Vector2(130f, 110f);
			slotUI.CooldownText.Visible = false;
			slotUI.Panel.AddChild(slotUI.CooldownText);
			
			// Click to cast
			SlotType capturedSlot = slot;
			slotUI.Panel.GuiInput += (InputEvent @event) =>
			{
				if (@event is InputEventMouseButton btn && btn.Pressed && btn.ButtonIndex == MouseButton.Left)
				{
					TriggerSlot(capturedSlot);
				}
			};
			
			_hbox.AddChild(slotUI.Panel);
			_slots.Add(slotUI);
		}
		
		// Position the HBox at bottom center after layout
		UpdatePosition();
	}
	
	private void UpdatePosition()
	{
		if (_hbox == null) return;
		
		// Get the viewport size
		Vector2 viewportSize = GetViewportRect().Size;
		
		// Position the HBox at bottom center
		float totalWidth = _slots.Count * 130f;
		float x = (viewportSize.X - totalWidth) / 2f;
		float y = viewportSize.Y - 130f;
		
		_hbox.Position = new Vector2(x, y);
		_hbox.Size = new Vector2(totalWidth, 120f);
	}
	
	public override void _Process(double delta)
	{
		// Update position every frame to handle window resizing
		UpdatePosition();
		
		if (_spellSystem == null) return;
		
		foreach (var slotUI in _slots)
		{
			var spell = _spellSystem.GetSpellInSlot(slotUI.SlotType);
			float cd = _spellSystem.GetCooldown(slotUI.SlotType);
			
			if (spell != null)
			{
				slotUI.NameLabel.Text = spell.Name;
				
				// Color by role
				var def = SpellCatalog.GetSpell((ushort)spell.SpellID);
				slotUI.NameLabel.Modulate = RoleColors.GetValueOrDefault(def.Role, Colors.White);
				
				// Cooldown overlay
				if (cd > 0f)
				{
					slotUI.CooldownOverlay.Visible = true;
					slotUI.CooldownText.Visible = true;
					slotUI.CooldownText.Text = cd.ToString("F1");
				}
				else
				{
					slotUI.CooldownOverlay.Visible = false;
					slotUI.CooldownText.Visible = false;
				}
			}
			else
			{
				slotUI.NameLabel.Text = "";
				slotUI.CooldownOverlay.Visible = false;
				slotUI.CooldownText.Visible = false;
			}
		}
	}
	
	// ==========================================
	// TRIGGER
	// ==========================================
	
	private void TriggerSlot(SlotType slot)
	{
		if (_player == null) return;
		
		// Simulate key press via the player's combat component
		var combat = _player.GetCombatComponent();
		if (combat != null)
		{
			combat.TriggerSlot(slot);
		}
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
			SlotType.SlotA => "A",
			SlotType.SlotE => "E",
			SlotType.Shift => "Shift",
			SlotType.Elite => "R",
			_ => "?"
		};
	}
}
