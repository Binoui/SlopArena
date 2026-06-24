namespace SlopArena.Shared
{
    public enum ActionState : byte
    {
        Idle = 0,
        Dashing = 1,
        Hitstun = 2,
        Sliding = 3,
        Attacking = 4,
        AirDodging = 5
        // Warping = 6 removed: warp is now a velocity override (WarpSpeed > 0), not a state
    }
}
