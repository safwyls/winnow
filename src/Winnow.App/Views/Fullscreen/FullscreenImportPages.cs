using System.Text;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenPurchaseHistoryPage : FullscreenPage
{
    private readonly SteamAccountImportViewModel? _model;
    private readonly StackPanel _results = new() { Spacing = 16 };
    private bool _disposed;
    private Button? _readResults;
    public override string Title => "Purchase history";
    public FullscreenPurchaseHistoryPage(FullscreenContext context) : base(context)
    {
        _model = context.Services is { } services ? ActivatorUtilities.CreateInstance<SteamAccountImportViewModel>(services, new FullscreenSavedPagesPicker(context)) : null;
        var body = FullscreenUi.Stack(FullscreenUi.Text(Title, 64));
        var controls = new List<Control[]>();
        if (_model is { } model)
        {
            body.Children.Add(FullscreenUi.Text(model.IntroMessage));
            body.Children.Add(FullscreenUi.Text(model.SignInRouteExplanation));
            var signIn = FullscreenUi.Button(model.SignInRouteButtonText, async () =>
            {
                if (!model.ImportFromSignInCommand.CanExecute(null)) return;
                _results.Children.Clear(); _results.Children.Add(FullscreenUi.Text(model.SignInBusyMessage));
                try { await model.ImportFromSignInCommand.ExecuteAsync(null); await context.RefreshAsync(); }
                catch (Exception) { model.ProblemMessage = "Couldn't import purchase history. Try again."; }
                ShowResults();
            });
            body.Children.Add(signIn); controls.Add([signIn]);
            body.Children.Add(FullscreenUi.Text(model.SavedPagesRouteExplanation));
            var saved = FullscreenUi.Button(model.SavedPagesRouteButtonText, async () =>
            {
                if (!model.ImportFromSavedPagesCommand.CanExecute(null)) return;
                try { await model.ImportFromSavedPagesCommand.ExecuteAsync(null); await context.RefreshAsync(); }
                catch (Exception) { model.ProblemMessage = "Couldn't read the saved pages. Try again."; }
                ShowResults();
            });
            body.Children.Add(saved); controls.Add([saved]);
            _readResults = FullscreenUi.Button("Read import results", () => context.Push(new FullscreenDetailsReadingPage(context, "Import results", string.Join("\n\n", _results.Children.OfType<TextBlock>().Select(text => text.Text)))));
            _readResults.IsEnabled = false;
            body.Children.Add(_readResults); controls.Add([_readResults]);
            body.Children.Add(FullscreenUi.Text(model.SavedPagesHintMessage, 24, "TextDim"));
            body.Children.Add(FullscreenUi.Text(model.SavedPagesLicensesHintMessage, 24, "TextDim"));
            body.Children.Add(_results);
            AttachedToVisualTree += async (_, _) =>
            {
                try { await model.RefreshCommand.ExecuteAsync(null); if (!_disposed) signIn.IsEnabled = model.SignInRouteAvailable; }
                catch (Exception) { if (!_disposed) context.Notify("Couldn't read the Steam connection status. Reopen this page to try again."); }
            };
        }
        else body.Children.Add(FullscreenUi.Text("Purchase history import is unavailable."));
        var back = FullscreenUi.Button("Back", context.Back); body.Children.Add(back); controls.Add([back]);
        Content = FullscreenUi.Scroll(body); SetFocusRows(controls.ToArray());
    }

    private void ShowResults()
    {
        if (_disposed || _model is not { } model) return;
        _results.Children.Clear();
        foreach (var message in new[] { model.NoticeMessage, model.ProblemMessage, model.HistoryTruncationMessage, model.LicensesTruncationMessage })
            if (!string.IsNullOrWhiteSpace(message)) _results.Children.Add(FullscreenUi.Text(message));
        if (model.HasDuplicatePages) _results.Children.Add(FullscreenUi.Text(model.DuplicatePagesMessage));
        if (model.ShowLicensesCountMismatch) _results.Children.Add(FullscreenUi.Text(model.LicensesCountMismatchMessage));
        if (model.ShowNothingApplied) _results.Children.Add(FullscreenUi.Text(model.NothingAppliedMessage));
        foreach (var file in model.PickedFiles) _results.Children.Add(FullscreenUi.Text($"{file.Name} · {file.Outcome}", 24, "TextDim"));
        foreach (var row in model.Counts.Concat(model.Skipped)) _results.Children.Add(FullscreenHistoryTypography.Data($"{row.Label}     {row.Value}", 28));
        if (_readResults is not null) _readResults.IsEnabled = true;
        _results.BringIntoView();
    }
    public override void Dispose() { _disposed = true; _model?.ImportFromSignInCommand.Cancel(); _model?.ImportFromSavedPagesCommand.Cancel(); base.Dispose(); }
}

internal sealed class FullscreenSavedPagesPicker(FullscreenContext context) : ISteamAccountPageFilePicker
{
    public async Task<IReadOnlyList<string>> PickAsync(string title, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Push(new FullscreenSavedPagesPage(context, title, completion));
        return await completion.Task.WaitAsync(ct);
    }
}

internal sealed class FullscreenSavedPagesPage : FullscreenPage
{
    private readonly TaskCompletionSource<IReadOnlyList<string>> _completion;
    private readonly List<string> _paths = [];
    private readonly TextBlock _picked;
    public override string Title => "Saved Steam pages";
    public FullscreenSavedPagesPage(FullscreenContext context, string title, TaskCompletionSource<IReadOnlyList<string>> completion) : base(context)
    {
        _completion = completion;
        _picked = FullscreenUi.Text("Choose the saved purchase history and licences pages.");
        var add = FullscreenUi.Button("Choose a page", async () =>
        {
            var path = await context.PickFile(title, [".html", ".htm"]);
            if (path is not null && !_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _paths.Add(path);
            _picked.Text = string.Join("\n", _paths.Select(Path.GetFileName));
        });
        var read = FullscreenUi.Button("Read selected pages", () => { _completion.TrySetResult(_paths.ToArray()); context.Back(); });
        var cancel = FullscreenUi.Button("Cancel", context.Back);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(Title, 64), _picked, add, read, cancel));
        SetFocusRows([add], [read, cancel]);
    }
    public override void Dispose() { _completion.TrySetResult([]); base.Dispose(); }
}

internal sealed class FullscreenExecutablePicker(FullscreenContext context) : IExecutableFilePicker
{
    public Task<string?> PickAsync(string title, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return context.PickFile(title, OperatingSystem.IsWindows() ? [".exe"] : null); }
}

internal sealed class FullscreenAcquisitionDestination(FullscreenContext context) : IAcquisitionExportDestination
{
    public async Task<bool> SaveAsync(string csv, CancellationToken ct = default)
    {
        var path = await context.SaveFile("Export acquisition CSV", "winnow-acquisitions.csv");
        if (path is null) return false;
        await File.WriteAllTextAsync(path, csv, new UTF8Encoding(true), ct);
        return true;
    }
}
