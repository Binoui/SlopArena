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
        Aiming = 8
    }
}
