using System;

namespace MoveBox.Shared
{
    public readonly struct MovementProfile
    {
        public string Name { get; init; }
        public float MaxSpeed { get; init; }
        public float BackwardMaxSpeed { get; init; }
        public float Acceleration { get; init; }
        public float AirAcceleration { get; init; }
        public float DragWhenStopped { get; init; }
        public float DragWhenMoving { get; init; }
        public float LandingSpeedRetain { get; init; }
        public float DashSpeed { get; init; }
        public bool EnableMomentumSlide { get; init; }
        public float SlideMomentumDrag { get; init; }
        public float SlideNormalDrag { get; init; }
        public float SlideMomentumMinSpeed { get; init; }
        public float SlideMaxSpeed { get; init; }
        public ushort AttackDurationTicks { get; init; }
        public ushort PostAttackSlideLockoutTicks { get; init; }
    }

    /// <summary>
    /// Single movement profile for the wizard arena brawler.
    /// </summary>
    public static class MovementProfiles
    {
        public static MovementProfile Active { get; private set; }
        
        static MovementProfiles()
        {
            Active = Default;
        }

        public static readonly MovementProfile Default = new MovementProfile
        {
            Name = "Wizard",
            MaxSpeed = 280f,
            BackwardMaxSpeed = 220f,
            Acceleration = 7500f,
            AirAcceleration = 2000f,
            DragWhenStopped = 14f,
            DragWhenMoving = 4.5f,
            LandingSpeedRetain = 0.85f,
            DashSpeed = 900f,
            EnableMomentumSlide = true,
            SlideMomentumDrag = 0.08f,
            SlideNormalDrag = 3.5f,
            SlideMomentumMinSpeed = 250f,
            SlideMaxSpeed = 1100f,
            AttackDurationTicks = 14,
            PostAttackSlideLockoutTicks = 20,
        };

        public static void SetActive(int profileId)
        {
            // Only one profile for now
            Active = Default;
        }

        public static void ApplyFromActionFlags(byte actionFlags)
        {
            // No profile switching via flags anymore
        }

        public static byte EncodeProfileBits(int profileId)
        {
            return 0;
        }
    }
}
