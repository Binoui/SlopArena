namespace SlopArena.Shared
{
    public enum ActionState : byte
    {
        Idle = 0,
        Dashing = 1,
        Hitstun = 2,
        Sliding = 3,
        Attacking = 4,
        AirDodging = 5,
        JumpSquat = 6,
        Warping = 7,
        /// <summary>Hold-to-aim phase (Kistu E): ability active, movement unlocked, jump/dash blocked.</summary>
        Aiming = 8,
        Run = 9,
        LedgeHang = 10,
        Crouching = 11,
        // Defense states; appended to preserve all existing wire values.
        Shielding = 12,
        ShieldDrop = 13,
        GrabAttempt = 14,
        Grabbed = 15,
        Throwing = 16,
        AirDodgeStartup = 17,
        AirDodgeMovement = 18,
        AirDodgeRecovery = 19
    }
}
