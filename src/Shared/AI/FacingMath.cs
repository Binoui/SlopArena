using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Horizontal facing-frame math shared by the bot policy, the match recorder's whiff-spot
/// normalization, and the tool's reach-envelope rendering. Kept here (Shared, not the tool)
/// so the recorder and the envelope use the identical rotation convention.
/// </summary>
public static class FacingMath
{
    /// <summary>
    /// Rotate a world-space horizontal delta into an entity's facing frame.
    /// Returns (side, forward): <c>side</c> along +X (the character's right at yaw 0),
    /// <c>forward</c> along +Z (the character's facing at yaw 0).
    /// This is the inverse of the sim's hitbox offset rotation
    /// <c>hx = OffX·cos − OffZ·sin, hz = OffX·sin + OffZ·cos</c>.
    /// </summary>
    public static (float Side, float Forward) ToFacingFrame(float dx, float dz, float facingYaw)
    {
        float cos = MathF.Cos(facingYaw);
        float sin = MathF.Sin(facingYaw);
        return (dx * cos + dz * sin, -dx * sin + dz * cos);
    }
}
