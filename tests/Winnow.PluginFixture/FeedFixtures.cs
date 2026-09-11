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
        if (await settings.GetAsync("behavior", cancellationToken) == "throw")
            throw new InvalidOperationException("fixture-private-key-must-not-escape");
        var canned = await settings.GetAsync("recommendations", cancellationToken);
        return canned is not null ? JsonSerializer.Deserialize<PluginRecommendation[]>(canned, JsonOptions)
            : library.Select(game => new PluginRecommendation(game.Id, 0.8, "A fixture recommendation for " + game.Id + ".")).ToArray();
    }
}
