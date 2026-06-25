#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Connects to the local UDP server over localhost:9876.
    /// Receives on a background thread; exposes received states on the main thread.
    ///
    /// Client -> Server (8 + 4 + InputState.Size bytes):
    ///   [0..7]   entityId (ulong)
    ///   [8..11]  tick (uint)
    ///   [12..]   InputState (InputState.Size bytes)
    ///
    /// Server -> Client (8 + 4 + CharacterStatePacket.Size bytes per entity):
    ///   [0..7]   entityId (ulong)
    ///   [8..11]  tick (uint)
    ///   [12..51] CharacterStatePacket (CharacterStatePacket.Size = 40 bytes)
    /// </summary>
    public class NetworkClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string _serverIp = "127.0.0.1";
        [SerializeField] private int _serverPort = 9876;

        private UdpClient? _udp;
        private IPEndPoint _serverEp = new(IPAddress.Loopback, 9876);
        private ulong _entityId = 1;
        private bool _connected;

        private Thread? _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<(ulong entityId, uint tick, CharacterState state)> _receivedQueue = new();

        /// <summary>This client's assigned entity ID.</summary>
        public ulong EntityId
        {
            get => _entityId;
            set => _entityId = value;
        }

        /// <summary>True while the UDP socket is connected.</summary>
        public bool IsServerConnected => _connected;

        /// <summary>Last received server tick (for comparison / latency estimation).</summary>
        public uint LastServerTick { get; private set; }

        private void Awake()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(_serverIp), _serverPort);
            Connect(endpoint);
        }

        private void OnDestroy()
        {
            _running = false;
            _receiveThread?.Join(1000);
            _udp?.Close();
            _udp = null;
            _connected = false;
        }

        /// <summary>Connect to the given server endpoint and start the receive loop.</summary>
        public void Connect(IPEndPoint endpoint)
        {
            if (_connected) return;

            _serverEp = endpoint;
            try
            {
                _udp = new UdpClient();
                _udp.Connect(_serverEp);
                _connected = true;
                Debug.Log($"[NetworkClient] Connected as entity {_entityId} to {endpoint}");

                _running = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "NetworkClient Receive"
                };
                _receiveThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Failed to connect: {ex.Message}");
                _connected = false;
            }
        }

        /// <summary>
        /// Send input for the current tick to the server.
        /// </summary>
        public void SendInput(InputState input, uint tick)
        {
            if (_udp == null || !_connected) return;

            int bufSize = 8 + 4 + InputState.Size;
            byte[] buf = new byte[bufSize];
            WriteUInt64LE(buf, 0, _entityId);
            WriteUInt32LE(buf, 8, tick);
            input.Write(buf.AsSpan(12));
            try
            {
                _udp.Send(buf, buf.Length);
            }
            catch (Exception)
            {
                _connected = false;
            }
        }

        /// <summary>
        /// Drain all received entity states since the last call.
        /// Safe to call from any thread, but intended for the main thread (e.g. Update / FixedUpdate).
        /// </summary>
        public Dictionary<ulong, CharacterState> ReceiveStates()
        {
            var result = new Dictionary<ulong, CharacterState>();
            while (_receivedQueue.TryDequeue(out var entry))
            {
                result[entry.entityId] = entry.state;
                LastServerTick = entry.tick;
            }
            return result;
        }

        // ── Background receive loop ──────────────────────────────────────────

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    // Poll with 1ms timeout — near-zero CPU when idle
                    if (_udp == null || !_udp.Client.Poll(1000, SelectMode.SelectRead))
                        continue;

                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = _udp.Receive(ref ep);
                    int minSize = 8 + 4 + CharacterStatePacket.Size;
                    if (buf.Length < minSize)
                        continue;

                    ulong eid = ReadUInt64LE(buf, 0);
                    uint tick = ReadUInt32LE(buf, 8);
                    var packet = CharacterStatePacket.Deserialize(buf.AsSpan(12));
                    _receivedQueue.Enqueue((eid, tick, packet.ToState()));
                }
                catch (Exception)
                {
                    if (_running)
                    {
                        _connected = false;
                        break;
                    }
                }
            }
        }

        // ── Manual little-endian helpers (avoid BitConverter allocation) ─────

        private static void WriteUInt64LE(byte[] buffer, int offset, ulong value)
        {
            buffer[offset]     = (byte)(value);
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset]     = (byte)(value);
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static ulong ReadUInt64LE(byte[] buffer, int offset)
        {
            return buffer[offset]
                 | ((ulong)buffer[offset + 1] << 8)
                 | ((ulong)buffer[offset + 2] << 16)
                 | ((ulong)buffer[offset + 3] << 24)
                 | ((ulong)buffer[offset + 4] << 32)
                 | ((ulong)buffer[offset + 5] << 40)
                 | ((ulong)buffer[offset + 6] << 48)
                 | ((ulong)buffer[offset + 7] << 56);
        }

        private static uint ReadUInt32LE(byte[] buffer, int offset)
        {
            return buffer[offset]
                 | ((uint)buffer[offset + 1] << 8)
                 | ((uint)buffer[offset + 2] << 16)
                 | ((uint)buffer[offset + 3] << 24);
        }
    }
}
