using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// One match instance — 2 players, 60Hz UDP simulation on a dedicated port.
    /// Runs on its own thread.
    /// </summary>
    public class MatchInstance
    {
        private readonly int _port;
        private readonly string _matchId;
        private readonly string _arenaName;

        private UdpClient? _udpServer;
        private IPEndPoint? _player1EndPoint;
        private IPEndPoint? _player2EndPoint;
        private bool _running = true;

        // Per-player state
        private CharacterState _p1State;
        private CharacterState _p2State;
        private CharacterDefinition _p1Def;
        private CharacterDefinition _p2Def;
        private ArenaDefinition _arena;
        private uint _serverTick;

        // Input buffering (per player, keyed by tick)
        private readonly List<ClientInputPacket> _p1Queue = new();
        private readonly List<ClientInputPacket> _p2Queue = new();

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
            _p1Def = CharacterRegistry.Get(CharacterClass.Manki);
            _p2Def = CharacterRegistry.Get(CharacterClass.Manki);

            InitializePlayerState(ref _p1State, _p1Def, 0);
            InitializePlayerState(ref _p2State, _p2Def, 1);

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

        private static void InitializePlayerState(ref CharacterState state, CharacterDefinition def, int spawnIndex)
        {
            var arena = ArenaRegistry.Get("pit");
            var spawn = arena.SpawnPoints.Length > spawnIndex
                ? arena.SpawnPoints[spawnIndex]
                : new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };

            state = new CharacterState
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

            if (p1Input.HasValue)
            {
                _serverTick = Math.Max(_serverTick, p1Input.Value.TickNumber);
                var input = PacketToInputState(p1Input.Value);
                Simulation.SimulateTick(ref _p1State, _p1Def, input, _arena);
            }

            if (p2Input.HasValue)
            {
                _serverTick = Math.Max(_serverTick, p2Input.Value.TickNumber);
                var input = PacketToInputState(p2Input.Value);
                Simulation.SimulateTick(ref _p2State, _p2Def, input, _arena);
            }

            // Send state to both players
            if (p1Input.HasValue || p2Input.HasValue)
                SendState();
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

        private void SendState()
        {
            if (_udpServer == null) return;

            // Player 1 state packet
            var p1Packet = StateToPacket(_p1State);
            Span<byte> p1Buffer = stackalloc byte[CharacterStatePacket.Size];
            p1Packet.Serialize(p1Buffer);

            // Player 2 state packet
            var p2Packet = StateToPacket(_p2State);
            Span<byte> p2Buffer = stackalloc byte[CharacterStatePacket.Size];
            p2Packet.Serialize(p2Buffer);

            // Send P1 state to P1, P2 state to P2
            // TODO: In full implementation, send both states to both players (for hit detection prediction)
            try
            {
                if (_player1EndPoint != null)
                    _udpServer.Send(p1Buffer.ToArray(), CharacterStatePacket.Size, _player1EndPoint);
                if (_player2EndPoint != null)
                    _udpServer.Send(p2Buffer.ToArray(), CharacterStatePacket.Size, _player2EndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Match:{_matchId}] Send error: {ex.Message}");
            }
        }

        private static CharacterStatePacket StateToPacket(CharacterState state)
        {
            return new CharacterStatePacket
            {
                TickNumber = 0, // Will be set by caller if needed
                PositionX = state.PX,
                PositionY = state.PZ, // PZ → world Y (up)
                PositionZ = state.PY, // PY → world Z (forward)
                VelocityX = state.VX,
                VelocityY = state.VZ,
                VelocityZ = state.VY,
                CurrentActionState = (byte)state.State,
                StateDurationFrames = state.StateTicks
            };
        }
    }
}
