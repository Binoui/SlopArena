using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed class CookedCharacterPackageLoadResult
{
    public CookedCharacterPackage? Package { get; }
    public BakedAnimationData? BakedAnimation { get; }
    public MatchContentIdentity Identity { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
    public bool IsValid => Package != null && Diagnostics.All(x => x.Severity != CharacterDiagnosticSeverity.Error);

    internal CookedCharacterPackageLoadResult(CookedCharacterPackage? package, BakedAnimationData? baked, MatchContentIdentity identity, IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Package = package; BakedAnimation = baked; Identity = identity;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(new List<CharacterDiagnostic>(diagnostics));
    }

    public CharacterDefinition ToCharacterDefinition(CharacterClass legacySelector = CharacterClass.None)
    {
        if (!IsValid || Package == null) throw new InvalidDataException("Cooked package is not valid.");
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(Package, legacySelector);
    }
}
public static class CookedCharacterPackageLoader
{
    public static CookedCharacterPackageLoadResult LoadAssembly(CharacterPackageAssemblyResult assembly)
    {
        if (assembly == null)
            return Failure(new List<CharacterDiagnostic> { Error("package.assembly.missing", "assembly", "Assembly result is required.") });
        try
        {
            using var document = JsonDocument.Parse(assembly.ManifestBytes);
            var root = document.RootElement;
            var requirement = new MatchContentPackageRequirement(
                root.GetProperty("packageId").GetString() ?? "",
                root.GetProperty("version").GetString() ?? "",
                root.GetProperty("cookedContentHash").GetString() ?? "",
                root.GetProperty("packageHash").GetString() ?? "");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [CharacterPackageAssembler.ManifestPath] = assembly.ManifestBytes,
                [CharacterPackageAssembler.RuntimePath] = assembly.RuntimeBytes,
                [CharacterPackageAssembler.PosePath] = assembly.PoseBytes,
                [CharacterPackageAssembler.BindingPath] = assembly.BindingBytes,
            };
            return LoadFiles(files, requirement);
        }
        catch (Exception ex)
        {
            return Failure(new List<CharacterDiagnostic> { Error("package.assembly.malformed", "manifest", ex.Message) });
        }
    }
    public static CookedCharacterPackageLoadResult LoadDirectory(string directory, MatchContentPackageRequirement requirement)
    {
        var d = new List<CharacterDiagnostic>();
        if (string.IsNullOrWhiteSpace(directory)) { d.Add(Error("package.directory.missing","directory","Package directory is required.")); return Failure(d); }
        var files = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach (var name in new[]{CharacterPackageAssembler.ManifestPath,CharacterPackageAssembler.RuntimePath,CharacterPackageAssembler.PosePath,CharacterPackageAssembler.BindingPath})
        {
            try { files[name]=File.ReadAllBytes(Path.Combine(directory,name)); }
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is NotSupportedException) { d.Add(Error("package.file.missing",name,ex.Message)); }
        }
        return d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error) ? Failure(d) : LoadFiles(files,requirement);
    }

    public static CookedCharacterPackageLoadResult LoadFiles(IReadOnlyDictionary<string,byte[]> files, MatchContentPackageRequirement requirement)
    {
        var d=new List<CharacterDiagnostic>();
        if(requirement==null) d.Add(Error("package.requirement.missing","requirement","Package requirement is required."));
        else if(!MatchContentCatalogBuilder.IsStablePackageId(requirement.PackageId)||string.IsNullOrWhiteSpace(requirement.Version)||!MatchContentCatalogBuilder.IsSha(requirement.CookedContentHash)||!MatchContentCatalogBuilder.IsSha(requirement.PackageHash)) d.Add(Error("package.requirement.invalid","requirement","Package requirement is incomplete or invalid."));
        var copied=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        if(files!=null) foreach(var p in files) copied[p.Key]=p.Value==null?null!:(byte[])p.Value.Clone();
        d.AddRange(CharacterPackageAssembler.Verify(copied).Diagnostics);
        if(d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error)) return Failure(d);
        try
        {
            var m=ParseManifest(copied[CharacterPackageAssembler.ManifestPath]);
            if(m.PackageId!=requirement!.PackageId||m.Version!=requirement.Version||m.CookedContentHash!=requirement.CookedContentHash||m.PackageHash!=requirement.PackageHash) d.Add(Error("package.identity.mismatch","manifest","Package identity does not match the requested requirement."));
            if(m.CookedSchemaVersion!=1||m.RuntimeApiMin!="1.0.0"||m.RuntimeApiMax!="1.x") d.Add(Error("package.compatibility.unsupported","manifest","Cooked package schema/API is not supported."));
            if(m.Dependencies.Count!=0) d.Add(Error("package.dependencies.unsupported","manifest.dependencies","Unresolved package dependencies are not supported."));
            foreach(var c in m.Capabilities) if(c.CapabilityVersion!="1"||!SupportedCapability(c.CapabilityId)) d.Add(Error("package.capability.unsupported",c.CapabilityId,"Cooked capability is not supported by this runtime."));
            var package=RuntimeParser.Parse(copied[CharacterPackageAssembler.RuntimePath]);
            if(package.Metadata.PackageId!=m.PackageId||package.Metadata.Version!=m.Version||package.Metadata.CookedSchemaVersion!=m.CookedSchemaVersion) d.Add(Error("package.runtime.metadata-mismatch",CharacterPackageAssembler.RuntimePath,"Runtime package metadata does not match manifest."));
            var baked=BakedAnimationData.LoadFromBin(copied[CharacterPackageAssembler.PosePath]);
            if(d.Any(x=>x.Severity==CharacterDiagnosticSeverity.Error)) return Failure(d);
            return new CookedCharacterPackageLoadResult(package,baked,new MatchContentIdentity(m.PackageId,m.Version,m.SourceHash,m.CookedContentHash,m.PackageHash),d);
        }
        catch(Exception ex) { d.Add(Error("package.runtime.malformed","package",ex.Message)); return Failure(d); }
    }

    private static CookedCharacterPackageLoadResult Failure(List<CharacterDiagnostic> d)=>new(null,null,new MatchContentIdentity("","","","",""),d);
    private static CharacterDiagnostic Error(string c,string p,string m)=>new(CharacterDiagnosticSeverity.Error,c,p,m);
    private static bool SupportedCapability(string id)=>id=="slop.internal.fightguy.cyclone-kick.v1"||id=="slop.internal.fightguy.dragon-beam.v1"||id=="slop.internal.fightguy.ki-shot.v1"||id=="slop.internal.fightguy.rising-dragon.v1";

    private sealed class ManifestInfo { public string PackageId=""; public string Version=""; public ushort CookedSchemaVersion; public string RuntimeApiMin=""; public string RuntimeApiMax=""; public string SourceHash=""; public string CookedContentHash=""; public string PackageHash=""; public readonly List<PackageDependencySource> Dependencies=new(); public readonly List<CookedCapabilityRequirement> Capabilities=new(); }
    private static ManifestInfo ParseManifest(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes); var r=doc.RootElement; if(r.ValueKind!=JsonValueKind.Object) throw new InvalidDataException("Manifest must be an object.");
        var m=new ManifestInfo{PackageId=S(r,"packageId"),Version=S(r,"version"),CookedSchemaVersion=U(r,"cookedSchemaVersion"),SourceHash=S(r,"sourceHash"),CookedContentHash=S(r,"cookedContentHash"),PackageHash=S(r,"packageHash")};
        m.RuntimeApiMin=S(r,"runtimeApiMin"); m.RuntimeApiMax=S(r,"runtimeApiMax");
        foreach(var x in A(r,"dependencies").EnumerateArray()){var q=O(x,"packageId","version","cookedHash");m.Dependencies.Add(new PackageDependencySource(S(q,"packageId"),S(q,"version"),S(q,"cookedHash")));}
        foreach(var x in A(r,"capabilityRequirements").EnumerateArray()){var q=O(x,"capabilityId","capabilityVersion");m.Capabilities.Add(new CookedCapabilityRequirement(S(q,"capabilityId"),S(q,"capabilityVersion")));} return m;
    }
    private static Dictionary<string,JsonElement> O(JsonElement e,params string[] f){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var set=new HashSet<string>(f,StringComparer.Ordinal);var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!set.Contains(p.Name)||!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Unknown or duplicate field.");foreach(var x in f)if(!d.ContainsKey(x))throw new InvalidDataException("Missing field.");return d;}
    private static string S(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()!:throw new InvalidDataException(n+" must be a string.");
    private static ushort U(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be an unsigned integer.");
    private static string S(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw new InvalidDataException(n+" must be a string.");
    private static ushort U(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be an unsigned integer.");
    private static JsonElement A(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");
    private static JsonElement A(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");

    private static class RuntimeParser
    {
        public static CookedCharacterPackage Parse(byte[] bytes)
        {
            using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;var mm=O(root.GetProperty("metadata"),"packageId","version","cookedSchemaVersion","compatibility");var api=O(mm["compatibility"],"runtimeApiMin","runtimeApiMax");var metadata=new CookedPackageMetadata(S(mm,"packageId"),S(mm,"version"),U(mm,"cookedSchemaVersion"),S(api,"runtimeApiMin"),S(api,"runtimeApiMax"));
            var c=O(root.GetProperty("character"),"displayName","weight","movement","presentation","capsuleRadius","capsuleHeight","hipHeight","hurtboxRadius","hurtboxCapsules","hurtboxBoneDefs","presentationIds","capabilityRequirements","slots");var mv=O(c["movement"],"runSpeed","runAccelerationA","runAccelerationB","dashSpeed","airSpeedMax","airAccelStick","airAccelBase","jumpForce","shortHopForce","airJumpVMultiplier","airJumpHMultiplier","gravity","airFloatGravity","dashDurationTicks","dashCooldownTicks","groundFriction","airFriction","maxFallSpeed","fastFallSpeed","maxJumps","jumpSquatTicks","floatWindowTicks","rushTicks");
            var movement=new CookedMovement(F(mv,"runSpeed"),F(mv,"runAccelerationA"),F(mv,"runAccelerationB"),F(mv,"dashSpeed"),F(mv,"airSpeedMax"),F(mv,"airAccelStick"),F(mv,"airAccelBase"),F(mv,"jumpForce"),F(mv,"shortHopForce"),F(mv,"airJumpVMultiplier"),F(mv,"airJumpHMultiplier"),F(mv,"gravity"),F(mv,"airFloatGravity"),U(mv,"dashDurationTicks"),U(mv,"dashCooldownTicks"),F(mv,"groundFriction"),F(mv,"airFriction"),F(mv,"maxFallSpeed"),F(mv,"fastFallSpeed"),B(mv,"maxJumps"),U(mv,"jumpSquatTicks"),U(mv,"floatWindowTicks"),U(mv,"rushTicks"));var pr=O(c["presentation"],"idle","run","dash","jump","fall","hitSmall","hitMedium","hitHard","landStartOffsetSeconds","modelResourcePath","visualScale","hurtboxBoneScale","modelYOffset","modelSoleOffset","autoModelYOffset");var presentation=new CookedPresentation(S(pr,"idle"),S(pr,"run"),S(pr,"dash"),S(pr,"jump"),S(pr,"fall"),S(pr,"hitSmall"),S(pr,"hitMedium"),S(pr,"hitHard"),F(pr,"landStartOffsetSeconds"),S(pr,"modelResourcePath"),F(pr,"visualScale"),F(pr,"hurtboxBoneScale"),F(pr,"modelYOffset"),F(pr,"modelSoleOffset"),Bo(pr,"autoModelYOffset"));
            var capsules=A(c["hurtboxCapsules"]).EnumerateArray().Select(x=>{var q=O(x,"startX","startY","startZ","endX","endY","endZ","radius");return new CookedHurtboxCapsule(F(q,"startX"),F(q,"startY"),F(q,"startZ"),F(q,"endX"),F(q,"endY"),F(q,"endZ"),F(q,"radius"));}).ToList();var bones=A(c["hurtboxBoneDefs"]).EnumerateArray().Select(x=>{var q=O(x,"boneId","offsetX","offsetY","offsetZ","radius");return new CookedHurtboxBone(S(q,"boneId"),F(q,"offsetX"),F(q,"offsetY"),F(q,"offsetZ"),F(q,"radius"));}).ToList();var ids=A(c["presentationIds"]).EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String?x.GetString()!:throw new InvalidDataException("Presentation ID must be a string.")).ToList();var caps=A(c["capabilityRequirements"]).EnumerateArray().Select(x=>{var q=O(x,"capabilityId","capabilityVersion");return new CookedCapabilityRequirement(S(q,"capabilityId"),S(q,"capabilityVersion"));}).ToList();var slots=A(c["slots"]).EnumerateArray().Select(ParseSlot).ToList();var definition=new CookedCharacterDefinition(S(c,"displayName"),F(c,"weight"),movement,presentation,F(c,"capsuleRadius"),F(c,"capsuleHeight"),F(c,"hipHeight"),F(c,"hurtboxRadius"),capsules,bones,ids,caps,slots);var b=O(root.GetProperty("budget"),"slotCount","stageCount","operationCount","hitboxCount","projectileCount","capabilityCount","maxTimelineDurationTicks");var budget=new CookedBudget(I(b,"slotCount"),I(b,"stageCount"),I(b,"operationCount"),I(b,"hitboxCount"),I(b,"projectileCount"),I(b,"capabilityCount"),I(b,"maxTimelineDurationTicks"));return new CookedCharacterPackage(metadata,definition,budget,Array.Empty<CharacterDiagnostic>(),bytes);
        }
        private static CookedSlotDefinition ParseSlot(JsonElement e){var q=O(e,"ordinal","id","isAir","name","description","iconId","behavior","aimMode","cooldownTicks","isRecoveryMove","preserveMomentumOnStart","timeline");var t=O(q["timeline"],"stages");return new CookedSlotDefinition(I(q,"ordinal"),S(q,"id"),Bo(q,"isAir"),S(q,"name"),S(q,"description"),S(q,"iconId"),(AuthoringAbilityBehavior)B(q,"behavior"),(AuthoringAimMode)B(q,"aimMode"),U(q,"cooldownTicks"),Bo(q,"isRecoveryMove"),Bo(q,"preserveMomentumOnStart"),new CookedTimeline(A(t,"stages").EnumerateArray().Select(ParseStage).ToList()));}
        private static CookedStage ParseStage(JsonElement e){var q=O(e,"durationTicks","iasaTicks","landingLagTicks","autoCancelBeforeTicks","autoCancelAfterTicks","animationIds","operations");return new CookedStage(U(q,"durationTicks"),U(q,"iasaTicks"),U(q,"landingLagTicks"),U(q,"autoCancelBeforeTicks"),U(q,"autoCancelAfterTicks"),A(q["animationIds"]).EnumerateArray().Select(x=>x.GetString()!).ToList(),A(q["operations"]).EnumerateArray().Select(ParseOperation).ToList());}
        private static CookedTimelineOperation ParseOperation(JsonElement e){var common=All(e);var k=(CookedOperationKind)B(common,"kind");var tick=U(common,"tick");var unit=(AuthoringUnit)B(common,"unit");return k switch{CookedOperationKind.SetVelocity=>Velocity(e,tick,unit),CookedOperationKind.SpawnHitbox=>new CookedSpawnHitboxOperation(tick,unit,ParseHitbox(O(e,"kind","tick","unit","hitbox")["hitbox"])),CookedOperationKind.SpawnProjectile=>new CookedSpawnProjectileOperation(tick,unit,ParseProjectile(O(e,"kind","tick","unit","projectile")["projectile"])),CookedOperationKind.SetAimState=>new CookedSetAimStateOperation(tick,unit,(AuthoringAimMode)B(O(e,"kind","tick","unit","aimState"),"aimState")),CookedOperationKind.StartCapability=>Capability(e,tick,unit),CookedOperationKind.EmitPresentation=>Emit(e,tick,unit),CookedOperationKind.CompleteTimeline=>new CookedCompleteTimelineOperation(tick,unit),_=>throw new InvalidDataException("Unknown cooked operation kind.")};}
        private static CookedSetVelocityOperation Velocity(JsonElement e,ushort tick,AuthoringUnit unit){var q=O(e,"kind","tick","unit","velocityMode","x","y","z");return new CookedSetVelocityOperation(tick,unit,(AuthoringVelocityMode)B(q,"velocityMode"),F(q,"x"),F(q,"y"),F(q,"z"));}
        private static CookedStartCapabilityOperation Capability(JsonElement e,ushort tick,AuthoringUnit unit){var q=O(e,"kind","tick","unit","capabilityId","capabilityVersion","parameters");var id=S(q,"capabilityId");return new CookedStartCapabilityOperation(tick,unit,id,S(q,"capabilityVersion"),ParseParameters(id,q["parameters"]));}
        private static CookedEmitPresentationOperation Emit(JsonElement e,ushort tick,AuthoringUnit unit){var q=O(e,"kind","tick","unit","presentationId","operationIndex");return new CookedEmitPresentationOperation(tick,unit,S(q,"presentationId"),I(q,"operationIndex"));}
        
        private static CookedHitbox ParseHitbox(JsonElement e){var q=O(e,"shape","radius","offsetX","offsetY","offsetZ","endOffsetX","endOffsetY","endOffsetZ","startBoneId","endBoneId","damage","angle","baseKnockback","knockbackGrowth","stunTicks","durationTicks","interruptible","hitGroup");return new CookedHitbox((AuthoringHitboxShape)B(q,"shape"),F(q,"radius"),F(q,"offsetX"),F(q,"offsetY"),F(q,"offsetZ"),F(q,"endOffsetX"),F(q,"endOffsetY"),F(q,"endOffsetZ"),N(q,"startBoneId"),N(q,"endBoneId"),F(q,"damage"),F(q,"angle"),F(q,"baseKnockback"),F(q,"knockbackGrowth"),U(q,"stunTicks"),U(q,"durationTicks"),Bo(q,"interruptible"),B(q,"hitGroup"));}
        private static CookedProjectile ParseProjectile(JsonElement e){var q=O(e,"launchOffsetX","launchOffsetY","launchOffsetZ","speed","gravity","radius","damage","angle","baseKnockback","knockbackGrowth","stunTicks","maxFlightTicks");return new CookedProjectile(F(q,"launchOffsetX"),F(q,"launchOffsetY"),F(q,"launchOffsetZ"),F(q,"speed"),F(q,"gravity"),F(q,"radius"),F(q,"damage"),F(q,"angle"),F(q,"baseKnockback"),F(q,"knockbackGrowth"),U(q,"stunTicks"),U(q,"maxFlightTicks"));}
        private static CookedCapabilityParameters ParseParameters(string id,JsonElement e){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Capability parameters must be an object.");var q=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())q[p.Name]=p.Value;return id switch{"slop.internal.fightguy.ki-shot.v1"=>new CookedKiShotCapabilityParameters(U(q,"startupTicks"),U(q,"durationTicks"),F(q,"launchOffsetY"),F(q,"projectileSpeed"),F(q,"gravity"),F(q,"hitboxRadius"),F(q,"damage"),F(q,"knockbackBase"),F(q,"knockbackGrowth"),F(q,"knockbackAngle"),U(q,"stunTicks"),U(q,"maxFlightTicks")),"slop.internal.fightguy.rising-dragon.v1"=>new CookedRisingDragonCapabilityParameters(F(q,"riseSpeed"),U(q,"riseTicks"),U(q,"riseDelay")),"slop.internal.fightguy.cyclone-kick.v1"=>new CookedCycloneKickCapabilityParameters(F(q,"forwardSpeed"),U(q,"windupTicks"),U(q,"hitboxEndTick"),U(q,"durationTicks"),F(q,"bodyRadius"),F(q,"sideRadius"),F(q,"sideOffset"),F(q,"damage"),F(q,"knockbackAngle"),F(q,"knockbackBase"),F(q,"knockbackGrowth"),U(q,"stunTicks"),F(q,"bodyY"),F(q,"sideY")),"slop.internal.fightguy.dragon-beam.v1"=>new CookedDragonBeamCapabilityParameters(U(q,"durationTicks"),U(q,"fireTick"),F(q,"launchOffsetY"),F(q,"beamRange"),F(q,"beamRadius"),F(q,"damage"),F(q,"knockbackAngle"),F(q,"knockbackBase"),F(q,"knockbackGrowth"),U(q,"stunTicks"),U(q, "hitboxDurationTicks")),_=>throw new InvalidDataException("Unknown capability parameters.")};}
        private static Dictionary<string,JsonElement> O(JsonElement e,params string[] f){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var set=new HashSet<string>(f,StringComparer.Ordinal);var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!set.Contains(p.Name)||!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Unknown or duplicate field.");foreach(var x in f)if(!d.ContainsKey(x))throw new InvalidDataException("Missing field.");return d;}
        private static Dictionary<string,JsonElement> All(JsonElement e){if(e.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Object required.");var d=new Dictionary<string,JsonElement>();foreach(var p in e.EnumerateObject())if(!d.TryAdd(p.Name,p.Value))throw new InvalidDataException("Duplicate field.");return d;}
        private static JsonElement A(JsonElement e)=>e.ValueKind==JsonValueKind.Array?e:throw new InvalidDataException("Array required.");
        private static JsonElement A(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var p)&&p.ValueKind==JsonValueKind.Array?p:throw new InvalidDataException(n+" must be an array.");
        private static string S(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()!:throw new InvalidDataException(n+" must be string.");private static string S(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()!:throw new InvalidDataException(n+" must be string.");private static string? N(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;private static float F(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetSingle(out var v)?v:throw new InvalidDataException(n+" must be number.");private static float F(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetSingle(out var v)?v:throw new InvalidDataException(n+" must be number.");private static ushort U(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be unsigned integer.");private static ushort U(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetUInt16(out var v)?v:throw new InvalidDataException(n+" must be unsigned integer.");private static byte B(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetByte(out var v)?v:throw new InvalidDataException(n+" must be unsigned byte.");private static byte B(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetByte(out var v)?v:throw new InvalidDataException(n+" must be unsigned byte.");private static bool Bo(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&(e.ValueKind==JsonValueKind.True||e.ValueKind==JsonValueKind.False)?e.GetBoolean():throw new InvalidDataException(n+" must be boolean.");private static int I(Dictionary<string,JsonElement>d,string n)=>d.TryGetValue(n,out var e)&&e.TryGetInt32(out var v)?v:throw new InvalidDataException(n+" must be integer.");private static int I(JsonElement e,string n)=>e.TryGetProperty(n,out var p)&&p.TryGetInt32(out var v)?v:throw new InvalidDataException(n+" must be integer.");
    }
}
