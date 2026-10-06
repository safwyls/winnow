using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Winnow.Api.Contracts.Companion;
using Winnow.Application;
using Winnow.Application.Companion;
using Winnow.Core.Repositories;

namespace Winnow.Backend;

/// <summary>
/// The phone-sync listener: a separate Kestrel app on the local network that runs only
/// while phone sync is on. It shares nothing with the loopback API's pipeline and serves
/// two routes, pairing and the read-only snapshot, over HTTPS with a certificate phones
/// pin by fingerprint.
/// </summary>
public sealed class CompanionLanHost(
    ISettingsRepository settings,
    CompanionCertificate certificates,
    CompanionDevices devices,
    CompanionPairingWindow pairing,
    CompanionSnapshotBuilder snapshots,
    IApplicationChangePublisher changes,
    ILogger<CompanionLanHost> log) : IHostedService, IAsyncDisposable
{
    public const string EnabledKey = "companion.enabled";
    public const string PortKey = "companion.port";

    /// <summary>A fixed default so a paired phone finds the PC again after a restart.</summary>
    public const int DefaultPort = 47630;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _app;
    private X509Certificate2? _certificate;
    private string? _problem;
    private int _port;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (await settings.GetAsync(EnabledKey, cancellationToken) == "true") await RestartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => StopListenerAsync();

    public async Task<CompanionStatus> StatusAsync(CancellationToken ct = default)
    {
        var enabled = await settings.GetAsync(EnabledKey, ct) == "true";
        var open = pairing.Current;
        var addresses = LocalAddresses();
        var fingerprint = _certificate is null ? null : CompanionCertificate.Fingerprint(_certificate);
        var running = _app is not null;
        var problem = _problem ?? (running && addresses.Count == 0 ? "This PC has no local network address phones can reach." : null);
        CompanionPairing? window = open is { } o && running && fingerprint is not null
            ? new CompanionPairing(o.Code, o.ExpiresAt, QrPayload(addresses, _port, fingerprint, o.Code))
            : null;
        return new CompanionStatus(enabled, running, running ? _port : await ConfiguredPortAsync(ct), problem, fingerprint, addresses,
            await devices.ListAsync(ct), window);
    }

    public async Task<CompanionStatus> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await settings.SetAsync(EnabledKey, enabled ? "true" : "false", ct);
        if (enabled) await RestartAsync(ct);
        else
        {
            pairing.Close();
            await StopListenerAsync();
            _problem = null;
        }
        changes.Publish("companion.changed");
        return await StatusAsync(ct);
    }

    public async Task<CompanionStatus> OpenPairingAsync(CancellationToken ct = default)
    {
        if (_app is null) throw new ApplicationConflictException("Turn on phone sync before pairing a phone.");
        pairing.Open();
        changes.Publish("companion.changed");
        return await StatusAsync(ct);
    }

    public async Task<CompanionStatus> ClosePairingAsync(CancellationToken ct = default)
    {
        pairing.Close();
        changes.Publish("companion.changed");
        return await StatusAsync(ct);
    }

    public async Task<bool> RemoveDeviceAsync(string id, CancellationToken ct = default)
    {
        var removed = await devices.RemoveAsync(id, ct);
        if (removed) changes.Publish("companion.changed");
        return removed;
    }

    private async Task<int> ConfiguredPortAsync(CancellationToken ct)
        => int.TryParse(await settings.GetAsync(PortKey, ct), out var port) && port is >= 0 and <= 65535 ? port : DefaultPort;

    private async Task RestartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopListenerCoreAsync();
            var (certificate, problem) = await certificates.LoadOrCreateAsync(ct);
            if (certificate is null)
            {
                _problem = problem;
                return;
            }
            _certificate = certificate;
            var port = await ConfiguredPortAsync(ct);
            var app = BuildListener(certificate, port);
            try
            {
                await app.StartAsync(ct);
            }
            catch (IOException)
            {
                await app.DisposeAsync();
                _problem = $"Another program is using port {port}, so phone sync could not start.";
                return;
            }
            _app = app;
            _port = new Uri(app.Urls.First()).Port;
            _problem = null;
            log.LogInformation("Phone sync listener started on port {Port}.", _port);
        }
        finally { _gate.Release(); }
    }

    private async Task StopListenerAsync()
    {
        await _gate.WaitAsync();
        try { await StopListenerCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task StopListenerCoreAsync()
    {
        if (_app is not { } app) return;
        _app = null;
        await app.StopAsync();
        await app.DisposeAsync();
        log.LogInformation("Phone sync listener stopped.");
    }

    private WebApplication BuildListener(X509Certificate2 certificate, int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        // Request logging could record tokens or codes; this listener logs nothing per request.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 4 * 1024;
            options.Listen(IPAddress.Any, port, listen => listen.UseHttps(certificate));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await next(context);
        });
        app.MapPost("/companion/v1/pair", async (HttpContext context) =>
        {
            CompanionPairRequest? request;
            try { request = await context.Request.ReadFromJsonAsync<CompanionPairRequest>(Json, context.RequestAborted); }
            catch (Exception e) when (e is JsonException or BadHttpRequestException or InvalidOperationException) { return Results.BadRequest(); }
            if (request is null || !pairing.TryRedeem(request.Code)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var (id, token) = await devices.AddAsync(request.DeviceName ?? "", context.RequestAborted);
            changes.Publish("companion.changed");
            return Results.Json(new CompanionPairResponse(id, token, Environment.MachineName), Json);
        });
        app.MapGet("/companion/v1/snapshot", async (HttpContext context) =>
        {
            var header = context.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : null;
            if (await devices.AuthenticateAsync(token, context.RequestAborted) is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);
            var snapshot = await snapshots.BuildAsync(context.RequestAborted);
            // The ETag covers the library, not the generation time, so an unchanged library answers 304.
            var body = JsonSerializer.SerializeToUtf8Bytes(snapshot with { GeneratedAt = default }, Json);
            var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(body))[..32]}\"";
            context.Response.Headers.ETag = etag;
            if (context.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Json(snapshot, Json);
        });
        app.MapFallback(() => Results.NotFound());
        return app;
    }

    /// <summary>Private IPv4 addresses on interfaces that are up: what a phone on the same
    /// network can reach.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a))
                .Select(a => a.ToString())
                .Distinct()];
        }
        catch (NetworkInformationException) { return []; }
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>What the QR code carries. Phones try each address and accept only the
    /// certificate with this fingerprint.</summary>
    public static string QrPayload(IReadOnlyList<string> addresses, int port, string fingerprint, string code)
        => $"winnow-deck://pair?v=1&h={string.Join(',', addresses)}&p={port}&f={fingerprint}&c={code}&n={Uri.EscapeDataString(Environment.MachineName)}";

    public async ValueTask DisposeAsync()
    {
        await StopListenerAsync();
        _certificate?.Dispose();
    }
}
