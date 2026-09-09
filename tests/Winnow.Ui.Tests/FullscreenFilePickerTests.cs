using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenFilePickerTests
{
    [AvaloniaFact]
    public async Task Saving_an_existing_file_requires_confirmation_and_picker_never_writes_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-tv-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "report.csv");
        await File.WriteAllTextAsync(destination, "keep this until export succeeds");
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            var completion = new TaskCompletionSource<string?>();
            var picker = new FullscreenFilePage(context, "Export", null, completion, directory, "report.csv");
            context.Push(picker);
            for (var attempt = 0; attempt < 100 && !picker.GetVisualDescendants().OfType<Button>().Any(); attempt++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            picker.FocusInitial(); picker.Handle(GamepadButtons.Down); picker.Handle(GamepadButtons.Accept);
            Assert.Equal("Replace report.csv?", view.CurrentPage.Title);
            Assert.False(completion.Task.IsCompleted);
            Dispatcher.UIThread.RunJobs();
            view.CurrentPage.FocusInitial(); view.Handle(GamepadButtons.Accept);
            Assert.Same(picker, view.CurrentPage);
            Assert.False(completion.Task.IsCompleted);
            picker.FocusInitial(); picker.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            view.CurrentPage.FocusInitial(); view.Handle(GamepadButtons.Down); view.Handle(GamepadButtons.Accept);
            Assert.Equal(destination, await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("keep this until export succeeds", await File.ReadAllTextAsync(destination));
        }
        finally { window.Close(); File.Delete(destination); Directory.Delete(directory); }
    }

    [AvaloniaFact]
    public async Task Controller_selects_only_allowed_files_and_back_completes_cancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-tv-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "cover.PNG");
        await File.WriteAllTextAsync(image, "read-only selection fixture");
        await File.WriteAllTextAsync(Path.Combine(directory, "unrelated.exe"), "never execute");
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            var completion = new TaskCompletionSource<string?>();
            var page = new FullscreenFilePage(context, "Choose cover", [".png"], completion, directory);
            context.Push(page);
            for (var attempt = 0; attempt < 100 && !page.GetVisualDescendants().OfType<Button>().Any(); attempt++)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var labels = page.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()).ToArray();
            Assert.Contains("File   cover.PNG", labels);
            Assert.DoesNotContain("File   unrelated.exe", labels);
            page.FocusInitial();
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Accept);
            Assert.Equal(image, await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("read-only selection fixture", await File.ReadAllTextAsync(image));
            var cancelled = new TaskCompletionSource<string?>();
            context.Push(new FullscreenFilePage(context, "Choose cover", null, cancelled, directory));
            view.Handle(GamepadButtons.Back);
            Assert.Null(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            window.Close();
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
