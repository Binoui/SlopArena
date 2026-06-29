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
        JumpSquat = 6
    }
}
