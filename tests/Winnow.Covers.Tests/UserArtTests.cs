using Winnow.Covers;
using Winnow.Covers.Igdb;
using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// Integration tests for the user-art import pipeline: file and URL ingest,
/// content sniffing, the <c>winnow://user-art/</c> reference shape, the cover
/// source that serves imported bytes back, and the <see cref="ArtKeys"/>
/// resolver that routes a stored URL to the right <see cref="CoverKey"/>.
/// </summary>
public class UserArtTests : IDisposable
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "winnow-user-art-" + Guid.NewGuid().ToString("n"));

    private readonly CoverCacheOptions _options;
    private readonly UserArtStore _store;

    public UserArtTests()
    {
        _options = new CoverCacheOptions { CacheDirectory = _root };
        _store = new UserArtStore(_options);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string WriteSourceFile(byte[] bytes)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "chosen-" + Guid.NewGuid().ToString("n") + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task An_imported_file_lands_under_the_configured_cache_directory()
    {
        var chosen = WriteSourceFile(Png);

        var imported = await _store.ImportFileAsync(chosen);

        Assert.True(imported.Ok);
        var token = UserArtRef.Token(imported.Reference);
        Assert.NotNull(token);
        Assert.StartsWith(_root, _store.PathFor(token!), StringComparison.Ordinal);
        Assert.True(File.Exists(_store.PathFor(token!)));
    }

    [Fact]
    public async Task The_file_the_user_chose_is_read_and_never_written()
    {
        var chosen = WriteSourceFile(Png);
        var before = File.GetLastWriteTimeUtc(chosen);

        await _store.ImportFileAsync(chosen);

        Assert.Equal(Png, File.ReadAllBytes(chosen));
        Assert.Equal(before, File.GetLastWriteTimeUtc(chosen));
    }

    [Fact]
    public async Task Something_that_is_not_an_image_is_refused()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(path, "this is not a picture at all");

        var imported = await _store.ImportFileAsync(path);

        Assert.False(imported.Ok);
        Assert.Equal(UserArtImportFailure.NotAnImage, imported.Failure);
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_a_refusal_rather_than_a_throw()
    {
        var imported = await _store.ImportFileAsync(Path.Combine(_root, "absent.png"));

        Assert.False(imported.Ok);
        Assert.Equal(UserArtImportFailure.FileNotFound, imported.Failure);
    }

    [Fact]
    public async Task A_url_that_is_not_http_is_refused_without_a_request()
    {
        var imported = await _store.ImportUrlAsync("file:///C:/secrets/passwords.png");

        Assert.False(imported.Ok);
        Assert.Equal(UserArtImportFailure.BadUrl, imported.Failure);
    }

    [Fact]
    public async Task The_cover_source_serves_the_imported_bytes_back()
    {
        var chosen = WriteSourceFile(Png);
        var imported = await _store.ImportFileAsync(chosen);
        var token = UserArtRef.Token(imported.Reference)!;

        var source = new UserArtCoverSource(_store);
        var key = CoverKey.User(token);

        Assert.True(source.CanHandle(key));
        Assert.False(source.CanHandle(CoverKey.Steam("620")));
        Assert.Equal(Png, await source.TryFetchAsync(key));
        Assert.Null(await source.TryFetchAsync(CoverKey.User("deadbeef")));
    }

    [Fact]
    public void A_stored_art_url_resolves_to_exactly_one_key()
    {
        Assert.Equal(
            CoverKey.User("abc123"),
            ArtKeys.Resolve("winnow://user-art/abc123"));

        Assert.Equal(
            CoverKey.Igdb("co1r76"),
            ArtKeys.Resolve("https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg"));

        Assert.Null(ArtKeys.Resolve("https://example.invalid/picture.png"));
        Assert.Null(ArtKeys.Resolve(null));
    }

    [Fact]
    public void A_reference_that_is_not_one_yields_no_token()
    {
        Assert.Null(UserArtRef.Token("https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg"));
        Assert.Null(UserArtRef.Token("winnow://user-art/"));
        Assert.Null(UserArtRef.Token("winnow://user-art/../../etc/passwd"));
        Assert.Equal("abc123", UserArtRef.Token(UserArtRef.Format("abc123")));
    }
}
