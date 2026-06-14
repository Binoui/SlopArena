#nullable enable
using Godot;

/// <summary>
/// Special effects for Bunny (Kung-Fu Rabbit) abilities.
/// Called AFTER stage resolution by AbilityRegistry.
/// Each method maps to a key in CharacterDefinition's SpecialEffectKeys.
/// </summary>
public static class BunnyAbilities
{
    /// <summary>
    /// RMB — Carrot Slam: ground pound + slow zone
    /// </summary>
    public static void CarrotSlam(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateImpactVisual(combat, pos + (Vector3.Down * 0.3f), 2f, new Color(1f, 0.7f, 0f));
        StatusSpells.CreateCircleVisual(combat, pos, 2f, new Color(1f, 0.5f, 0f, 0.3f), 0.4f);
    }

    /// <summary>
    /// Q — Whirling Carrot: boomerang projectile + mark
    /// </summary>
    public static void WhirlingCarrot(CombatComponent combat)
    {
        Vector3 forward = combat.GetCameraForward();
        Vector3 pos = combat.GetOwnerPosition() + (Vector3.Up * 1.2f);
        StatusSpells.CreateCircleVisual(combat, pos + (forward * 3f), 0.8f, new Color(1f, 0.6f, 0f, 0.3f), 0.5f);
        StatusSpells.CreateImpactVisual(combat, pos + (forward * 4f), 0.8f, new Color(1f, 0.8f, 0.2f));
    }

    /// <summary>
    /// E — Flip Kick: backflip kick mobility
    /// </summary>
    public static void FlipKick(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateCircleVisual(combat, pos + (Vector3.Back * 0.5f), 0.6f, new Color(0.5f, 0.8f, 1f, 0.3f), 0.3f);
    }

    /// <summary>
    /// R — Dragon's Kick: powerful kick (buffed if target marked by Q)
    /// </summary>
    public static void DragonKick(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        Vector3 forward = -combat.GetOwnerForward(); // Mixama +Z fix
        StatusSpells.CreateImpactVisual(combat, pos + (forward * 2f) + (Vector3.Up * 0.8f), 2.5f, new Color(0.8f, 0.4f, 1f));
        StatusSpells.CreateCircleVisual(combat, pos + (forward * 2f), 3f, new Color(0.7f, 0.3f, 1f, 0.3f), 0.3f);
    }

    /// <summary>
    /// F — Jade Hare (Ult): circular kick ultimate
    /// </summary>
    public static void JadeHare(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateImpactVisual(combat, pos + (Vector3.Up * 0.3f), 3.5f, new Color(0.3f, 0.8f, 1f));
        StatusSpells.CreateCircleVisual(combat, pos, 3.5f, new Color(0.2f, 0.7f, 1f, 0.3f), 0.8f);
    }
}
