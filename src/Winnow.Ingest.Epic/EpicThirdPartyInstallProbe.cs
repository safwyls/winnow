using Microsoft.Win32;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Install state for one title, as this reader is able to observe it.
/// <see cref="Installed"/> follows <c>CandidateOwnership.Installed</c>'s
/// three-valued contract exactly: <c>null</c> means <i>this source cannot know</i>,
/// which is a different statement from <c>false</c>.
/// </summary>
/// <param name="Installed">true/false when the probe reached an answer; null when it could not look.</param>
/// <param name="InstallPath">Install directory when <paramref name="Installed"/> is true.</param>
public readonly record struct EpicInstallState(bool? Installed, string? InstallPath)
{
    /// <summary>The "cannot know" answer — neither installed nor uninstalled.</summary>
    public static readonly EpicInstallState Unknown = new(null, null);

    /// <summary>A positive observation with a path.</summary>
    public static EpicInstallState At(string path) => new(true, path);

    /// <summary>A negative observation: this reader looked where the answer lives and it was not there.</summary>
    public static readonly EpicInstallState NotInstalled = new(false, null);
}

/// <summary>
/// Resolves the install state of an Epic-owned title that is delivered through a
/// different launcher (docs/spikes/epic-gog-local-files.md section 7). These
/// titles have no <c>.item</c> manifest, so the manifests directory says nothing
/// about them — and "no manifest" must therefore <b>not</b> be read as "not
/// installed" for them.
/// </summary>
public interface IEpicThirdPartyInstallProbe
{
    /// <summary>
    /// Looks up the delivering launcher's own install record. Returns
    /// <see cref="EpicInstallState.Unknown"/> when there is no pointer to follow
    /// or the lookup is not possible on this platform.
    /// </summary>
    /// <param name="registryPath">HKLM-relative key path from the third-party JSON.</param>
    /// <param name="registryValueName">Value name under that key holding the install directory.</param>
    EpicInstallState Probe(string registryPath, string registryValueName);
}

/// <summary>
/// Reads the delivering launcher's HKLM install key, read-only. The paths Epic
/// records already contain <c>WOW6432Node</c>, so they are used verbatim rather
/// than through a 32-bit registry view.
///
/// <para><b>Why a missing key is a real <c>false</c> and not a shrug.</b> Epic
/// names the exact key that holds the install directory. If the key or value is
/// absent, the delivering launcher has no install record for the title, which is
/// the same kind of observation the Steam reader makes when it walks every
/// library root and finds no appmanifest. What is <i>not</i> an answer is being
/// unable to look at all — off Windows, without a pointer, or when the read
/// throws — and those cases return <see cref="EpicInstallState.Unknown"/>.</para>
/// </summary>
public sealed class WindowsEpicThirdPartyInstallProbe : IEpicThirdPartyInstallProbe
{
    /// <inheritdoc/>
    public EpicInstallState Probe(string registryPath, string registryValueName)
    {
        if (!OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(registryPath)
            || string.IsNullOrWhiteSpace(registryValueName))
        {
            return EpicInstallState.Unknown;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(registryPath);
            if (key is null)
            {
                // The delivering launcher has no record of this title.
                return EpicInstallState.NotInstalled;
            }

            if (key.GetValue(registryValueName) is not string path || string.IsNullOrWhiteSpace(path))
            {
                return EpicInstallState.NotInstalled;
            }

            return Directory.Exists(path)
                ? EpicInstallState.At(Path.TrimEndingDirectorySeparator(path))
                : EpicInstallState.NotInstalled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            // Could not look. Not the same as looking and finding nothing.
            return EpicInstallState.Unknown;
        }
    }
}
