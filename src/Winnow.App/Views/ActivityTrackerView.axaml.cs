using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class ActivityTrackerView : UserControl
{
    public ActivityTrackerView()
    {
        InitializeComponent();
        TimelinePlot.DetailSelected += text =>
        {
            if (DataContext is ActivityTrackerViewModel model) model.SelectDetail(text);
        };
        TimelinePlot.TrackedSessionsRequested += () =>
        {
            if (DataContext is ActivityTrackerViewModel { HasTrackedSessions: true } model)
                model.ShowTrackedSessionsCommand.Execute(null);
        };
    }
}
