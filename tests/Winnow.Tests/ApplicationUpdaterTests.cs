using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ApplicationUpdaterTests
{
    [Theory]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")]
    [InlineData("1.0.0-beta.10", "1.0.0")]
    [InlineData("1.0.0", "1.0.1-beta.1")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    public void Semantic_versions_sort_in_release_order(string earlier, string later)
    {
        Assert.True(ReleaseVersion.Parse(earlier)!.CompareTo(ReleaseVersion.Parse(later)) < 0);
        Assert.True(ReleaseVersion.Parse(later)!.CompareTo(ReleaseVersion.Parse(earlier)) > 0);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-beta.01")]
    [InlineData("1.0.0/../../bad")]
    public void Malformed_release_versions_are_rejected(string value) => Assert.Null(ReleaseVersion.Parse(value));

    [Fact]
    public async Task Stable_ignores_drafts_and_both_forms_of_prerelease_and_downgrades()
    {
        using var http = Http(_ => Json(new[] { Release("1.0.0"), Release("2.0.0"),
            Release("3.0.0", draft: true), Release("4.0.0", prerelease: true), Release("5.0.0-beta.1") }));
        var client = new GitHubReleaseClient(http);
        Assert.Equal("2.0.0", (await client.FindAsync(ReleaseVersion.Parse("1.0.0")!, false, Suffix, default))!.Version.Text);
        Assert.Equal("5.0.0-beta.1", (await client.FindAsync(ReleaseVersion.Parse("1.0.0")!, true, Suffix, default))!.Version.Text);
    }

    [Theory]
    [InlineData("win-x64.zip")]
    [InlineData("linux-x64.deb")]
    [InlineData("linux-x64.tar.gz")]
    public async Task Selects_the_exact_platform_asset(string suffix)
    {
        using var http = Http(_ => Json(new[] { Release("2.0.0", suffix: suffix) }));
        var found = await new GitHubReleaseClient(http).FindAsync(ReleaseVersion.Parse("1.0.0")!, false, suffix, default);
        Assert.EndsWith(suffix, found!.DownloadUrl);
    }

    [Fact]
    public async Task Release_selection_scans_later_pages_and_rejects_foreign_downloads()
    {
        using var http = Http(request => request.RequestUri!.Query.EndsWith("page=1", StringComparison.Ordinal)
            ? Json(Enumerable.Range(0, 100).Select(_ => Release("1.0.0")))
            : Json(new[] { Release("2.0.0"), Release("9.0.0", url: "https://attacker.invalid/payload.exe") }));
        var found = await new GitHubReleaseClient(http).FindAsync(ReleaseVersion.Parse("1.0.0")!, false, Suffix, default);
        Assert.Equal("9.0.0", found!.Version.Text);
        Assert.Null(found.Sha256);
        Assert.Equal(found.ReleaseUrl, found.DownloadUrl);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Failed_manual_checks_are_not_reported_as_up_to_date(int status)
    {
        using var scope = new Scope(Http(_ => new HttpResponseMessage((HttpStatusCode)status)));
        await scope.Updater.CheckAsync();
        Assert.Contains("Couldn't", scope.Updater.Snapshot.Status);
        Assert.False(scope.Updater.Snapshot.Busy);
        Assert.False(scope.Updater.Snapshot.CanRestart);
    }

    [Fact]
    public async Task Background_download_stages_verified_bytes_and_restart_is_explicit()
    {
        using var scope = new Scope(PackageHttp());
        await scope.Updater.CheckAsync();
        Assert.True(scope.Updater.Snapshot.CanRestart);
        Assert.Equal(100, scope.Updater.Snapshot.Progress);
        Assert.False(scope.Shutdown);
        Assert.Null(scope.Installer.Path);
        await scope.Updater.RestartAsync();
        Assert.True(scope.Shutdown);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(scope.Installer.Path!));
    }

    [Fact]
    public async Task Disabling_beta_discards_staged_prerelease_without_downgrading()
    {
        using var scope = new Scope(PackageHttp("2.0.0-beta.1"));
        await scope.Updater.SetIncludeBetaAsync(true);
        Assert.True(scope.Updater.Snapshot.CanRestart);
        await scope.Updater.SetIncludeBetaAsync(false);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.Null(scope.Updater.Snapshot.AvailableVersion);
        Assert.Empty(Directory.GetFiles(scope.Directory));
        Assert.Equal("false", scope.Settings.Values[ApplicationUpdater.BetaKey]);
        await scope.Updater.RestartAsync();
        Assert.False(scope.Shutdown);
    }

    [Fact]
    public async Task Disabled_automatic_updates_allow_manual_download_and_persist()
    {
        using var scope = new Scope(PackageHttp());
        await scope.Updater.SetAutomaticAsync(false);
        await scope.Updater.CheckAsync();
        Assert.True(scope.Updater.Snapshot.CanDownload);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.Equal("false", scope.Settings.Values[ApplicationUpdater.AutomaticKey]);
        await scope.Updater.DownloadAsync();
        Assert.True(scope.Updater.Snapshot.CanRestart);
    }

    [Fact]
    public async Task Unsupported_installations_offer_links_without_downloading()
    {
        using var scope = new Scope(PackageHttp(), supported: false);
        await scope.Updater.CheckAsync();
        Assert.NotNull(scope.Updater.Snapshot.DownloadUrl);
        Assert.False(scope.Updater.Snapshot.CanDownload);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.False(Directory.Exists(scope.Directory));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:bad")]
    public async Task Missing_or_invalid_digest_never_allows_staging(string? digest)
    {
        using var scope = new Scope(Http(_ => Json(new[] { Release("2.0.0", digest: digest) })));
        await scope.Updater.CheckAsync();
        Assert.False(scope.Updater.Snapshot.CanDownload);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.Contains("verification digest", scope.Updater.Snapshot.Status);
    }

    [Fact]
    public async Task Hash_mismatch_deletes_partial_and_allows_retry()
    {
        using var scope = new Scope(PackageHttp(bytes: [9, 8, 7]));
        await scope.Updater.CheckAsync();
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.True(scope.Updater.Snapshot.CanDownload);
        Assert.Contains("verified", scope.Updater.Snapshot.Status);
        Assert.Empty(Directory.GetFiles(scope.Directory));
    }

    [Fact]
    public async Task Redirect_outside_GitHub_is_rejected_before_following_it()
    {
        var calls = 0;
        using var scope = new Scope(Http(request =>
        {
            calls++;
            return request.RequestUri!.Host == "api.github.com" ? Json(new[] { Release("2.0.0") })
                : new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.invalid/payload") } };
        }));
        await scope.Updater.CheckAsync();
        Assert.Equal(2, calls);
        Assert.False(scope.Updater.Snapshot.CanRestart);
    }

    [Theory]
    [InlineData("1.0.0-dev")]
    [InlineData("1.0.0-ci.15")]
    public async Task Development_builds_never_check_or_stage_releases(string current)
    {
        using var scope = new Scope(Http(_ => throw new InvalidOperationException("Unexpected HTTP")), current: current);
        await scope.Updater.CheckAsync();
        Assert.Contains("Development", scope.Updater.Snapshot.Status);
    }

    private const string Suffix = "win-x64-setup.exe";
    [Fact]
    public async Task Turning_off_automatic_updates_cancels_an_inflight_check_before_it_can_download()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = new Scope(new HttpClient(new AsyncHandler(async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(new[] { Release("2.0.0") });
        })));
        var check = scope.Updater.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scope.Updater.SetAutomaticAsync(false).WaitAsync(TimeSpan.FromSeconds(5));
        await check;
        Assert.False(scope.Updater.Snapshot.Automatic);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.False(scope.Updater.Snapshot.Busy);
        Assert.False(Directory.Exists(scope.Directory));
    }

    [Fact]
    public async Task Cancelling_a_partial_download_removes_bytes_and_leaves_retry_available()
    {
        var stream = new PausedStream();
        using var scope = new Scope(Http(request => request.RequestUri!.Host == "api.github.com"
            ? Json(new[] { Release("2.0.0") })
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
        var check = scope.Updater.CheckAsync();
        await stream.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(Directory.GetFiles(scope.Directory, "*.partial"));
        scope.Updater.CancelDownload();
        await check.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(Directory.GetFiles(scope.Directory));
        Assert.True(scope.Updater.Snapshot.CanDownload);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.False(scope.Updater.Snapshot.CanCancel);
    }

    [Fact]
    public async Task Failed_handoff_keeps_app_open_and_permits_redownload()
    {
        using var scope = new Scope(PackageHttp());
        await scope.Updater.CheckAsync();
        scope.Installer.Fail = true;
        await scope.Updater.RestartAsync();
        Assert.False(scope.Shutdown);
        Assert.False(scope.Updater.Snapshot.CanRestart);
        Assert.True(scope.Updater.Snapshot.CanDownload);
        Assert.Empty(Directory.GetFiles(scope.Directory));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request, ct);
    }
    private sealed class PausedStream : Stream
    {
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_first) { _first = false; buffer.Span[0] = 1; return 1; }
            Paused.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static readonly byte[] Payload = [1, 2, 3];
    private static readonly string Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Payload));
    private static object Release(string version, bool draft = false, bool prerelease = false,
        string suffix = Suffix, string? url = null, string? digest = "default") => new
    {
        tag_name = "v" + version, draft, prerelease,
        assets = new[] { new { name = $"Winnow-{version}-{suffix}", state = "uploaded", size = Payload.Length,
            browser_download_url = url ?? $"https://github.com/safwyls/winnow/releases/download/v{version}/Winnow-{version}-{suffix}",
            digest = digest == "default" ? Digest : digest } }
    };
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) };
    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> action) => new(new Handler(action));
    private static HttpClient PackageHttp(string version = "2.0.0", byte[]? bytes = null) => Http(request =>
        request.RequestUri!.Host == "api.github.com" ? Json(new[] { Release(version) })
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Payload) });
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(action(request));
    }
    private sealed class Settings : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
    }
    private sealed class Installer(bool supported) : IUpdateInstaller
    {
        public bool IsSupported => supported;
        public string? Path { get; private set; }
        public bool Fail { get; set; }
        public Task PrepareAsync(string installerPath, string sha256, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidDataException("Installer changed after staging.");
            Path = installerPath;
            return Task.CompletedTask;
        }
    }
    private sealed class Scope : IDisposable
    {
        private readonly HttpClient _http;
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "winnow-update-tests-" + Guid.NewGuid().ToString("N"));
        public Settings Settings { get; } = new();
        public Installer Installer { get; }
        public ApplicationUpdater Updater { get; }
        public bool Shutdown { get; private set; }
        public Scope(HttpClient http, bool supported = true, string current = "1.0.0")
        {
            _http = http;
            Installer = new(supported);
            Updater = new(new(http), Settings, Installer, Directory, current, Suffix, () => Shutdown = true, NullLogger<ApplicationUpdater>.Instance);
        }
        public void Dispose()
        {
            Updater.Dispose();
            _http.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
