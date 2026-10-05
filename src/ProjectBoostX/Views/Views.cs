using System.Windows;
using System.Windows.Controls;

namespace BoostParaPc.Views;

// Telas sem lógica própria: tudo vem do viewmodel pelo DataContext.
public partial class HomeView : UserControl { public HomeView() => InitializeComponent(); }
public partial class OptimizationsView : UserControl { public OptimizationsView() => InitializeComponent(); }
public partial class GamesView : UserControl { public GamesView() => InitializeComponent(); }
public partial class CleanupView : UserControl { public CleanupView() => InitializeComponent(); }
public partial class StartupView : UserControl { public StartupView() => InitializeComponent(); }
public partial class MonitorView : UserControl { public MonitorView() => InitializeComponent(); }
public partial class HistoryView : UserControl { public HistoryView() => InitializeComponent(); }
public partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }

public partial class FirstRunDialog : Window
{
    public FirstRunDialog() => InitializeComponent();

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
}
