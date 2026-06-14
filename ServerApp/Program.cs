using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SlopArena.Shared;

namespace ServerApp;

class Program
{
    static void Main(string[] args)
    {
        int port = 9876;
        var udp = new UdpClient(port);
        Console.WriteLine($"[Server] Listening on UDP port {port}");

        var arena = ArenaRegistry.Get("split");
        var sim = new ServerSimulation(arena);

        // Determine character class from command-line arg (default: Manki)
        var playerClass = CharacterClass.Manki;
        if (args.Length > 0 && Enum.TryParse<CharacterClass>(args[0], true, out var parsed))
            playerClass = parsed;
        var charDef = CharacterRegistry.Get(playerClass);
        Console.WriteLine($"[Server] Character class: {playerClass}");

        // Load baked skeleton data
        BakedAnimationData? bakedData = null;
        if (!string.IsNullOrEmpty(charDef.BakedDataPath))
        {
            try
            {
                string sysPath = charDef.BakedDataPath.Replace("res://", "");
                var binData = System.IO.File.ReadAllBytes(sysPath);
                bakedData = BakedAnimationData.LoadFromBin(binData);
                Console.WriteLine($"[Server] Loaded baked data: {sysPath} ({binData.Length} bytes, {bakedData.Animations.Length} anims)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Failed to load baked data: {ex.Message}");
                Console.WriteLine($"[Server] Will use fallback capsules");
            }
        }

        sim.RegisterEntity(1, charDef, new CharacterState
        {
            PX = arena.SpawnPoints[5].X,
            PY = arena.SpawnPoints[5].Y + 5f,
            PZ = arena.SpawnPoints[5].Z,
            FacingYaw = arena.SpawnPoints[5].Yaw,
            JumpsLeft = charDef.Movement.MaxJumps,
            AirDodgesLeft = 1,
            Cooldown0 = charDef.LMB.CooldownTicks,
            Cooldown1 = charDef.RMB.CooldownTicks,
            Cooldown2 = charDef.Q.CooldownTicks,
            Cooldown3 = charDef.E.CooldownTicks,
            Cooldown4 = charDef.R.CooldownTicks,
            Cooldown5 = charDef.F.CooldownTicks,
        }, bakedData);

        var clients = new Dictionary<ulong, IPEndPoint>();
        var inputBuffer = new Dictionary<ulong, (uint tick, InputState input)>();
        var tickInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        var nextTick = DateTime.UtcNow + tickInterval;

        _ = Task.Run(() => ReceiveLoop(udp, inputBuffer, clients));
        Console.WriteLine("[Server] Running 60Hz tick loop...");

        while (true)
        {
            var now = DateTime.UtcNow;
            if (now >= nextTick)
            {
                nextTick += tickInterval;
                // Collect per-client tick numbers BEFORE clearing
                var clientTicks = inputBuffer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.tick);

                sim.Tick(inputBuffer.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.input));
                inputBuffer.Clear();

                var states = sim.GetAllStates();
                foreach (var kvp in clients)
                {
                    ulong entityId = kvp.Key;
                    var ep = kvp.Value;
                    uint clientTick = clientTicks.TryGetValue(entityId, out var t) ? t : 0u;

                    foreach (var other in states)
                    {
                        var packet = CharacterStatePacket.FromState(other.Value, clientTick);
                        byte[] buf = new byte[8 + 4 + CharacterStatePacket.Size];
                        BitConverter.TryWriteBytes(buf.AsSpan(0, 8), other.Key);
                        BitConverter.TryWriteBytes(buf.AsSpan(8, 4), clientTick);
                        packet.Serialize(buf.AsSpan(12));
                        udp.Send(buf, buf.Length, ep);
                    }
                }
            }
            else Thread.Sleep(1);
        }
    }

    static void ReceiveLoop(UdpClient udp, Dictionary<ulong, (uint tick, InputState input)> inputBuffer, Dictionary<ulong, IPEndPoint> clients)
    {
        while (true)
        {
            IPEndPoint? remote = null;
            byte[] data;
            try { data = udp.Receive(ref remote); }
            catch { break; }
            if (remote == null || data.Length < 8 + 4 + InputState.Size) continue;

            ulong entityId = BitConverter.ToUInt64(data, 0);
            uint tick = BitConverter.ToUInt32(data, 8);
            if (!clients.ContainsKey(entityId))
            {
                clients[entityId] = remote;
                Console.WriteLine($"[Server] Client {entityId} from {remote}");
            }
            inputBuffer[entityId] = (tick, InputState.Deserialize(data.AsSpan(12)));
        }
    }
}
