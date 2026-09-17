using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

public sealed class GameActionsFixture : ILibrarySourcePlugin, IPluginGameActions
{
    private IPluginContext _context = null!;
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    { _context = context; return ValueTask.CompletedTask; }

    public async Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        var state = await _context.Settings.GetAsync("state", cancellationToken);
        if (state == "unavailable") return null;
        return [new("stable-package", state == "provisional" ? "package.identifier" : "Xbox fixture")
        {
            TitleIsProvisional = state == "provisional",
            Installed = state != "removed", InstallPath = state != "removed" ? Path.GetTempPath() : null,
            LibrarySourceLabel = "Played history — not proof of ownership",
            LastPlayedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            Actions = state != "removed" ? [PluginGameActionKind.Play, PluginGameActionKind.OpenStore] : [PluginGameActionKind.OpenStore],
        }];
    }

    public async Task<PluginGameActionResult> ExecuteGameActionAsync(string sourceId, PluginGameActionKind action,
        CancellationToken cancellationToken = default)
    {
        await _context.Settings.SetAsync("last-action", sourceId + ":" + action, cancellationToken);
        return new(sourceId == "stable-package");
    }
}
