using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SlopArena.Shared;

public enum CharacterCookProfile : byte
{
    Workshop = 0,
    TrustedBuiltIn = 1,
}

public sealed class CharacterCompileResult
{
    public CookedCharacterPackage? CookedPackage { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == CharacterDiagnosticSeverity.Error);

    public CharacterCompileResult(CookedCharacterPackage? cookedPackage, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        CookedPackage = cookedPackage;
        Diagnostics = new System.Collections.ObjectModel.ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
    }
}

public static class CharacterPackageCompiler
{
    private const ushort SchemaVersion = 1;
    private const string RuntimeApiMin = "1.0.0";
    private const string RuntimeApiMax = "1.x";
    private static readonly string[] CanonicalSlots = CanonicalSlotProjection.All
        .Select(slot => slot.Id)
        .ToArray();
    private static readonly IReadOnlyList<string> CanonicalSlotIdsReadOnly = Array.AsReadOnly(CanonicalSlots);
    public static IReadOnlyList<string> CanonicalSlotIds => CanonicalSlotIdsReadOnly;
    private static readonly string[] TrustedCapabilities =
    {
        "slop.internal.fightguy.ki-shot.v1",
        "slop.internal.fightguy.rising-dragon.v1",
        "slop.internal.fightguy.cyclone-kick.v1",
        "slop.internal.fightguy.dragon-beam.v1",
    };

    public static CharacterCompileResult Compile(string packageManifestJson, string characterJson, CharacterCookProfile profile = CharacterCookProfile.Workshop)
    {
        var parsed = CharacterPackageSourceCodec.Load(packageManifestJson, characterJson);
        if (!parsed.IsValid)
            return new CharacterCompileResult(null, parsed.Diagnostics);
        return Compile(parsed.Source!, profile);
    }

    public static CharacterCompileResult Compile(CharacterPackageSource source, CharacterCookProfile profile = CharacterCookProfile.Workshop)
    {
        var diagnostics = new DiagnosticBag();
        if (source == null)
        {
            diagnostics.Error("schema.missing", "source", "Character package source is null.");
            return new CharacterCompileResult(null, diagnostics.ToList());
        }

        try
        {
            ValidateAndCook(source, profile, diagnostics, out var package);
            return new CharacterCompileResult(package, diagnostics.ToList());
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is FormatException || ex is OverflowException || ex is NullReferenceException || ex is ArgumentException)
        {
            diagnostics.Error("schema.invalid", "source", ex.Message);
            return new CharacterCompileResult(null, diagnostics.ToList());
        }
    }


