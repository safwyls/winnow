using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Ingest.Steam.AccountPages;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SavedLicensePagesTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_presentations_import_two_saved_pages_and_repeat_without_duplicate_facts(bool fullscreen)
    {
        using var db = new TempDatabase();
        var directory = Directory.CreateTempSubdirectory("winnow-saved-licences").FullName;
        var first = Path.Combine(directory, "first.html");
        var second = Path.Combine(directory, "second.html");
        await File.WriteAllTextAsync(first, Page("Alpha", 1));
        await File.WriteAllTextAsync(second, Page("Beta", 2));
        var facts = new AccountFactRepository(db.Factory);
        var importer = new SteamAccountPageImportService(new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), facts, new AccountAcquisitionRepository(db.Factory),
            db.Factory, new LibrarySyncGate(), NullLogger<SteamAccountPageImportService>.Instance);
        var recording = new RecordingImporter(importer);
        var services = new ServiceCollection().AddSingleton<ISteamAccountPageImport>(recording)
            .AddSingleton<ISteamAccountPageFileLoader, SteamAccountPageFileLoader>().BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        var model = new SteamAccountImportViewModel(recording, new SteamAccountPageFileLoader(), new Picker([first, second]));
        using var page = new FullscreenPurchaseHistoryPage(context);
        Control view = fullscreen ? page : new SteamAccountImportView { DataContext = model };
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        var chosen = new Queue<string>();
        context.FilePicker = (_, _) => Task.FromResult<string?>(chosen.Dequeue());
        context.PageRequested += picker =>
        {
            window.Content = picker;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Click(picker, "Choose a page");
            Click(picker, "Choose a page");
            Click(picker, "Read selected pages");
            picker.Dispose();
        };
        context.BackRequested += () => window.Content = view;
        try
        {
            window.Show();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            for (var pass = 0; pass < 2; pass++)
            {
                chosen.Enqueue(first); chosen.Enqueue(second);
                Click(view, SteamAccountImportCopy.SavedPagesRouteButton);
                if (!fullscreen) await model.ImportFromSavedPagesCommand.ExecutionTask!;
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    Dispatcher.UIThread.RunJobs();
                    if (recording.Calls > pass
                        && (!fullscreen || view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "second.html · LOADED"))) break;
                    await Task.Delay(10);
                }
                Assert.Equal(2, (await facts.GetLicensesAsync("steam")).Count);
                Assert.Equal(pass + 1, recording.Calls);
                if (fullscreen)
                    Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "second.html · LOADED");
                else
                {
                    Assert.All(model.PickedFiles, file => Assert.Equal("LOADED", file.Outcome));
                    Assert.False(model.HasDuplicatePages);
                }
            }
        }
        finally { window.Close(); services.Dispose(); Directory.Delete(directory, true); }
    }

    private static void Click(Control view, string text)
    {
        var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (button.Command?.CanExecute(button.CommandParameter) == true) button.Command.Execute(button.CommandParameter);
    }

    private static string Page(string title, int page) => $"""
        <div class="license_paginator_ctn"><span>Showing licenses {page}-{page} of 2</span></div>
        <table class="account_table"><tr><th class="license_date_col">Date</th><th>Item</th></tr>
        <tr><td class="license_date_col">Sep 1, 2026</td><td>{title}</td><td class="license_acquisition_col">Steam Store</td></tr></table>
        """;

    private sealed class Picker(IReadOnlyList<string> paths) : ISteamAccountPageFilePicker
    {
        public Task<IReadOnlyList<string>> PickAsync(string title, CancellationToken ct = default) => Task.FromResult(paths);
    }

    private sealed class RecordingImporter(ISteamAccountPageImport inner) : ISteamAccountPageImport
    {
        public int Calls { get; private set; }
        public async Task<SteamAccountPageImportReport> ImportAsync(SteamAccountPages pages, CancellationToken ct = default)
        {
            var report = await inner.ImportAsync(pages, ct);
            Calls++;
            return report;
        }
    }
}
