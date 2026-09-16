using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Monitor;

namespace Winnow.App.ViewModels;

/// <summary>Shared session health and local diagnostic access for both shells.</summary>
public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly SessionWatcherHealth? _health;
    private readonly Action<string> _openFolder;

    public DiagnosticsViewModel(SessionWatcherHealth? health = null, string? dataDirectory = null,
        Action<string>? openFolder = null)
    {
        _health = health;
        LogsDirectory = Path.Combine(dataDirectory ?? Program.DataLocation.Root, "logs");
        _openFolder = openFolder ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
        if (health is not null)
        {
            health.Changed += (_, _) =>
            {
                if (Dispatcher.UIThread.CheckAccess()) Refresh();
                else Dispatcher.UIThread.Post(Refresh);
            };
            Refresh();
        }
    }

    public string LogsDirectory { get; }
    public string ReportHelp => "For a bug report, include the Winnow version, what happened and when, and the recent log files.";
    public string WatcherNotice => "Session tracking needs attention. Some play sessions may be missing. See logs for details.";
    [ObservableProperty] public partial bool HasWatcherFailure { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
    private void Refresh() => HasWatcherFailure = _health?.HasFailures == true;

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            _openFolder(LogsDirectory);
            Problem = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Problem = $"Couldn't open the logs folder. Open it manually: {LogsDirectory}";
        }
    }
}
