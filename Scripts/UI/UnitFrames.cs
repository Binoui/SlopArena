#nullable enable
using Godot;
using System;
using SlopArena.Shared;

/// <summary>
/// WoW-style Unit Frames — top-left of the screen.
/// 
/// Layout:
/// ┌─────────────────────────────────────┐
/// │ [Player Portrait]  Player Name      │
/// │ ████████████████░░░░  HP: 80/100    │
/// ├─────────────────────────────────────┤
/// │ [Target Portrait]  Target Name      │
/// │ ████████████░░░░░░░░  HP: 60/100    │
/// └─────────────────────────────────────┘
/// 
/// Player frame is always visible.
/// Target frame appears when a target is selected.
/// </summary>
public partial class UnitFrames : Control
{
	// ==========================================
	// REFERENCES
	// ==========================================
	
	private PlayerController? _player;
	private DummyManager? _dummyMgr;
	
	// ==========================================
	// TARGET STATE
	// ==========================================
	
	private ulong _targetEntityId = 0;
	private bool _hasTarget = false;
	
	// ==========================================
	// UI NODES — PLAYER FRAME
	// ==========================================
	
	private Panel _playerFrame = null!;
	private Label _playerNameLabel = null!;
	private ColorRect _playerHpBar = null!;
	private ColorRect _playerHpBarBg = null!;
	private Label _playerHpText = null!;
	private TextureRect _playerPortrait = null!;
	
	// ==========================================
	// UI NODES — TARGET FRAME
	// ==========================================
	
	private Panel _targetFrame = null!;
	private Label _targetNameLabel = null!;
	private ColorRect _targetHpBar = null!;
	private ColorRect _targetHpBarBg = null!;
	private Label _targetHpText = null!;
	private TextureRect _targetPortrait = null!;
	
	// ==========================================
	// LAYOUT CONSTANTS
	// ==========================================
	
	private const float FrameWidth = 280f;
	private const float FrameHeight = 70f;
	private const float BarHeight = 18f;
	private const float PortraitSize = 50f;
	private const float Padding = 8f;
	private const float FrameSpacing = 10f;
	
	// ==========================================
	// INIT
	// ==========================================
	
	public void Setup(PlayerController player, DummyManager? dummyMgr)
	{
		_player = player;
		_dummyMgr = dummyMgr;
		
		BuildUI();
	}
	
