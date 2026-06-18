/// <summary>
/// Return value from Ability.Tick().
/// Fields are optional — null means "no change" for that value.
/// </summary>
public struct AbilityInputState
{
    /// <summary>If set, overrides the AimYaw sent to the sim this frame.</summary>
    public float? AimYaw;
    /// <summary>If set, sets AimDistance (cm) sent to the sim this frame.</summary>
    public ushort? AimDistance;

    /// <summary>
    /// If set, fires this ability slot on the sim (1-6).
    /// Typically set on release for held abilities, or immediately for instant ones.
    /// </summary>
    public byte? ActiveSlot;
}
