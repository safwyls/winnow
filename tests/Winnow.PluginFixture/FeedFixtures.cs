using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

/// <summary>Canned feed behavior controlled through the same scoped settings a loaded plugin receives.</summary>
public sealed class ConfigurableFeedPlugin : IRecommendationFeedPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private IPluginContext? _context;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(IReadOnlyList<PluginGame> library,
        CancellationToken cancellationToken = default)
    {
        var settings = _context!.Settings;
        await settings.SetAsync("supplied-games", JsonSerializer.Serialize(library, JsonOptions), cancellationToken);
        var behavior = await settings.GetAsync("behavior", cancellationToken);
        if (behavior == "throw")
            throw new InvalidOperationException("fixture-private-key-must-not-escape");
        if (behavior is "wait" or "wait-ignore-cancellation")
            await settings.GetAsync("gate", behavior == "wait" ? cancellationToken : CancellationToken.None);
        var canned = await settings.GetAsync("recommendations", cancellationToken);
        return canned is not null ? JsonSerializer.Deserialize<PluginRecommendation[]>(canned, JsonOptions)
            : library.Select(game => new PluginRecommendation(game.Id, 0.8, "A fixture recommendation for " + game.Id + ".")).ToArray();
    }
}

public sealed class ImmediateFeedPlugin : IRecommendationFeedPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(IReadOnlyList<PluginGame> library,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginRecommendation>?>(library.Select(game =>
            new PluginRecommendation(game.Id, 0.7, "An immediate recommendation for " + game.Id + ".")).ToArray());
}
