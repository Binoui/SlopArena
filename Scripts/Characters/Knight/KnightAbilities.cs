#nullable enable
using Godot;

/// <summary>
/// Special effects for Knight abilities.
/// Based on King Arthur from DKO, adapted for 1v1.
/// </summary>
public static class KnightAbilities
{
    /// <summary>
    /// R — Knight's Resolve: parry stance. Briefly invincible.
    /// If hit during parry window, counter-attack with bonus damage.
    /// </summary>
    public static void KnightsResolve(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateCircleVisual(combat, pos, 2.5f, new Color(0.9f, 0.8f, 0.3f, 0.3f), 0.4f);

        // Parry window: 12 ticks = 0.2s
        // Check for incoming attacks by doing a proximity check
        var hits = combat.CheckMeleeCone(pos, combat.GetCameraForward(), 3f, 60f, 0f, 0f, 0f);
        if (hits.Count > 0)
        {
            // Counter-attack: bonus damage and knockback
            StatusSpells.CreateImpactVisual(combat, pos, 3f, new Color(1f, 1f, 0.5f));
            combat.CheckCircleHit(pos, 3f, 15f, 25f, 10f);
        }
    }

    /// <summary>
    /// F — Might of Excalibur (Ult): ground slam + visual.
    /// Damage handled by stage resolution; this adds the visual flare.
    /// </summary>
    public static void MightOfExcalibur(CombatComponent combat)
    {
        Vector3 pos = combat.GetOwnerPosition();
        StatusSpells.CreateCircleVisual(combat, pos, 6f, new Color(1f, 0.9f, 0.2f, 0.3f), 0.8f);
        StatusSpells.CreateImpactVisual(combat, pos, 5f, new Color(1f, 1f, 0.5f));
    }
}
