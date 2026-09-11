namespace Winnow.Covers;

/// <summary>Shared transform endpoint for stored artwork, procedural art and the Avalonia token resources.</summary>
public static class DormancyStyle
{
    public const double SaturationFloor = 0.22;

    // Dark Steam capsules lost recognizability at the old gradient-derived
    // 0.60 floor. Saturation carries dormancy; brightness preserves the title.
    public const double BrightnessFloor = 0.68;
    public const double HueDegrees = -6;
}
