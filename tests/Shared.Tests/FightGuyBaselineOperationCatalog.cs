using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SlopArena.Shared;

public static class FightGuyBaselineOperationCatalog
{
    public static IReadOnlyList<CookedSlotDefinition> Slots { get; } = BuildSlots();

    private static IReadOnlyList<CookedSlotDefinition> BuildSlots()
    {
        var ground1 = Slot(0, "ground.1", "Low Kick", "Low Kick", "icon.1", H(4, AuthoringHitboxShape.Sphere, .35f, 0f, 0f, .21f, "bone.right-foot", 4f, 8, 4f, 20f, 14));
        var ground2 = Slot(1, "ground.2", "Straight Punch", "Straight Punch", "icon.2", H(5, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, .21f, "bone.right-hand", 7f, 25, 5f, 26f, 18));
        var ground3 = Slot(2, "ground.3", "Sweeping Kick", "Sweeping Kick", "icon.3", H(7, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, .21f, "bone.right-foot", 7f, 55, 5f, 24f, 18, duration: 6));
        var ground4 = Slot(3, "ground.4", "Double Kick", "Double Kick", "icon.4", H(10, AuthoringHitboxShape.Capsule, .42f, 0f, 0f, 0f, "bone.left-foot", 14f, 28, 9f, 42f, 26, "bone.right-foot", 7));
        var groundA = Slot(4, "ground.A", "Ki Shot", "Ki Shot", "icon.a", 120, false, false, "anim.ki-shot", new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, "slop.internal.fightguy.ki-shot.v1", "1", new CookedKiShotCapabilityParameters(8, 24, 1.2f, 25f, 1f, .5f, 6f, 3f, 4.5f, 30, 12, 90)));
        var groundE = Slot(5, "ground.E", "Rising Dragon", "Rising Dragon", "icon.e", 240, true, false, "anim.rising-dragon",
            new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, "slop.internal.fightguy.rising-dragon.v1", "1", new CookedRisingDragonCapabilityParameters(11f, 12, 8)),
            H(6, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, .23f, "bone.right-hand", 8f, 75, 30f, 6f, 22, duration: 25),
            H(6, AuthoringHitboxShape.Sphere, .3f, 0f, .18f, 0f, "bone.head", 8f, 75, 30f, 6f, 22, duration: 25),
            H(10, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, .63f, "bone.hips", 8f, 75, 30f, 6f, 22, duration: 5));
        var groundR = Slot(6, "ground.R", "Cyclone Kick", "Cyclone Kick", "icon.r", 360, false, false, "anim.cyclone-kick",
            new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, "slop.internal.fightguy.cyclone-kick.v1", "1", new CookedCycloneKickCapabilityParameters(17f, 6, 34, 40, .8f, .4f, .8f, 7f, 15, 8f, 5f, 6, .8f, .3f)),
            new CookedEmitPresentationOperation(0, AuthoringUnit.Ticks, "presentation.cyclone-kick.start", 10));
        var groundF = Slot(7, "ground.F", "Dragon Beam", "Dragon Beam", "icon.f", 1200, false, false, "anim.dragon-beam",
            new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks, "slop.internal.fightguy.dragon-beam.v1", "1", new CookedDragonBeamCapabilityParameters(28, 24, 1.2f, 18f, .45f, 14f, 20, 18f, 10f, 24, 2)));

        var air1 = Slot(8, "air.1", "Double Punch", "Double Punch", "icon.1", 0, false, false, "anim.double-punch",
            H(6, AuthoringHitboxShape.Sphere, .3f, 0f, 0f, 0f, "bone.right-hand", 3f, 55, 5f, 24f, 12),
            H(16, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, 0f, "bone.left-hand", 5f, 45, 7f, 30f, 16));
        var air2 = Slot(9, "air.2", "Floating Kick", "Floating Kick", "icon.2", 0, false, false, "anim.floating-kick",
            H(7, AuthoringHitboxShape.Capsule, .35f, 0f, 0f, 0f, "bone.left-foot", 8f, 25, 5f, 26f, 18, "bone.hips", 5, 1),
            H(12, AuthoringHitboxShape.Capsule, .35f, 0f, 0f, 0f, "bone.left-foot", 5f, 20, 3f, 16f, 12, "bone.hips", 20, 1));
        var air3 = Slot(10, "air.3", "High Kick", "High Kick", "icon.3", 0, false, false, "anim.high-kick",
            H(14, AuthoringHitboxShape.Sphere, .35f, 0f, 0f, .14f, "bone.right-foot", 8f, 65, 5f, 26f, 20, duration: 6));
        var air4 = Slot(11, "air.4", "Air Smash", "Air Smash", "icon.4", 0, false, false, "anim.air-smash",
            H(20, AuthoringHitboxShape.Sphere, .4f, 0f, 0f, .24f, "bone.right-hand", 13f, 25, 8f, 42f, 26, duration: 7));

        return new ReadOnlyCollection<CookedSlotDefinition>(new List<CookedSlotDefinition>
        {
            ground1, ground2, ground3, ground4, groundA, groundE, groundR, groundF,
            air1, air2, air3, air4,
            Alias(groundA, 12, "air.A"), Alias(groundE, 13, "air.E"), Alias(groundR, 14, "air.R"), Alias(groundF, 15, "air.F"),
        });
    }

    private static CookedSlotDefinition Slot(int ordinal, string id, string name, string description, string icon, params CookedTimelineOperation[] operations)
        => Slot(ordinal, id, name, description, icon, 0, false, false, AnimationFor(id), operations);

    private static string AnimationFor(string id)
        => id switch
        {
            "ground.1" => "anim.low-kick",
            "ground.2" => "anim.straight-punch",
            "ground.3" => "anim.sweeping-kick",
            "ground.4" => "anim.double-kick",
            "air.1" => "anim.double-punch",
            "air.2" => "anim.floating-kick",
            "air.3" => "anim.high-kick",
            "air.4" => "anim.air-smash",
            _ => "anim." + id.Replace('.', '-'),
        };

    private static CookedSlotDefinition Slot(int ordinal, string id, string name, string description, string icon, ushort cooldown, bool recovery, bool preserveMomentum, string animation, params CookedTimelineOperation[] operations)
    {
        var air = id.StartsWith("air.", StringComparison.Ordinal);
        var duration = id switch
        {
            "ground.1" => 17, "air.1" => 33, "ground.2" => 25, "air.2" => 42,
            "ground.3" => 29, "air.3" => 44, "ground.4" => 60, "air.4" => 54,
            "ground.A" => 24, "ground.E" => 34, "ground.R" => 40, "ground.F" => 28,
            _ => throw new InvalidOperationException("Unknown FightGuy slot.")
        };
        var iasa = id switch
        {
            "ground.1" => 13, "air.1" => 29, "ground.2" => 22, "air.2" => 36,
            "ground.3" => 25, "air.3" => 41, "ground.4" => 56, "air.4" => 50, _ => 0
        };
        var landing = id switch { "air.1" or "air.2" or "air.3" => 9, "air.4" => 12, _ => 0 };
        var autoBefore = air && id != "air.4" ? 5 : id == "air.4" ? 5 : 0;
        var autoAfter = id switch { "air.1" => 23, "air.2" => 29, "air.3" => 30, "air.4" => 38, _ => 0 };
        var hitboxes = new List<CookedTimelineOperation>();
        foreach (var operation in operations) hitboxes.Add(operation);
        return new CookedSlotDefinition(ordinal, id, air, name, description, icon,
            id is "ground.A" or "ground.F" ? AuthoringAbilityBehavior.Projectile : AuthoringAbilityBehavior.MeleeCombo,
            id is "ground.A" or "ground.F" ? AuthoringAimMode.CameraForward3D : AuthoringAimMode.None,
            cooldown, recovery, preserveMomentum,
            new CookedTimeline(new[] { new CookedStage((ushort)duration, (ushort)iasa, (ushort)landing, (ushort)autoBefore, (ushort)autoAfter, new[] { animation }, hitboxes) }));
    }

    private static CookedSlotDefinition Alias(CookedSlotDefinition source, int ordinal, string id)
    {
        var stages = new List<CookedStage>();
        foreach (var stage in source.Timeline.Stages)
        {
            var operations = new List<CookedTimelineOperation>();
            var operationOrdinal = id == "air.R" ? 24 : -1;
            foreach (var operation in stage.Operations)
                operations.Add(Clone(operation, operationOrdinal));
            stages.Add(new CookedStage(stage.DurationTicks, stage.IasaTicks, stage.LandingLagTicks, stage.AutoCancelBeforeTicks, stage.AutoCancelAfterTicks, new List<string>(stage.AnimationIds), operations));
        }
        return new CookedSlotDefinition(ordinal, id, true, source.Name, source.Description, source.IconId, source.Behavior, source.AimMode, source.CooldownTicks, source.IsRecoveryMove, source.PreserveMomentumOnStart, new CookedTimeline(stages));
    }
    private static CookedTimelineOperation Clone(CookedTimelineOperation operation, int aliasOperationOrdinal)
        => operation switch
        {
            CookedStartCapabilityOperation x => new CookedStartCapabilityOperation(x.Tick, x.Unit, x.CapabilityId, x.CapabilityVersion, x.Parameters),
            CookedSpawnHitboxOperation x => new CookedSpawnHitboxOperation(x.Tick, x.Unit, x.Hitbox),
            CookedEmitPresentationOperation x => new CookedEmitPresentationOperation(x.Tick, x.Unit, x.PresentationId, aliasOperationOrdinal >= 0 ? aliasOperationOrdinal : x.OperationIndex),
            _ => throw new InvalidOperationException("Unknown FightGuy catalog operation."),
        };

    private static CookedSpawnHitboxOperation H(ushort tick, AuthoringHitboxShape shape, float radius, float offsetX, float offsetY, float offsetZ, string bone, float damage, float angle, float baseKnockback, float growth, ushort stun, string? endBone = null, ushort duration = 5, byte hitGroup = 0)
        => new(tick, AuthoringUnit.Meters, new CookedHitbox(shape, radius, offsetX, offsetY, offsetZ, 0f, 0f, 0f, RuntimeBone(bone), RuntimeBone(endBone), damage, angle, baseKnockback, growth, stun, duration, true, hitGroup));
    
    private static string? RuntimeBone(string? bone)
        => bone switch
        {
            "bone.head" => "mixamorig:Head",
            "bone.hips" => "mixamorig:Hips",
            "bone.right-hand" => "mixamorig:RightHand",
            "bone.left-hand" => "mixamorig:LeftHand",
            "bone.right-foot" => "mixamorig:RightFoot",
            "bone.left-foot" => "mixamorig:LeftFoot",
            null => null,
            _ => throw new InvalidOperationException("Unknown FightGuy catalog bone."),
        };
}
