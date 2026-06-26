using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Shared;
using SlopArena.Server;

class Program
{
    static void Main(string[] args)
    {
        // Parse CLI args: --arena <name> --port <number>
        string arenaName = "split";
        int port = 9876;

        // Log received args
        Console.Error.WriteLine($"[Server] Args: [{string.Join(", ", args)}]");
        Console.Error.WriteLine($"[Server] CWD: {Environment.CurrentDirectory}");
        Console.Error.WriteLine($"[Server] BaseDir: {AppContext.BaseDirectory}");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--arena" && i + 1 < args.Length)
                arenaName = args[++i];
            else if (args[i] == "--port" && i + 1 < args.Length)
                port = int.Parse(args[++i]);
        }

        Console.Error.WriteLine($"[Server] Parsed arenaName='{arenaName}' port={port}");

        // Load arena data
        string arenaDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "arenas"));
        Console.Error.WriteLine($"[Server] Loading arenas from: {arenaDir}");
        ArenaRegistry.LoadFromDirectory(arenaDir);
        var loaded = ArenaRegistry.All;
        Console.Error.WriteLine($"[Server] Available arenas: {string.Join(", ", loaded.Select(a => a.Name))}");
        var arena = ArenaRegistry.Get(arenaName);
        Console.Error.WriteLine($"[Server] Got arena: {arena.Name} ({arena.CollisionTriangles?.Length ?? 0} tris)");
        JitterCollisionWorld jitter = new JitterCollisionWorld(arena);
        var sim = new ServerSimulation(arena);

        // Register two players
        var charDef = CharacterRegistry.Get(CharacterClass.Manki);
        var p1Spawn = arena.SpawnPoints.Length > 0 ? arena.SpawnPoints[0] : new SpawnPoint();
        var p2Spawn = arena.SpawnPoints.Length > 1 ? arena.SpawnPoints[1] : new SpawnPoint();

        sim.RegisterEntity(1, charDef, new CharacterState
        {
            PX = p1Spawn.X, PY = p1Spawn.Y + 5f, PZ = p1Spawn.Z,
            FacingYaw = p1Spawn.Yaw, JumpsLeft = charDef.Movement.MaxJumps,
        });
        sim.RegisterEntity(2, charDef, new CharacterState
        {
            PX = p2Spawn.X, PY = p2Spawn.Y + 1f, PZ = p2Spawn.Z,
            FacingYaw = p2Spawn.Yaw, JumpsLeft = charDef.Movement.MaxJumps,
        });
        var udp = new UdpClient();
        udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        Console.WriteLine($"[Server] Listening on UDP port {port}");
        var clients = new Dictionary<ulong, IPEndPoint>();
        var inputBuffer = new Dictionary<ulong, (uint tick, InputState input)>();
        var tickInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var nextTick = DateTime.UtcNow + tickInterval;

        _ = Task.Run(() => ReceiveLoop(udp, inputBuffer, clients));
        Console.WriteLine("[Server] Running 60Hz tick loop...");

        uint tick = 0;
        while (true)
        {
            var now = DateTime.UtcNow;
            if (now < nextTick)
            {
                Thread.Sleep(1);
                continue;
            }
            nextTick += tickInterval;
            tick++;

            // Periodic status every 60 ticks (1 second)
            if (tick % 60 == 0)
                Console.Error.WriteLine($"[Server] Alive tick={tick} clients={clients.Count} inputBuf={inputBuffer.Count}");

            var inputs = new Dictionary<ulong, InputState>();
            foreach (var kvp in clients)
            {
                var eid = kvp.Key;
                if (inputBuffer.TryGetValue(eid, out var entry))
                {
                    inputs[eid] = entry.input;
                }
            }
            sim.Tick(inputs);

            // Post-sim Jitter2 collision for each entity
            foreach (var kvp in sim.GetAllStates())
            {
                var state = kvp.Value;
                int maxCand = Math.Min(arena.CollisionTriangles?.Length ?? 0, 4096);
                var candidates = new int[maxCand];
                int candCount = Simulation.GetCandidateTriangles(
                    state.PX, state.PY, state.PZ, 0.3f, arena, candidates);

                if (candCount > 0)
                {
                    jitter.CollideCharacter(
                        ref state.PX, ref state.PY, ref state.PZ,
                        ref state.VX, ref state.VY, ref state.VZ,
                        candidates, candCount);
                }
            }

            // Send state to each client
            foreach (var kvp in clients)
            {
                ulong eid = kvp.Key;
                var ep = kvp.Value;
                var s = sim.GetState(eid);
                byte[] buf = new byte[8 + 4 + CharacterStatePacket.Size];
                WriteUInt64LE(buf, 0, eid);
                WriteUInt32LE(buf, 8, tick);
                CharacterStatePacket.FromState(s).Serialize(buf.AsSpan(12));
                udp.Send(buf, buf.Length, ep);
                Console.Error.WriteLine($"[Server] Tick {tick}: sent entity {eid} pos=({s.PX:F1},{s.PY:F1},{s.PZ:F1}) grounded={s.IsGrounded}");

            }
        }
    }

    static void ReceiveLoop(UdpClient udp,
        Dictionary<ulong, (uint tick, InputState input)> inputBuffer,
        Dictionary<ulong, IPEndPoint> clients)
    {
        while (true)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buf = udp.Receive(ref ep);
                if (buf.Length < 8 + 4 + InputState.Size) continue;

                ulong eid = ReadUInt64LE(buf, 0);
                uint tick = ReadUInt32LE(buf, 8);
                var input = InputState.Deserialize(buf.AsSpan(12));

                lock (inputBuffer)
                {
                    inputBuffer[eid] = (tick, input);
                }

                // Register client endpoint if first time
                if (!clients.ContainsKey(eid))
                {
                    clients[eid] = ep;
                    Console.WriteLine($"[Server] Client {eid} connected from {ep}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Server] Receive error: {ex.Message}");
            }
        }
    }

    static void WriteUInt64LE(byte[] buf, int off, ulong val)
    {
        buf[off] = (byte)val;
        buf[off + 1] = (byte)(val >> 8);
        buf[off + 2] = (byte)(val >> 16);
        buf[off + 3] = (byte)(val >> 24);
        buf[off + 4] = (byte)(val >> 32);
        buf[off + 5] = (byte)(val >> 40);
        buf[off + 6] = (byte)(val >> 48);
        buf[off + 7] = (byte)(val >> 56);
    }

    static void WriteUInt32LE(byte[] buf, int off, uint val)
    {
        buf[off] = (byte)val;
        buf[off + 1] = (byte)(val >> 8);
        buf[off + 2] = (byte)(val >> 16);
        buf[off + 3] = (byte)(val >> 24);
    }

    static ulong ReadUInt64LE(byte[] buf, int off)
    {
        return buf[off]
             | ((ulong)buf[off + 1] << 8)
             | ((ulong)buf[off + 2] << 16)
             | ((ulong)buf[off + 3] << 24)
             | ((ulong)buf[off + 4] << 32)
             | ((ulong)buf[off + 5] << 40)
             | ((ulong)buf[off + 6] << 48)
             | ((ulong)buf[off + 7] << 56);
    }

    static uint ReadUInt32LE(byte[] buf, int off)
    {
        return buf[off]
             | ((uint)buf[off + 1] << 8)
             | ((uint)buf[off + 2] << 16)
             | ((uint)buf[off + 3] << 24);
    }
}
