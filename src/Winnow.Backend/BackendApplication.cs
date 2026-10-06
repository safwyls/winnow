using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Winnow.Api.Contracts.Protocol;
using Winnow.Application;
using Winnow.Application.Details;
using Microsoft.OpenApi;

namespace Winnow.Backend;

public static class BackendApplication
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        var directoryIndex = Array.IndexOf(args, "--data-dir");
        if (directoryIndex < 0 || directoryIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[directoryIndex + 1]))
            throw new ArgumentException("Pass --data-dir <path> to choose the library owned by this backend.");
        var dataLocation = Winnow.App.Services.WinnowDataLocation.ResolveOverride(args[directoryIndex + 1]);
        var dataDirectory = dataLocation.Root;
        var databaseAlreadyExisted = File.Exists(dataLocation.DatabasePath);
        var backgroundEnabled = !args.Contains("--no-sync") && !args.Contains("--seed-sample");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environments.Production, ApplicationName = typeof(BackendApplication).Assembly.FullName });
        Winnow.App.Services.DiagnosticLogging.Configure(builder.Logging, dataDirectory, "backend.log");
        builder.Configuration.AddJsonFile(Path.Combine(BackendInstallationLease.FindInstallationDirectory(AppContext.BaseDirectory), "appsettings.local.json"), optional: true, reloadOnChange: false);
        builder.Services.AddSingleton<BackendInstallationLease>();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            options.Limits.MaxRequestBodySize = 16 * 1024 * 1024;
        });
        builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Winnow local backend";
            document.Info.Version = "1";
            document.Info.Description = "Local authenticated API. Read backend/endpoint.json in the selected data directory for the current address and bearer token. Subscribe to /api/v1/events before loading snapshots; resync-required invalidates all cached state.";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["localBearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Per-backend token from the current user's protected discovery file."
            };
            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("localBearer", document)] = []
            });
            return Task.CompletedTask;
        }));
        builder.Services.AddSingleton(_ => new BackendOwnership(dataDirectory));
        builder.Services.AddSingleton<BackendEventHub>();
        builder.Services.AddSingleton<IApplicationChangePublisher>(services => services.GetRequiredService<BackendEventHub>());
        builder.Services.AddWinnowApplication(dataLocation.DatabasePath, pooling: false);
        builder.Services.AddWinnowRuntime(dataDirectory, backgroundEnabled, dataLocation.DatabasePath);
        builder.Services.AddWinnowDetails();
        builder.Services.AddWinnowImports();
        builder.Services.AddSingleton<Winnow.Application.Identity.IdentityReviewApplication>();
        builder.Services.AddSingleton<Winnow.Application.Connections.PluginConnections>();
        builder.Services.AddSingleton<Winnow.Application.Connections.StoreConnectionApplication>();
        builder.Services.AddSingleton<Winnow.Application.Connections.ConnectionOperations>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Winnow.Application.Connections.ConnectionOperations>());
        builder.Services.AddSingleton<BackendProgressReporter>();
        builder.Services.AddSingleton<IProgress<Winnow.App.Services.EnrichmentProgress>>(sp => sp.GetRequiredService<BackendProgressReporter>());
        builder.Services.AddSingleton<Winnow.Ingest.Epic.Web.Auth.EpicInteractiveSignIn>();
        builder.Services.AddSingleton(new BackendRunOptions(backgroundEnabled));
        builder.Services.AddHostedService<BackendStartupService>();
        builder.Services.AddSingleton<ICompanionSecretProtector, DpapiCompanionSecretProtector>();
        builder.Services.AddSingleton<CompanionCertificate>();
        builder.Services.AddSingleton<Winnow.Application.Companion.CompanionDevices>();
        builder.Services.AddSingleton<Winnow.Application.Companion.CompanionPairingWindow>();
        builder.Services.AddSingleton<Winnow.Application.Companion.CompanionSnapshotBuilder>();
        builder.Services.AddSingleton<CompanionLanHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CompanionLanHost>());
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        try
        {
        _ = app.Services.GetRequiredService<BackendInstallationLease>();
        var ownership = app.Services.GetRequiredService<BackendOwnership>();
        var events = app.Services.GetRequiredService<BackendEventHub>();
        var sessionHealth = app.Services.GetRequiredService<Winnow.Monitor.SessionWatcherHealth>();
        EventHandler healthChanged = (_, _) => events.Publish("diagnostics.changed", "sessions");
        sessionHealth.Changed += healthChanged;
        app.Lifetime.ApplicationStopped.Register(() => sessionHealth.Changed -= healthChanged);
        app.Services.InitializeWinnowDatabase();
        app.Services.GetRequiredService<Winnow.App.Services.FirstRunSetupService>()
            .InitializeAsync(databaseAlreadyExisted, args.Contains("--seed-sample")).GetAwaiter().GetResult();
#if DEBUG
        if (args.Contains("--seed-sample"))
            Winnow.App.Services.SampleDataSeeder.SeedAsync(app.Services).GetAwaiter().GetResult();
#endif
        app.Lifetime.ApplicationStarted.Register(() => ownership.Publish(new BackendDiscovery(
            app.Urls.Single(), ownership.Token, events.Epoch, Environment.ProcessId, "1")));

        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var local = context.Connection.RemoteIpAddress;
            var expectedAuthority = $"127.0.0.1:{context.Connection.LocalPort}";
            if (local is null || !IPAddress.IsLoopback(local) ||
                !string.Equals(request.Host.Value, expectedAuthority, StringComparison.OrdinalIgnoreCase))
            { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            if (request.Headers.TryGetValue("Origin", out var origin) &&
                (origin.Count != 1 || !string.Equals(origin[0], $"http://{expectedAuthority}", StringComparison.OrdinalIgnoreCase)))
            { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            var supplied = request.Headers.Authorization.ToString();
            var expected = "Bearer " + ownership.Token;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
            { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false })
            {
                bodyLimit.MaxRequestBodySize = request.Path.Value switch
                {
                    "/api/v1/imports/steam/load-files" or "/api/v1/imports/steam/pages" => 180L * 1024 * 1024,
                    "/api/v1/metadata/art-upload" => 24L * 1024 * 1024,
                    _ => 16L * 1024 * 1024
                };
            }
            try { await next(context); }
            catch (Winnow.Core.Domain.ManualEntryConflictException exception)
            {
                await Results.Problem(exception.Message, statusCode: 409,
                    extensions: new Dictionary<string, object?> { ["field"] = exception.Field, ["reason"] = exception.Reason.ToString() }).ExecuteAsync(context);
            }
            catch (Winnow.Core.Identity.IdentityLinkRefusedException exception)
            {
                await Results.Problem(exception.Message, statusCode: 409,
                    extensions: new Dictionary<string, object?> { ["refusal"] = exception.Refusal.ToString() }).ExecuteAsync(context);
            }
            catch (ApplicationConflictException exception) { await Results.Problem(exception.Message, statusCode: 409).ExecuteAsync(context); }
            catch (ApplicationNotFoundException exception) { await Results.Problem(exception.Message, statusCode: 404).ExecuteAsync(context); }
            catch (ArgumentException exception) { await Results.Problem(exception.Message, statusCode: 400).ExecuteAsync(context); }
            catch (Exception exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
            {
                app.Logger.LogError("API operation failed ({FaultType}).", exception.GetType().Name);
                await Results.Problem("The operation failed. Reload current state before retrying.", statusCode: 500).ExecuteAsync(context);
            }
        });
        app.MapGet("/api/v1/health", () => new BackendHealth("1", events.Epoch, events.Sequence));
        app.MapGet("/api/v1/capabilities", () => new BackendCapabilities("1", ["events", "feed", "connections", "presentation-preferences", "library", "lists", "manual-games", "identity", "library-preferences", "companion"]));
        app.MapGet("/api/v1/events", async (HttpContext context) =>
        {
            using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
            var connectionToken = connectionLifetime.Token;
            using var subscription = events.Subscribe(context.Request.Headers["Last-Event-ID"].FirstOrDefault());
            context.Response.ContentType = "text/event-stream";
            await context.Response.StartAsync(connectionToken);
            try
            {
                while (!connectionToken.IsCancellationRequested)
                {
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
                    heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        if (!await subscription.Reader.WaitToReadAsync(heartbeat.Token)) break;
                        while (subscription.Reader.TryRead(out var change))
                        {
                            var json = JsonSerializer.Serialize(change, JsonOptions);
                            await context.Response.WriteAsync($"id: {change.Cursor}\nevent: change\ndata: {json}\n\n", connectionToken);
                        }
                    }
                    catch (OperationCanceledException) when (!connectionToken.IsCancellationRequested)
                    { await context.Response.WriteAsync(": heartbeat\n\n", connectionToken); }
                    await context.Response.Body.FlushAsync(connectionToken);
                }
            }
            catch (OperationCanceledException) when (connectionToken.IsCancellationRequested) { }
        });
        app.MapPost("/api/v1/lifecycle/shutdown", (HttpContext context) =>
        {
            events.Publish("backend.stopping");
            context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.Accepted();
        });
        app.MapLibraryEndpoints();
        app.MapFeedApi();
        app.MapPreferencesApi();
        app.MapConnectionApi();
        app.MapStoreConnectionApi();
        app.MapDetailsEndpoints();
        app.MapImportEndpoints();
        app.MapGameActionApi();
        app.MapIdentityEndpoints();
        app.MapArtworkApi();
        app.MapOperationApi();
        app.MapCompanionApi();
        app.MapOpenApi("/api/v1/openapi.json");
        return app;
        }
        catch
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