	private void BuildUI()
	{
		// Position the whole control in the top-left
		SetAnchorsPreset(LayoutPreset.TopLeft);
		Position = new Vector2(20f, 20f);
		Size = new Vector2(FrameWidth * 2 + FrameSpacing, FrameHeight + Padding * 2);
		
		// ==========================================
		// PLAYER FRAME
		// ==========================================
		
		_playerFrame = new Panel();
		_playerFrame.Position = new Vector2(0f, 0f);
		_playerFrame.Size = new Vector2(FrameWidth, FrameHeight);
		_playerFrame.Modulate = new Color(0.15f, 0.15f, 0.2f, 0.85f);
		AddChild(_playerFrame);
		
		// Portrait background
		var portraitBg = new ColorRect();
		portraitBg.Position = new Vector2(Padding, (FrameHeight - PortraitSize) / 2f);
		portraitBg.Size = new Vector2(PortraitSize, PortraitSize);
		portraitBg.Color = new Color(0.1f, 0.1f, 0.15f);
		_playerFrame.AddChild(portraitBg);
		
		// Portrait (colored square for now — could be a class icon later)
		_playerPortrait = new TextureRect();
		_playerPortrait.Position = new Vector2(Padding + 2f, (FrameHeight - PortraitSize) / 2f + 2f);
		_playerPortrait.Size = new Vector2(PortraitSize - 4f, PortraitSize - 4f);
		// Create a simple colored placeholder
		var portraitImage = Image.Create((int)(PortraitSize - 4), (int)(PortraitSize - 4), false, Image.Format.Rgba8);
		portraitImage.Fill(new Color(0.2f, 0.5f, 1.0f)); // Blue for player
		var portraitTexture = ImageTexture.CreateFromImage(portraitImage);
		_playerPortrait.Texture = portraitTexture;
		_playerFrame.AddChild(_playerPortrait);
		
		// Player name
		_playerNameLabel = new Label();
		_playerNameLabel.Position = new Vector2(Padding + PortraitSize + 8f, 6f);
		_playerNameLabel.Size = new Vector2(FrameWidth - Padding - PortraitSize - 16f, 22f);
		_playerNameLabel.Text = "Player";
		_playerNameLabel.AddThemeFontSizeOverride("font_size", 14);
		_playerNameLabel.Modulate = new Color(0.8f, 0.9f, 1.0f);
		_playerFrame.AddChild(_playerNameLabel);
		
		// HP bar background
		float barX = Padding + PortraitSize + 8f;
		float barY = 30f;
		_playerHpBarBg = new ColorRect();
		_playerHpBarBg.Position = new Vector2(barX, barY);
		_playerHpBarBg.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_playerHpBarBg.Color = new Color(0.1f, 0.05f, 0.05f);
		_playerFrame.AddChild(_playerHpBarBg);
		
		// HP bar fill
		_playerHpBar = new ColorRect();
		_playerHpBar.Position = new Vector2(barX, barY);
		_playerHpBar.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_playerHpBar.Color = new Color(0.2f, 0.8f, 0.2f); // Green
		_playerFrame.AddChild(_playerHpBar);
		
		// HP text
		_playerHpText = new Label();
		_playerHpText.Position = new Vector2(barX, barY);
		_playerHpText.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_playerHpText.HorizontalAlignment = HorizontalAlignment.Center;
		_playerHpText.VerticalAlignment = VerticalAlignment.Center;
		_playerHpText.Text = "100 / 100";
		_playerHpText.AddThemeFontSizeOverride("font_size", 12);
		_playerHpText.Modulate = Colors.White;
		_playerFrame.AddChild(_playerHpText);
		
		// ==========================================
		// TARGET FRAME
		// ==========================================
		
		_targetFrame = new Panel();
		_targetFrame.Position = new Vector2(FrameWidth + FrameSpacing, 0f);
		_targetFrame.Size = new Vector2(FrameWidth, FrameHeight);
		_targetFrame.Modulate = new Color(0.15f, 0.15f, 0.2f, 0.85f);
		_targetFrame.Visible = false; // Hidden until a target is selected
		AddChild(_targetFrame);
		
		// Portrait background
		var targetPortraitBg = new ColorRect();
		targetPortraitBg.Position = new Vector2(Padding, (FrameHeight - PortraitSize) / 2f);
		targetPortraitBg.Size = new Vector2(PortraitSize, PortraitSize);
		targetPortraitBg.Color = new Color(0.1f, 0.1f, 0.15f);
		_targetFrame.AddChild(targetPortraitBg);
		
		// Portrait
		_targetPortrait = new TextureRect();
		_targetPortrait.Position = new Vector2(Padding + 2f, (FrameHeight - PortraitSize) / 2f + 2f);
		_targetPortrait.Size = new Vector2(PortraitSize - 4f, PortraitSize - 4f);
		var targetPortraitImage = Image.Create((int)(PortraitSize - 4), (int)(PortraitSize - 4), false, Image.Format.Rgba8);
		targetPortraitImage.Fill(new Color(1.0f, 0.2f, 0.2f)); // Red for target
		var targetPortraitTexture = ImageTexture.CreateFromImage(targetPortraitImage);
		_targetPortrait.Texture = targetPortraitTexture;
		_targetFrame.AddChild(_targetPortrait);
		
		// Target name
		_targetNameLabel = new Label();
		_targetNameLabel.Position = new Vector2(Padding + PortraitSize + 8f, 6f);
		_targetNameLabel.Size = new Vector2(FrameWidth - Padding - PortraitSize - 16f, 22f);
		_targetNameLabel.Text = "Target";
		_targetNameLabel.AddThemeFontSizeOverride("font_size", 14);
		_targetNameLabel.Modulate = new Color(1.0f, 0.8f, 0.8f);
		_targetFrame.AddChild(_targetNameLabel);
		
		// HP bar background
		_targetHpBarBg = new ColorRect();
		_targetHpBarBg.Position = new Vector2(barX, barY);
		_targetHpBarBg.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_targetHpBarBg.Color = new Color(0.1f, 0.05f, 0.05f);
		_targetFrame.AddChild(_targetHpBarBg);
		
		// HP bar fill
		_targetHpBar = new ColorRect();
		_targetHpBar.Position = new Vector2(barX, barY);
		_targetHpBar.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_targetHpBar.Color = new Color(0.8f, 0.2f, 0.2f); // Red
		_targetFrame.AddChild(_targetHpBar);
		
		// HP text
		_targetHpText = new Label();
		_targetHpText.Position = new Vector2(barX, barY);
		_targetHpText.Size = new Vector2(FrameWidth - barX - Padding, BarHeight);
		_targetHpText.HorizontalAlignment = HorizontalAlignment.Center;
		_targetHpText.VerticalAlignment = VerticalAlignment.Center;
		_targetHpText.Text = "100 / 100";
		_targetHpText.AddThemeFontSizeOverride("font_size", 12);
		_targetHpText.Modulate = Colors.White;
		_targetFrame.AddChild(_targetHpText);
	}
	
