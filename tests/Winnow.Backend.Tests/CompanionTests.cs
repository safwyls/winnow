using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Companion;
using Winnow.Api.Contracts.Library;
using Winnow.Application.Library;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class CompanionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Stands in for DPAPI, which only Windows has. Base64 is not encryption; this
    /// only exercises the store-and-reload path.</summary>
    private sealed class TestProtector(bool available = true) : ICompanionSecretProtector
    {
        public bool IsAvailable => available;
        public string? Protect(byte[] plaintext) => available ? Convert.ToBase64String(plaintext) : null;
        public byte[]? Unprotect(string protectedBase64) => available ? Convert.FromBase64String(protectedBase64) : null;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required string Directory { get; init; }
        public required WebApplication App { get; init; }
        public required HttpClient Local { get; init; }

        public static async Task<Harness> StartAsync(bool protectorAvailable = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-companion-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton<ICompanionSecretProtector>(new TestProtector(protectorAvailable)));
            // An ephemeral port keeps parallel test runs apart.
            await app.Services.GetRequiredService<ISettingsRepository>().SetAsync(CompanionLanHost.PortKey, "0");
            await app.StartAsync();
            var local = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            local.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.Services.GetRequiredService<BackendOwnership>().Token);
            return new Harness { Directory = directory, App = app, Local = local };
        }

        public async Task<CompanionStatus> StatusAsync() => (await Local.GetFromJsonAsync<CompanionStatus>("/api/v1/companion", Json))!;

        public async Task<CompanionStatus> EnableAsync(bool enabled)
        {
            var response = await Local.PutAsJsonAsync("/api/v1/companion/enabled", new SetCompanionEnabled(enabled), Json);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CompanionStatus>(Json))!;
        }

        public async Task<CompanionStatus> OpenPairingAsync()
        {
            var response = await Local.PostAsync("/api/v1/companion/pairing", null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<CompanionStatus>(Json))!;
        }

        /// <summary>A phone's client: it trusts only the certificate with the pinned fingerprint.</summary>
        public static HttpClient Phone(CompanionStatus status, string? pin = null)
        {
            var expected = pin ?? status.Fingerprint!;
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && string.Equals(certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), expected, StringComparison.OrdinalIgnoreCase),
            };
            return new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{status.Port}") };
        }

        public async ValueTask DisposeAsync()
        {
            Local.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    [Fact]
    public async Task PhoneSyncIsOffByDefaultAndOnlyListensWhileEnabled()
    {
        await using var h = await Harness.StartAsync();
        var off = await h.StatusAsync();
        Assert.False(off.Enabled);
        Assert.False(off.Running);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Local.PostAsync("/api/v1/companion/pairing", null)).StatusCode);

        var on = await h.EnableAsync(true);
        Assert.True(on.Running);
        Assert.NotEqual(0, on.Port);
        Assert.Matches("^[0-9a-f]{64}$", on.Fingerprint!);
        using (var phone = Harness.Phone(on))
            Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/companion/v1/snapshot")).StatusCode);

        var disabled = await h.EnableAsync(false);
        Assert.False(disabled.Running);
        using var after = Harness.Phone(on);
        await Assert.ThrowsAsync<HttpRequestException>(() => after.GetAsync("/companion/v1/snapshot"));
    }

    [Fact]
    public async Task APairedPhoneReadsTheSnapshotAndNothingElseUntilRevoked()
    {
        await using var h = await Harness.StartAsync();
        var library = h.App.Services.GetRequiredService<ILibraryApplication>();
        var game = await library.CreateManualGameAsync(new ManualGameRequest("Ironveil", 2021, InstallPath: @"C:\Games\Ironveil-secret-path",
            IgdbId: 1942, SteamAppId: "292030"));
        var list = await library.CreateListAsync(new CreateListRequest("Couch co-op", [game.ReleaseId]));
        await library.CreateLiveListAsync(new CreateLiveListRequest("Live", new Winnow.Core.Queries.LibraryFilter()));
        await h.EnableAsync(true);
        var pairing = (await h.OpenPairingAsync()).Pairing!;
        var status = await h.StatusAsync();
        Assert.Contains($"&f={status.Fingerprint}&c={pairing.Code}", pairing.QrPayload);
        Assert.StartsWith("winnow-deck://pair?v=1&h=", pairing.QrPayload);

        using var phone = Harness.Phone(status);
        var paired = await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest(pairing.Code, "Pixel 9"), Json);
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var credentials = (await paired.Content.ReadFromJsonAsync<CompanionPairResponse>(Json))!;
        // The code is single-use.
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest(pairing.Code, "Second"), Json)).StatusCode);
        Assert.Null((await h.StatusAsync()).Pairing);

        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        var response = await phone.GetAsync("/companion/v1/snapshot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-path", raw);
        Assert.DoesNotContain(credentials.Token, raw);
        var snapshot = JsonSerializer.Deserialize<CompanionSnapshot>(raw, Json)!;
        Assert.Equal(1, snapshot.Version);
        var ironveil = Assert.Single(snapshot.Games);
        Assert.Equal(("Ironveil", 2021), (ironveil.Title, ironveil.FirstReleaseYear));
        var entry = Assert.Single(ironveil.Entries);
        Assert.Equal(game.ReleaseId, entry.ReleaseId);
        Assert.Equal("292030", entry.StoreIds.GetValueOrDefault("steam"));
        var shared = Assert.Single(snapshot.Lists);
        Assert.Equal((list.Id, "Couch co-op"), (shared.Id, shared.Name));
        Assert.Equal([game.ReleaseId], shared.ReleaseIds);

        // An unchanged library answers 304 to the same ETag.
        using var again = new HttpRequestMessage(HttpMethod.Get, "/companion/v1/snapshot");
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await phone.SendAsync(again)).StatusCode);

        // The LAN listener serves pairing and the snapshot only, even with a valid token.
        foreach (var path in new[] { "/api/v1/health", "/api/v1/library", "/companion/v1/other", "/" })
            Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync(path)).StatusCode);

        var device = Assert.Single((await h.StatusAsync()).Devices);
        Assert.Equal("Pixel 9", device.Name);
        Assert.NotNull(device.LastSyncAt);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Local.DeleteAsync($"/api/v1/companion/devices/{device.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/companion/v1/snapshot")).StatusCode);
        Assert.Empty((await h.StatusAsync()).Devices);
    }

    [Fact]
    public async Task WrongCodesClosePairingAndTokensAreNeverStored()
    {
        await using var h = await Harness.StartAsync();
        await h.EnableAsync(true);
        var pairing = (await h.OpenPairingAsync()).Pairing!;
        var status = await h.StatusAsync();
        using var phone = Harness.Phone(status);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest("WRONGCODE" + i, "Guess"), Json)).StatusCode);
        // Five wrong codes closed the window, so even the right code now fails.
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest(pairing.Code, "Late"), Json)).StatusCode);
        Assert.Null((await h.StatusAsync()).Pairing);

        var reopened = (await h.OpenPairingAsync()).Pairing!;
        var response = await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest(reopened.Code, "Phone"), Json);
        var token = (await response.Content.ReadFromJsonAsync<CompanionPairResponse>(Json))!.Token;
        var stored = await h.App.Services.GetRequiredService<ISettingsRepository>().GetAsync(Winnow.Application.Companion.CompanionDevices.DevicesKey);
        Assert.DoesNotContain(token, stored);
        Assert.Equal(HttpStatusCode.BadRequest, (await phone.PostAsync("/companion/v1/pair", new StringContent("not json"))).StatusCode);
    }

    [Fact]
    public async Task AWrongCertificateIsRefusedByAPinningPhone()
    {
        await using var h = await Harness.StartAsync();
        var status = await h.EnableAsync(true);
        using var phone = Harness.Phone(status, pin: new string('0', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => phone.GetAsync("/companion/v1/snapshot"));
    }

    [Fact]
    public async Task TheCertificateSurvivesARestartSoPairedPhonesKeepTheirPin()
    {
        await using var h = await Harness.StartAsync();
        var first = (await h.EnableAsync(true)).Fingerprint;
        await h.EnableAsync(false);
        Assert.Equal(first, (await h.EnableAsync(true)).Fingerprint);
    }

    [Fact]
    public async Task WithoutKeyEncryptionPhoneSyncRefusesToStartAndSaysWhy()
    {
        await using var h = await Harness.StartAsync(protectorAvailable: false);
        var status = await h.EnableAsync(true);
        Assert.True(status.Enabled);
        Assert.False(status.Running);
        Assert.Contains("encrypt", status.Problem);
        Assert.Null(await h.App.Services.GetRequiredService<ISettingsRepository>().GetAsync(CompanionCertificate.CertificateKey));
    }

    [Fact]
    public void DpapiProtectsTheCertificateKeyOnWindows()
    {
        // DPAPI exists only on Windows; Windows CI runs this, other hosts return early.
        if (!OperatingSystem.IsWindows()) return;
        var protector = new DpapiCompanionSecretProtector();
        Assert.True(protector.IsAvailable);
        byte[] secret = [1, 2, 3, 4, 5];
        var stored = protector.Protect(secret)!;
        Assert.NotEqual(Convert.ToBase64String(secret), stored);
        Assert.Equal(secret, protector.Unprotect(stored));
        Assert.Null(protector.Unprotect("not base64!"));
    }
    [Fact]
    public async Task TheFrontendClientDrivesPhoneSyncThroughTheLoopbackApi()
    {
        await using var h = await Harness.StartAsync();
        using var api = Winnow.Api.Client.WinnowApiClient.Attach(h.Directory);
        var settings = new Winnow.Api.Client.ApiCompanionSettings(api);

        var conflict = await Assert.ThrowsAsync<Winnow.Api.Client.BackendApiException>(() => settings.OpenPairingAsync());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var on = await settings.SetEnabledAsync(true);
        Assert.True(on.Running);
        var pairing = (await settings.OpenPairingAsync()).Pairing!;
        using var phone = Harness.Phone(on);
        (await phone.PostAsJsonAsync("/companion/v1/pair", new CompanionPairRequest(pairing.Code, "Pixel 9"), Json)).EnsureSuccessStatusCode();
        var paired = await settings.StatusAsync();
        Assert.Null(paired.Pairing);
        var device = Assert.Single(paired.Devices);
        Assert.Equal("Pixel 9", device.Name);
        Assert.NotNull((await settings.OpenPairingAsync()).Pairing);
        Assert.Null((await settings.ClosePairingAsync()).Pairing);
        await settings.RemoveDeviceAsync(device.Id);
        Assert.Empty((await settings.StatusAsync()).Devices);
        var missing = await Assert.ThrowsAsync<Winnow.Api.Client.BackendApiException>(() => settings.RemoveDeviceAsync(device.Id));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await settings.SetEnabledAsync(false)).Running);
    }
}

