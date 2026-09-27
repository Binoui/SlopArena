namespace SlopArena.Shared;

/// <summary>Deterministic, shared initial tuning for defense actions (60 Hz simulation ticks).</summary>
public static class DefenseConfig
{
    public const ushort ShieldDropTicks = 7;
    public const ushort GrabStartupTicks = 7;
    public const ushort GrabActiveTicks = 3;
    public const ushort GrabWhiffRecoveryTicks = 18;
    public const ushort GrabClashRecoveryTicks = 10;
    public const ushort ThrowReleaseTicks = 12;
    public const ushort ThrowAttackerRecoveryTicks = 12;
    public const ushort AirDodgeInvulnerabilityTicks = 5;
    public const ushort AirDodgeMovementTicks = 10;
    public const ushort AirDodgeRecoveryTicks = 20;
}
