#nullable enable
using System;
using System.Buffers.Binary;
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
        private readonly ConcurrentQueue<MatchResultPacket> _matchResultQueue = new();
        private readonly ConcurrentQueue<TimelinePresentationEvent> _presentationEventQueue = new();

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
            BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(0, 8), _entityId);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8, 4), tick);
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
        /// Drain the receive queue into raw per-entity packets — tick, hasInput/Input relay,
        /// and state all intact. RollbackSimulationBridge routes self packets to
        /// RollbackSimulator.ReconcileSelf and everything else to IngestOpponentBatch.
        /// </summary>
        public List<ServerEntityPacket> ReceiveEntityPackets()
        {
            var result = new List<ServerEntityPacket>();
            while (_receivedQueue.TryDequeue(out var entry))
            {
                result.Add(entry);
                LastServerTick = entry.Tick;
            }
            return result;
        }

        public List<TimelinePresentationEvent> ReceivePresentationEvents()
        {
            var result = new List<TimelinePresentationEvent>();
            while (_presentationEventQueue.TryDequeue(out var entry))
                result.Add(entry);
            return result;
        }
        /// <summary>Drain authoritative final match snapshots received from the server.</summary>
        public List<MatchResultPacket> ReceiveMatchResults()
        {
            var result = new List<MatchResultPacket>();
            while (_matchResultQueue.TryDequeue(out var entry))
                result.Add(entry);
            return result;
        }


        // ── Receive loop ──

        private void ReceiveLoop()
        {
            while (_running)
            {
                try
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buf = _udp.Receive(ref ep);
                    if (MatchResultPacket.TryDeserialize(buf, out var matchResult))
                    {
                        _matchResultQueue.Enqueue(matchResult!);
                        continue;
                    }
                    if (PresentationEventPacket.TryDeserialize(buf, out var presentationPacket))
                    {
                        _presentationEventQueue.Enqueue(presentationPacket!.Value.ToEvent());
                        continue;
                    }

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

    }
}
