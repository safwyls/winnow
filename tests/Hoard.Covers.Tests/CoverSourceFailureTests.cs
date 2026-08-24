using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Covers.Tests;

/// <summary>
/// The difference between "this game has no art" and "the CDN would not talk to
/// us", and why it is worth 30 days of a user's grid.
///
/// <para>A negative marker is written on the strength of a source declining, and
/// <c>CoverDiskCache</c> clears one only on a successful write — so a wrong
/// marker is not merely a miss, it is a month of procedural placeholder art with
/// no way for the user to ask again. A 404 earns that; a 403 does not.</para>
/// </summary>
public class CoverSourceFailureTests
{
    /// <summary>A CDN that refuses everything, the way a WAF greeting a cold library's burst would.</summary>
    private sealed class ForbiddenCdn : FakeCoverCdn
    {
        protected override Task<HttpResponseMessage> OnSendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task A_403_is_not_an_answer_about_whether_art_exists()
    {
        var cdn = new ForbiddenCdn();
        var options = new CoverCacheOptions { SteamCdnBaseUrl = "https://cdn.test.invalid/steam/apps" };
        var source = new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance);

        // A 404 answers "no capsule of this shape exists" and TryFetchAsync
        // reports it as null. A 403 answers nothing, so it must surface.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => source.TryFetchAsync(CoverKey.Steam("220")));
    }

    [Fact]
    public async Task A_403_leaves_no_negative_marker_and_the_next_launch_retries()
    {
        using var dir = new TempCoverDirectory();
        var key = CoverKey.Steam("220");
        var disk = new CoverDiskCache(dir.Options());

        using (var pipeline = dir.Pipeline(new ForbiddenCdn()))
        {
            // The pipeline degrades to "not yet" — it never throws for a cover.
            Assert.Null(await pipeline.GetAsync(key, 320));
        }

        Assert.False(File.Exists(disk.NegativePath(key)), "a 403 wrote a 30-day .none marker");
        Assert.False(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly])));

        // The CDN recovers; the grid does too, with no cache to clear.
        var healthy = new FakeCoverCdn();
        healthy.AddCapsule("220", TestArt.Capsule());
        using (var pipeline = dir.Pipeline(healthy))
        {
            using var art = await pipeline.GetAsync(key, 320);
            Assert.NotNull(art);
        }
    }

    /// <summary>
    /// The contrast case, and the behaviour the 403 fix must not break: a 404
    /// IS an answer, and paying for it once a month is the whole point of the
    /// negative marker.
    /// </summary>
    [Fact]
    public async Task A_404_still_writes_the_negative_marker()
    {
        using var dir = new TempCoverDirectory();
        var key = CoverKey.Steam("228980");
        var disk = new CoverDiskCache(dir.Options());

        using var pipeline = dir.Pipeline(new FakeCoverCdn());
        Assert.Null(await pipeline.GetAsync(key, 320));

        Assert.True(File.Exists(disk.NegativePath(key)));

        // The marker is only believed under the source set that wrote it — here
        // Steam alone, which is what TempCoverDirectory.Pipeline registers.
        Assert.True(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly])));
    }

    /// <summary>The one source <see cref="TempCoverDirectory.Pipeline"/> registers.</summary>
    private const string SteamOnly = "steam-capsule";

    /// <summary>
    /// Temp names must be unique across processes, not merely across threads: a
    /// second Hoard over the same cache directory hands out managed thread id 1
    /// just like the first, and two writers on one temp name means one of them
    /// moves a truncated file into place under a name that says it is complete.
    /// A thread id cannot show that; concurrent writes of different sizes to
    /// distinct keys can show the file never ends up truncated.
    /// </summary>
    [Fact]
    public async Task Concurrent_writes_never_leave_a_truncated_file_behind()
    {
        using var dir = new TempCoverDirectory();
        var disk = new CoverDiskCache(dir.Options());

        var payloads = Enumerable.Range(0, 24)
            .Select(i => (Key: CoverKey.Steam(i.ToString()), Bytes: new byte[1024 + (i * 97)]))
            .ToArray();

        foreach (var (_, bytes) in payloads)
        {
            Random.Shared.NextBytes(bytes);
        }

        await Task.WhenAll(payloads.Select(p => Task.Run(() =>
        {
            // Same key written repeatedly from several tasks: every writer races
            // every other on the same destination path.
            for (var attempt = 0; attempt < 4; attempt++)
            {
                disk.WriteSource(p.Key, p.Bytes);
            }
        })));

        foreach (var (key, bytes) in payloads)
        {
            Assert.True(disk.TryReadSource(key, out var read));
            Assert.Equal(bytes.Length, read.Length);
            Assert.Equal(bytes, read);
        }

        // And nothing is left lying around under a .tmp name.
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }
}
