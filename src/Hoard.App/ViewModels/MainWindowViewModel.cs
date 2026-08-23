using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoard.App.ViewModels;

/// <summary>Window shell: hosts the library view.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(LibraryViewModel library)
    {
        Library = library;
    }

    public LibraryViewModel Library { get; }
}
