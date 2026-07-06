namespace SlopArena.Client.Camera
{
    /// <summary>
    /// Result produced each FixedUpdate tick by an aim handler.
    /// Null fields mean "not set by this handler"; TrainingMatch passes them as null to BuildInputState.
    /// </summary>
    public struct AimContext
    {
        public float? AimYawRad;
        public float? AimPitchRad;
        public ushort? AimDistanceCm;
        public bool IsAiming;
    }
}
