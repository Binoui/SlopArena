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
}
