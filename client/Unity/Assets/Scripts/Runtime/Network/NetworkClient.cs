#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
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
        private volatile bool _connected;
        private Thread? _receiveThread;
        private volatile bool _running;
        private readonly ConcurrentQueue<MatchResultPacket> _matchResultQueue = new();
        private readonly ConcurrentQueue<TimelinePresentationEvent> _presentationEventQueue = new();

        private long _nextPingNonce;
        private long _lastPingSentAt;
        private long _lastPingReceivedAt;
        private long _lastPingMsBits;
        private long _lastPingTick;
        private long _lastPingRequestAt;
        private long _lastServerPacketAt;
        private readonly byte[] _pingRequest = new byte[12];
        public float? LastPingMilliseconds
        {
            get
            {
                long receivedAt = Interlocked.Read(ref _lastPingReceivedAt);
                return receivedAt != 0 && ElapsedSeconds(receivedAt, Stopwatch.GetTimestamp()) <= 3
                    ? (float)BitConverter.Int64BitsToDouble(Interlocked.Read(ref _lastPingMsBits))
                    : null;
            }
        }
        public uint LastPingServerTick => unchecked((uint)Interlocked.Read(ref _lastPingTick));
        public string ServerEndpoint => _serverEp.ToString();
        private static readonly byte[] PongMagic = { (byte)'P', (byte)'O', (byte)'N', (byte)'G' };
        private readonly ConcurrentQueue<ServerEntityPacket> _receivedQueue = new();
        public ulong EntityId { get => _entityId; set => _entityId = value; }
        public bool IsServerConnected => _connected;
        public uint LastServerTick { get; private set; }
        private static double ElapsedSeconds(long start, long end)
            => (end - start) / (double)Stopwatch.Frequency;
        private static double ElapsedMilliseconds(long start, long end)
            => (end - start) * 1000d / Stopwatch.Frequency;
        

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
            Interlocked.Exchange(ref _lastPingReceivedAt, 0);
            Interlocked.Exchange(ref _lastPingRequestAt, 0);
            Interlocked.Exchange(ref _lastServerPacketAt, 0);
            Interlocked.Exchange(ref _lastPingSentAt, 0);
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
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkClient] Send failed: {ex.Message}");
                _udp?.Close();
                _udp = null;
                _connected = false;
                Interlocked.Exchange(ref _lastPingReceivedAt, 0);
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
                    if (buf.Length == 16 && ep.Equals(_serverEp) &&
                        buf.AsSpan(0, 4).SequenceEqual(PongMagic))
                    {
                        long now = Stopwatch.GetTimestamp();
                        long requestAt = Interlocked.Read(ref _lastPingSentAt);
                        long nonce = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(4, 8));
                        if (nonce == Interlocked.Read(ref _nextPingNonce) && requestAt != 0)
                        {
                            double milliseconds = ElapsedMilliseconds(requestAt, now);
                            Interlocked.Exchange(ref _lastPingMsBits, BitConverter.DoubleToInt64Bits(milliseconds));
                            Interlocked.Exchange(ref _lastPingTick, BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(12, 4)));
                            Interlocked.Exchange(ref _lastPingReceivedAt, now);
                            _connected = true;
                        }
                        continue;
                    }
                    if (!ep.Equals(_serverEp)) continue;
                    
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

                    // State envelopes are a strict protocol cutover. Ignore malformed,
                    // truncated, or unsupported-version datagrams without killing receive.
                    if (buf.Length != ServerEntityPacket.NoInputSize &&
                        buf.Length != ServerEntityPacket.MaxSize)
                        continue;
                    try
                    {
                        _receivedQueue.Enqueue(ServerEntityPacket.Deserialize(buf));
                        Interlocked.Exchange(ref _lastServerPacketAt, Stopwatch.GetTimestamp());
                        _connected = true;
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    catch (InvalidDataException)
                    {
                        continue;
                    }
                }
                catch
                {
                    if (_running) break;
                }
            }
        }

        // ── Socket retry ──
        private void SendPingRequest()
        {
            if (!_connected || _udp == null) return;
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastPingRequestAt);
            if (ElapsedSeconds(previous, now) < 1.0) return;
            if (Interlocked.CompareExchange(ref _lastPingRequestAt, now, previous) != previous) return;
            long nonce = Interlocked.Increment(ref _nextPingNonce);
            _pingRequest[0] = (byte)'P'; _pingRequest[1] = (byte)'I';
            _pingRequest[2] = (byte)'N'; _pingRequest[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(_pingRequest.AsSpan(4), nonce);
            Interlocked.Exchange(ref _lastPingSentAt, now);
            try { _udp.Send(_pingRequest, _pingRequest.Length, _serverEp); }
            catch { Interlocked.Exchange(ref _lastPingReceivedAt, 0); _connected = false; }
        }

        private void Update()
        {
            if (_udp == null && !_running)
            {
                Debug.Log("[NetworkClient] Recreating socket...");
                CreateSocket();
                StartReceiveThread();
            }
            long lastPingAt = Interlocked.Read(ref _lastPingReceivedAt);
            if (lastPingAt != 0 && ElapsedSeconds(lastPingAt, Stopwatch.GetTimestamp()) > 3)
            {
                Interlocked.Exchange(ref _lastPingReceivedAt, 0);
                _connected = false;
            }
            if (_lastServerPacketAt != 0 && ElapsedSeconds(_lastServerPacketAt, Stopwatch.GetTimestamp()) > 3)
                _connected = false;
            SendPingRequest();
        }

    }
}