	// ==========================================
	// TARGETING
	// ==========================================
	
	/// <summary>
	/// Set the current target entity ID.
	/// Pass 0 to clear target.
	/// </summary>
	public void SetTarget(ulong entityId)
	{
		_targetEntityId = entityId;
		_hasTarget = entityId > 0;
		_targetFrame.Visible = _hasTarget;
		
		if (!_hasTarget)
		{
			_targetNameLabel.Text = "";
			_targetHpText.Text = "";
		}
	}
	
	public ulong GetTarget() => _targetEntityId;
	public bool HasTarget() => _hasTarget;
	
	// ==========================================
	// UPDATE
	// ==========================================
	
	public override void _Process(double delta)
	{
		if (_player == null) return;
		
		// --- Update player frame ---
		float playerHp = _player.GetHP();
		float playerMaxHp = _player.GetMaxHP();
		float playerRatio = Mathf.Clamp(playerHp / playerMaxHp, 0f, 1f);
		
		float barWidth = _playerHpBarBg.Size.X;
		_playerHpBar.Size = new Vector2(barWidth * playerRatio, BarHeight);
		_playerHpText.Text = $"{playerHp:F0} / {playerMaxHp:F0}";
		
		// Color changes based on HP %
		if (playerRatio > 0.6f)
			_playerHpBar.Color = new Color(0.2f, 0.8f, 0.2f); // Green
		else if (playerRatio > 0.3f)
			_playerHpBar.Color = new Color(0.9f, 0.7f, 0.1f); // Yellow
		else
			_playerHpBar.Color = new Color(0.9f, 0.2f, 0.1f); // Red
		
		// --- Update target frame ---
		if (_hasTarget && _dummyMgr != null)
		{
			// Dummy IDs are 100-104
			int dummyIndex = (int)(_targetEntityId - 100);
			if (dummyIndex >= 0 && dummyIndex < 5)
			{
				int targetHp = _dummyMgr.GetHP(dummyIndex);
				int targetMaxHp = _dummyMgr.GetMaxHP();
				float targetRatio = Mathf.Clamp((float)targetHp / targetMaxHp, 0f, 1f);
				
				float targetBarWidth = _targetHpBarBg.Size.X;
				_targetHpBar.Size = new Vector2(targetBarWidth * targetRatio, BarHeight);
				_targetHpText.Text = $"{targetHp} / {targetMaxHp}";
				_targetNameLabel.Text = $"Dummy {dummyIndex + 1}";
				
				// Color
				if (targetRatio > 0.6f)
					_targetHpBar.Color = new Color(0.8f, 0.2f, 0.2f); // Red
				else if (targetRatio > 0.3f)
					_targetHpBar.Color = new Color(0.9f, 0.7f, 0.1f); // Yellow
				else
					_targetHpBar.Color = new Color(0.5f, 0.1f, 0.1f); // Dark red
			}
			else
			{
				// Unknown target — hide frame
				_targetFrame.Visible = false;
				_hasTarget = false;
			}
		}
	}
}
