using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.Services;

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

/// <summary>
/// The live state <see cref="Dormancy"/> resolves through: whether covers are
/// dimmed at all (§8's "settings toggle to disable the dormancy ramp entirely
/// for users who prefer uniform art"), and whether the hover restore animates.
///
/// <para>This is a valve on the ramp, not a bypass of it. <see cref="Dormancy"/>
/// still computes the whole §5.1 curve; when <see cref="DimsDormantCovers"/> is
/// off this resolves it to 1.0, which lands the two-layer cross-fade on the
/// vivid layer at full opacity. Nothing about the cover cache changes: the
/// pre-computed floor variants stay on disk, stay loaded under the vivid layer,
/// and stay valid, so turning dimming back on is one property write and the next
/// paint — never a reload and never a re-render of the cache.</para>
///
/// <para>With dimming off the hover restore is a no-op by construction rather
/// than by a special case: the resting alpha and the hovered alpha are both 1.0,
/// so there is no value for the 140ms transition to travel.</para>
/// </summary>
public partial class DormancyRamp : ObservableObject
{
    /// <summary>Settings key the preference persists under.</summary>
    public const string DimCoversSettingKey = "display.dim_dormant_covers";

    private static readonly bool SystemPrefersReducedMotion = ProbeReducedMotion();

    /// <summary>
    /// On by default: the ramp is §1's thesis, and a user who wants uniform art
    /// has to say so. §8 keeps the encoding decorative-redundant either way —
    /// idle time is text on hover and a sortable column, and the unread badge is
    /// backed by the rail count.
    /// </summary>
    [ObservableProperty]
    public partial bool DimsDormantCovers { get; set; } = true;

    /// <summary>
    /// §8: reduced motion snaps the hover restore instead of fading it. Seeded
    /// from the OS ("Show animations in Windows"), and independent of
    /// <see cref="DimsDormantCovers"/> — with dimming off there is no value
    /// changing, so the two settings compose rather than contradict.
    /// </summary>
    [ObservableProperty]
    public partial bool ReducedMotion { get; set; } = SystemPrefersReducedMotion;

    /// <summary>
    /// Resting vivid-layer opacity for a tile: the §5.1 ramp value, or 1.0 when
    /// the user has turned dimming off.
    /// </summary>
    public double VividAlphaFor(DateTime? lastPlayedUtc, DateTime nowUtc)
        => DimsDormantCovers ? Dormancy.VividAlphaFor(lastPlayedUtc, nowUtc) : 1.0;

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(
        uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    /// <summary>
    /// Avalonia 11 surfaces no reduced-motion setting, so this reads Windows'
    /// own (SPI_GETCLIENTAREAANIMATION). Anything that fails or is not Windows
    /// answers "animate" — the accessibility floor is a preference to honour
    /// when it is stated, never a default to guess at.
    /// </summary>
    private static bool ProbeReducedMotion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var animationsEnabled = 1;
            return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref animationsEnabled, 0)
                && animationsEnabled == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
