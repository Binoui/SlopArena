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
            case "slop.internal.kistu.dash-slash.v1" when parameters is CookedKistuDashSlashCapabilityParameters dash:
                capability = new KistuDashSlash(dash);
                return true;
            case "slop.internal.kistu.rising-slash.v1" when parameters is CookedKistuRisingSlashCapabilityParameters rising:
                capability = new KistuRisingSlash(rising);
                return true;
            case "slop.internal.kistu.blade-flurry.v1" when parameters is CookedKistuBladeFlurryCapabilityParameters flurry:
                capability = new KistuUltFlurry(flurry);
                return true;
            case "slop.internal.bonk.targeted-jump-slam.v1" when parameters is CookedBonkTargetedJumpSlamCapabilityParameters bonk:
                capability = new BonkTargetedJumpSlam(bonk);
                return true;
            case "slop.internal.manki.round-bomb.v1" when parameters is CookedMankiRoundBombCapabilityParameters bomb:
                capability = new MankiRoundBomb(bomb);
                return true;
            case "slop.internal.manki.grapple.v1" when parameters is CookedMankiGrappleCapabilityParameters grapple:
                capability = new MankiGrapple(grapple);
                return true;
            case "slop.internal.manki.bazooka.v1" when parameters is CookedMankiBazookaCapabilityParameters bazooka:
                capability = new MankiBazooka(bazooka);
                return true;
            case "slop.internal.manki.overclock.v1" when parameters is CookedMankiOverclockCapabilityParameters overclock:
                capability = new MankiOverclock(overclock);
                return true;
            default:
                return false;
        }
    }
}
