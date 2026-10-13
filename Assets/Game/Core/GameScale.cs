namespace LocalTanks
{
    /// <summary>
    /// The single coefficient that converts World of Tanks metres into game units (ADR-064).
    ///
    /// The world is already built in this scale: the converted arena is imported 1:1 in it, the fleet is
    /// scaled from real hull lengths in it, and every measured distance on a map is expressed in it. That is
    /// why the coefficient is not a display detail: `WoTMapAsset.metresPerUnit` divides imported sizes by it,
    /// so declaring a different number would silently resize the arena instead of relabelling it.
    ///
    /// Everything that shows a distance to a player or converts a client value uses these two methods. A
    /// second divisor anywhere — in the editor, in the pipeline or in the interface — is a defect, and
    /// <c>GameScaleTests</c> fails when the pipeline configuration disagrees with this constant.
    /// </summary>
    public static class GameScale
    {
        /// <summary>Metres in one game unit. Equal to the scale the arena and the fleet are built in.</summary>
        public const float MetresPerUnit = 3.835f;

        public static float MetresToUnits(float metres) => metres / MetresPerUnit;

        public static float UnitsToMetres(float units) => units * MetresPerUnit;
    }
}
