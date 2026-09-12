using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>Desktop statistics with a shared chart projection and expandable details.</summary>
public partial class AccountStatsView : UserControl
{
    public AccountStatsView()
    {
        InitializeComponent();

        // The previewer gets the populated screen; runtime leaves the
        // DataContext to the shell. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.AccountStats;
        }
    }
}
