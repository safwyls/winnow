using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>Desktop statistics with a shared chart projection and expandable details.</summary>
public partial class AccountStatsView : UserControl
{
    public static readonly Avalonia.StyledProperty<bool> ShowHeaderProperty =
        Avalonia.AvaloniaProperty.Register<AccountStatsView, bool>(nameof(ShowHeader), true);
    public bool ShowHeader { get => GetValue(ShowHeaderProperty); set => SetValue(ShowHeaderProperty, value); }

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
