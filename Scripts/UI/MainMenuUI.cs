#nullable enable
using Godot;
using System;

/// <summary>
/// Main menu screen — 3 options: Training, Join Server, Host Server.
/// Scene-based: layout is in main_menu.tscn, this script wires events + styling.
/// Open Scripts/UI/main_menu.tscn in the Godot editor to edit the layout visually.
/// </summary>
public partial class MainMenuUI : Control
{
	public event Action? OnTrainingMode;
	public event Action? OnJoinServer;
	public event Action? OnHostServer;
	public event Action? OnQuit;

	private Button _trainingBtn = null!;
	private Button _joinBtn = null!;
	private Button _hostBtn = null!;
	private Button _quitBtn = null!;
	private Label _title = null!;

	public override void _Ready()
	{
		// Get nodes from the scene
		_title = GetNode<Label>("Title");
		_trainingBtn = GetNode<Button>("TrainingBtn");
		_joinBtn = GetNode<Button>("JoinBtn");
		_hostBtn = GetNode<Button>("HostBtn");
		_quitBtn = GetNode<Button>("QuitBtn");

		// Apply theme styles
		UITheme.StyleTitle(_title);
		UITheme.StyleButton(_trainingBtn);
		UITheme.StyleButton(_joinBtn);
		UITheme.StyleButton(_hostBtn, UITheme.Teal);
		UITheme.StyleButton(_quitBtn, UITheme.Danger);

		// Wire events
		_trainingBtn.Pressed += () => OnTrainingMode?.Invoke();
		_joinBtn.Pressed += () => OnJoinServer?.Invoke();
		_hostBtn.Pressed += () => OnHostServer?.Invoke();
		_quitBtn.Pressed += () => OnQuit?.Invoke();
	}
}
