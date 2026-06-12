namespace SlopArena.Shared
{
    /// <summary>
    /// Defines a hurtbox sphere attached to a skeleton bone.
    /// The server samples the animation, gets the bone world position,
    /// adds the offset, and creates a sphere hurtbox.
    /// </summary>
    public struct HurtboxBoneDef
    {
        /// <summary>Bone name in the skeleton (e.g. "mixamorig:Spine2", "mixamorig:Head").</summary>
        public string BoneName;
        /// <summary>Local offset from the bone origin (in bone-local space).</summary>
        public float OffX, OffY, OffZ;
        /// <summary>Sphere radius.</summary>
        public float Radius;

        public HurtboxBoneDef(string bone, float ox, float oy, float oz, float r)
        {
            BoneName = bone;
            OffX = ox; OffY = oy; OffZ = oz;
            Radius = r;
        }
    }
}
