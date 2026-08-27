using SlopArena.Shared;

namespace SlopArena.Shared.Abilities;

public static class InternalCapabilityRegistry
{
    public static bool TryCreate(
        string capabilityId,
        string capabilityVersion,
        CookedCapabilityParameters parameters,
        out ServerAbility capability)
    {
        capability = null!;
        if (capabilityVersion != "1" || parameters == null)
            return false;

        switch (capabilityId)
        {
            case "slop.internal.fightguy.ki-shot.v1" when parameters is CookedKiShotCapabilityParameters ki:
                capability = new FightGuyKiShot(ki);
                return true;
            case "slop.internal.fightguy.rising-dragon.v1" when parameters is CookedRisingDragonCapabilityParameters rising:
                capability = new FightGuyRisingKick(rising);
                return true;
            case "slop.internal.fightguy.cyclone-kick.v1" when parameters is CookedCycloneKickCapabilityParameters cyclone:
                capability = new FightGuyCycloneKick(cyclone);
                return true;
            case "slop.internal.fightguy.dragon-beam.v1" when parameters is CookedDragonBeamCapabilityParameters beam:
                capability = new FightGuyDragonBeam(beam);
                return true;
            default:
                return false;
        }
    }
}
