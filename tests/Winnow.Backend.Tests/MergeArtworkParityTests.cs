using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class MergeArtworkParityTests
{
    [Theory]
    [InlineData("user", false, "user", "abc123")]
    [InlineData("igdb", true, "igdb", "co2abc")]
    [InlineData("igdb", false, "steam", "42")]
    public async Task Queue_artwork_state_preserves_user_pin_and_store_precedence(
        string art, bool pinned, string expectedProvider, string expectedId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-merge-art-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            var works = host.Services.GetRequiredService<IWorkRepository>();
            var releases = host.Services.GetRequiredService<IReleaseRepository>();
            var url = art == "user" ? UserArtRef.Format("abc123")
                : "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg";
            var work = await works.InsertAsync(new Work { Name = "Prey", CoverUrl = url });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Prey" });
            await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "42" });
            if (pinned)
                Assert.Equal(WorkIgdbPinOutcome.Pinned, await host.Services.GetRequiredService<IWorkIgdbPinRepository>()
                    .PinAsync(new WorkIgdbPinAssignment { WorkId = work, IgdbId = 999, CoverUrl = url }));
            var other = await works.InsertAsync(new Work { Name = "Prey (other edition)" });
            var otherRelease = await releases.InsertAsync(new Release { WorkId = other, Name = "Prey (other edition)" });
            await releases.AddExternalIdAsync(new ExternalId { ReleaseId = otherRelease, Provider = "steam", ProviderId = "43" });

            await host.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            var selected = await client.GetAsync<ArtworkState>($"works/{work}/artwork/Cover");
            Assert.NotNull(selected.Current);
            Assert.Equal(new CoverKey(expectedProvider, expectedId), selected.Current.PreviewKey);
            var unchanged = await client.GetAsync<ArtworkState>($"works/{other}/artwork/Cover");
            Assert.NotNull(unchanged.Current);
            Assert.Equal(CoverKey.Steam("43"), unchanged.Current.PreviewKey);
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
