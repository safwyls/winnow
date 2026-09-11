using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ManualEntryFormTests
{
    [AvaloniaTheory]
    [InlineData(false, "correction")]
    [InlineData(true, "correction")]
    [InlineData(false, "legacy")]
    [InlineData(true, "legacy")]
    [InlineData(false, "store")]
    [InlineData(true, "store")]
    [InlineData(false, "mapping")]
    [InlineData(true, "mapping")]
    public async Task Saving_on_each_surface_corrects_tracked_ids_or_explains_why_the_form_stays_open(bool fullscreen, string scenario)
    {
        using var db = new TempDatabase();
        var manual = new ManualEntryRepository(db.Factory);
        var pins = new WorkIgdbPinRepository(db.Factory);
        var entry = await manual.CreateAsync(new() { Title = "Original", IgdbId = 333, SteamAppId = "123" });
        var model = new LibrarySettingsViewModel(new HiddenGameRepository(db.Factory), manual,
            new LibraryQueryRepository(db.Factory), new SettingsRepository(db.Factory));
        await model.RefreshAsync();
        var staleRow = Assert.Single(model.ManualEntries);
        // Opening refreshes a stale settings row, including the authoritative ID.
        await pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 444, Name = "Chosen title" });
        await model.BeginEditCommand.ExecuteAsync(staleRow);
        Assert.Equal("444", model.DraftIgdbId);
        Assert.Equal("Chosen title", model.DraftTitle);

        if (scenario == "legacy")
        {
            using var connection = db.Factory.Open();
            connection.Execute("DELETE FROM manual_entry_identifiers;");
        }
        else if (scenario == "store")
        {
            await new OwnershipRepository(db.Factory).InsertAsync(new() { ReleaseId = entry.ReleaseId, Store = "steam" });
        }
        else if (scenario == "mapping")
        {
            await pins.PinAsync(new() { WorkId = entry.WorkId, IgdbId = 555, Name = "Later choice" });
        }

        model.DraftTitle = "Corrected title";
        model.DraftYear = "2020";
        model.DraftSteamAppId = "456";
        model.DraftIgdbId = "666";
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var tv = fullscreen ? new FullscreenManualGamePage(context, model) : null;
        Control view = tv is not null ? tv : new LibrarySettingsView { DataContext = model };
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var save = view.GetVisualDescendants().OfType<Button>()
                .Single(button => fullscreen ? Equals(button.Content, "Save") : button.Command == model.SaveFormCommand);
            save.Focus();
            if (tv is not null) tv.Handle(GamepadButtons.Accept);
            else
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            if (model.SaveFormCommand.ExecutionTask is { } pending) await pending;
            Dispatcher.UIThread.RunJobs();

            var stored = (await manual.GetAsync(entry.OwnershipId))!;
            if (scenario == "correction")
            {
                Assert.False(model.IsFormOpen);
                Assert.Equal("Corrected title", stored.Title);
                Assert.Equal(2020, stored.FirstReleaseYear);
                Assert.Equal(666, stored.IgdbId);
                Assert.Equal("456", stored.SteamAppId);
            }
            else
            {
                Assert.True(model.IsFormOpen);
                Assert.Equal("Corrected title", model.DraftTitle);
                Assert.Equal("456", model.DraftSteamAppId);
                var error = scenario == "mapping" ? model.IgdbIdError : model.SteamAppIdError;
                Assert.NotNull(error);
                Assert.Contains(scenario == "mapping" ? "Cancel and reopen" : "Keep it to edit details", error, StringComparison.Ordinal);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.IsVisible && text.Text == error);
                Assert.Equal("123", stored.SteamAppId);
                Assert.Equal(scenario == "mapping" ? 555 : 444, stored.IgdbId);
            }
        }
        finally { window.Close(); }
    }
}
