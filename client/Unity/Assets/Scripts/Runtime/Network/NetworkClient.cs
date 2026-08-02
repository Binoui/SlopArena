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
    public class NetworkClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string _serverIp = "127.0.0.1";
        [SerializeField] private int _serverPort = 9876;

        private volatile UdpClient? _udp;
        private IPEndPoint _serverEp = new(IPAddress.Loopback, 9876);
        private ulong _entityId = 1;
        private bool _connected;
        private Thread? _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<ServerEntityPacket> _receivedQueue = new();

        public ulong EntityId { get => _entityId; set => _entityId = value; }
        public bool IsServerConnected => _connected;
        public uint LastServerTick { get; private set; }

        // ── Lifecycle ──

        private void Awake()
        {
            _serverEp = new IPEndPoint(IPAddress.Parse(_serverIp), _serverPort);
            CreateSocket();
            StartReceiveThread();
        }

        private void CreateSocket()
        {
            try
            {
                _udp?.Close();
                _udp = new UdpClient();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Failed to create socket: {ex.Message}");
                _udp = null;
            }
        }

        private void StartReceiveThread()
        {
            if (_running) return;
            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "NetworkClient Receive"
            };
            _receiveThread.Start();
        }

        private void OnDestroy()
        {
            _running = false;
            _receiveThread?.Join(1000);
            _udp?.Close();
            _udp = null;
            _connected = false;
        }

        /// <summary>
        /// Re-point the client at a new server address. Safe to call before first SendInput.
        /// Closes the existing socket and opens a fresh one aimed at the new endpoint.
        /// </summary>
        public void Connect(string ip, int port)
        {
            _running = false;
            _receiveThread?.Join(500);
            _udp?.Close();
            _udp = null;
            _connected = false;

            _serverIp = ip;
            _serverPort = port;
            _serverEp = new IPEndPoint(IPAddress.Parse(ip), port);
            CreateSocket();
            StartReceiveThread();
        }

        // ── Send / Receive ──

        public void SendInput(InputState input, uint tick)
        {
            if (_udp == null) return;

            int bufSize = 8 + 4 + InputState.Size;
            byte[] buf = new byte[bufSize];
            WriteUInt64LE(buf, 0, _entityId);
            WriteUInt32LE(buf, 8, tick);
            input.Write(buf.AsSpan(12));
            try
            {
                _udp.Send(buf, buf.Length, _serverEp);
                _connected = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Send failed: {ex.Message}");
                _udp?.Close();
                _udp = null;
                _connected = false;
            }
        }

        /// <summary>
        /// Drain the receive queue into the latest server-authoritative states.
        /// The input-relay section (issue #80) is deliberately dropped here —
        /// the rollback bridge drains it via its own accessor when re-sim lands.
        /// </summary>
        public Dictionary<ulong, CharacterState> ReceiveStates()
        {
            var result = new Dictionary<ulong, CharacterState>();
            while (_receivedQueue.TryDequeue(out var entry))
            {
                result[entry.EntityId] = entry.State.ToState();
                LastServerTick = entry.Tick;
            }
            return result;
        }

        // ── Receive loop ──

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    if (_udp == null || !_udp.Client.Poll(1000, SelectMode.SelectRead))
                        continue;

                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = _udp.Receive(ref ep);
                    if (buf.Length < ServerEntityPacket.BaseSize) continue;

                    _receivedQueue.Enqueue(ServerEntityPacket.Deserialize(buf));
                }
                catch
                {
                    if (_running) break;
                }
            }
        }

        // ── Socket retry ──

        private void Update()
        {
            if (_udp == null && !_running)
            {
                Debug.Log("[NetworkClient] Recreating socket...");
                CreateSocket();
                StartReceiveThread();
            }
        }

        // ── Little-endian helpers ──

        private static void WriteUInt64LE(byte[] buf, int off, ulong val)
        {
            buf[off] = (byte)val; buf[off+1] = (byte)(val>>8);
            buf[off+2] = (byte)(val>>16); buf[off+3] = (byte)(val>>24);
            buf[off+4] = (byte)(val>>32); buf[off+5] = (byte)(val>>40);
            buf[off+6] = (byte)(val>>48); buf[off+7] = (byte)(val>>56);
        }

        private static void WriteUInt32LE(byte[] buf, int off, uint val)
        {
            buf[off] = (byte)val; buf[off+1] = (byte)(val>>8);
            buf[off+2] = (byte)(val>>16); buf[off+3] = (byte)(val>>24);
        }
    }
}
