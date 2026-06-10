using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SlopArena.Shared;

namespace SlopArena.Server
{
    static class Program
    {
        private static UdpClient _udpServer;
        private static IPEndPoint _clientEndPoint;
        private static bool _running = true;

        // Server tick counter
        private static uint _serverTick = 0;

        // Player state (using CharacterState from Shared/)
        private static CharacterState _playerState;
        private static CharacterDefinition _playerDef;
        private static ArenaDefinition _arena;

        // Input buffering queue
        private static readonly System.Collections.Generic.List<ClientInputPacket> _inputQueue = new();

        static void Main(string[] args)
        {
            Console.WriteLine("=== SlopArena Authoritative Server ===");
            Console.WriteLine("Using Simulation.cs (tick-based, server-authoritative)");

            // Initialize arena and character
            _arena = ArenaRegistry.Get("pit");
            _playerDef = CharacterRegistry.Get(CharacterClass.Manki);

            // Spawn player at first spawn point
            var spawn = _arena.SpawnPoints.Length > 0 ? _arena.SpawnPoints[0] : new SpawnPoint { X = 40f, Y = 0.5f, Z = 40f, Yaw = 0f };
            _playerState = new CharacterState
            {
                PX = spawn.X,
                PY = spawn.Y,
                PZ = spawn.Z,
                FacingYaw = spawn.Yaw,
                State = ActionState.Idle,
                IsGrounded = true,
                JumpsLeft = _playerDef.Movement.MaxJumps,
                AirDodgesLeft = 1,
                DamagePercent = 0,
            };

            int port = 7777;
            try
            {
                _udpServer = new UdpClient(port);
                _udpServer.Client.Blocking = false;
                Console.WriteLine($"Server listening on port {port} (UDP)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting server: {ex.Message}");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            double nextTickTime = 0;
            const double tickDurationMs = 1000.0 / 60.0; // 60Hz

            Console.WriteLine("Simulation running at 60Hz. Press Ctrl+C to stop.");

            while (_running)
            {
                double currentTime = stopwatch.Elapsed.TotalMilliseconds;
                if (currentTime >= nextTickTime)
                {
                    ReceiveInputs();
                    Tick();
                    nextTickTime += tickDurationMs;

                    // Catch-up limit: if we're >10 ticks behind, skip forward
                    if (currentTime > nextTickTime + tickDurationMs * 10)
                    {
                        nextTickTime = currentTime;
                    }
                }
                else
                {
                    int sleepTime = (int)(nextTickTime - currentTime) - 1;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep(sleepTime);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }

            _udpServer.Close();
        }

        private static void ReceiveInputs()
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
                        if (packet.TickNumber > _serverTick)
                        {
                            _clientEndPoint = remoteEP;

                            // Prevent duplicates in the queue
                            bool exists = false;
                            for (int i = 0; i < _inputQueue.Count; i++)
                            {
                                if (_inputQueue[i].TickNumber == packet.TickNumber)
                                {
                                    exists = true;
                                    break;
                                }
                            }
                            if (!exists)
                            {
                                _inputQueue.Add(packet);
                            }
                        }
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock)
                    {
                        Console.WriteLine($"Socket exception: {ex.Message}");
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving input: {ex.Message}");
                    break;
                }
            }
        }

        private static void Tick()
        {
            if (_inputQueue.Count > 0)
            {
                // Sort inputs chronologically
                _inputQueue.Sort((a, b) => a.TickNumber.CompareTo(b.TickNumber));

                int simulatedCount = 0;
                foreach (var inputPacket in _inputQueue)
                {
                    if (inputPacket.TickNumber > _serverTick)
                    {
                        _serverTick = inputPacket.TickNumber;

                        // Convert ClientInputPacket to InputState
                        var input = new InputState
                        {
                            Up = (inputPacket.MovementFlags & 0x01) != 0,
                            Left = (inputPacket.MovementFlags & 0x02) != 0,
                            Down = (inputPacket.MovementFlags & 0x04) != 0,
                            Right = (inputPacket.MovementFlags & 0x08) != 0,
                            Jump = (inputPacket.MovementFlags & 0x10) != 0,
                            Dash = (inputPacket.MovementFlags & 0x20) != 0,
                            Crouch = (inputPacket.MovementFlags & 0x80) != 0,
                            Attack = (inputPacket.ActionFlags & 0x01) != 0,
                            // Compute normalized movement direction from flags
                            MoveX = ((inputPacket.MovementFlags & 0x08) != 0 ? 1f : 0f) - ((inputPacket.MovementFlags & 0x02) != 0 ? 1f : 0f),
                            MoveY = ((inputPacket.MovementFlags & 0x01) != 0 ? 1f : 0f) - ((inputPacket.MovementFlags & 0x04) != 0 ? 1f : 0f),
                        };

                        // Simulate one tick using Simulation.SimulateTick
                        Simulation.SimulateTick(ref _playerState, _playerDef, input, _arena, onHit: null);

                        simulatedCount++;
                        if (simulatedCount >= 120) // Cap to prevent packet-burst exploit
                        {
                            break;
                        }
                    }
                }

                _inputQueue.Clear();
                SendState();
            }
        }

        private static void SendState()
        {
            if (_udpServer == null || _clientEndPoint == null) return;

            // Convert CharacterState to CharacterStatePacket
            var packet = new CharacterStatePacket
            {
                TickNumber = _serverTick,
                PositionX = _playerState.PX,
                PositionY = _playerState.PZ, // note: PZ in CharacterState = Y in packet (world up)
                PositionZ = _playerState.PY, // note: PY in CharacterState = Z in packet (forward)
                VelocityX = _playerState.VX,
                VelocityY = _playerState.VZ,
                VelocityZ = _playerState.VY,
                CurrentActionState = (byte)_playerState.State,
                StateDurationFrames = _playerState.StateTicks
            };

            Span<byte> buffer = stackalloc byte[CharacterStatePacket.Size];
            packet.Serialize(buffer);

            try
            {
                _udpServer.Send(buffer.ToArray(), buffer.Length, _clientEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending state snapshot: {ex.Message}");
            }
        }
    }
}
