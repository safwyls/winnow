using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Winnow.Api.Client;
using Winnow.Covers;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class ArtworkTests
{
    [Fact]
    public async Task MetadataArtworkUploadAcceptsTheDocumentedSixteenMiBLimitBeforeImageValidation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-artwork-limit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            var work = await host.Services.GetRequiredService<Winnow.Core.Repositories.IWorkRepository>()
                .InsertAsync(new Winnow.Core.Domain.Work { Name = "Large artwork" });
            await host.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            var metadata = await client.GetAsync<Winnow.Api.Contracts.Details.MetadataResponse>($"games/{work}/metadata");
            // Base64 makes an allowed 16 MiB image larger than the ordinary JSON limit.
            // Invalid image content should reach domain validation, not fail at HTTP 413.
            var result = await client.SendAsync<Winnow.Api.Contracts.Details.UploadMetadataArtRequest,
                Winnow.Api.Contracts.Details.MutationOutcome>(HttpMethod.Post, $"games/{work}/metadata/art-upload",
                new("cover_url", new byte[16 * 1024 * 1024], metadata.Revision));
            Assert.NotEqual("Applied", result.Outcome);
            Assert.NotEqual("TooLarge", result.Outcome);
            var unchanged = await client.GetAsync<Winnow.Api.Contracts.Details.MetadataResponse>($"games/{work}/metadata");
            Assert.Equal(metadata.Revision, unchanged.Revision);
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UploadedArtworkIsVisibleToAnotherClientAndStaleResetIsRefused()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-artwork-edit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            var work = await host.Services.GetRequiredService<Winnow.Core.Repositories.IWorkRepository>()
                .InsertAsync(new Winnow.Core.Domain.Work { Name = "Artwork game" });
            await host.StartAsync();
            using var first = WinnowApiClient.Attach(directory);
            using var second = WinnowApiClient.Attach(directory);
            var firstBrowser = new ApiArtworkBrowserService(first);
            var secondBrowser = new ApiArtworkBrowserService(second);
            var slot = Winnow.Core.Domain.ArtworkSlot.Icon;
            Assert.Null(await firstBrowser.GetCurrentAsync(work, slot));
            Assert.Null(await secondBrowser.GetCurrentAsync(work, slot));
            var original = await second.GetAsync<Winnow.App.Services.ArtworkState>($"works/{work}/artwork/{slot}");
            var file = Path.Combine(directory, "selected.png");
            var source = new Source();
            await File.WriteAllBytesAsync(file, (await source.TryFetchAsync(new("test", "one")))!);
            Assert.True((await firstBrowser.ImportFileAsync(work, slot, file)).Success);
            var conflict = await Assert.ThrowsAsync<BackendApiException>(() => second.SendAsync<
                Winnow.App.Services.ArtworkResetRequest, Winnow.App.Services.ArtworkSaveResult>(HttpMethod.Post,
                $"works/{work}/artwork/{slot}/reset", new(original.Revision)));
            Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
            var rejected = await secondBrowser.ResetAsync(work, slot);
            Assert.False(rejected.Success);
            Assert.Contains("Reopen", rejected.Message);
            var current = await secondBrowser.GetCurrentAsync(work, slot);
            Assert.NotNull(current);
            Assert.Equal("user", current.SourceId);
            Assert.True((await secondBrowser.ResetAsync(work, slot)).Success);
            Assert.Null(await firstBrowser.GetCurrentAsync(work, slot));
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AuthenticatedArtworkIsSharedAndRejectsUnboundedOrPathRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = new Source();
            await using var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton<ICoverSource>(source));
            await host.StartAsync();
            using var first = WinnowApiClient.Attach(directory);
            using var second = WinnowApiClient.Attach(directory);
            var bytes = await first.GetArtworkAsync("test", "one", 128);
            Assert.NotNull(bytes);
            using var bitmap = SKBitmap.Decode(bytes);
            Assert.NotNull(bitmap);
            Assert.Equal(bytes, await second.GetArtworkAsync("test", "one", 128));
            Assert.Equal(1, source.Fetches);
            var size = await Assert.ThrowsAsync<BackendApiException>(() => first.GetArtworkAsync("test", "one", 100000));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, size.StatusCode);
            var path = await Assert.ThrowsAsync<BackendApiException>(() => first.GetArtworkAsync("test", "folder/one"));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, path.StatusCode);
            Assert.Null(await first.GetArtworkAsync("unknown", "one"));
            await host.StopAsync();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BackendDependencyGraphContainsNoAvaloniaAssembly()
    {
        var visited = new HashSet<string>();
        var pending = new Queue<System.Reflection.Assembly>();
        pending.Enqueue(typeof(BackendApplication).Assembly);
        while (pending.TryDequeue(out var assembly))
        {
            if (!visited.Add(assembly.GetName().Name!)) continue;
            foreach (var dependency in assembly.GetReferencedAssemblies())
            {
                Assert.False(dependency.Name!.StartsWith("Avalonia", StringComparison.Ordinal),
                    $"{assembly.GetName().Name} references {dependency.Name}");
                if (dependency.Name.StartsWith("Winnow", StringComparison.Ordinal))
                    pending.Enqueue(System.Reflection.Assembly.Load(dependency));
            }
        }
    }

    private sealed class Source : ICoverSource
    {
        public string Name => "test";
        public int Fetches { get; private set; }
        public bool CanHandle(CoverKey key) => key.Provider == "test";
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            Fetches++;
            using var bitmap = new SKBitmap(128, 192);
            bitmap.Erase(SKColors.DarkCyan);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult<byte[]?>(encoded.ToArray());
        }
    }
}
