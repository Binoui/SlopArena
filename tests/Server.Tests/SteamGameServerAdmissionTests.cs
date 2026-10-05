using System.Buffers.Binary;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class SteamGameServerAdmissionTests
{
    private const string ContentHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _utcTicks = now.UtcDateTime.Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
    }

    private static MatchContentCatalog LoadCatalog()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        return catalog.Catalog!;
    }

    private static MatchInstance NewSteamMatch(string id, MatchContentCatalog catalog, TestClock clock,
        IReadOnlyList<MatchPlayer> roster, DateTimeOffset deadline,
        Action<Guid, string>? onCancel = null, Action<Guid, long>? onResult = null) =>
        new(0, id, "slop_court", roster, catalog, _ => { }, onMatchResult: onResult,
            onMatchCancelled: onCancel, admissionDeadlineUtc: deadline, contentHash: ContentHash,
            steamSend: static (_, _, _, _) => { }, clock: clock);

    private static void MarkSteamReady(MatchInstance match, long connectionId, ulong entityId)
    {
        var ready = new NetplayControlPacket(NetplayControlKind.Ready, entityId, match.ServerTick, match.StartTick);
        Assert.True(match.TryMarkSteamReady(connectionId, ready));
    }
    [Fact]
    public void SteamInputCodecRejectsEntitySpoofAndAcceptsOnlyTheBoundSlot()
    {
        var frame = new byte[1 + 8 + 4 + InputState.Size];
        frame[0] = SteamGameplayWire.Input;
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(1, 8), 41);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(9, 4), 17);
        new InputState { Down = true }.Write(frame.AsSpan(13));

        Assert.False(SteamGameplayInputCodec.TryParse(frame, 42, out _, out _));
        Assert.True(SteamGameplayInputCodec.TryParse(frame, 41, out uint tick, out var input));
        Assert.Equal(17u, tick);
        Assert.True(input.Down);
    }

    [Fact]
    public void SteamMatchAdmissionIsRosterAndMatchScopedAndBlocksDuplicateWaitingHandles()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var deadline = clock.GetUtcNow().AddSeconds(60);
        var first = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) }, deadline);
        var second = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(2001, CharacterClass.FightGuy, 1), new MatchPlayer(2002, CharacterClass.Manki, 2) }, deadline);

        Assert.False(first.TryBindSteamPlayer(9999, 11, ContentHash, out _, out _, out _));
        Assert.False(first.TryBindSteamPlayer(1001, 11, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", out _, out _, out _));
        Assert.True(first.TryBindSteamPlayer(1001, 11, ContentHash, out ulong firstEntity, out _, out _));
        Assert.Equal(1UL, firstEntity);
        Assert.True(second.TryBindSteamPlayer(2001, 22, ContentHash, out ulong secondEntity, out _, out _));
        MarkSteamReady(first, 11, firstEntity);
        MarkSteamReady(second, 22, secondEntity);
        Assert.False(first.TryQueueSteamInput(22, 1, default));
        Assert.True(second.TryQueueSteamInput(22, 1, default));

        Assert.False(first.TryBindSteamPlayer(1001, 33, ContentHash, out _, out _, out byte denial));
        Assert.Equal((byte)5, denial);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(first.TryBindSteamPlayer(1002, 44, ContentHash, out _, out _, out denial));
        Assert.Equal((byte)3, denial);
    }

    [Fact]
    public void ActiveSameAccountReconnectReplacesOldHandleAndRejectsItsInput()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var match = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60));
        match.Start();
        try
        {
            bool firstAdmitted = match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out byte denial);
            Assert.True(firstAdmitted,
                $"First admission denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            bool secondAdmitted = match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out denial);
            Assert.True(secondAdmitted,
                $"Second admission denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 11, 1);
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 22, 2);
            Assert.True(SpinWait.SpinUntil(() => match.HasStartedCountdown, 2000),
                $"Expected countdown, running={match.IsRunning}.");

            bool reconnected = match.TryBindSteamPlayer(1001, 33, ContentHash, out _, out long replaced, out denial);
            Assert.True(reconnected,
                $"Same-account reconnect denied: code={denial}, countdown={match.HasStartedCountdown}, running={match.IsRunning}.");
            Assert.Equal(11L, replaced);
            Assert.False(match.TryQueueSteamInput(11, match.ServerTick + 1, default));
            Assert.False(match.TryQueueSteamInput(33, match.ServerTick + 1, default));
            MarkSteamReady(match, 33, 1);
            Assert.True(match.TryQueueSteamInput(33, match.ServerTick + 1, default));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamCountdownBroadcast_UsesExactFramedNoInputPacket()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var matchId = Guid.NewGuid();
        var firstState = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var match = new MatchInstance(0, matchId.ToString("D"), "slop_court",
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) },
            catalog, _ => { }, admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60),
            contentHash: ContentHash,
            steamSend: (_, _, frame, _) =>
            {
                if (frame[0] == SteamGameplayWire.State)
                    firstState.TrySetResult(frame);
            }, clock: clock);
        match.Start();
        try
        {
            Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
            var frame = await firstState.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1 + ServerEntityPacket.NoInputSize, frame.Length);
            var state = ServerEntityPacket.Deserialize(frame.AsSpan(1));
            Assert.False(state.HasInput);
            Assert.Contains(state.EntityId, new[] { 1UL, 2UL });
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamOpponentReceivesWibouKunaiUntilServerRemovesThem()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var playing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawned = new TaskCompletionSource<ProjectileVisualPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var swordActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var swordRemoved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), "slop_court",
            new[] { new MatchPlayer(1001, CharacterClass.Wibou, 1),
                new MatchPlayer(1002, CharacterClass.FightGuy, 2) },
            LoadCatalog(), _ => { }, contentHash: ContentHash,
            admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60),
            steamSend: (_, recipient, frame, _) =>
            {
                if (recipient != 2) return;
                if (frame[0] == SteamGameplayWire.State
                    && ServerEntityPacket.Deserialize(frame.AsSpan(1)).State.MatchState == MatchState.Playing)
                    playing.TrySetResult(true);
                if (frame[0] == SteamGameplayWire.Projectile
                    && ProjectileVisualPacket.TryDeserialize(frame.AsSpan(1), out var packet))
                {
                    if (packet.Projectiles.Count == 3) spawned.TrySetResult(packet);
                    else if (packet.Projectiles.Count == 0 && spawned.Task.IsCompleted)
                        removed.TrySetResult(true);
                }
                if (frame[0] == SteamGameplayWire.SwordTrail
                    && SwordTrailSnapshotPacket.TryDeserialize(frame.AsSpan(1), out var sword))
                {
                    if (sword.ActiveOwnerIds.Count == 1 && sword.ActiveOwnerIds[0] == 1)
                        swordActive.TrySetResult(true);
                    else if (sword.ActiveOwnerIds.Count == 0 && swordActive.Task.IsCompleted)
                        swordRemoved.TrySetResult(true);
                }
            }, clock: clock);
        match.Start();
        try
        {
            Assert.True(match.TryBindSteamPlayer(1001, 11, ContentHash, out _, out _, out _));
            Assert.True(match.TryBindSteamPlayer(1002, 22, ContentHash, out _, out _, out _));
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 11, 1);
            Assert.False(match.HasStartedCountdown);
            MarkSteamReady(match, 22, 2);
            await playing.Task.WaitAsync(TimeSpan.FromSeconds(8));
            uint nextTick = match.ServerTick + 6;
            for (uint i = 0; i < 120; i++)
            {
                Assert.True(match.TryQueueSteamInput(11, nextTick++,
                    i == 0 ? new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true } : default));
                Thread.Sleep(16);
            }
            var packet = await spawned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(3, packet.Projectiles.Select(x => x.OperationIndex).Distinct().Count());
            Assert.All(packet.Projectiles, x => Assert.Equal(CharacterClass.Wibou, x.Character));
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(4));
            nextTick = match.ServerTick + 6;
            for (uint i = 0; i < 80; i++)
            {
                Assert.True(match.TryQueueSteamInput(11, nextTick++,
                    i == 0 ? new InputState { ActiveSlot = AbilitySlots.Slot1 } : default));
                Thread.Sleep(16);
            }
            await swordActive.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await swordRemoved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            match.Stop();
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
        }
    }

    [Fact]
    public async Task SteamWaitingAndOpponentAbsenceAbortWithoutCompetitiveResult()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        int resultReports = 0;
        var waitingReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1), new MatchPlayer(1002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60), (_, reason) => waitingReason.TrySetResult(reason),
            (_, _) => Interlocked.Increment(ref resultReports));
        waiting.Start();
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("unfilled", await waitingReason.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        var absenceReason = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = NewSteamMatch(Guid.NewGuid().ToString("D"), catalog, clock,
            new[] { new MatchPlayer(2001, CharacterClass.FightGuy, 1), new MatchPlayer(2002, CharacterClass.Manki, 2) },
            clock.GetUtcNow().AddSeconds(60), (_, reason) => absenceReason.TrySetResult(reason),
            (_, _) => Interlocked.Increment(ref resultReports));
        active.Start();
        try
        {
            Assert.True(active.TryBindSteamPlayer(2001, 55, ContentHash, out _, out _, out _));
            Assert.True(active.TryBindSteamPlayer(2002, 66, ContentHash, out _, out _, out _));
            MarkSteamReady(active, 55, 1);
            MarkSteamReady(active, 66, 2);
            Assert.True(SpinWait.SpinUntil(() => active.HasStartedCountdown, 2000));
            active.DisconnectSteamPlayer(55);
            await Task.Delay(50); // Let the simulation observe absence before advancing the injected clock.
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal("absent", await absenceReason.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            active.Stop();
            Assert.True(SpinWait.SpinUntil(() => !active.IsRunning, 2000));
        }
        Assert.True(SpinWait.SpinUntil(() => !waiting.IsRunning, 2000));
        Assert.Equal(0, Volatile.Read(ref resultReports));
    }
    [Fact]
    public async Task DevelopmentRoomWaitingDeadlineReleasesUnfilledMatchWithoutWinner()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var catalog = LoadCatalog();
        var canceled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int resultReports = 0;
        var match = new MatchInstance(0, Guid.NewGuid().ToString("D"), "slop_court",
            new[] { new MatchPlayer(1001, CharacterClass.FightGuy, 1),
                new MatchPlayer(1002, CharacterClass.Manki, 2) }, catalog, _ => { },
            onMatchResult: (_, _) => Interlocked.Increment(ref resultReports),
            onMatchCancelled: (_, reason) => canceled.TrySetResult(reason),
            admissionDeadlineUtc: clock.GetUtcNow().AddSeconds(60), clock: clock);
        match.Start();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal("unfilled", await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => !match.IsRunning, 2000));
            Assert.Equal(0, Volatile.Read(ref resultReports));
        }
        finally
        {
            match.Stop();
        }
    }

}
