using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ArtworkBrowserTests
{
    [AvaloniaFact]
    public async Task Fullscreen_triggers_cycle_artwork_slots_and_preserve_their_browsing_state()
    {
        var service = new BrowserService { CandidateCount = 20 };
        using var model = new ArtworkBrowserViewModel(service, 1, "Game");
        await model.OpenAsync();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenArtworkPage(context, model);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show(); context.Push(page);
        try
        {
            Flush();
            var selected = model.Sources[0].Items[0];
            model.SelectCommand.Execute(selected); Flush();
            ScrollViewer Gallery() => page.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ArtworkCandidateScroll");
            Gallery().Offset = new(0, 150); Flush();
            var offset = Gallery().Offset.Y;
            foreach (var slot in new[] { ArtworkSlot.Cover, ArtworkSlot.Icon, ArtworkSlot.Hero })
            {
                view.Handle(GamepadButtons.PageNext); Flush();
                Assert.Equal(slot, model.Slot);
                Assert.Equal($"slot:{slot}", AutomationProperties.GetAutomationId(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
            }
            Assert.Same(selected, model.Selected);
            Assert.Equal(offset, Gallery().Offset.Y, 1);
            foreach (var slot in new[] { ArtworkSlot.Icon, ArtworkSlot.Cover, ArtworkSlot.Hero })
            {
                view.Handle(GamepadButtons.PagePrevious); Flush();
                Assert.Equal(slot, model.Slot);
            }
            model.IsBusy = true;
            view.Handle(GamepadButtons.PageNext); Flush();
            view.Handle(GamepadButtons.PagePrevious); Flush();
            Assert.Equal(ArtworkSlot.Hero, model.Slot);
            model.IsBusy = false;
            Assert.Empty(service.Writes);
            Assert.Empty(service.Resets);
            Capture(window, "artwork-fullscreen-trigger-slots");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_artwork_source_uses_the_shared_link_router()
    {
        var router = new SourceRouter();
        var browser = new ArtworkBrowserViewModel(new BrowserService { IncludeAttribution = true }, 1, "Game");
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, artworkBrowser: browser, linkRouter: router);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1280, Height = 820, Content = view };
        window.Show();
        try
        {
            await browser.OpenAsync();
            browser.SelectCommand.Execute(browser.Sources[0].Items[0]); Flush();
            var source = Named(view, "Open artwork source");
            source.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
            Assert.Equal("https://www.steamgriddb.com/grid/1", Assert.Single(router.Links).Uri);
            Assert.Equal("Opened in your browser.", browser.Problem);
        }
        finally { window.Close(); }
    }

    private sealed class SourceRouter : IGameLinkRouter
    {
        public List<GameLink> Links { get; } = [];
        public Task<LinkOpenResult> OpenAsync(GameLink link, string title)
        {
            Links.Add(link);
            return Task.FromResult(new LinkOpenResult(true, "Opened in your browser."));
        }
    }

    [AvaloniaFact]
    public async Task Preview_and_back_do_not_write_and_apply_changes_only_the_selected_slot()
    {
        var service = new BrowserService();
        var refreshes = 0;
        using var model = new ArtworkBrowserViewModel(service, 42, "Game", afterCommit: _ => { refreshes++; return Task.CompletedTask; });
        await model.OpenAsync(ArtworkSlot.Cover);
        model.SelectCommand.Execute(model.Sources[0].Items[0]);
        Assert.Empty(service.Writes);
        model.Close(); Assert.Empty(service.Writes);
        await model.OpenAsync(ArtworkSlot.Icon);
        model.SelectCommand.Execute(model.Sources[0].Items[0]);
        await model.ApplyCommand.ExecuteAsync(null);
        Assert.Equal([(42L, ArtworkSlot.Icon)], service.Writes);
        Assert.Equal(1, refreshes);
        Assert.True(model.Current!.IsCurrent);
        Assert.False(model.CanApply);
        await model.AutomaticCommand.ExecuteAsync(null);
        Assert.Equal([(42L, ArtworkSlot.Icon)], service.Resets);
    }

    [AvaloniaFact]
    public async Task Sources_fail_independently_retry_and_page_without_replacing_selection()
    {
        var service = new BrowserService { FailIgdb = true };
        using var model = new ArtworkBrowserViewModel(service, 1, "Game");
        await model.OpenAsync();
        var steam = model.Sources[0]; var igdb = model.Sources[1];
        Assert.Single(steam.Items); Assert.True(igdb.CanRetry);
        var selected = steam.Items[0]; model.SelectCommand.Execute(selected);
        await steam.MoreCommand.ExecuteAsync(null);
        Assert.Equal(2, steam.Items.Count); Assert.Same(selected, model.Selected);
        service.FailIgdb = false; await igdb.RetryCommand.ExecuteAsync(null);
        Assert.Single(igdb.Items); Assert.False(igdb.CanRetry);
        service.FailSave = true; await model.ApplyCommand.ExecuteAsync(null);
        Assert.NotNull(model.Problem); Assert.Empty(service.Writes); Assert.Same(selected, model.Selected);
    }

    [AvaloniaFact]
    public async Task Closing_ignores_late_provider_results_and_unsupported_icon_is_explicit()
    {
        var service = new BrowserService { PendingSteam = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var model = new ArtworkBrowserViewModel(service, 1, "Game");
        var opening = model.OpenAsync();
        model.Close();
        service.PendingSteam.SetResult(new([Candidate("steam", ArtworkSlot.Hero, "late")]));
        await opening;
        Assert.Empty(model.Sources[0].Items);
        service.PendingSteam = null;
        await model.OpenAsync(ArtworkSlot.Icon);
        Assert.Contains("does not offer icon", model.Sources[1].Message);
        Assert.Empty(model.Sources[1].Items);
    }

    [AvaloniaFact]
    public async Task Rapid_reopen_restarts_loading_and_retains_previews_across_slots()
    {
        var late = new TaskCompletionSource<ArtworkBrowserPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new BrowserService { PendingSteam = late };
        using var model = new ArtworkBrowserViewModel(service, 1, "Game");
        var old = model.OpenAsync(); model.Close(); service.PendingSteam = null;
        await model.OpenAsync();
        var chosen = Assert.Single(model.Sources[0].Items);
        model.SelectCommand.Execute(chosen);
        model.ChooseSourceCommand.Execute("steam");
        await model.ChooseSlotCommand.ExecuteAsync(ArtworkSlot.Cover);
        await model.ChooseSlotCommand.ExecuteAsync(ArtworkSlot.Hero);
        Assert.Same(chosen, model.Selected); Assert.Equal("steam", model.SourceId);
        late.SetResult(new([Candidate("steam", ArtworkSlot.Hero, "late")])); await old;
        Assert.Same(chosen, Assert.Single(model.Sources[0].Items)); Assert.False(model.Sources[0].IsLoading);
        model.Close();
        await service.ImportUrlAsync(1, ArtworkSlot.Hero, "external-edit");
        await model.OpenAsync(); Assert.Equal("external-edit", model.Current!.Candidate.AssetId);
    }

    [AvaloniaFact]
    public async Task Commit_refreshes_projection_before_current_and_distinguishes_refresh_failure()
    {
        var service = new BrowserService();
        var failRefresh = false;
        using var model = new ArtworkBrowserViewModel(service, 1, "Game", afterCommit: _ =>
        {
            if (failRefresh) throw new IOException("Refresh unavailable");
            service.CurrentProjection = Candidate("steam", ArtworkSlot.Hero, "refreshed-projection");
            return Task.CompletedTask;
        });
        await model.OpenAsync(); await model.AutomaticCommand.ExecuteAsync(null);
        Assert.Equal("refreshed-projection", model.Current!.Candidate.AssetId);
        failRefresh = true;
        model.SelectCommand.Execute(model.Sources[0].Items[0]); await model.ApplyCommand.ExecuteAsync(null);
        Assert.Single(service.Writes); Assert.Contains("Artwork saved", model.Problem);
        Assert.DoesNotContain("Could not save", model.Problem);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_slot_retains_its_browsing_position(bool fullscreen)
    {
        using var model = new ArtworkBrowserViewModel(new BrowserService { CandidateCount = 20 }, 1, "Game");
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        await model.OpenAsync();
        Control view = fullscreen ? new FullscreenArtworkPage(context, model) : new ArtworkBrowserView { DataContext = model };
        var window = new Window { Width = 1280, Height = 900, Content = view }; window.Show();
        ScrollViewer Gallery() => view.GetVisualDescendants().OfType<ScrollViewer>().Single(scroll => scroll.Name == (fullscreen ? "ArtworkCandidateScroll" : "CandidateScroll"));
        try
        {
            Flush(); Gallery().Offset = new(0, 150); Flush();
            var heroOffset = Gallery().Offset.Y; Assert.True(heroOffset > 0);
            await model.ChooseSlotCommand.ExecuteAsync(ArtworkSlot.Cover); Flush();
            Gallery().Offset = new(0, 90); Flush(); var coverOffset = Gallery().Offset.Y;
            await model.ChooseSlotCommand.ExecuteAsync(ArtworkSlot.Hero); Flush();
            Assert.Equal(heroOffset, Gallery().Offset.Y, 1);
            await model.ChooseSlotCommand.ExecuteAsync(ArtworkSlot.Cover); Flush();
            Assert.Equal(coverOffset, Gallery().Offset.Y, 1);
        }
        finally { window.Close(); (view as IDisposable)?.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Desktop_details_host_restores_more_focus_on_escape()
    {
        using var pixels = ArtworkPixels();
        var browser = new ArtworkBrowserViewModel(new BrowserService(), 1, "A distant shore", new Leases(new CoverArt(pixels, pixels)));
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, artworkBrowser: browser);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1280, Height = 900, Content = view };
        window.Show();
        try
        {
            await browser.OpenAsync(); Flush();
            browser.SelectCommand.Execute(browser.Sources[0].Items[0]); Flush();
            Assert.True(view.FindControl<ArtworkBrowserView>("ArtworkBrowserView")!.IsEffectivelyVisible);
            Capture(window, "artwork-desktop-details");
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Flush();
            Assert.False(browser.IsOpen);
            Assert.Same(view.FindControl<Button>("MoreActionsButton"), window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_artwork_row_restores_its_browse_button_and_unsaved_metadata_draft()
    {
        var browser = new ArtworkBrowserViewModel(new BrowserService(), 1, "Game");
        var service = new MetadataService();
        var editor = new GameMetadataEditorViewModel(service, 1, service.Snapshot, artworkBrowser: browser);
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, metadataEditor: editor, artworkBrowser: browser);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1280, Height = 900, Content = view };
        window.Show();
        try
        {
            await editor.OpenCommand.ExecuteAsync(null); Flush();
            editor.Rows[0].Draft = "Unfinished title";
            var row = editor.Rows.OfType<MetadataArtRowViewModel>().First(art => art.IsCover);
            var browse = Named(view, row.BrowseAutomationName); browse.BringIntoView(); Flush(); browse.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
            Assert.True(browser.IsOpen); Assert.Equal(ArtworkSlot.Cover, browser.Slot);
            Assert.True(view.FindControl<Border>("MetadataOverlay")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<Border>("MetadataOverlay")!.IsEffectivelyEnabled);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Flush();
            Assert.True(editor.IsOpen); Assert.False(browser.IsOpen);
            Assert.Same(browse, window.FocusManager!.GetFocusedElement());
            Assert.Equal("Unfinished title", editor.Rows[0].Draft);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Flush();
            Assert.False(editor.IsOpen);
            Assert.Same(view.FindControl<Button>("MoreActionsButton"), window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1200, 640)]
    [InlineData(1280, 820)]
    [InlineData(1920, 1080)]
    [InlineData(800, 700)]
    [InlineData(940, 820)]
    public async Task Metadata_overlay_bounds_fields_and_keeps_navigation_reachable(int width, int height)
    {
        var service = new MetadataService();
        var browser = new ArtworkBrowserViewModel(new BrowserService(), 1, "Game");
        var snapshot = width == 940 ? service.Snapshot with
        {
            Fields = service.Snapshot.Fields.Select(field => field with { Source = FieldSources.User }).ToArray()
        } : service.Snapshot;
        var editor = new GameMetadataEditorViewModel(service, 1, snapshot, picker: new ImagePicker(), artworkBrowser: browser);
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, metadataEditor: editor, artworkBrowser: browser);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = width, Height = height, Content = view };
        if (width == 940) ThemeTypographyResources.Apply(window.Resources, Winnow.App.Themes.ThemeTypography.Default with { SizePercent = 120 });
        window.Show();
        try
        {
            await editor.OpenCommand.ExecuteAsync(null); Flush();
            var overlay = view.FindControl<Border>("MetadataOverlay")!;
            var form = view.FindControl<GameMetadataEditorView>("MetadataEditorView")!;
            var scroll = form.FindControl<ScrollViewer>("MetadataFormScroll")!;
            var back = view.FindControl<Button>("MetadataBackButton")!;
            Assert.False(view.FindControl<Border>("Card")!.IsEffectivelyEnabled);
            AssertVisibleInside(overlay, window);
            AssertVisibleInside(back, overlay);
            AssertVisibleInside(scroll, overlay);
            Assert.InRange(overlay.Bounds.Width, Math.Min(width - 48, 1440) - 1, Math.Min(width - 48, 1440) + 1);
            Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
            for (var index = 0; index < 30; index++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Flush();
                Assert.Contains(overlay, Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement()).GetVisualAncestors());
            }
            for (var index = 0; index < 30; index++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift); Flush();
                Assert.Contains(overlay, Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement()).GetVisualAncestors());
            }
            back.Focus(); scroll.ScrollToHome(); Flush();
            Capture(window, $"metadata-overlay-{width}x{height}");
            editor.Rows.Single(row => row.Field == WorkFields.Summary).Draft = string.Join(" ", Enumerable.Repeat("A longer description that should wrap comfortably without widening the editor.", 15));
            var year = editor.Rows.Single(row => row.Field == WorkFields.FirstReleaseYear);
            year.Draft = "not a year";
            await year.SaveCommand.ExecuteAsync(null); Flush();
            Assert.True(year.HasProblem);
            Capture(window, $"metadata-overlay-validation-{width}x{height}");
            var position = back.TranslatePoint(default, window);
            scroll.ScrollToEnd(); Flush();
            Assert.Equal(position, back.TranslatePoint(default, window));
            foreach (var field in form.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible))
            {
                field.BringIntoView(); Flush();
                AssertVisibleInside(field, scroll);
            }
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Flush();
            Assert.False(editor.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1280, 820)]
    [InlineData(1200, 640)]
    [InlineData(1920, 1080)]
    public async Task Desktop_overlay_keeps_preview_and_actions_visible_while_only_gallery_scrolls(int width, int height)
    {
        var service = new BrowserService { CandidateCount = 24, IncludeAttribution = true };
        using var pixels = ArtworkPixels();
        var model = new ArtworkBrowserViewModel(service, 1, "A distant shore", new Leases(new CoverArt(pixels, pixels)), new ImagePicker());
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, artworkBrowser: model);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        try
        {
            await model.OpenAsync(); Flush();
            var overlay = view.FindControl<Border>("ArtworkOverlay")!;
            var card = view.FindControl<Border>("Card")!;
            var browser = view.FindControl<ArtworkBrowserView>("ArtworkBrowserView")!;
            Assert.False(card.IsEffectivelyEnabled);
            Assert.DoesNotContain(card, overlay.GetVisualAncestors());
            Assert.InRange(overlay.Bounds.Width, Math.Min(width - 48, 1440) - 1, Math.Min(width - 48, 1440) + 1);
            Assert.True(overlay.Bounds.Width > card.Bounds.Width);
            Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == details.Title);
            AssertVisibleInside(view.FindControl<Button>("ArtworkBackButton")!, overlay);
            var preview = browser.FindControl<Control>("PreviewPanel")!;
            var gallery = browser.FindControl<ScrollViewer>("CandidateScroll")!;
            Assert.DoesNotContain(preview.GetVisualAncestors(), control => control is ScrollViewer);
            var apply = Named(browser, "Use artwork");
            foreach (var slot in new[] { ArtworkSlot.Hero, ArtworkSlot.Cover, ArtworkSlot.Icon })
            {
                await model.ChooseSlotCommand.ExecuteAsync(slot); Flush();
                var writesBeforePreview = service.Writes.Count;
                var candidate = browser.GetVisualDescendants().OfType<Button>().First(button => button.DataContext is ArtworkCandidateViewModel { IsCurrent: false });
                candidate.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
                Assert.True(apply.IsEnabled); Assert.Equal(writesBeforePreview, service.Writes.Count);
                await model.ApplyCommand.ExecuteAsync(null); Flush();
                Assert.Equal(writesBeforePreview + 1, service.Writes.Count);
                Assert.True(model.HasStatus); Assert.False(apply.IsEnabled);
                AssertVisibleInside(preview, overlay);
                AssertVisibleInside(apply, overlay);
                AssertVisibleInside(Named(browser, "Use automatic artwork"), overlay);
                AssertVisibleInside(Named(browser, "Import artwork URL"), overlay);
                AssertVisibleInside(Named(browser, "Choose artwork file"), overlay);
                AssertVisibleInside(Named(browser, "Open artwork source"), preview);
                foreach (var image in preview.GetVisualDescendants().OfType<Image>().Where(image => image.IsEffectivelyVisible))
                    AssertVisibleInside(image, preview);
                if (slot == ArtworkSlot.Hero)
                {
                    model.FullscreenCropCommand.Execute(null); Flush();
                    AssertVisibleInside(browser.FindControl<Image>("FullscreenHeroPreview")!, preview);
                    model.DesktopCropCommand.Execute(null); Flush();
                }
                var previewPosition = preview.TranslatePoint(default, window);
                var actionPosition = apply.TranslatePoint(default, window);
                gallery.Offset = new(0, 500); Flush();
                Assert.True(gallery.Offset.Y > 0);
                Assert.Equal(previewPosition, preview.TranslatePoint(default, window));
                Assert.Equal(actionPosition, apply.TranslatePoint(default, window));
                Capture(window, $"artwork-overlay-{width}x{height}-{slot.ToString().ToLowerInvariant()}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_overlay_cycles_keyboard_focus_and_back_restores_details_without_writing()
    {
        var service = new BrowserService();
        var browser = new ArtworkBrowserViewModel(service, 1, "Game");
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, artworkBrowser: browser);
        var view = new GameDetailsView { DataContext = details };
        var outside = new Button { Content = "Library action" };
        var host = new Grid(); host.Children.Add(outside); host.Children.Add(view);
        var window = new Window { Width = 1280, Height = 820, Content = host }; window.Show();
        try
        {
            await browser.OpenAsync(); Flush();
            var overlay = view.FindControl<Border>("ArtworkOverlay")!;
            var back = view.FindControl<Button>("ArtworkBackButton")!;
            browser.SelectCommand.Execute(browser.Sources[0].Items[0]); Flush();
            foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            {
                back.Focus(NavigationMethod.Tab); Flush();
                var visited = new HashSet<Control>();
                var returned = false;
                for (var step = 0; step < 60; step++)
                {
                    window.KeyPressQwerty(PhysicalKey.Tab, modifiers); Flush();
                    var focus = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
                    Assert.Contains(overlay, focus.GetVisualAncestors());
                    visited.Add(focus);
                    if (ReferenceEquals(focus, back)) { returned = true; break; }
                }
                Assert.True(returned); Assert.True(visited.Count >= 8);
            }
            back.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
            Assert.False(browser.IsOpen); Assert.Empty(service.Writes);
            Assert.True(view.FindControl<Border>("Card")!.IsEffectivelyEnabled);
            Assert.Same(view.FindControl<Button>("MoreActionsButton"), window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1.4)]
    public async Task Fullscreen_controller_can_select_and_preserves_candidate_focus_when_pages_arrive(bool reducedMotion, double textScale)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { ReducedMotion = reducedMotion, TextScale = textScale };
        using var pixels = ArtworkPixels();
        using var model = new ArtworkBrowserViewModel(new BrowserService(), 1, "Game", new Leases(new CoverArt(pixels, pixels)));
        await model.OpenAsync();
        using var page = new FullscreenArtworkPage(context, model);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        context.Push(page);
        try
        {
            Flush();
            var candidate = page.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "steam:first");
            candidate.Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.Equal("first", model.Selected!.Candidate.AssetId);
            await model.Sources[0].MoreCommand.ExecuteAsync(null); Flush();
            Assert.Equal("steam:first", AutomationProperties.GetAutomationId(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
            var apply = Named(page, "Use artwork"); Assert.True(apply.IsEnabled);
            var at = apply.TranslatePoint(default, window)!.Value;
            Assert.InRange(at.Y + apply.Bounds.Height * context.EffectiveUiScale, 0, window.ClientSize.Height);
            apply.Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.True(model.Current!.IsCurrent);
            Capture(window, $"artwork-fullscreen-motion-{reducedMotion}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_metadata_keeps_field_order_and_validation_navigation()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var service = new MetadataService();
        using var editor = new GameMetadataEditorViewModel(service, 1, service.Snapshot);
        await editor.OpenCommand.ExecuteAsync(null);
        using var page = new FullscreenDetailsMetadataPage(context, editor);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show(); context.Push(page);
        FullscreenPage? requested = null;
        context.PageRequested += value => requested = value;
        try
        {
            Flush();
            var fields = page.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetName(button) != "Back").ToArray();
            Assert.Equal(editor.Rows.Select(row => row.MenuLabel), fields.Select(AutomationProperties.GetName));
            Capture(window, "metadata-fullscreen-fields");
            var year = editor.Rows.Single(row => row.Field == WorkFields.FirstReleaseYear);
            Named(page, year.MenuLabel).Focus(); page.Handle(GamepadButtons.Accept); Flush();
            var fieldPage = Assert.IsType<FullscreenDetailsFieldPage>(requested);
            year.Draft = "invalid";
            await year.SaveCommand.ExecuteAsync(null); Flush();
            Assert.True(year.HasProblem);
            Capture(window, "metadata-fullscreen-validation");
            fieldPage.Handle(GamepadButtons.Back); Flush();
            Assert.Equal("", year.Draft);
            Assert.True(page.IsEffectivelyVisible);
            Assert.True(Named(page, year.MenuLabel).IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    }

    private static Button Named(Control root, string name) => root.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);
    private static void AssertVisibleInside(Control control, Control container)
    {
        Assert.True(control.IsEffectivelyVisible);
        var origin = control.TranslatePoint(default, container)!.Value;
        var far = control.TranslatePoint(new(control.Bounds.Width, control.Bounds.Height), container)!.Value;
        Assert.True(far.X > origin.X && far.Y > origin.Y, $"{control.Name ?? control.GetType().Name} must have visible area.");
        Assert.InRange(origin.X, -1, container.Bounds.Width + 1);
        Assert.InRange(origin.Y, -1, container.Bounds.Height + 1);
        Assert.InRange(far.X, 0, container.Bounds.Width + 1);
        Assert.InRange(far.Y, 0, container.Bounds.Height + 1);
    }
    private static void Flush() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); window.CaptureRenderedFrame()!.Save(Path.Combine(directory, name + ".png"));
    }
    private static ArtworkCandidate Candidate(string source, ArtworkSlot slot, string id) => new(source, source == "steam" ? "Steam" : "IGDB", id, slot, CoverKey.Steam("1")) { Width = 1920, Height = 1080 };
    private static RenderTargetBitmap ArtworkPixels()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(800, 450));
        var art = new Grid { Width = 800, Height = 450, Background = new LinearGradientBrush
        { StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(1, 1, RelativeUnit.Relative), GradientStops = [new(Color.Parse("#123846"), 0), new(Color.Parse("#D7A55B"), 1)] } };
        art.Children.Add(new TextBlock { Text = "A DISTANT SHORE", Foreground = Brushes.White, FontSize = 56,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        art.Measure(new(800, 450)); art.Arrange(new(0, 0, 800, 450)); bitmap.Render(art); return bitmap;
    }
    private sealed class Leases(CoverArt art) : ICoverLeases
    {
        public ICoverLease Acquire(CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor) => new Lease(key, art);
    }
    private sealed class Lease(CoverKey key, CoverArt art) : ICoverLease
    {
        public CoverKey Key => key;
        public int Width => 800;
        public CoverLayers Layers => CoverLayers.Vivid;
        public bool TryGetArt(out CoverArt value) { value = art; return true; }
        public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(art);
        public void Dispose() { }
    }
    private sealed class BrowserService : IArtworkBrowserService
    {
        public IReadOnlyList<ArtworkBrowserSource> Sources { get; } = [new("steam", "Steam", [ArtworkSlot.Hero, ArtworkSlot.Cover, ArtworkSlot.Icon]), new("igdb", "IGDB", [ArtworkSlot.Hero, ArtworkSlot.Cover])];
        public List<(long, ArtworkSlot)> Writes { get; } = [];
        public List<(long, ArtworkSlot)> Resets { get; } = [];
        public bool FailIgdb { get; set; }
        public bool FailSave { get; set; }
        public int CandidateCount { get; set; } = 1;
        public bool IncludeAttribution { get; set; }
        public TaskCompletionSource<ArtworkBrowserPage>? PendingSteam { get; set; }
        private ArtworkCandidate? _current;
        public ArtworkCandidate? CurrentProjection { get; set; }
        public Task<ArtworkCandidate?> GetCurrentAsync(long workId, ArtworkSlot slot, CancellationToken ct = default) => Task.FromResult(CurrentProjection ?? _current);
        public Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string sourceId, string? cursor = null, CancellationToken ct = default)
        {
            if (sourceId == "steam" && PendingSteam is not null) return PendingSteam.Task;
            if (sourceId == "igdb" && FailIgdb) throw new IOException("Unavailable");
            return Task.FromResult(new ArtworkBrowserPage(Enumerable.Range(0, CandidateCount).Select(index => Candidate(sourceId, slot,
                index == 0 ? cursor is null ? "first" : "second" : $"{cursor}:{index}") with
                { Creator = IncludeAttribution ? "Community artist with a longer display name" : null,
                    PageUrl = IncludeAttribution ? "https://www.steamgriddb.com/grid/1" : null }).ToArray(), cursor is null ? "page2" : null));
        }
        public Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkCandidate candidate, CancellationToken ct = default)
        { if (FailSave) return Task.FromResult(new ArtworkSaveResult(false, "Image unavailable. Try again.")); Writes.Add((workId, slot)); _current = candidate; return Task.FromResult(new ArtworkSaveResult(true, "Artwork saved.")); }
        public Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
        { Resets.Add((workId, slot)); _current = null; return Task.FromResult(new ArtworkSaveResult(true, "Using automatic artwork.")); }
        public Task<ArtworkSaveResult> ImportFileAsync(long workId, ArtworkSlot slot, string path, CancellationToken ct = default) => SaveAsync(workId, slot, Candidate("file", slot, path), ct);
        public Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, string url, CancellationToken ct = default) => SaveAsync(workId, slot, Candidate("url", slot, url), ct);
    }
    private sealed class ImagePicker : IImageFilePicker
    {
        public Task<string?> PickAsync(string title, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }
    private sealed class MetadataService : IWorkMetadataEditService
    {
        public WorkMetadataSnapshot Snapshot { get; } = new(1, "Game", false,
            WorkFields.All.Select(field => new WorkMetadataField(field, field == WorkFields.Name ? "Game" : null, null)).ToArray());
        public Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default) => Task.FromResult<WorkMetadataSnapshot?>(Snapshot);
        public Task<WorkFieldEditOutcome> SetFieldAsync(long workId, string field, string? value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkFieldEditOutcome> ResetFieldAsync(long workId, string field, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkArtEditOutcome> SetArtFromFileAsync(long workId, string field, string path, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WorkArtEditOutcome> SetArtFromUrlAsync(long workId, string field, string url, CancellationToken ct = default) => throw new NotSupportedException();
        public CoverKey? ArtKeyFor(string? value) => null;
    }
}
