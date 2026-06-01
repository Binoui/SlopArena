#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Action Bar HUD — shows 6 class abilities with keybind labels and cooldowns.
/// Class abilities: LMB, RMB, 1, 2, 3, 4
/// </summary>
public partial class ActionBarHUD : Control
{
	private PlayerController? _player;
	
	private class AbilitySlot
	{
		public Panel Panel = null!;
		public Label KeyLabel = null!;
		public Label NameLabel = null!;
		public ColorRect CooldownOverlay = null!;
		public Label CooldownText = null!;
	}
	
	private readonly List<AbilitySlot> _slots = new();
	private Label? _classNameLabel;
	
	public void Setup(PlayerController player)
	{
		_player = player;
		BuildUI();
	}
	
	private void BuildUI()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		
		// Class name label (top of action bar area)
		_classNameLabel = new Label();
		_classNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_classNameLabel.AddThemeFontSizeOverride("font_size", 20);
		_classNameLabel.Modulate = new Color(1f, 0.8f, 0.2f);
		_classNameLabel.Position = new Vector2(0f, 940f);
		_classNameLabel.Size = new Vector2(1920f, 30f);
		AddChild(_classNameLabel);
		
		// Horizontal container for ability slots
		var hbox = new HBoxContainer();
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.Position = new Vector2(0f, 970f);
		hbox.Size = new Vector2(1920f, 80f);
		AddChild(hbox);
		
		string[] keys = { "LMB", "RMB", "Q", "E", "R", "F" };
		
		for (int i = 0; i < 6; i++)
		{
			var slot = new AbilitySlot();
			
			var panel = new Panel();
			panel.CustomMinimumSize = new Vector2(140f, 70f);
			panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.5f), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 });
			hbox.AddChild(panel);
			slot.Panel = panel;
			
			// Key label (top of slot)
			var keyLabel = new Label();
			keyLabel.Text = keys[i];
			keyLabel.HorizontalAlignment = HorizontalAlignment.Center;
			keyLabel.AddThemeFontSizeOverride("font_size", 16);
			keyLabel.Modulate = new Color(1f, 0.8f, 0.2f);
			keyLabel.Position = new Vector2(0f, 2f);
			keyLabel.Size = new Vector2(140f, 20f);
			panel.AddChild(keyLabel);
			slot.KeyLabel = keyLabel;
			
			// Ability name
			var nameLabel = new Label();
			nameLabel.Text = "...";
			nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
			nameLabel.AddThemeFontSizeOverride("font_size", 13);
			nameLabel.Modulate = new Color(0.8f, 0.8f, 0.8f);
			nameLabel.Position = new Vector2(0f, 24f);
			nameLabel.Size = new Vector2(140f, 22f);
			nameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
			panel.AddChild(nameLabel);
			slot.NameLabel = nameLabel;
			
			// Cooldown overlay (hidden by default)
			var cdOverlay = new ColorRect();
			cdOverlay.Color = new Color(0f, 0f, 0f, 0.6f);
			cdOverlay.Position = new Vector2(0f, 0f);
			cdOverlay.Size = new Vector2(140f, 70f);
			cdOverlay.Visible = false;
			panel.AddChild(cdOverlay);
			slot.CooldownOverlay = cdOverlay;
			
			// Cooldown text
			var cdText = new Label();
			cdText.HorizontalAlignment = HorizontalAlignment.Center;
			cdText.VerticalAlignment = VerticalAlignment.Center;
			cdText.AddThemeFontSizeOverride("font_size", 20);
			cdText.Modulate = new Color(1f, 1f, 1f, 0.9f);
			cdText.Position = new Vector2(0f, 15f);
			cdText.Size = new Vector2(140f, 30f);
			cdText.Visible = false;
			panel.AddChild(cdText);
			slot.CooldownText = cdText;
			
			_slots.Add(slot);
		}
		
		UpdateAbilityNames();
	}
	
	private void UpdateAbilityNames()
	{
		if (_player == null) return;
		
		var pc = _player.GetClass();
		string className = pc switch
		{
			PlayerController.PlayerClass.Vanguard => "VANGUARD",
			PlayerController.PlayerClass.Wraith => "WRAITH",
			PlayerController.PlayerClass.Channeler => "CHANNELER",
			_ => "UNKNOWN"
		};
		
		if (_classNameLabel != null)
			_classNameLabel.Text = className;
		
		string[] nameSet = pc switch
		{
			PlayerController.PlayerClass.Vanguard => new[]{ "Combo", "Heavy", "Shield Bash", "War Cry", "Intervene", "Thunderclap" },
			PlayerController.PlayerClass.Wraith => new[]{ "Combo", "Heavy", "Viper Shot", "Shadow Step", "Rapid Fire", "Freezing Trap" },
			PlayerController.PlayerClass.Channeler => new[]{ "Combo", "Heavy", "Frostbolt", "Dragon's Breath", "Ice Lance", "Meteor" },
			_ => new[]{ "-", "-", "-", "-", "-", "-" }
		};
		
		for (int i = 0; i < _slots.Count && i < nameSet.Length; i++)
		{
			_slots[i].NameLabel.Text = nameSet[i];
		}
	}
	
	public void OnClassChanged()
	{
		UpdateAbilityNames();
	}
}
