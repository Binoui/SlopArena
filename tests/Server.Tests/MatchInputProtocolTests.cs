using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchInputProtocolTests
{
    [Fact]
    public void InvalidUplinksCannotClaimEndpointOrStartCountdown()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages, new LegacyCharacterCatalogAdapter());
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        Assert.NotEmpty(ArenaRegistry.All);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var sentinel = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var rejected = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var accepted = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), ArenaRegistry.All[0].Name,
            new[] { new MatchPlayer(101, CharacterClass.FightGuy, 1), new MatchPlayer(102, CharacterClass.Manki, 2) },
            catalog.Catalog!, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        byte[] Input(ulong id)
        {
            var bytes = new byte[12 + InputState.Size];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, id);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
            new InputState { Down = true, DownPressed = true }.Write(bytes.AsSpan(12));
            return bytes;
        }
        ServerEntityPacket AwaitState(UdpClient client, byte[]? repeat = null)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                if (repeat != null) client.Send(repeat, repeat.Length, endpoint);
                if (client.Client.Poll(30_000, SelectMode.SelectRead))
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = client.Receive(ref remote);
                    if (bytes.Length == ServerEntityPacket.NoInputSize || bytes.Length == ServerEntityPacket.MaxSize)
                        return ServerEntityPacket.Deserialize(bytes);
                }
            }
            throw new TimeoutException("No authoritative state received from local match.");
        }
        try
        {
            match.Start();
            var socketField = typeof(MatchInstance).GetField("_udpServer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => socketField.GetValue(match) != null, 3000), "Local UDP listener did not bind.");
            var sentinelInput = Input(2);
            sentinel.Send(sentinelInput, sentinelInput.Length, endpoint);
            var invalid = Input(1);
            rejected.Send(invalid, invalid.Length - 1, endpoint); // old 20-byte input
            invalid[^1] = 99;
            rejected.Send(invalid, invalid.Length, endpoint); // unsupported version
            var oversized = new byte[invalid.Length + 1];
            Input(1).CopyTo(oversized, 0);
            rejected.Send(oversized, oversized.Length, endpoint);
            var state = AwaitState(accepted, Input(1));
            var ping = new byte[12];
            ping[0] = (byte)'P'; ping[1] = (byte)'I'; ping[2] = (byte)'N'; ping[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(4), 0x102030405060708);
            accepted.Send(ping, ping.Length, endpoint);
            var pingTimer = Stopwatch.StartNew();
            byte[]? pong = null;
            while (pingTimer.ElapsedMilliseconds < 1500)
            {
                if (!accepted.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var received = accepted.Receive(ref remote);
                if (received.Length == 16 && received[0] == (byte)'P' && received[1] == (byte)'O' &&
                    received[2] == (byte)'N' && received[3] == (byte)'G')
                {
                    pong = received;
                    break;
                }
            }
            Assert.NotNull(pong);
            Assert.Equal(0x102030405060708, BinaryPrimitives.ReadInt64LittleEndian(pong!.AsSpan(4, 8)));

            rejected.Send(ping, ping.Length, endpoint);
            Assert.False(rejected.Client.Poll(200_000, SelectMode.SelectRead), "An unadmitted peer received a ping echo.");
            Assert.Equal(MatchState.Countdown, state.State.MatchState);
            Assert.False(rejected.Client.Poll(0, SelectMode.SelectRead));
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }
    [Fact]
    public void UdpOpponentReceivesWibouKunaiAndEmptyRemovalSnapshot()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages, new LegacyCharacterCatalogAdapter());
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var wibou = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var opponent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), ArenaRegistry.All[0].Name,
            new[] { new MatchPlayer(101, CharacterClass.Wibou, 1),
                new MatchPlayer(102, CharacterClass.FightGuy, 2) },
            catalog.Catalog!, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        void Send(UdpClient client, ulong entity, uint tick, InputState input)
        {
            var frame = new byte[12 + InputState.Size];
            BinaryPrimitives.WriteUInt64LittleEndian(frame, entity);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), tick);
            input.Write(frame.AsSpan(12));
            client.Send(frame, frame.Length, endpoint);
        }
        match.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => match.IsRunning, 3000));
            var timer = Stopwatch.StartNew();
            bool playing = false;
            while (timer.Elapsed < TimeSpan.FromSeconds(8) && !playing)
            {
                Send(wibou, 1, 0, default);
                Send(opponent, 2, 0, default);
                if (!opponent.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var frame = opponent.Receive(ref remote);
                if ((frame.Length == ServerEntityPacket.NoInputSize || frame.Length == ServerEntityPacket.MaxSize)
                    && ServerEntityPacket.Deserialize(frame).State.MatchState == MatchState.Playing)
                    playing = true;
            }
            Assert.True(playing, "UDP match did not reach Playing.");
            for (uint tick = 1; tick <= 120; tick++)
                Send(wibou, 1, tick, tick == 1
                    ? new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true } : default);
            bool sawThree = false, sawEmptyAfter = false;
            timer.Restart();
            while (timer.Elapsed < TimeSpan.FromSeconds(4) && !sawEmptyAfter)
            {
                if (!opponent.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var frame = opponent.Receive(ref remote);
                if (!ProjectileVisualPacket.TryDeserialize(frame, out var snapshot)) continue;
                if (snapshot.Projectiles.Count == 3) sawThree = true;
                if (sawThree && snapshot.Projectiles.Count == 0) sawEmptyAfter = true;
            }
            Assert.True(sawThree, "Opponent never received three authoritative kunai.");
            Assert.True(sawEmptyAfter, "Opponent never received the kunai removal snapshot.");
            for (uint tick = 121; tick <= 200; tick++)
                Send(wibou, 1, tick, tick == 121 ? new InputState { ActiveSlot = 3 } : default);
            bool sawSword = false, sawSwordEmptyAfter = false;
            timer.Restart();
            while (timer.Elapsed < TimeSpan.FromSeconds(4) && !sawSwordEmptyAfter)
            {
                if (!opponent.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var frame = opponent.Receive(ref remote);
                if (!SwordTrailSnapshotPacket.TryDeserialize(frame, out var snapshot)) continue;
                if (snapshot.ActiveOwnerIds.Count == 1 && snapshot.ActiveOwnerIds[0] == 1) sawSword = true;
                if (sawSword && snapshot.ActiveOwnerIds.Count == 0) sawSwordEmptyAfter = true;
            }
            Assert.True(sawSword, "Opponent never received the active Wibou sword hitbox.");
            Assert.True(sawSwordEmptyAfter, "Opponent never received the sword trail removal snapshot.");
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }

}
