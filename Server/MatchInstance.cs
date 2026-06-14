using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// One match instance — 2 players, 60Hz UDP simulation on a dedicated port.
    /// Runs on its own thread. Uses ServerSimulation for full hit detection,
    /// hurtbox tracking, and void death.
    /// </summary>
    public class MatchInstance
    {
        private const ulong P1EntityId = 1;
        private const ulong P2EntityId = 2;

        private readonly int _port;
        private readonly string _matchId;
        private readonly string _arenaName;

        private UdpClient? _udpServer;
        private IPEndPoint? _player1EndPoint;
        private IPEndPoint? _player2EndPoint;
        private bool _running = true;

        private ArenaDefinition _arena;
        private ServerSimulation _sim = null!;

        // Input buffering (per player, keyed by tick)
        private readonly List<ClientInputPacket> _p1Queue = new();
        private readonly List<ClientInputPacket> _p2Queue = new();
        private uint _serverTick;

        private Thread? _thread;
        private readonly Action<int> _onMatchEnd;

        public MatchInstance(int port, string matchId, string arenaName, Action<int> onMatchEnd)
        {
            _port = port;
            _matchId = matchId;
            _arenaName = arenaName;
            _onMatchEnd = onMatchEnd;
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = $"Match-{_matchId}" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _udpServer?.Close(); } catch { }
        }

        public bool IsRunning => _running;

        private void Run()
        {
            Console.WriteLine($"[Match:{_matchId}] Starting on port {_port}");

            _arena = ArenaRegistry.Get(_arenaName);
            var p1Def = CharacterRegistry.Get(CharacterClass.Manki);
            var p2Def = CharacterRegistry.Get(CharacterClass.Manki);

            // Load baked skeleton data
            var p1Baked = LoadBakedData(p1Def);
            var p2Baked = LoadBakedData(p2Def);

            // Create simulation and register both entities
            _sim = new ServerSimulation(_arena);
            _sim.RegisterEntity(P1EntityId, p1Def, CreateInitialState(p1Def, 0), p1Baked);
            _sim.RegisterEntity(P2EntityId, p2Def, CreateInitialState(p2Def, 1), p2Baked);

            try
            {
                _udpServer = new UdpClient(_port);
                _udpServer.Client.Blocking = false;
                Console.WriteLine($"[Match:{_matchId}] Listening on UDP {_port}, waiting for 2 players...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Match:{_matchId}] Error binding port {_port}: {ex.Message}");
                _onMatchEnd(_port);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            double nextTickTime = 0;
            const double tickDurationMs = 1000.0 / 60.0;

            while (_running)
            {
                double currentTime = stopwatch.Elapsed.TotalMilliseconds;
                if (currentTime >= nextTickTime)
                {
                    ReceiveInputs();
                    if (_player1EndPoint != null && _player2EndPoint != null)
                        Tick();
                    nextTickTime += tickDurationMs;

                    if (currentTime > nextTickTime + tickDurationMs * 10)
                        nextTickTime = currentTime;
                }
                else
                {
                    int sleepTime = (int)(nextTickTime - currentTime) - 1;
                    if (sleepTime > 0)
                        Thread.Sleep(sleepTime);
                    else
                        Thread.Yield();
                }
            }

            try { _udpServer?.Close(); } catch { }
            Console.WriteLine($"[Match:{_matchId}] Stopped.");
            _onMatchEnd(_port);
        }

        private static BakedAnimationData? LoadBakedData(CharacterDefinition def)
        {
            if (string.IsNullOrEmpty(def.BakedDataPath)) return null;

            try
            {
                string sysPath = def.BakedDataPath.Replace("res://", "");
                var binData = System.IO.File.ReadAllBytes(sysPath);
                var baked = BakedAnimationData.LoadFromBin(binData);
                Console.WriteLine($"[Server] Loaded baked data: {sysPath} ({binData.Length} bytes, {baked.Animations.Length} anims)");
                return baked;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Failed to load baked data: {ex.Message} — using fallback capsules");
                return null;
            }
        }

        private CharacterState CreateInitialState(CharacterDefinition def, int spawnIndex)
        {
            var spawn = _arena.SpawnPoints.Length > spawnIndex
                ? _arena.SpawnPoints[spawnIndex]
                : new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };

            return new CharacterState
            {
                PX = spawn.X,
                PY = spawn.Y,
                PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = def.Movement.MaxJumps,
                AirDodgesLeft = 1,
                DamagePercent = 0,
            };
        }

        private void ReceiveInputs()
        {
            if (_udpServer == null) return;

            while (true)
            {
                try
                {
                    if (_udpServer.Available == 0) break;

                    var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpServer.Receive(ref remoteEP);

                    if (data.Length >= ClientInputPacket.Size)
                    {
                        var packet = ClientInputPacket.Deserialize(data);

                        // Determine which player this is from
                        bool isP1 = _player1EndPoint != null &&
                                    _player1EndPoint.Address.Equals(remoteEP.Address) &&
                                    _player1EndPoint.Port == remoteEP.Port;

                        bool isP2 = _player2EndPoint != null &&
                                    _player2EndPoint.Address.Equals(remoteEP.Address) &&
                                    _player2EndPoint.Port == remoteEP.Port;

                        // New player connecting
                        if (!isP1 && !isP2)
                        {
                            if (_player1EndPoint == null)
                            {
                                _player1EndPoint = remoteEP;
                                Console.WriteLine($"[Match:{_matchId}] Player 1 connected: {remoteEP}");
                            }
                            else if (_player2EndPoint == null)
                            {
                                _player2EndPoint = remoteEP;
                                Console.WriteLine($"[Match:{_matchId}] Player 2 connected: {remoteEP} — match starting!");
                            }
                            continue;
                        }

                        if (packet.TickNumber <= _serverTick) continue;

                        var queue = isP1 ? _p1Queue : _p2Queue;

                        // Prevent duplicates
                        bool exists = false;
                        for (int i = 0; i < queue.Count; i++)
                        {
                            if (queue[i].TickNumber == packet.TickNumber)
                            { exists = true; break; }
                        }
                        if (!exists)
                            queue.Add(packet);
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock)
                        Console.WriteLine($"[Match:{_matchId}] Socket error: {ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Match:{_matchId}] Receive error: {ex.Message}");
                    break;
                }
            }
        }

        private void Tick()
        {
            var p1Input = FlushQueue(_p1Queue, out _);
            var p2Input = FlushQueue(_p2Queue, out _);

            var inputs = new Dictionary<ulong, InputState>();

            if (p1Input.HasValue)
            {
                _serverTick = Math.Max(_serverTick, p1Input.Value.TickNumber);
                inputs[P1EntityId] = PacketToInputState(p1Input.Value);
            }

            if (p2Input.HasValue)
            {
                _serverTick = Math.Max(_serverTick, p2Input.Value.TickNumber);
                inputs[P2EntityId] = PacketToInputState(p2Input.Value);
            }

            // Run authoritative simulation (movement + hit detection + hurtboxes + void death)
            if (inputs.Count > 0)
            {
                _sim.Tick(inputs);
                SendState();
            }
        }

        private void SendState()
        {
            if (_udpServer == null) return;

            var p1Packet = CharacterStatePacket.FromState(_sim.GetState(P1EntityId), _serverTick);
            var p2Packet = CharacterStatePacket.FromState(_sim.GetState(P2EntityId), _serverTick);

            Span<byte> p1Buffer = stackalloc byte[CharacterStatePacket.Size];
            p1Packet.Serialize(p1Buffer);

            Span<byte> p2Buffer = stackalloc byte[CharacterStatePacket.Size];
            p2Packet.Serialize(p2Buffer);

            try
            {
                // Send both states to both players (T1.6)
                if (_player1EndPoint != null)
                {
                    _udpServer.Send(p1Buffer.ToArray(), CharacterStatePacket.Size, _player1EndPoint);
                    _udpServer.Send(p2Buffer.ToArray(), CharacterStatePacket.Size, _player1EndPoint);
                }
                if (_player2EndPoint != null)
                {
                    _udpServer.Send(p1Buffer.ToArray(), CharacterStatePacket.Size, _player2EndPoint);
                    _udpServer.Send(p2Buffer.ToArray(), CharacterStatePacket.Size, _player2EndPoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Flush the input queue: take the last valid packet, discard the rest.
        /// Returns the packet to process, or null if queue was empty.
        /// </summary>
        private static ClientInputPacket? FlushQueue(List<ClientInputPacket> queue, out int count)
        {
            count = queue.Count;
            if (count == 0) return null;

            // Use the LAST packet (most recent input for this tick's batch)
            var last = queue[count - 1];
            queue.Clear();
            return last;
        }

        private static InputState PacketToInputState(ClientInputPacket packet)
        {
            return new InputState
            {
                Up = (packet.MovementFlags & 0x01) != 0,
                Left = (packet.MovementFlags & 0x02) != 0,
                Down = (packet.MovementFlags & 0x04) != 0,
                Right = (packet.MovementFlags & 0x08) != 0,
                Jump = (packet.MovementFlags & 0x10) != 0,
                Dash = (packet.MovementFlags & 0x20) != 0,
                Crouch = (packet.MovementFlags & 0x80) != 0,
                Attack = (packet.ActionFlags & 0x01) != 0,
                MoveX = ((packet.MovementFlags & 0x08) != 0 ? 1f : 0f) - ((packet.MovementFlags & 0x02) != 0 ? 1f : 0f),
                MoveY = ((packet.MovementFlags & 0x01) != 0 ? 1f : 0f) - ((packet.MovementFlags & 0x04) != 0 ? 1f : 0f),
            };
        }
    }
}
