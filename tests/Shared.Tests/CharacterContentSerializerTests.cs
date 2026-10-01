using System;
using System.IO;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;
public sealed class CharacterContentSerializerTests
{







    [Fact]
    public void LoadFile_ReportsMissingPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var ex = Assert.Throws<InvalidDataException>(() => CharacterContentSerializer.LoadFile(path));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void LoadFile_ReportsMalformedPathAndReason()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{");
            var ex = Assert.Throws<InvalidDataException>(() => CharacterContentSerializer.LoadFile(path));
            Assert.Contains(path, ex.Message);
            Assert.Contains("Invalid character content", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsMissingSchemaVersion()
        => Assert.Contains("Missing character schemaVersion", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsMissingId()
        => Assert.Contains("Missing character id", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsUnsupportedSchemaVersion()
        => Assert.Contains("Unsupported character schemaVersion 2", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":2,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{}}" )).Message);

    [Fact]
    public void Load_RejectsUnknownEnum()
        => Assert.Contains("Invalid character content", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"behavior\":\"NotABehavior\",\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsNumericUnknownEnum()
        => Assert.Contains("Invalid character content", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"behavior\":99,\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsUnknownAbilityKey()
        => Assert.Contains("unknown ability key", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"unknown\":{\"stages\":[]}}}" )).Message);

    [Fact]
    public void Load_RejectsInvalidAliasTarget()
        => Assert.Contains("invalid alias target", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"e\":{\"stages\":[]}},\"airAliases\":{\"airE\":\"missing\"}}" )).Message);

    [Fact]
    public void Load_RejectsAbilityMissingStages()
        => Assert.Contains("missing stages", Assert.Throws<InvalidDataException>(() => LoadText(
            "{\"schemaVersion\":1,\"id\":\"fightguy\",\"class\":\"FightGuy\",\"abilities\":{\"slot1\":{\"name\":\"No stages\"}}}" )).Message);


    [Fact]
    public void LegacySnapshot_PreservesCapsuleCollisionForResolvedCharacter()
    {
        var targetDef = new LegacyCharacterCatalogAdapter()
            .Snapshot(CharacterClass.Nilus).Definition;
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(targetDef);
        sim.RegisterEntity(1, targetDef, player);

        var target = TestHelpers.NpcState(0f, 6f);
        target.PY = TestHelpers.GroundPY(targetDef);
        sim.RegisterEntity(100, targetDef, target);

        TestHelpers.TickN(sim, TestHelpers.Input(activeSlot: 4), 10);

        Assert.True(sim.GetState(100).DamagePercent > 0,
            "the resolved legacy hurtbox must receive Riftwalk's arrival burst");
    }

    private static CharacterDefinition LoadText(string json) => CharacterContentSerializer.Load(json);



}
