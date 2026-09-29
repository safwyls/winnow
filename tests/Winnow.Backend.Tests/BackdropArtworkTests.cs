using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class BackdropArtworkTests
{
    [Fact]
    public async Task Candidate_endpoint_preserves_grouped_choices_source_order_and_portrait_fallback_without_fetching_images()
    {
        await WithHost(async (services, client) =>
        {
            var works = services.GetRequiredService<IWorkRepository>();
            var releases = services.GetRequiredService<IReleaseRepository>();
            var parent = await works.InsertAsync(new Work { Name = "Backdrop parent", BackgroundUrl = UserArtRef.Format("legacy") });
            var child = await works.InsertAsync(new Work { Name = "Backdrop child" });
            var release = await releases.InsertAsync(new Release { WorkId = child, Name = "Child copy" });
            await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "620" });
            await services.GetRequiredService<IIdentityLinkRepository>().LinkAsync(new() { ParentWorkId = parent, ChildWorkIds = [child] });
            var choices = services.GetRequiredService<IArtworkChoiceRepository>();
            foreach (var slot in new[] { ArtworkSlot.Hero, ArtworkSlot.Cover })
                await choices.SetAsync(new ArtworkChoice { WorkId = child, Slot = slot, Kind = ArtworkChoiceKind.Manual,
                    SourceId = "user", AssetId = slot.ToString(), AssetKey = UserArtRef.Format(slot == ArtworkSlot.Hero ? "saved" : "portrait") });
            var images = services.GetRequiredService<IWorkImageRepository>();
            foreach (var (kind, id, width, height) in new[] { (ImageKinds.Artwork, "art", 6000, 1000), (ImageKinds.Screenshot, "shot", 3840, 2160) })
                await images.UpsertAsync(new WorkImages { WorkId = parent, Source = ImageSources.Igdb, Kind = kind,
                    ImageIds = id, Images = [new GameImage { ImageId = id, Width = width, Height = height }], ObservedAt = DateTime.UtcNow });
            var result = await client.GetAsync<BackdropArtwork>($"works/{parent}/backdrop?aspectRatio=1.7777777777777777");
            Assert.Equal(new[] { CoverKey.User("saved"), CoverKey.User("legacy"), CoverKey.SteamHero("620"),
                CoverKey.IgdbBackdrop("art"), CoverKey.IgdbBackdrop("shot"), CoverKey.SteamHeroStandard("620") }, result.Candidates.Select(candidate => candidate.Key));
            Assert.Equal(CoverKey.User("portrait"), result.CoverKey);
            Assert.Equal(6, result.Candidates.Single(candidate => candidate.Key.Id == "art").AspectRatio);
            Assert.Equal(16d / 9, result.Candidates.Single(candidate => candidate.Key.Id == "shot").AspectRatio);
            Assert.All(result.Candidates.Where(candidate => candidate.Key.Provider.StartsWith("steam-", StringComparison.Ordinal)), candidate => Assert.True(candidate.FitWholeHero));
            Assert.False(result.Candidates.Single(candidate => candidate.Key.Id == "art").FitWholeHero);
            await services.GetRequiredService<ArtworkPreferences>().SaveAsync(["igdb", "steam"]);
            result = await client.GetAsync<BackdropArtwork>($"works/{parent}/backdrop");
            Assert.Equal(CoverKey.IgdbBackdrop("art"), result.Candidates[2].Key);
            Assert.Equal(CoverKey.SteamHero("620"), result.Candidates[4].Key);
        });
    }

    [Fact]
    public async Task Saved_background_survives_failed_metadata_and_cancellation_is_not_swallowed()
    {
        var images = new FailingImages();
        await WithHost(async (services, client) =>
        {
            var work = await services.GetRequiredService<IWorkRepository>().InsertAsync(new Work {
                Name = "Saved image", BackgroundUrl = UserArtRef.Format("saved"), CoverUrl = UserArtRef.Format("portrait") });
            var result = await client.GetAsync<BackdropArtwork>($"works/{work}/backdrop");
            Assert.Equal(CoverKey.User("saved"), Assert.Single(result.Candidates).Key);
            Assert.Equal(CoverKey.User("portrait"), result.CoverKey);
            images.Cancel = true;
            using var cancellation = new CancellationTokenSource();
            images.Cancellation = cancellation;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<ArtworkBrowserService>()
                .GetBackdropAsync(work, 16d / 9, cancellation.Token));
        }, services => services.Replace(ServiceDescriptor.Singleton<IWorkImageRepository>(images)));
    }

    [Fact]
    public async Task Candidate_endpoint_rejects_invalid_dimensions_and_unknown_games_have_no_art()
    {
        await WithHost(async (_, client) =>
        {
            foreach (var ratio in new[] { "0", "-1", "33", "NaN", "Infinity" })
            {
                var error = await Assert.ThrowsAsync<BackendApiException>(() => client.GetAsync<BackdropArtwork>($"works/1/backdrop?aspectRatio={ratio}"));
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, error.StatusCode);
            }
            var missing = await client.GetAsync<BackdropArtwork>("works/999/backdrop");
            Assert.Empty(missing.Candidates);
            Assert.Null(missing.CoverKey);
        });
    }

    [Fact]
    public async Task Image_endpoint_supports_the_original_3840_bucket_and_rejects_larger_requests()
    {
        await WithHost(async (_, client) =>
        {
            using var bitmap = SKBitmap.Decode(await client.GetArtworkAsync("fixture", "wide", 3840));
            Assert.NotNull(bitmap);
            Assert.Equal(3840, bitmap.Width);
            var error = await Assert.ThrowsAsync<BackendApiException>(() => client.GetArtworkAsync("fixture", "wide", 3841));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, error.StatusCode);
        }, services => services.AddSingleton<ICoverSource>(new LocalImage()));
    }

    private static async Task WithHost(Func<IServiceProvider, WinnowApiClient, Task> action, Action<IServiceCollection>? configure = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backdrop-http", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"], configure);
            await host.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            await action(host.Services, client);
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class FailingImages : IWorkImageRepository
    {
        public bool Cancel { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default)
        {
            if (Cancel) { Cancellation!.Cancel(); ct.ThrowIfCancellationRequested(); }
            throw new IOException("Image metadata unavailable");
        }
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class LocalImage : ICoverSource
    {
        public string Name => "fixture";
        public bool CanHandle(CoverKey key) => key.Provider == Name;
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            using var bitmap = new SKBitmap(4000, 20);
            bitmap.Erase(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult<byte[]?>(bytes.ToArray());
        }
    }
}
