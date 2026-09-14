using Winnow.PluginSdk;

namespace Example.Installed;

public sealed class Provider : IRecommendationFeedPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(
        IReadOnlyList<PluginGame> library, CancellationToken cancellationToken = default)
    {
        var results = new List<PluginRecommendation>();
        foreach (var game in library)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (game.Installed && game.PlaytimeMinutes == 0 && game.LastPlayedAt is null)
                results.Add(new(game.Id, 0.8, "Installed and ready, with no recorded playtime."));
        }
        return Task.FromResult<IReadOnlyList<PluginRecommendation>?>(results);
    }
}
