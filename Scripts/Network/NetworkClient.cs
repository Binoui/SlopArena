#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;

/// <summary>
/// Connects to the local UDP server.
/// Each frame: sends InputState (+ tick), receives CharacterState[].
/// </summary>
public partial class NetworkClient : Node
{
    private UdpClient? _udp;
    private IPEndPoint _serverEp = new(IPAddress.Loopback, 9876); // default, overridden in Connect()
    private ulong _entityId;
    private bool _connected;

    new public bool IsConnected => _connected;

    /// <summary>Last received server tick (for comparison).</summary>
    public uint LastServerTick { get; private set; }

    public void Connect(ulong entityId, string ip = "127.0.0.1", int port = 9876)
    {
        _entityId = entityId;
        _serverEp = new IPEndPoint(IPAddress.Parse(ip), port);
        try
        {
            _udp = new UdpClient();
            _udp.Connect(_serverEp);
            _connected = true;
            GD.Print($"[NetworkClient] Connected as entity {entityId} to {ip}:{port}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetworkClient] Failed to connect: {ex.Message}");
        }
    }

    /// <summary>
    /// Send packet format: entityId(8) + tick(4) + InputState(10) = 22 bytes
    /// </summary>
    public void SendInput(InputState input, uint tick)
    {
        if (_udp == null || !_connected) return;
        byte[] buf = new byte[8 + 4 + InputState.Size];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 8), _entityId);
        BitConverter.TryWriteBytes(buf.AsSpan(8, 4), tick);
        input.Write(buf.AsSpan(12));
        try { _udp.Send(buf, buf.Length); }
        catch { _connected = false; }
    }

    /// <summary>
    /// Receive packet format: entityId(8) + tick(4) + CharacterStatePacket(32) = 44 bytes each.
    /// Returns dict: entityId → (tick, state)
    /// </summary>
    public Dictionary<ulong, (uint tick, CharacterState state)> ReceiveStates()
    {
        var result = new Dictionary<ulong, (uint, CharacterState)>();
        if (_udp == null || !_connected) return result;

        while (_udp.Available > 0)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buf = _udp.Receive(ref ep);
                if (buf.Length < 8 + 4 + CharacterStatePacket.Size) continue;

                ulong eid = BitConverter.ToUInt64(buf, 0);
                uint tick = BitConverter.ToUInt32(buf, 8);
                var packet = CharacterStatePacket.Deserialize(buf.AsSpan(12));
                result[eid] = (tick, packet.ToState());
                LastServerTick = tick;
            }
            catch { break; }
        }
        return result;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            _udp?.Close();
            _udp = null;
        }
    }
}