    private static void ValidateAndCook(CharacterPackageSource source, CharacterCookProfile profile, DiagnosticBag d, out CookedCharacterPackage? package)
    {
        package = null;
        var m = source.Manifest;
        var c = source.Character;
        if (m == null || c == null) { d.Error("schema.missing", "source", "Manifest and character are required."); return; }
        if (m.ManifestSchemaVersion != SchemaVersion) d.Error("schema.unsupported", "manifest.manifestSchemaVersion", "Only manifest schema version 1 is supported.");
        if (c.AuthoringSchemaVersion != SchemaVersion) d.Error("schema.unsupported", "character.authoringSchemaVersion", "Only authoring schema version 1 is supported.");
        ValidateId(m.PackageId, "manifest.packageId", d);
        if (string.IsNullOrWhiteSpace(m.Version) || !IsSemVer(m.Version)) d.Error("value.out-of-range", "manifest.version", "Version must be SemVer 2.0 text.");
        foreach (var field in new[] { (m.Creator, "manifest.creator"), (m.License, "manifest.license"), (m.Attribution, "manifest.attribution") }) if (string.IsNullOrWhiteSpace(field.Item1)) d.Error("value.out-of-range", field.Item2, "Value must be non-empty.");
        for (var i = 0; i < (m.Dependencies?.Count ?? 0); i++)
        {
            var dependency = m.Dependencies[i];
            ValidateId(dependency.PackageId, $"manifest.dependencies[{i}].packageId", d);
            if (string.IsNullOrWhiteSpace(dependency.Version) || string.IsNullOrWhiteSpace(dependency.CookedHash)) d.Error("value.out-of-range", $"manifest.dependencies[{i}]", "Dependency version and cooked hash are required.");
        }
        var capabilityMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var capabilityCount = 0;
        foreach (var requirement in c.CapabilityRequirements ?? System.Array.Empty<CapabilityRequirementSource>())
        {
            capabilityCount++;
            ValidateId(requirement.CapabilityId, "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", d);
            if (!capabilityMap.TryAdd(requirement.CapabilityId, requirement.CapabilityVersion)) d.Error("id.duplicate", "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", "Duplicate capability requirement.");
            if (profile == CharacterCookProfile.Workshop && requirement.CapabilityId.StartsWith("slop.internal.", StringComparison.Ordinal)) d.Error("capability.untrusted", "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", "Trusted built-in capabilities are not allowed in Workshop profile.");
            if (profile == CharacterCookProfile.TrustedBuiltIn && (!TrustedCapabilities.Contains(requirement.CapabilityId) || requirement.CapabilityVersion != "1")) d.Error("capability.unknown", "character.capabilityRequirements[" + (capabilityCount - 1) + "].capabilityId", "Capability is not admitted by the trusted profile.");
        }
        if (capabilityCount > CookedBudget.MaxCapabilityRequirements) d.Error("budget.exceeded", "character.capabilityRequirements", "Capability requirement budget exceeded.");
        ValidateFinite(c, d);
        ValidateIds(c, d);
        var explicitSlots = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        for (var i = 0; i < (c.Slots?.Count ?? 0); i++)
        {
            var slot = c.Slots[i];
            if (!CanonicalSlots.Contains(slot.Id)) d.Error("id.invalid", "character.slots[" + i + "].id", "Unknown canonical slot ID.");
            else if (!explicitSlots.TryAdd(slot.Id, slot)) d.Error("id.duplicate", "character.slots[" + i + "].id", "Duplicate explicit slot.");
            ValidateSlot(slot, i, capabilityMap, c, d);
        }
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < (c.Aliases?.Count ?? 0); i++)
        {
            var alias = c.Aliases[i];
            if (!CanonicalSlots.Contains(alias.From) || !CanonicalSlots.Contains(alias.To)) d.Error("id.invalid", "character.aliases[" + i + "]", "Alias IDs must be canonical slot IDs.");
            if (explicitSlots.ContainsKey(alias.From)) d.Error("id.duplicate", "character.aliases[" + i + "].from", "Alias overwrites an explicit slot.");
            if (!aliases.TryAdd(alias.From, alias.To)) d.Error("id.duplicate", "character.aliases[" + i + "].from", "Duplicate alias.");
        }
        var resolved = new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        foreach (var id in CanonicalSlots) ResolveSlot(id, explicitSlots, aliases, resolved, new HashSet<string>(StringComparer.Ordinal), d);
        var cookedSlots = new List<CookedSlotDefinition>(CanonicalSlots.Length);
        var stageCount = 0; var operationCount = 0; var hitboxCount = 0; var projectileCount = 0; var capabilityOperationCount = 0; var maxDuration = 0; var operationOrdinal = 0;
        for (var ordinal = 0; ordinal < CanonicalSlots.Length; ordinal++)
        {
            if (!resolved.TryGetValue(CanonicalSlots[ordinal], out var slot)) continue;
            var timeline = CookTimeline(slot.Timeline, d, ref stageCount, ref operationCount, ref hitboxCount, ref projectileCount, ref capabilityOperationCount, ref maxDuration, ref operationOrdinal);
            cookedSlots.Add(new CookedSlotDefinition(ordinal, CanonicalSlots[ordinal], ordinal >= 8, slot.Name, slot.Description, slot.IconId, slot.Behavior, slot.AimMode, slot.CooldownTicks, slot.IsRecoveryMove, slot.PreserveMomentumOnStart, timeline));
        }
        if (resolved.Count != CanonicalSlots.Length) d.Error("reference.unresolved", "character.slots", "Not all canonical slots resolve.");
        if (d.HasErrors) return;
        var metadata = new CookedPackageMetadata(m.PackageId, m.Version, SchemaVersion, RuntimeApiMin, RuntimeApiMax);
        var definition = new CookedCharacterDefinition(c.DisplayName, c.Weight, CookMovement(c.Movement), CookPresentation(c.Presentation), c.CapsuleRadius, c.CapsuleHeight, c.HipHeight, c.HurtboxRadius, c.HurtboxCapsules.Select(x => new CookedHurtboxCapsule(x.StartX, x.StartY, x.StartZ, x.EndX, x.EndY, x.EndZ, x.Radius)).ToList(), c.HurtboxBoneDefs.Select(x => new CookedHurtboxBone(x.BoneId, x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius)).ToList(), c.PresentationIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), c.CapabilityRequirements.OrderBy(x => x.CapabilityId, StringComparer.Ordinal).Select(x => new CookedCapabilityRequirement(x.CapabilityId, x.CapabilityVersion)).ToList(), cookedSlots);
        var budget = new CookedBudget(cookedSlots.Count, stageCount, operationCount, hitboxCount, projectileCount, capabilityOperationCount, maxDuration);
        var bytes = WriteCanonical(metadata, definition, budget);
        package = new CookedCharacterPackage(metadata, definition, budget, d.ToList(), bytes);
    }


    private static void ValidateSlot(CharacterSlotSource slot, int index, Dictionary<string, string> capabilities, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        ValidateId(slot.IconId, $"character.slots[{index}].iconId", d);
        if (slot.Timeline == null || slot.Timeline.Stages == null) { d.Error("schema.missing", $"character.slots[{index}].timeline", "Timeline is required."); return; }
        if (slot.Timeline.Stages.Count == 0 || slot.Timeline.Stages.Count > CookedBudget.MaxStagesPerTimeline) d.Error("budget.exceeded", $"character.slots[{index}].timeline.stages", "Timeline stage budget exceeded or empty.");
        foreach (var stage in slot.Timeline.Stages)
        {
            if (stage.DurationTicks == 0 || stage.IasaTicks > stage.DurationTicks || stage.LandingLagTicks > stage.DurationTicks || stage.AutoCancelBeforeTicks > stage.DurationTicks || stage.AutoCancelAfterTicks > stage.DurationTicks) d.Error("value.out-of-range", "character.slots[" + index + "].timeline", "Stage timing is outside its duration.");
            if (stage.Operations.Count > CookedBudget.MaxOperationsPerStage) d.Error("budget.exceeded", "character.slots[" + index + "].timeline", "Stage operation budget exceeded.");
            foreach (var id in stage.AnimationIds) { ValidateId(id, "character.animationId", d); if (!IsKnownAnimation(id, c)) d.Error("reference.unresolved", "character.animationId", "Animation ID is not declared."); }
            foreach (var operation in stage.Operations)
            {
                if (operation.Tick >= stage.DurationTicks) d.Error("value.out-of-range", "character.operation.tick", "Operation tick must be within its stage.");
                if (operation is StartCapabilityOperationSource capability)
                {
                    if (!capabilities.TryGetValue(capability.CapabilityId, out var version)) d.Error("capability.unknown", "character.operation.capabilityId", "Capability is not declared.");
                    else if (version != capability.CapabilityVersion) d.Error("capability.version-mismatch", "character.operation.capabilityVersion", "Capability version does not match its declaration.");
                }
                ValidateOperation(operation, c, d);
            }
        }
    }

    private static bool IsKnownAnimation(string id, CharacterAuthoringDocument c)
        => id.StartsWith("anim.", StringComparison.Ordinal);

    private static void ValidateOperation(CharacterTimelineOperationSource operation, CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var expected = operation switch { SetVelocityOperationSource => AuthoringUnit.MetersPerSecond, SpawnHitboxOperationSource => AuthoringUnit.Meters, SpawnProjectileOperationSource => AuthoringUnit.Meters, _ => AuthoringUnit.Ticks };
        if (operation.Unit != expected) d.Error("unit.unknown", "character.operation.unit", "Unit does not match operation contract.");
        switch (operation)
        {
            case SetVelocityOperationSource velocity:
                ValidateFiniteValues(new[] { velocity.X, velocity.Y, velocity.Z }, "character.operation", d);
                break;
            case SpawnHitboxOperationSource hitbox:
                ValidateFiniteValues(new[] { hitbox.Hitbox.Radius, hitbox.Hitbox.OffsetX, hitbox.Hitbox.OffsetY, hitbox.Hitbox.OffsetZ, hitbox.Hitbox.EndOffsetX, hitbox.Hitbox.EndOffsetY, hitbox.Hitbox.EndOffsetZ, hitbox.Hitbox.Damage, hitbox.Hitbox.Angle, hitbox.Hitbox.BaseKnockback, hitbox.Hitbox.KnockbackGrowth }, "character.hitbox", d);
                ValidateNonNegative(hitbox.Hitbox.Radius, "character.hitbox.radius", d); ValidateNonNegative(hitbox.Hitbox.Damage, "character.hitbox.damage", d); ValidateAngle(hitbox.Hitbox.Angle, "character.hitbox.angle", d); ValidateNonNegative(hitbox.Hitbox.BaseKnockback, "character.hitbox.baseKnockback", d); ValidateNonNegative(hitbox.Hitbox.KnockbackGrowth, "character.hitbox.knockbackGrowth", d);
                if (hitbox.Hitbox.DurationTicks == 0) d.Error("value.out-of-range", "character.hitbox.durationTicks", "Duration must be greater than zero.");
                ValidateBoneReference(hitbox.Hitbox.StartBoneId, c, "character.hitbox.startBoneId", d); ValidateBoneReference(hitbox.Hitbox.EndBoneId, c, "character.hitbox.endBoneId", d);
                break;
            case SpawnProjectileOperationSource projectile:
                ValidateFiniteValues(new[] { projectile.Projectile.LaunchOffsetX, projectile.Projectile.LaunchOffsetY, projectile.Projectile.LaunchOffsetZ, projectile.Projectile.Speed, projectile.Projectile.Gravity, projectile.Projectile.Radius, projectile.Projectile.Damage, projectile.Projectile.Angle, projectile.Projectile.BaseKnockback, projectile.Projectile.KnockbackGrowth }, "character.projectile", d);
                ValidateNonNegative(projectile.Projectile.Speed, "character.projectile.speed", d); ValidateNonNegative(projectile.Projectile.Radius, "character.projectile.radius", d); ValidateNonNegative(projectile.Projectile.Damage, "character.projectile.damage", d); ValidateAngle(projectile.Projectile.Angle, "character.projectile.angle", d);
                break;
            case StartCapabilityOperationSource capability:
                ValidateCapabilityParams(capability.Parameters, d);
                break;
            case EmitPresentationOperationSource presentation when !c.PresentationIds.Contains(presentation.PresentationId, StringComparer.Ordinal):
                d.Error("reference.unresolved", "character.operation.presentationId", "Presentation ID is not declared.");
                break;
        }
    }

    private static void ValidateBoneReference(string? id, CharacterAuthoringDocument c, string path, DiagnosticBag d)
    {
        if (id != null && !c.HurtboxBoneDefs.Any(x => x.BoneId == id)) d.Error("reference.unresolved", path, "Bone ID is not declared.");
    }

    private static void ValidateFiniteValues(IEnumerable<float> values, string path, DiagnosticBag d)
    {
        foreach (var value in values) if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", path, "Numeric value must be finite.");
    }

    private static void ValidateCapabilityParams(TypedCapabilityParameters? parameters, DiagnosticBag d)
    {
        if (parameters == null) { d.Error("operation.parameter-missing", "character.operation.parameters", "Capability parameters are required."); return; }
        foreach (var value in CapabilityFloats(parameters)) if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", "character.operation.parameters", "Capability parameter must be finite.");
        foreach (var value in CapabilityFloats(parameters)) ValidateNonNegative(value, "character.operation.parameters", d);
        if (parameters is KiShotCapabilityParameters ki) ValidateAngle(ki.KnockbackAngle, "character.operation.parameters.knockbackAngle", d);
        if (parameters is CycloneKickCapabilityParameters cyclone) ValidateAngle(cyclone.KnockbackAngle, "character.operation.parameters.knockbackAngle", d);
        if (parameters is DragonBeamCapabilityParameters beam) ValidateAngle(beam.KnockbackAngle, "character.operation.parameters.knockbackAngle", d);
    }
    private static IEnumerable<float> CapabilityFloats(TypedCapabilityParameters p)
    {
        return p switch
        {
            KiShotCapabilityParameters x => new[] { x.LaunchOffsetY, x.ProjectileSpeed, x.Gravity, x.HitboxRadius, x.Damage, x.KnockbackBase, x.KnockbackGrowth, x.KnockbackAngle },
            RisingDragonCapabilityParameters x => new[] { x.RiseSpeed },
            CycloneKickCapabilityParameters x => new[] { x.ForwardSpeed, x.BodyRadius, x.SideRadius, x.SideOffset, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.BodyY, x.SideY },
            DragonBeamCapabilityParameters x => new[] { x.LaunchOffsetY, x.BeamRange, x.BeamRadius, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth },
            _ => System.Array.Empty<float>(),
        };
    }

    private static void ValidateIds(CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var seenBones = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < c.HurtboxBoneDefs.Count; i++) { var id = c.HurtboxBoneDefs[i].BoneId; ValidateId(id, $"character.hurtboxBoneDefs[{i}].boneId", d); if (!seenBones.Add(id)) d.Error("id.duplicate", $"character.hurtboxBoneDefs[{i}].boneId", "Duplicate bone ID."); }
        var seenPresentation = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < c.PresentationIds.Count; i++) { ValidateId(c.PresentationIds[i], $"character.presentationIds[{i}]", d); if (!seenPresentation.Add(c.PresentationIds[i])) d.Error("id.duplicate", $"character.presentationIds[{i}]", "Duplicate presentation ID."); }
        var standardAnimations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in new[] { c.Presentation.Idle, c.Presentation.Run, c.Presentation.Dash, c.Presentation.Jump, c.Presentation.Fall, c.Presentation.HitSmall, c.Presentation.HitMedium, c.Presentation.HitHard })
        {
            ValidateId(id, "character.presentation", d);
            if (!standardAnimations.Add(id)) d.Error("id.duplicate", "character.presentation", "Duplicate standard animation ID.");
        }
        foreach (var stageId in c.PresentationIds) if (!PresentationUsed(stageId, c)) d.Warning("presentation.unused-id", "character.presentationIds", "Declared presentation ID is not emitted by a timeline operation.");
    }

    private static bool PresentationUsed(string id, CharacterAuthoringDocument c)
        => c.Slots.SelectMany(s => s.Timeline.Stages).SelectMany(s => s.Operations).OfType<EmitPresentationOperationSource>().Any(x => x.PresentationId == id);

    private static void ValidateFinite(CharacterAuthoringDocument c, DiagnosticBag d)
    {
        var floats = new List<float> { c.Weight, c.CapsuleRadius, c.CapsuleHeight, c.HipHeight, c.HurtboxRadius, c.Presentation.LandStartOffsetSeconds, c.Presentation.VisualScale, c.Presentation.HurtboxBoneScale, c.Presentation.ModelYOffset, c.Presentation.ModelSoleOffset };
        floats.AddRange(c.HurtboxCapsules.SelectMany(x => new[] { x.StartX, x.StartY, x.StartZ, x.EndX, x.EndY, x.EndZ, x.Radius }));
        floats.AddRange(c.HurtboxBoneDefs.SelectMany(x => new[] { x.OffsetX, x.OffsetY, x.OffsetZ, x.Radius }));
        foreach (var value in floats) if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", "character", "Numeric value must be finite.");
    }

    private static CharacterSlotSource? ResolveSlot(string id, Dictionary<string, CharacterSlotSource> explicitSlots, Dictionary<string, string> aliases, Dictionary<string, CharacterSlotSource> resolved, HashSet<string> visiting, DiagnosticBag d)
    {
        if (resolved.TryGetValue(id, out var existing)) return existing;
        if (!visiting.Add(id)) { d.Error("alias.cycle", "character.aliases", "Alias cycle detected."); return null; }
        CharacterSlotSource? slot = null;
        if (explicitSlots.TryGetValue(id, out var explicitSlot)) slot = explicitSlot;
        else if (aliases.TryGetValue(id, out var target)) slot = ResolveSlot(target, explicitSlots, aliases, resolved, visiting, d);
        else d.Error("alias.missing-target", "character.slots." + id, "Canonical slot has no explicit definition or alias.");
        visiting.Remove(id);
        if (slot != null) resolved[id] = CloneSlot(slot);
        return slot;
    }

    private static CharacterSlotSource CloneSlot(CharacterSlotSource source)
        => source with
        {
            Timeline = new CharacterTimelineSource(
                source.Timeline.Stages.Select(stage => new CharacterStageSource(
                    stage.DurationTicks,
                    stage.IasaTicks,
                    stage.LandingLagTicks,
                    stage.AutoCancelBeforeTicks,
                    stage.AutoCancelAfterTicks,
                    stage.AnimationIds.ToList(),
                    stage.Operations.Select(CloneOperation).ToList())).ToList())
        };

    private static CharacterTimelineOperationSource CloneOperation(CharacterTimelineOperationSource op)
        => op switch { SetVelocityOperationSource x => x with { }, SpawnHitboxOperationSource x => x with { Hitbox = x.Hitbox with { } }, SpawnProjectileOperationSource x => x with { Projectile = x.Projectile with { } }, SetAimStateOperationSource x => x with { }, StartCapabilityOperationSource x => x with { Parameters = CloneParameters(x.Parameters) }, EmitPresentationOperationSource x => x with { }, CompleteTimelineOperationSource x => x with { }, _ => throw new InvalidDataException("Unknown operation.") };

    private static TypedCapabilityParameters CloneParameters(TypedCapabilityParameters p) => p switch { KiShotCapabilityParameters x => x with { }, RisingDragonCapabilityParameters x => x with { }, CycloneKickCapabilityParameters x => x with { }, DragonBeamCapabilityParameters x => x with { }, _ => throw new InvalidDataException("Unknown capability parameters.") };

    private static CookedTimeline CookTimeline(CharacterTimelineSource source, DiagnosticBag d, ref int stages, ref int operations, ref int hitboxes, ref int projectiles, ref int capabilities, ref int maxDuration, ref int operationOrdinal)
    {
        var cookedStages = new List<CookedStage>();
        var duration = 0;
        var timelineOperations = 0;
        foreach (var stage in source.Stages)
        {
            stages++; duration += stage.DurationTicks; maxDuration = Math.Max(maxDuration, duration);
            var cookedOps = new List<CookedTimelineOperation>();
            foreach (var op in stage.Operations)
            {
                operations++;
                timelineOperations++;
                var cookedOperationOrdinal = operationOrdinal++;
                switch (op)
                {
                    case SetVelocityOperationSource x: cookedOps.Add(new CookedSetVelocityOperation(x.Tick, x.Unit, x.VelocityMode, x.X, x.Y, x.Z)); break;
                    case SpawnHitboxOperationSource x: hitboxes++; cookedOps.Add(new CookedSpawnHitboxOperation(x.Tick, x.Unit, new CookedHitbox(x.Hitbox.Shape, x.Hitbox.Radius, x.Hitbox.OffsetX, x.Hitbox.OffsetY, x.Hitbox.OffsetZ, x.Hitbox.EndOffsetX, x.Hitbox.EndOffsetY, x.Hitbox.EndOffsetZ, x.Hitbox.StartBoneId, x.Hitbox.EndBoneId, x.Hitbox.Damage, x.Hitbox.Angle, x.Hitbox.BaseKnockback, x.Hitbox.KnockbackGrowth, x.Hitbox.StunTicks, x.Hitbox.DurationTicks, x.Hitbox.Interruptible, x.Hitbox.HitGroup))); break;
                    case SpawnProjectileOperationSource x: projectiles++; cookedOps.Add(new CookedSpawnProjectileOperation(x.Tick, x.Unit, new CookedProjectile(x.Projectile.LaunchOffsetX, x.Projectile.LaunchOffsetY, x.Projectile.LaunchOffsetZ, x.Projectile.Speed, x.Projectile.Gravity, x.Projectile.Radius, x.Projectile.Damage, x.Projectile.Angle, x.Projectile.BaseKnockback, x.Projectile.KnockbackGrowth, x.Projectile.StunTicks, x.Projectile.MaxFlightTicks))); break;
                    case SetAimStateOperationSource x: cookedOps.Add(new CookedSetAimStateOperation(x.Tick, x.Unit, x.AimState)); break;
                    case StartCapabilityOperationSource x: capabilities++; cookedOps.Add(new CookedStartCapabilityOperation(x.Tick, x.Unit, x.CapabilityId, x.CapabilityVersion, CookParameters(x.Parameters))); break;
                    case EmitPresentationOperationSource x: cookedOps.Add(new CookedEmitPresentationOperation(x.Tick, x.Unit, x.PresentationId, cookedOperationOrdinal)); break;
                    case CompleteTimelineOperationSource x: cookedOps.Add(new CookedCompleteTimelineOperation(x.Tick, x.Unit)); break;
                }
            }
            cookedStages.Add(new CookedStage(stage.DurationTicks, stage.IasaTicks, stage.LandingLagTicks, stage.AutoCancelBeforeTicks, stage.AutoCancelAfterTicks, stage.AnimationIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), cookedOps));
        }
        if (timelineOperations > CookedBudget.MaxOperationsPerTimeline) d.Error("budget.exceeded", "character.timeline.operations", "Timeline operation budget exceeded.");
        return new CookedTimeline(cookedStages);
    }

    private static CookedCapabilityParameters CookParameters(TypedCapabilityParameters p) => p switch { KiShotCapabilityParameters x => new CookedKiShotCapabilityParameters(x.StartupTicks, x.DurationTicks, x.LaunchOffsetY, x.ProjectileSpeed, x.Gravity, x.HitboxRadius, x.Damage, x.KnockbackBase, x.KnockbackGrowth, x.KnockbackAngle, x.StunTicks, x.MaxFlightTicks), RisingDragonCapabilityParameters x => new CookedRisingDragonCapabilityParameters(x.RiseSpeed, x.RiseTicks, x.RiseDelay), CycloneKickCapabilityParameters x => new CookedCycloneKickCapabilityParameters(x.ForwardSpeed, x.WindupTicks, x.HitboxEndTick, x.DurationTicks, x.BodyRadius, x.SideRadius, x.SideOffset, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.StunTicks, x.BodyY, x.SideY), DragonBeamCapabilityParameters x => new CookedDragonBeamCapabilityParameters(x.DurationTicks, x.FireTick, x.LaunchOffsetY, x.BeamRange, x.BeamRadius, x.Damage, x.KnockbackAngle, x.KnockbackBase, x.KnockbackGrowth, x.StunTicks, x.HitboxDurationTicks), _ => throw new InvalidDataException("Unknown capability parameters.") };

    private static CookedMovement CookMovement(CharacterMovementSource x) => new(x.RunSpeed, x.RunAccelerationA, x.RunAccelerationB, x.DashSpeed, x.AirSpeedMax, x.AirAccelStick, x.AirAccelBase, x.JumpForce, x.ShortHopForce, x.AirJumpVMultiplier, x.AirJumpHMultiplier, x.Gravity, x.AirFloatGravity, x.DashDurationTicks, x.DashCooldownTicks, x.GroundFriction, x.AirFriction, x.MaxFallSpeed, x.FastFallSpeed, x.MaxJumps, x.JumpSquatTicks, x.FloatWindowTicks, x.RushTicks);
    private static CookedPresentation CookPresentation(CharacterPresentationSource x) => new(x.Idle, x.Run, x.Dash, x.Jump, x.Fall, x.HitSmall, x.HitMedium, x.HitHard, x.LandStartOffsetSeconds, x.ModelResourcePath, x.VisualScale, x.HurtboxBoneScale, x.ModelYOffset, x.ModelSoleOffset, x.AutoModelYOffset);

    private static byte[] WriteCanonical(CookedPackageMetadata metadata, CookedCharacterDefinition definition, CookedBudget budget)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default, Indented = false }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("metadata"); writer.WriteStartObject(); writer.WriteString("packageId", metadata.PackageId); writer.WriteString("version", metadata.Version); writer.WriteNumber("cookedSchemaVersion", metadata.CookedSchemaVersion); writer.WritePropertyName("compatibility"); writer.WriteStartObject(); writer.WriteString("runtimeApiMin", metadata.RuntimeApiMin); writer.WriteString("runtimeApiMax", metadata.RuntimeApiMax); writer.WriteEndObject(); writer.WriteEndObject();
            writer.WritePropertyName("character"); writer.WriteStartObject(); writer.WriteString("displayName", definition.DisplayName); Number(writer, "weight", definition.Weight); WriteMovement(writer, definition.Movement); WritePresentation(writer, definition.Presentation); Number(writer, "capsuleRadius", definition.CapsuleRadius); Number(writer, "capsuleHeight", definition.CapsuleHeight); Number(writer, "hipHeight", definition.HipHeight); Number(writer, "hurtboxRadius", definition.HurtboxRadius);
            writer.WritePropertyName("hurtboxCapsules"); writer.WriteStartArray(); foreach (var x in definition.HurtboxCapsules) { writer.WriteStartObject(); Number(writer, "startX", x.StartX); Number(writer, "startY", x.StartY); Number(writer, "startZ", x.StartZ); Number(writer, "endX", x.EndX); Number(writer, "endY", x.EndY); Number(writer, "endZ", x.EndZ); Number(writer, "radius", x.Radius); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("hurtboxBoneDefs"); writer.WriteStartArray(); foreach (var x in definition.HurtboxBoneDefs.OrderBy(x => x.BoneId, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("boneId", x.BoneId); Number(writer, "offsetX", x.OffsetX); Number(writer, "offsetY", x.OffsetY); Number(writer, "offsetZ", x.OffsetZ); Number(writer, "radius", x.Radius); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("presentationIds"); writer.WriteStartArray(); foreach (var x in definition.PresentationIds) writer.WriteStringValue(x); writer.WriteEndArray(); writer.WritePropertyName("capabilityRequirements"); writer.WriteStartArray(); foreach (var x in definition.CapabilityRequirements) { writer.WriteStartObject(); writer.WriteString("capabilityId", x.CapabilityId); writer.WriteString("capabilityVersion", x.CapabilityVersion); writer.WriteEndObject(); } writer.WriteEndArray();
            writer.WritePropertyName("slots"); writer.WriteStartArray(); foreach (var x in definition.Slots.OrderBy(x => x.Ordinal)) WriteSlot(writer, x); writer.WriteEndArray(); writer.WriteEndObject();
            writer.WritePropertyName("budget"); writer.WriteStartObject(); writer.WriteNumber("slotCount", budget.SlotCount); writer.WriteNumber("stageCount", budget.StageCount); writer.WriteNumber("operationCount", budget.OperationCount); writer.WriteNumber("hitboxCount", budget.HitboxCount); writer.WriteNumber("projectileCount", budget.ProjectileCount); writer.WriteNumber("capabilityCount", budget.CapabilityCount); writer.WriteNumber("maxTimelineDurationTicks", budget.MaxTimelineDurationTicks); writer.WriteEndObject(); writer.WriteEndObject(); writer.Flush();
        }
        return stream.ToArray();
    }

    private static void WriteMovement(Utf8JsonWriter w, CookedMovement x) { w.WritePropertyName("movement"); w.WriteStartObject(); Number(w, "runSpeed", x.RunSpeed); Number(w, "runAccelerationA", x.RunAccelerationA); Number(w, "runAccelerationB", x.RunAccelerationB); Number(w, "dashSpeed", x.DashSpeed); Number(w, "airSpeedMax", x.AirSpeedMax); Number(w, "airAccelStick", x.AirAccelStick); Number(w, "airAccelBase", x.AirAccelBase); Number(w, "jumpForce", x.JumpForce); Number(w, "shortHopForce", x.ShortHopForce); Number(w, "airJumpVMultiplier", x.AirJumpVMultiplier); Number(w, "airJumpHMultiplier", x.AirJumpHMultiplier); Number(w, "gravity", x.Gravity); Number(w, "airFloatGravity", x.AirFloatGravity); w.WriteNumber("dashDurationTicks", x.DashDurationTicks); w.WriteNumber("dashCooldownTicks", x.DashCooldownTicks); Number(w, "groundFriction", x.GroundFriction); Number(w, "airFriction", x.AirFriction); Number(w, "maxFallSpeed", x.MaxFallSpeed); Number(w, "fastFallSpeed", x.FastFallSpeed); w.WriteNumber("maxJumps", x.MaxJumps); w.WriteNumber("jumpSquatTicks", x.JumpSquatTicks); w.WriteNumber("floatWindowTicks", x.FloatWindowTicks); w.WriteNumber("rushTicks", x.RushTicks); w.WriteEndObject(); }
    private static void WritePresentation(Utf8JsonWriter w, CookedPresentation x) { w.WritePropertyName("presentation"); w.WriteStartObject(); w.WriteString("idle", x.Idle); w.WriteString("run", x.Run); w.WriteString("dash", x.Dash); w.WriteString("jump", x.Jump); w.WriteString("fall", x.Fall); w.WriteString("hitSmall", x.HitSmall); w.WriteString("hitMedium", x.HitMedium); w.WriteString("hitHard", x.HitHard); Number(w, "landStartOffsetSeconds", x.LandStartOffsetSeconds); w.WriteString("modelResourcePath", x.ModelResourcePath); Number(w, "visualScale", x.VisualScale); Number(w, "hurtboxBoneScale", x.HurtboxBoneScale); Number(w, "modelYOffset", x.ModelYOffset); Number(w, "modelSoleOffset", x.ModelSoleOffset); w.WriteBoolean("autoModelYOffset", x.AutoModelYOffset); w.WriteEndObject(); }
    private static void WriteSlot(Utf8JsonWriter w, CookedSlotDefinition x) { w.WriteStartObject(); w.WriteNumber("ordinal", x.Ordinal); w.WriteString("id", x.Id); w.WriteBoolean("isAir", x.IsAir); w.WriteString("name", x.Name); w.WriteString("description", x.Description); w.WriteString("iconId", x.IconId); w.WriteNumber("behavior", (byte)x.Behavior); w.WriteNumber("aimMode", (byte)x.AimMode); w.WriteNumber("cooldownTicks", x.CooldownTicks); w.WriteBoolean("isRecoveryMove", x.IsRecoveryMove); w.WriteBoolean("preserveMomentumOnStart", x.PreserveMomentumOnStart); w.WritePropertyName("timeline"); w.WriteStartObject(); w.WritePropertyName("stages"); w.WriteStartArray(); foreach (var stage in x.Timeline.Stages) WriteStage(w, stage); w.WriteEndArray(); w.WriteEndObject(); w.WriteEndObject(); }
    private static void WriteStage(Utf8JsonWriter w, CookedStage x) { w.WriteStartObject(); w.WriteNumber("durationTicks", x.DurationTicks); w.WriteNumber("iasaTicks", x.IasaTicks); w.WriteNumber("landingLagTicks", x.LandingLagTicks); w.WriteNumber("autoCancelBeforeTicks", x.AutoCancelBeforeTicks); w.WriteNumber("autoCancelAfterTicks", x.AutoCancelAfterTicks); w.WritePropertyName("animationIds"); w.WriteStartArray(); foreach (var id in x.AnimationIds) w.WriteStringValue(id); w.WriteEndArray(); w.WritePropertyName("operations"); w.WriteStartArray(); foreach (var op in x.Operations) WriteOperation(w, op); w.WriteEndArray(); w.WriteEndObject(); }
    private static void WriteOperation(Utf8JsonWriter w, CookedTimelineOperation x) { w.WriteStartObject(); w.WriteNumber("kind", (byte)x.Kind); w.WriteNumber("tick", x.Tick); w.WriteNumber("unit", (byte)x.Unit); switch (x) { case CookedSetVelocityOperation v: w.WriteNumber("velocityMode", (byte)v.VelocityMode); Number(w, "x", v.X); Number(w, "y", v.Y); Number(w, "z", v.Z); break; case CookedSpawnHitboxOperation h: WriteHitbox(w, h.Hitbox); break; case CookedSpawnProjectileOperation p: WriteProjectile(w, p.Projectile); break; case CookedSetAimStateOperation a: w.WriteNumber("aimState", (byte)a.AimState); break; case CookedStartCapabilityOperation c: w.WriteString("capabilityId", c.CapabilityId); w.WriteString("capabilityVersion", c.CapabilityVersion); w.WritePropertyName("parameters"); WriteParameters(w, c.Parameters); break; case CookedEmitPresentationOperation p: w.WriteNumber("operationIndex", p.OperationIndex); w.WriteString("presentationId", p.PresentationId); break; } w.WriteEndObject(); }
    private static void WriteHitbox(Utf8JsonWriter w, CookedHitbox x) { w.WritePropertyName("hitbox"); w.WriteStartObject(); w.WriteNumber("shape", (byte)x.Shape); Number(w, "radius", x.Radius); Number(w, "offsetX", x.OffsetX); Number(w, "offsetY", x.OffsetY); Number(w, "offsetZ", x.OffsetZ); Number(w, "endOffsetX", x.EndOffsetX); Number(w, "endOffsetY", x.EndOffsetY); Number(w, "endOffsetZ", x.EndOffsetZ); if (x.StartBoneId != null) w.WriteString("startBoneId", x.StartBoneId); else w.WriteNull("startBoneId"); if (x.EndBoneId != null) w.WriteString("endBoneId", x.EndBoneId); else w.WriteNull("endBoneId"); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("durationTicks", x.DurationTicks); w.WriteBoolean("interruptible", x.Interruptible); w.WriteNumber("hitGroup", x.HitGroup); w.WriteEndObject(); }
    private static void WriteProjectile(Utf8JsonWriter w, CookedProjectile x) { w.WritePropertyName("projectile"); w.WriteStartObject(); Number(w, "launchOffsetX", x.LaunchOffsetX); Number(w, "launchOffsetY", x.LaunchOffsetY); Number(w, "launchOffsetZ", x.LaunchOffsetZ); Number(w, "speed", x.Speed); Number(w, "gravity", x.Gravity); Number(w, "radius", x.Radius); Number(w, "damage", x.Damage); Number(w, "angle", x.Angle); Number(w, "baseKnockback", x.BaseKnockback); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); w.WriteEndObject(); }
    private static void WriteParameters(Utf8JsonWriter w, CookedCapabilityParameters p) { w.WriteStartObject(); switch (p) { case CookedKiShotCapabilityParameters x: w.WriteNumber("startupTicks", x.StartupTicks); w.WriteNumber("durationTicks", x.DurationTicks); Number(w, "launchOffsetY", x.LaunchOffsetY); Number(w, "projectileSpeed", x.ProjectileSpeed); Number(w, "gravity", x.Gravity); Number(w, "hitboxRadius", x.HitboxRadius); Number(w, "damage", x.Damage); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); Number(w, "knockbackAngle", x.KnockbackAngle); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("maxFlightTicks", x.MaxFlightTicks); break; case CookedRisingDragonCapabilityParameters x: Number(w, "riseSpeed", x.RiseSpeed); w.WriteNumber("riseTicks", x.RiseTicks); w.WriteNumber("riseDelay", x.RiseDelay); break; case CookedCycloneKickCapabilityParameters x: Number(w, "forwardSpeed", x.ForwardSpeed); w.WriteNumber("windupTicks", x.WindupTicks); w.WriteNumber("hitboxEndTick", x.HitboxEndTick); w.WriteNumber("durationTicks", x.DurationTicks); Number(w, "bodyRadius", x.BodyRadius); Number(w, "sideRadius", x.SideRadius); Number(w, "sideOffset", x.SideOffset); Number(w, "damage", x.Damage); Number(w, "knockbackAngle", x.KnockbackAngle); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); Number(w, "bodyY", x.BodyY); Number(w, "sideY", x.SideY); break; case CookedDragonBeamCapabilityParameters x: w.WriteNumber("durationTicks", x.DurationTicks); w.WriteNumber("fireTick", x.FireTick); Number(w, "launchOffsetY", x.LaunchOffsetY); Number(w, "beamRange", x.BeamRange); Number(w, "beamRadius", x.BeamRadius); Number(w, "damage", x.Damage); Number(w, "knockbackAngle", x.KnockbackAngle); Number(w, "knockbackBase", x.KnockbackBase); Number(w, "knockbackGrowth", x.KnockbackGrowth); w.WriteNumber("stunTicks", x.StunTicks); w.WriteNumber("hitboxDurationTicks", x.HitboxDurationTicks); break; } w.WriteEndObject(); }
    private static void Number(Utf8JsonWriter w, string name, float value) => w.WriteNumber(name, value);
    private static string EnumText(Enum x) => x.ToString();

    private sealed class DiagnosticBag
    {
        private readonly List<(int Order, CharacterDiagnostic Value)> _items = new(); private int _order;
        public bool HasErrors => _items.Any(x => x.Value.Severity == CharacterDiagnosticSeverity.Error);
        public void Error(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message)));
        public void Warning(string code, string path, string message) => _items.Add((_order++, new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, code, path, message)));
        public List<CharacterDiagnostic> ToList() => _items.OrderBy(x => x.Order).ThenBy(x => x.Value.Code, StringComparer.Ordinal).Select(x => x.Value).ToList();
    }

    private static void ValidateId(string value, string path, DiagnosticBag d) { if (string.IsNullOrEmpty(value) || value.Length > 64 || value[0] < 'a' || value[0] > 'z' || value.Any(x => !(x >= 'a' && x <= 'z') && !(x >= '0' && x <= '9') && x != '.' && x != '-')) d.Error("id.invalid", path, "ID must be lowercase ASCII and start with a letter."); }
    private static bool IsSemVer(string value)
    {
        var parts = value.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && parts[0].All(char.IsDigit) && parts[1].All(char.IsDigit) && parts[2].All(char.IsDigit);
    }
    private static void ValidateNonNegative(float value, string path, DiagnosticBag d) { if (float.IsNaN(value) || float.IsInfinity(value)) d.Error("value.non-finite", path, "Value must be finite."); else if (value < 0) d.Error("value.out-of-range", path, "Value must be non-negative."); }
    private static void ValidateAngle(float value, string path, DiagnosticBag d) { if (value < -90 || value > 90) d.Error("value.out-of-range", path, "Angle must be between -90 and 90 degrees."); }
}
