namespace SlopArena.Shared
{
    /// <summary>
    /// A single hurtbox capsule defined in the character's local space.
    /// The server computes world positions from CharacterState + these offsets.
    /// No Godot dependency — pure C# for both client and server.
    /// CenterX/Y/Z are relative to the character's center (Hips).
    /// When Sx==Ex, the capsule degenerates to a sphere.
    /// </summary>
    public struct HurtboxCapsule
    {
        /// <summary>Capsule start offset (local space, meters)</summary>
        public float Sx, Sy, Sz;
        /// <summary>Capsule end offset (local space, meters)</summary>
        public float Ex, Ey, Ez;
        public float Radius;

        public HurtboxCapsule(float sx, float sy, float sz, float ex, float ey, float ez, float radius)
        {
            Sx = sx; Sy = sy; Sz = sz;
            Ex = ex; Ey = ey; Ez = ez;
            Radius = radius;
        }
    }
}
