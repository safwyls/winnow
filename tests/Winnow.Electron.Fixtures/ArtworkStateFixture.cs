using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb.Credentials;

namespace Winnow.Electron.Fixtures;

internal sealed record ArtworkStateSeed(string Kind);
internal sealed record ArtworkStateChange(string Stage);

internal sealed class ArtworkStateFixture(ISqliteConnectionFactory database, IIdentityLinkRepository links,
    IGroupHeaderPreferenceRepository headers, IWorkIgdbPinRepository pins, IWorkImageRepository images,
    IArtworkChoiceRepository choices, ArtworkPreferences preferences, LibraryChangePublisher publisher,
    PreviewResponseControls responses, ArtworkStateSource source, UserArtStore userArt)
{
    public const string GridUrl = "https://cdn2.steamgriddb.com/hero/61ba87bf4177f576150389d84d14bb01.png";
    public static readonly DateTime Now = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
    public string Kind { get; private set; } = "startup";
    public string Stage { get; private set; } = "startup";
    public bool Ready { get; private set; }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ArtworkStateFixture>();
        services.AddSingleton<ArtworkStateSource>();
        services.AddSingleton<PreviewResponseControls>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, ArtworkStateNoCredentials>();
        services.RemoveAll<ICoverSource>();
        services.AddSingleton<ICoverSource>(provider => provider.GetRequiredService<ArtworkStateSource>());
        services.AddSingleton<ICoverSource, UserArtCoverSource>();
    }

    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var fixture = context.RequestServices.GetRequiredService<ArtworkStateFixture>();
            var controls = context.RequestServices.GetRequiredService<PreviewResponseControls>();
            var call = controls.Begin(context, fixture.Ready);
            if (call is null) { await next(context); return; }
            var body = context.Response.Body;
            await using var captured = new MemoryStream();
            context.Response.Body = captured;
            try
            {
                await next(context);
                var bytes = captured.ToArray();
                call.StatusCode = context.Response.StatusCode;
                call.ResponseBytes = bytes.Length;
                call.ResponseSha256 = Convert.ToHexString(SHA256.HashData(bytes));
                call.Json = context.Response.ContentType?.Contains("json", StringComparison.Ordinal) == true ? Encoding.UTF8.GetString(bytes) : null;
                call.ResponseReady = true;
                await controls.WaitAsync(call, context.RequestAborted);
                context.Response.Body = body;
                await body.WriteAsync(bytes, context.RequestAborted);
                call.Delivered = true;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { context.Response.Body = body; call.Completed = true; }
        });
        const string prefix = "/__fixture/artwork-state";
        app.MapPost(prefix + "/seed", (ArtworkStateSeed input, ArtworkStateFixture fixture) => fixture.SeedAsync(input));
        app.MapGet(prefix + "/state", (ArtworkStateFixture fixture) => fixture.StateAsync());
        app.MapPost(prefix + "/change", (ArtworkStateChange input, ArtworkStateFixture fixture) => fixture.ChangeAsync(input.Stage));
        app.MapPost(prefix + "/arm", (PreviewArm input, PreviewResponseControls controls) => controls.Arm(input));
        app.MapPost(prefix + "/release", (PreviewRelease input, PreviewResponseControls controls) =>
        { controls.Release(input.GateId); return Results.NoContent(); });
    }

    public async Task<object> SeedAsync(ArtworkStateSeed input)
    {
        if (input.Kind is not ("browser" or "live" or "root" or "grouped")) throw new ArgumentException("Unknown artwork state fixture.");
        using (var connection = database.Open())
        {
            if (Ready || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0) throw new InvalidOperationException("Seed a fresh fixture only.");
            Kind = input.Kind;
            Stage = Kind == "browser" ? "preferred" : "initial";
            source.MissingSteamHero = Kind == "root";
            if (Kind == "browser")
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name,cover_url) VALUES(1,'Game','Game','https://images.igdb.com/igdb/image/upload/t_cover_big/manual.jpg'),(2,'Game','Game',NULL);
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Game','windows'),(2,2,'Game','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'epic',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','620');
                    """);
            else if (Kind == "root")
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name,background_url) VALUES(1,'Group root','Group root','winnow://user-art/rootsaved'),(2,'Owned Steam copy','Owned Steam copy','winnow://user-art/childsaved');
                    INSERT INTO releases(id,work_id,name,platform) VALUES(2,2,'Owned Steam copy','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(2,2,'steam',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(2,'steam','42');
                    """);
            else if (Kind == "grouped")
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Fixture','Fixture');
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Fixture','windows'),(2,1,'Fixture','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'gog',1),(2,2,'steam',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(2,'steam','42');
                    """);
            else
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Fixture','Fixture');
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Fixture','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','42');
                    """);
            connection.Execute("INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true')");
        }
        // Keep the source fixture's legacy SteamGridDB block available; bytes remain local.
        preferences.ConfigureSources([new(ArtworkPreferences.SteamGridDb, "SteamGridDB")]);
        await preferences.LoadAsync();
        if (Kind is "browser" or "root")
            await links.LinkAsync(new() { ParentWorkId = 1, ChildWorkIds = [2] });
        if (Kind == "browser")
        {
            await PinAsync("preferred", 1234);
            if (!await headers.SetAsync(1, "epic")) throw new InvalidOperationException("Header fixture was not a current group.");
        }
        if (Kind == "live") await images.UpsertAsync(new()
        {
            WorkId = 1, Source = "igdb", Kind = "artwork", ImageIds = "art", ObservedAt = Now,
            Images = [new() { ImageId = "art", Width = 3840, Height = 2160 }],
        });
        if (Kind == "root") await images.UpsertAsync(new()
        {
            WorkId = 2, Source = "steamgriddb", Kind = "artwork", ImageIds = "50584", ObservedAt = Now,
            Images = [new() { ImageId = "50584", Url = GridUrl, Width = 1920, Height = 620 }],
        });
        Ready = true;
        await publisher.PublishAsync(default);
        return await StateAsync();
    }

    public async Task<object> ChangeAsync(string stage)
    {
        if (Kind != "browser" || stage is not ("newprojection" or "steam440")) throw new ArgumentException("Unknown projection stage.");
        if (stage == "newprojection") await PinAsync(stage, 5678);
        else
        {
            await pins.ClearAsync(2);
            using var connection = database.Open();
            connection.Execute("UPDATE external_ids SET provider_id='440' WHERE provider='steam' AND release_id=1");
        }
        Stage = stage;
        await publisher.PublishAsync(default);
        return await StateAsync();
    }

    private async Task PinAsync(string image, long id)
    {
        var outcome = await pins.PinAsync(new() { WorkId = 2, IgdbId = id, Name = "Game", CoverUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{image}.jpg" });
        if (outcome != WorkIgdbPinOutcome.Pinned) throw new InvalidOperationException("Fixture pin was refused.");
    }

    public async Task<object> StateAsync()
    {
        var selected = await choices.GetAllAsync();
        using var connection = database.Open();
        return new { Kind, Stage, Ready, WorkId = 1, HeaderWorkId = Kind == "browser" ? 2 : 1,
            ProcessId = Environment.ProcessId, Calls = responses.Calls, SourceCalls = source.Calls,
            Choices = selected, SourceOrder = preferences.SourceOrder,
            StoredSourceOrder = connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key='enrichment.artwork_source_order'"),
            Retained = selected.Select(choice => new { choice.WorkId, choice.AssetId,
                Exists = UserArtRef.Token(choice.AssetKey) is { } token && File.Exists(userArt.PathFor(token)) }).ToArray() };
    }
}

internal sealed class ArtworkStateSource : ICoverSource
{
    public sealed record Call(string Provider, string Id, bool Available, int Bytes);
    private readonly ConcurrentQueue<Call> _calls = new();
    public IReadOnlyList<Call> Calls => _calls.ToArray();
    public bool MissingSteamHero { get; set; }
    public string Name => "artwork-state-fixture";
    public bool CanHandle(CoverKey key) => key.Provider is "steam" or "steam-hero" or "steam-hero-standard" or "igdb" or "igdb-backdrop" or "steamgriddb-hero"
        or SteamBrowserArtworkSource.CoverProvider or SteamBrowserArtworkSource.HeroProvider or SteamBrowserArtworkSource.StandardHeroProvider or SteamBrowserArtworkSource.IconProvider;
    public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        byte[]? bytes = MissingSteamHero && key == CoverKey.SteamHero("42") ? null : Image(key);
        _calls.Enqueue(new(key.Provider, key.Id, bytes is not null, bytes?.Length ?? 0));
        return Task.FromResult(bytes);
    }
    public static byte[] Image(CoverKey key)
    {
        var cover = key.Provider is "steam" or "igdb" or SteamBrowserArtworkSource.CoverProvider;
        var replacement = key == CoverKey.IgdbBackdrop("art");
        using var bitmap = new SKBitmap(cover ? 100 : 32, cover ? 80 : replacement ? 18 : 10);
        bitmap.Erase(cover ? SKColors.Coral : replacement ? SKColors.MediumPurple : SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

// This fixture reads real cached/local metadata, independent of credentials inherited
// by the native Electron process. It must never attempt an external token request.
internal sealed class ArtworkStateNoCredentials : IIgdbCredentialProvider
{
    public ValueTask<IgdbCredentials?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult<IgdbCredentials?>(null);
    public void Invalidate() { }
}
