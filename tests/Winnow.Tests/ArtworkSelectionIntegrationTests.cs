using Dapper;
using SkiaSharp;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkSelectionIntegrationTests
{
    [Fact]
    public async Task Service_shares_confirmed_copies_but_keeps_expansions_and_unlinked_works_separate()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var parent = await works.InsertAsync(new Work { Name = "Game" });
        var child = await works.InsertAsync(new Work { Name = "Other store copy" });
        var expansion = await works.InsertAsync(new Work { Name = "Expansion" });
        var links = new IdentityLinkRepository(db.Factory);
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = parent, ChildWorkIds = [child] });
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent, ChildWorkIds = [expansion], Kind = IdentityLinkKinds.ExpansionOf
        });
        var choices = new ArtworkChoiceRepository(db.Factory);
        var service = new ArtworkSelectionService(choices, links);
        await service.SaveAsync(new ArtworkChoice
        {
            WorkId = child, Slot = ArtworkSlot.Icon, AssetKey = "winnow://user-art/icon",
            SourceId = "plugin", AssetId = "icon"
        });
        Assert.Equal(child, (await service.GetAsync(parent, ArtworkSlot.Icon))!.WorkId);
        Assert.Null(await service.GetAsync(expansion, ArtworkSlot.Icon));
        Assert.Equal(child, Assert.Single(await choices.GetAllAsync()).WorkId);

        await links.RetractLinkAsync(child);
        Assert.Null(await service.GetAsync(parent, ArtworkSlot.Icon));
        Assert.NotNull(await service.GetAsync(child, ArtworkSlot.Icon));
    }

    [Theory]
    [InlineData(WorkFields.CoverUrl, ArtworkSlot.Cover)]
    [InlineData(WorkFields.BackgroundUrl, ArtworkSlot.Hero)]
    public async Task Configured_imports_survive_refresh_and_reopen_then_reset_the_group_slot(
        string field, ArtworkSlot slot)
    {
        using var db = new TempDatabase();
        var root = Path.Combine(Path.GetTempPath(), "winnow-artwork-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var bitmap = new SKBitmap(24, 16);
            bitmap.Erase(SKColors.Teal);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = encoded.ToArray();
            var source = Path.Combine(root, "chosen.png");
            await File.WriteAllBytesAsync(source, bytes);
            var options = new CoverCacheOptions { CacheDirectory = Path.Combine(root, "covers") };
            var store = new UserArtStore(options);
            var works = new WorkRepository(db.Factory);
            var parent = await works.InsertAsync(new Work { Name = "Game", CoverUrl = "https://example.com/cover", BackgroundUrl = "https://example.com/hero" });
            var child = await works.InsertAsync(new Work { Name = "Copy" });
            var links = new IdentityLinkRepository(db.Factory);
            await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = parent, ChildWorkIds = [child] });
            var choices = new ArtworkChoiceRepository(db.Factory);
            var selections = new ArtworkSelectionService(choices, links);
            var fields = new WorkFieldSourceRepository(db.Factory);
            var editor = new WorkMetadataEditService(works, fields, art: store, selections: selections);

            Assert.Equal(WorkArtEditOutcome.Applied, await editor.SetArtFromFileAsync(child, field, source));
            var selected = (await selections.GetAsync(parent, slot))!;
            Assert.Equal(child, selected.WorkId);
            Assert.Equal("user", selected.SourceId);
            Assert.Equal(ArtworkChoiceKind.Manual, selected.Kind);
            File.Delete(source);

            // A later provider observation must not replace the retained selection.
            using (var connection = db.Factory.Open())
                connection.Execute("UPDATE works SET cover_url = 'https://example.com/new', background_url = 'https://example.com/new' WHERE id = @parent;", new { parent });
            var reopened = new WorkMetadataEditService(works, fields,
                art: new UserArtStore(options),
                selections: new ArtworkSelectionService(new ArtworkChoiceRepository(db.Factory), links));
            var snapshot = await reopened.GetAsync(parent);
            var artField = Assert.Single(snapshot!.Fields, value => value.Field == field);
            Assert.Equal(selected.AssetKey, artField.Value);
            Assert.True(artField.IsUserOwned);
            Assert.True(new UserArtStore(options).TryRead(UserArtRef.Token(selected.AssetKey)!, out var retained));
            Assert.Equal(bytes, retained);

            Assert.Equal(WorkArtEditOutcome.FileNotFound,
                await reopened.SetArtFromFileAsync(parent, field, source));
            await File.WriteAllBytesAsync(source, bytes[..16]);
            Assert.Equal(WorkArtEditOutcome.NotAnImage,
                await reopened.SetArtFromFileAsync(parent, field, source));
            Assert.Equal(selected, await selections.GetAsync(parent, slot));

            // Two valid one-pixel GIF frames exercise animation refusal after the
            // legacy importer has accepted the header and copied the input.
            var animated = Convert.FromHexString(
                "47494638396101000100800000000000FFFFFF" +
                "2C0000000001000100000202440100" +
                "2C00000000010001000002024401003B");
            using (var animationData = SKData.CreateCopy(animated))
            using (var animation = SKCodec.Create(animationData))
                Assert.Equal(2, animation.FrameCount);
            await File.WriteAllBytesAsync(source, animated);
            Assert.Equal(WorkArtEditOutcome.NotAnImage,
                await reopened.SetArtFromFileAsync(parent, field, source));
            Assert.Equal(selected.AssetKey, (await selections.GetAsync(parent, slot))!.AssetKey);
            await selections.SaveAsync(new ArtworkChoice
            {
                WorkId = parent, Slot = ArtworkSlot.Icon, AssetKey = selected.AssetKey,
                SourceId = "user", AssetId = "icon"
            });
            Assert.Equal(WorkFieldEditOutcome.Applied, await reopened.ResetFieldAsync(parent, field));
            Assert.Null(await selections.GetAsync(parent, slot));
            Assert.Null(await selections.GetAsync(child, slot));
            Assert.NotNull(await selections.GetAsync(parent, ArtworkSlot.Icon));
            var automatic = Assert.Single((await reopened.GetAsync(parent))!.Fields, value => value.Field == field);
            Assert.Equal("https://example.com/new", automatic.Value);
            Assert.False(automatic.IsUserOwned);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
