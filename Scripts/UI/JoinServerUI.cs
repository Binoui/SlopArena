#nullable enable
using Godot;
using System;

/// <summary>
/// Join Server screen — enter IP address and connect.
/// Scene-based: layout in join_server.tscn, this script wires styling + events.
/// </summary>
public partial class JoinServerUI : Control
{
    public event Action<string>? OnConnect;
    public event Action? OnBack;

    private LineEdit _ipField = null!;
    private Button _connectBtn = null!;
    private Label _statusLabel = null!;
    private Label _ipLabel = null!;

    public override void _Ready()
    {
        _ipField = GetNode<LineEdit>("IpField");
        _connectBtn = GetNode<Button>("ConnectBtn");
        _statusLabel = GetNode<Label>("StatusLabel");
        _ipLabel = GetNode<Label>("IpLabel");

        UITheme.StyleTitle(GetNode<Label>("Title"));
        UITheme.StyleBody(_ipLabel, fontSize: 18);
        UITheme.StyleBody(_statusLabel, UITheme.DimWhite);
        UITheme.StyleInput(_ipField);
        UITheme.StyleButton(_connectBtn, UITheme.Success);
        UITheme.StyleButton(GetNode<Button>("BackBtn"), UITheme.DimWhite, 16);

        _ipField.TextChanged += (_) => _statusLabel.Text = "";
        _connectBtn.Pressed += OnConnectPressed;
        GetNode<Button>("BackBtn").Pressed += () => OnBack?.Invoke();
    }

    private void OnConnectPressed()
    {
        string ip = _ipField.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(ip))
        {
            _statusLabel.Text = "Please enter an IP address";
            _statusLabel.Modulate = UITheme.Danger;
            return;
        }
        OnConnect?.Invoke(ip);
    }

    public void SetStatus(string msg, bool isError = false)
    {
        _statusLabel.Text = msg;
        _statusLabel.Modulate = isError ? UITheme.Danger : UITheme.DimWhite;
    }
}
