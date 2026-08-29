using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the STATS screen. There is deliberately none of it beyond
/// <c>InitializeComponent</c>.
///
/// <para>Every figure on this screen is a formatted string on
/// <see cref="ViewModels.AccountStatsViewModel"/>, including the decision not to
/// show one: a mixed-currency capture withholds its amounts in the view model,
/// where the rule can be tested, rather than in a converter or a binding.</para>
/// </summary>
public partial class AccountStatsView : UserControl
{
    public AccountStatsView()
    {
        InitializeComponent();
    }
}
