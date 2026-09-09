using Avalonia.Controls;

namespace Winnow.App.Views.Fullscreen;

/// <summary>A paged, controller-accessible file browser; selecting a file never executes it.</summary>
internal sealed class FullscreenFilePage : FullscreenPage
{
    private readonly string _title;
    private readonly IReadOnlyList<string>? _extensions;
    private readonly TaskCompletionSource<string?> _completion;
    private string? _directory;
    private int _page;
    private int _generation;
    private readonly TextBox? _fileName;
    private readonly bool _browse;
    public override string Title => _title;
    public FullscreenFilePage(FullscreenContext context, string title, IReadOnlyList<string>? extensions, TaskCompletionSource<string?> completion, string? initialDirectory = null, string? suggestedName = null, bool browse = false) : base(context)
    {
        _title = title; _extensions = extensions; _completion = completion; _directory = initialDirectory; _browse = browse;
        if (suggestedName is not null) _fileName = new TextBox { Text = Path.GetFileName(suggestedName), FontSize = 32, Watermark = "File name" };
        Render();
    }
    private async void Render()
    {
        var generation = ++_generation;
        Content = FullscreenUi.Text("Reading folder…", 32);
        try
        {
            var path = _directory;
            var entries = await Task.Run(() => path is null
                ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => (Path: d.RootDirectory.FullName, Directory: true)).ToArray()
                : System.IO.Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase).Select(p => (Path: p, Directory: true))
                    .Concat(System.IO.Directory.EnumerateFiles(path).Where(p => _fileName is null && (_extensions is null || _extensions.Count == 0 || _extensions.Any(e => string.Equals(System.IO.Path.GetExtension(p), e.StartsWith('.') ? e : "." + e, StringComparison.OrdinalIgnoreCase))))
                    .Order(StringComparer.OrdinalIgnoreCase).Select(p => (Path: p, Directory: false))).ToArray());
            if (generation != _generation) return;
            var rows = new List<Control[]>();
            var panel = FullscreenUi.Stack(FullscreenUi.Text(_title, 48), FullscreenUi.Text(path ?? "Choose a drive", 28, "TextDim"));
            void Add(string label, Action action) { var button = FullscreenUi.Button(label, action); panel.Children.Add(button); rows.Add([button]); }
            if (_fileName is not null && path is not null)
            {
                if (_fileName.Parent is Panel old) old.Children.Remove(_fileName);
                panel.Children.Add(_fileName); rows.Add([_fileName]);
                Add("Save here", () => SelectDestination(path));
            }
            if (path is not null) Add("Parent folder", () => { _directory = System.IO.Directory.GetParent(path)?.FullName; _page = 0; Render(); });
            foreach (var entry in entries.Skip(_page * 8).Take(8))
                Add((entry.Directory ? "Folder   " : "File   ") + (System.IO.Path.GetFileName(entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : entry.Path), () =>
                {
                    if (entry.Directory) { _directory = entry.Path; _page = 0; Render(); }
                    else if (_browse) ShowFile(entry.Path);
                    else { _completion.TrySetResult(entry.Path); Context.Back(); }
                });
            if (_page > 0) Add("Previous page", () => { _page--; Render(); });
            if ((_page + 1) * 8 < entries.Length) Add("Next page", () => { _page++; Render(); });
            Add(_browse ? "Back" : "Cancel", Context.Back);
            Content = FullscreenUi.Scroll(panel); SetFocusRows(rows.ToArray()); FocusInitial();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            var back = FullscreenUi.Button("Back to drives", () => { _directory = null; _page = 0; Render(); });
            Content = FullscreenUi.Stack(FullscreenUi.Text("This folder cannot be opened.", 32), back); SetFocusRows([back]); FocusInitial();
        }
    }
    private void ShowFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            Context.Push(new FullscreenDetailsReadingPage(Context, file.Name,
                $"{file.FullName}\n\n{file.Length:N0} bytes\nModified {file.LastWriteTime:f}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Context.Notify("This file is no longer available."); }
    }
    private void SelectDestination(string directory)
    {
        var name = _fileName?.Text?.Trim() ?? "";
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name)
        { Context.Notify("Enter a file name without a folder path."); return; }
        var destination = Path.Combine(directory, name);
        if (Directory.Exists(destination)) { Context.Notify("A folder already has that name. Choose another file name."); return; }
        void Finish() { _completion.TrySetResult(destination); Context.Back(); }
        if (File.Exists(destination)) Context.ShowActions($"Replace {name}?", [new("Cancel", () => { }), new("Replace file", Finish)]);
        else Finish();
    }
    public override void Dispose() { _generation++; _completion.TrySetResult(null); base.Dispose(); }
}
