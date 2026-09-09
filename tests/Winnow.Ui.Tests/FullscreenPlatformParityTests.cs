using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenPlatformParityTests
{
    private static FullscreenContext Context() => new(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);

    [AvaloniaFact]
    public void Api_key_draft_is_masked_local_and_erased_on_close()
    {
        var context = Context();
        var original = context.Shared.Stores.SteamApiKeyInput;
        var page = new FullscreenSteamApiKeyPage(context);
        var window = new Window { Content = page };
        try
        {
            window.Show();
            var field = page.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.NotEqual('\0', field.PasswordChar);
            field.Text = "unsaved-local-draft";
            Assert.Equal(original, context.Shared.Stores.SteamApiKeyInput);
            page.Dispose();
            Assert.Equal("", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Saved_page_selection_requires_read_and_cancel_returns_no_paths()
    {
        var context = Context();
        IReadOnlyList<string>? extensions = null;
        context.FilePicker = (_, types) => { extensions = types; return Task.FromResult<string?>("C:/temporary/history.html"); };
        var completion = new TaskCompletionSource<IReadOnlyList<string>>();
        var page = new FullscreenSavedPagesPage(context, "Select pages", completion);
        var window = new Window { Content = page };
        try
        {
            window.Show();
            var choose = page.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Choose a page"));
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { ".html", ".htm" }, extensions);
            Assert.False(completion.Task.IsCompleted);
            page.Dispose();
            Assert.Empty(await completion.Task);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Acquisition_export_cancels_without_writing_and_preserves_csv_encoding()
    {
        var context = Context();
        var destination = new FullscreenAcquisitionDestination(context);
        context.SaveFilePicker = (_, _) => Task.FromResult<string?>(null);
        Assert.False(await destination.SaveAsync("never written"));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            context.SaveFilePicker = (_, name) => { Assert.Equal("winnow-acquisitions.csv", name); return Task.FromResult<string?>(path); };
            const string csv = "title,currency\r\nÉlan,EUR\r\n";
            Assert.True(await destination.SaveAsync(csv));
            Assert.Equal(csv, await File.ReadAllTextAsync(path));
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, (await File.ReadAllBytesAsync(path)).Take(3));
        }
        finally { File.Delete(path); }
    }
}
