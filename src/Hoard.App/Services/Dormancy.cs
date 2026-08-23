namespace Hoard.App.Services;

/// <summary>
/// The §5.1 dormancy ramp: months since last played → saturation, clamped at
/// the 0.22 / 0.68 floor (a cover you can't identify is a cover you can't
/// choose). Rendered via the two-layer cross-fade settled in
/// docs/spikes/avalonia-dormancy-rendering.md: a pre-computed "floor" variant
/// sits under the vivid art, and the vivid layer's opacity is
/// α = (S − 0.22) / 0.78. Brightness then tracks the §5.1 table within 0.04.
/// </summary>
public static class Dormancy
{
    public const double SatFloor = 0.22;

    /// <summary>
    /// Brightness floor. Raised from the originally documented 0.60 after the
    /// ramp was first seen on real cover art: 0.60 was calibrated against
    /// procedural gradients, and it compounds with Steam capsules that are
    /// already dark. In a library whose default sort opens on its most dormant
    /// titles, that spent the ramp's dynamic range before the first scroll.
    /// Saturation, not brightness, is what carries the "dormant" signal.
    /// </summary>
    public const double BrightFloor = 0.68;

    private const double DaysPerMonth = 30.4375;

    // §5.1 ramp table, interpolated piecewise-linearly between rows.
    private static readonly (double Months, double Saturation)[] Ramp =
    [
        (0, 1.00),
        (6, 0.72),
        (12, 0.50),
        (24, 0.34),
        (36, SatFloor),
    ];

    /// <summary>Saturation for a last-played instant; null = never played = floor.</summary>
    public static double SaturationFor(DateTime? lastPlayedUtc, DateTime nowUtc)
    {
        if (lastPlayedUtc is null)
        {
            return SatFloor;
        }

        var months = Math.Max(0, (nowUtc - lastPlayedUtc.Value).TotalDays / DaysPerMonth);
        for (var i = 1; i < Ramp.Length; i++)
        {
            var (m1, s1) = Ramp[i];
            if (months <= m1)
            {
                var (m0, s0) = Ramp[i - 1];
                var t = (months - m0) / (m1 - m0);
                return s0 + (s1 - s0) * t;
            }
        }

        return SatFloor;
    }

    /// <summary>
    /// Vivid-layer opacity for the cross-fade: α = (S − 0.22) / 0.78.
    /// 1.0 = fully vivid, 0.0 = floor variant only.
    /// </summary>
    public static double VividAlphaFor(DateTime? lastPlayedUtc, DateTime nowUtc)
        => (SaturationFor(lastPlayedUtc, nowUtc) - SatFloor) / (1.0 - SatFloor);
}
