using Hoard.Covers.Igdb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Covers.Tests;

/// <summary>
/// A <c>.none</c> marker says "every source declined", and that sentence is only
/// true relative to the sources that existed when it was written.
///
/// <para>This is not hypothetical. On the library this was measured against,
/// Steam-only left 96 markers with a 30-day TTL; IGDB has cover art for 64 of
/// those games. Without an identity on the marker, adding IGDB would change
/// nothing a user could see for a month — and the only remedy would be deleting
/// the cache directory, which also throws away the 520 covers that were never in
/// question.</para>
/// </summary>
public class CoverNegativeMarkerTests
{
    private const string SteamOnly = "steam-capsule";

    [Fact]
    public void A_negative_written_under_an_older_source_set_is_retried()
    {
        using var dir = new TempCoverDirectory();
        var disk = new CoverDiskCache(dir.Options());
        var key = CoverKey.Steam("223710");

        disk.MarkMissing(key, CoverSourceSet.Identity([SteamOnly]));

        // Still honoured under the set that wrote it...
        Assert.True(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly])));

        // ...and retired the moment a source that was not consulted appears.
        Assert.False(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly, "igdb-cover"])));
        Assert.False(File.Exists(disk.NegativePath(key)), "a retired marker was left to be re-read");
    }

    [Fact]
    public void A_negative_written_under_the_current_source_set_is_still_honoured()
    {
        using var dir = new TempCoverDirectory();
        var disk = new CoverDiskCache(dir.Options());
        var key = CoverKey.Steam("228980");
        var current = CoverSourceSet.Identity([SteamOnly, "igdb-cover"]);

        disk.MarkMissing(key, current);

        // Steamworks Common Redistributables: neither source has art, and asking
        // again every launch is exactly what the marker exists to prevent.
        Assert.True(disk.IsKnownMissing(key, current));
    }

    [Fact]
    public void A_pre_identity_marker_from_an_older_build_is_retried()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var key = CoverKey.Steam("223710");

        // What the shipped cache actually holds: a zero-byte .none, written when
        // markers carried no identity at all. It must not be believed.
        Directory.CreateDirectory(options.CacheDirectory);
        File.WriteAllBytes(disk.NegativePath(key), []);

        Assert.False(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly])));
    }

    [Fact]
    public void Reordering_the_same_sources_does_not_throw_away_a_valid_negative()
    {
        using var dir = new TempCoverDirectory();
        var disk = new CoverDiskCache(dir.Options());
        var key = CoverKey.Steam("228980");

        disk.MarkMissing(key, CoverSourceSet.Identity([SteamOnly, "igdb-cover"]));

        // Order decides which source wins, not whether they all declined.
        Assert.True(disk.IsKnownMissing(key, CoverSourceSet.Identity(["igdb-cover", SteamOnly])));
    }

    [Fact]
    public async Task Registering_igdb_reopens_the_negatives_steam_wrote_alone()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var key = CoverKey.Steam("223710");

        // Run 1: Steam is the only source and has no capsule for this appid.
        using (var pipeline = dir.Pipeline(cdn, options))
        {
            Assert.Null(await pipeline.GetAsync(key, 320));
            Assert.True(pipeline.IsKnownMissing(key));
        }

        Assert.True(File.Exists(new CoverDiskCache(options).NegativePath(key)));

        // Run 2: IGDB is registered. No cache was deleted, no startup sweep ran,
        // and nothing was re-downloaded that already had art — but this key's
        // question is asked again, and answered.
        var igdb = new FakeIgdbClient();
        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        using (var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            new IgdbCoverSource(
                igdb,
                cdn,
                options,
                new IgdbCoverOptions { BatchLinger = TimeSpan.FromMilliseconds(30) },
                NullLogger<IgdbCoverSource>.Instance)))
        {
            Assert.False(pipeline.IsKnownMissing(key));
            using var art = await pipeline.GetAsync(key, 320);
            Assert.NotNull(art);
        }
    }

    [Fact]
    public async Task Configuring_igdb_reopens_the_negatives_it_wrote_while_silent()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var key = CoverKey.Steam("223710");
        var igdb = new FakeIgdbClient { Configured = false };
        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        IgdbCoverSource NewSource() => new(
            igdb, cdn, options,
            new IgdbCoverOptions { BatchLinger = TimeSpan.FromMilliseconds(30) },
            NullLogger<IgdbCoverSource>.Instance);

        CoverPipeline NewPipeline() => dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            NewSource());

        // Run 1: IGDB is registered but has no credentials, so it can say
        // nothing. The marker records that, not "IGDB has no art".
        using (var pipeline = NewPipeline())
        {
            Assert.Null(await pipeline.GetAsync(key, 320));
        }

        // Run 2: the user pastes their client id and secret. Registering nothing
        // new — same code, same sources — but the source set is not the same,
        // and the tile must not wait out a 30-day TTL to find out.
        igdb.Configured = true;
        using (var pipeline = NewPipeline())
        {
            Assert.False(pipeline.IsKnownMissing(key));
            using var art = await pipeline.GetAsync(key, 320);
            Assert.NotNull(art);
        }
    }

    [Fact]
    public async Task A_negative_is_written_only_when_every_source_declines()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        var filled = CoverKey.Steam("223710");   // Steam 404s, IGDB answers.
        var empty = CoverKey.Steam("228980");    // Neither has art.

        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));
        igdb.AddWithoutCover("228980");

        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            new IgdbCoverSource(
                igdb,
                cdn,
                options,
                new IgdbCoverOptions { BatchLinger = TimeSpan.FromMilliseconds(30) },
                NullLogger<IgdbCoverSource>.Instance));

        using (var art = await pipeline.GetAsync(filled, 320))
        {
            Assert.NotNull(art);
        }

        Assert.Null(await pipeline.GetAsync(empty, 320));

        Assert.False(File.Exists(disk.NegativePath(filled)), "a key with art was marked missing");
        Assert.True(File.Exists(disk.NegativePath(empty)));

        // And the marker names both sources, so adding a third reopens it.
        Assert.True(disk.IsKnownMissing(empty, CoverSourceSet.Identity([SteamOnly, "igdb-cover"])));
    }

    [Fact]
    public void An_identity_with_no_sources_is_still_a_stable_answer()
    {
        using var dir = new TempCoverDirectory();
        var disk = new CoverDiskCache(dir.Options());
        var key = new CoverKey("epic", "abc");

        var none = CoverSourceSet.Identity(Array.Empty<ICoverSource>(), key);
        Assert.Equal(CoverSourceSet.NoSources, none);

        disk.MarkMissing(key, none);
        Assert.True(disk.IsKnownMissing(key, none));
        Assert.False(disk.IsKnownMissing(key, CoverSourceSet.Identity([SteamOnly])));
    }
}
