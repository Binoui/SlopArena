#nullable enable
using Godot;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

/// <summary>
/// Host Lobby screen — shows IP/port, connected player count, Start button.
/// Scene-based: layout in host_lobby.tscn, this script wires styling + events.
/// </summary>
public partial class HostLobbyUI : Control
{
    public event Action? OnStartGame;
    public event Action? OnBack;

    private Label _ipDisplay = null!;
    private Label _playersCount = null!;
    private Label _hint = null!;
    private Button _startBtn = null!;
    private int _connectedPlayers = 1;

    public override void _Ready()
    {
        _ipDisplay = GetNode<Label>("InfoPanel/IpDisplay");
        _playersCount = GetNode<Label>("InfoPanel/PlayersCount");
        _hint = GetNode<Label>("Hint");
        _startBtn = GetNode<Button>("StartBtn");

        // Style all nodes
        UITheme.StyleTitle(GetNode<Label>("Title"));
        UITheme.StyleHeading(GetNode<Label>("InfoPanel/IpHeading"));
        UITheme.StyleHeading(GetNode<Label>("InfoPanel/PlayersHeading"));
        UITheme.StyleBody(_hint, UITheme.DimWhite, 14);
        UITheme.StyleButton(_startBtn, UITheme.Orange, 24);
        UITheme.StyleButton(GetNode<Button>("BackBtn"), UITheme.DimWhite, 16);
        UITheme.StyleBody(_ipDisplay, UITheme.TealBright, 24);
        UITheme.StyleBody(_playersCount, UITheme.DimWhite, 24);

        // Set IP
        string localIp = GetLocalIpAddress();
        _ipDisplay.Text = $"{localIp}:9876";

        // Wire events
        _startBtn.Pressed += () => OnStartGame?.Invoke();
        GetNode<Button>("BackBtn").Pressed += () => OnBack?.Invoke();
    }

    public void SetPlayerCount(int count)
    {
        _connectedPlayers = count;
        _playersCount.Text = $"{count} / 2";
        _startBtn.Disabled = count < 2;
        _hint.Text = count >= 2 ? "All players connected!" : "Waiting for other players to connect...";
        _hint.Modulate = count >= 2 ? UITheme.Success : UITheme.DimWhite;
    }

    private static string GetLocalIpAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address) &&
                    !addr.Address.ToString().StartsWith("169.254"))
                    return addr.Address.ToString();
            }
        }
        return "127.0.0.1";
    }
}
