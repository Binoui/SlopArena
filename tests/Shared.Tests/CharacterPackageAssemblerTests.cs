using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class CharacterPackageAssemblerTests
{
    [Fact]
    public void Assemble_IsDeterministic_AndVerifiesCanonicalTree()
    {
        var first = AssembleFixture();
        var second = AssembleFixture();
        Assert.True(first.IsValid, string.Join("\n", first.Diagnostics.Select(x => x.Message)));
        Assert.Equal(first.ManifestBytes, second.ManifestBytes);
        Assert.Equal(first.RuntimeBytes, second.RuntimeBytes);
        Assert.Equal(first.PoseBytes, second.PoseBytes);
        Assert.Equal(first.BindingBytes, second.BindingBytes);
        Assert.Equal(first.PackageHash, second.PackageHash);
        Assert.Equal(new[] { "manifest.json", "character.runtime.json", "poses.bin", "client.bindings" },
            new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath });

        var files = Files(first);
        var verification = CharacterPackageAssembler.Verify(files);
        Assert.True(verification.IsValid, string.Join("\n", verification.Diagnostics.Select(x => x.Message)));
    }

    [Fact]
    public void Verify_RejectsMissingExtraAndTamperedPayloads()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files.Remove(CharacterPackageAssembler.PosePath);
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes;
        files["unexpected"] = new byte[] { 1 };
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
        files.Remove("unexpected");
        files[CharacterPackageAssembler.BindingPath][0] ^= 1;
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void Verify_RejectsTrailingPoseBytesAndOrphanBinding()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes.Concat(new byte[] { 0 }).ToArray();
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);

        files = Files(result);
        string json = Encoding.UTF8.GetString(files[CharacterPackageAssembler.BindingPath]);
        json = json.Replace("]}", ",{\"semanticId\":\"anim.orphan\",\"poseTrackId\":\"anim.orphan\",\"clipGlobalObjectId\":\"x\",\"poseName\":\"anim.orphan\",\"frameCount\":1,\"clipLengthBits\":0,\"sampleRate\":60,\"extrapolation\":0}]}", StringComparison.Ordinal);
        files[CharacterPackageAssembler.BindingPath] = Encoding.UTF8.GetBytes(json);
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void Verify_RejectsTruncatedPayloadsAndUnknownManifestPath()
    {
        var result = AssembleFixture();
        var files = Files(result);
        files[CharacterPackageAssembler.PosePath] = result.PoseBytes.Take(7).ToArray();
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);

        files = Files(result);
        files[CharacterPackageAssembler.ManifestPath] = Encoding.UTF8.GetBytes("{\"manifestSchemaVersion\":1}");
        Assert.False(CharacterPackageAssembler.Verify(files).IsValid);
    }

    [Fact]
    public void WarningsAndSortedMetadataSurviveAssembly()
    {
        var package = Compile();
        var input = BuildInput(package,
            new[] { new PackageDependencySource("zeta", "1.0.0", Hash("z")), new PackageDependencySource("alpha", "1.0.0", Hash("a")) },
            new[] { new CookedCapabilityRequirement("zeta.cap", "1"), new CookedCapabilityRequirement("alpha.cap", "1") },
            new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, "warning.one", "test", "kept") });
        var result = CharacterPackageAssembler.Assemble(input);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(x => x.Message)));
        string manifest = Encoding.UTF8.GetString(result.ManifestBytes);
        Assert.True(manifest.IndexOf("\"packageId\":\"alpha\"", StringComparison.Ordinal) < manifest.IndexOf("\"packageId\":\"zeta\"", StringComparison.Ordinal));
        Assert.Contains("\"severity\":\"warning\",\"code\":\"warning.one\"", manifest, StringComparison.Ordinal);
    }

    private static CharacterPackageAssemblyResult AssembleFixture()
    {
        var package = Compile();
        return CharacterPackageAssembler.Assemble(BuildInput(package, Array.Empty<PackageDependencySource>(), Array.Empty<CookedCapabilityRequirement>(), Array.Empty<CharacterDiagnostic>()));
    }

    private static CharacterPackageAssemblyInput BuildInput(CookedCharacterPackage package, IReadOnlyList<PackageDependencySource> dependencies, IReadOnlyList<CookedCapabilityRequirement> capabilities, IReadOnlyList<CharacterDiagnostic> warnings)
    {
        string sourceHash = new string('a', 64);
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            package.Definition.Presentation.Idle, package.Definition.Presentation.Run, package.Definition.Presentation.Dash,
            package.Definition.Presentation.Jump, package.Definition.Presentation.Fall, package.Definition.Presentation.HitSmall,
            package.Definition.Presentation.HitMedium, package.Definition.Presentation.HitHard,
        };
        foreach (var slot in package.Definition.Slots)
            foreach (var stage in slot.Timeline.Stages)
                foreach (string id in stage.AnimationIds) names.Add(id);

        var binding = new StringBuilder("{\"packageId\":\"fightguy\",\"catalogSchemaVersion\":1,\"bindingSchemaVersion\":1,\"poseFormat\":\"SKEL\",\"poseVersion\":1,\"sampleRate\":60,\"sourceHash\":\"");
        binding.Append(sourceHash).Append("\",\"rigGlobalObjectId\":\"rig\",\"animations\":[");
        bool first = true;
        foreach (string name in names.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!first) binding.Append(',');
            first = false;
            binding.Append("{\"semanticId\":").Append(JsonSerializer.Serialize(name)).Append(",\"poseTrackId\":").Append(JsonSerializer.Serialize(name)).Append(",\"clipGlobalObjectId\":\"clip\",\"poseName\":").Append(JsonSerializer.Serialize(name)).Append(",\"frameCount\":1,\"clipLengthBits\":0,\"sampleRate\":60,\"extrapolation\":0}");
        }
        binding.Append("]}");

        using var poses = new MemoryStream();
        WriteUInt32(poses, 0x4C454B53); WriteUInt32(poses, 1); WriteUInt32(poses, 1); WriteUInt32(poses, (uint)names.Count);
        WriteString(poses, "root");
        foreach (string name in names.OrderBy(x => x, StringComparer.Ordinal))
        {
            WriteString(poses, name); WriteUInt32(poses, 1); WriteUInt32(poses, 0); WriteUInt32(poses, 0); WriteUInt32(poses, 0);
        }
        return new CharacterPackageAssemblyInput(
            package.Metadata.PackageId, package.Metadata.Version, "Binoui", "MIT", "SlopArena", 1,
            package.Metadata.CookedSchemaVersion, package.Metadata.RuntimeApiMin, package.Metadata.RuntimeApiMax, sourceHash,
            dependencies, capabilities, "test-cooker", "test-unity", 1, "SKEL", 1, 60, warnings,
            package.CanonicalBytes, poses.ToArray(), Encoding.UTF8.GetBytes(binding.ToString()), package);
    }

    private static CookedCharacterPackage Compile()
    {
        string root = FindRepoFile("client/Unity/Assets/CharacterPackages/fightguy");
        var result = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "package.json")),
            File.ReadAllText(Path.Combine(root, "character.json")), CharacterCookProfile.TrustedBuiltIn);
        Assert.NotNull(result.CookedPackage);
        return result.CookedPackage!;
    }

    private static Dictionary<string, byte[]> Files(CharacterPackageAssemblyResult result) => new(StringComparer.Ordinal)
    {
        [CharacterPackageAssembler.ManifestPath] = result.ManifestBytes,
        [CharacterPackageAssembler.RuntimePath] = result.RuntimeBytes,
        [CharacterPackageAssembler.PosePath] = result.PoseBytes,
        [CharacterPackageAssembler.BindingPath] = result.BindingBytes,
    };

    private static string FindRepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(relative);
    }

    [Fact]
    public void CommittedFightGuyPackage_Verifies()
    {
        string directory = FindRepoFile("content-cooked/fightguy");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath })
            files[name] = File.ReadAllBytes(Path.Combine(directory, name));
        var result = CharacterPackageAssembler.Verify(files);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
    }

    private static string Hash(string value) => new string('b', 64);
    private static void WriteUInt32(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value), 0, 4);
    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }
}
