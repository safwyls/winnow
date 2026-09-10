using System.Globalization;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>One persisted cursor, written before ingest can make a new library look established.</summary>
public sealed class FirstRunSetupService(ISettingsRepository? settings = null)
{
    public const string ProgressKey = "setup.progress.v1";
    private string _fallback = "done";
    private bool _suppressAutomaticSetup;
    public string? StartupProblem { get; private set; }

    public async Task InitializeAsync(bool databaseAlreadyExisted, bool suppressAutomaticSetup,
        CancellationToken ct = default)
    {
        _suppressAutomaticSetup = suppressAutomaticSetup;
        _fallback = databaseAlreadyExisted || suppressAutomaticSetup ? "done" : "0";
        if (settings is null) return;
        try
        {
            var stored = await settings.GetAsync(ProgressKey, ct);
            if (stored is null) await settings.SetAsync(ProgressKey, _fallback, ct);
            else _fallback = stored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            StartupProblem = "Setup progress could not be saved. You can try again with Continue or Skip setup.";
        }
    }

    /// <returns>The step cursor, or null when setup is complete.</returns>
    public async Task<int?> LoadAsync(CancellationToken ct = default)
    {
        if (_suppressAutomaticSetup) return null;
        var value = settings is null ? _fallback : await settings.GetAsync(ProgressKey, ct) ?? _fallback;
        if (value == "done") return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index is >= 0 and <= 8 ? index : 0;
    }

    public async Task SaveAsync(int? step, CancellationToken ct = default)
    {
        if (step is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(step));
        var value = step?.ToString(CultureInfo.InvariantCulture) ?? "done";
        if (settings is not null) await settings.SetAsync(ProgressKey, value, ct);
        _fallback = value;
        StartupProblem = null;
    }
}
