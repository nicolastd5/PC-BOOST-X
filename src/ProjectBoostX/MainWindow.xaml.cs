using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BoostParaPc;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { if (Application.Current is App app && app.TryHideToTray()) e.Cancel = true; };
        ContentRendered += (_, _) => { if (App.StartInTray) Hide(); };
        Loaded += async (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                AdminBadge.Text = vm.IsAdmin ? "Modo administrador" : "Sem admin — some ajustes falharão";
                AdminBadge.Foreground = vm.IsAdmin
                    ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                    : (System.Windows.Media.Brush)FindResource("WarningBrush");
                await vm.InitializeAsync();
            }
        };
    }
}

/// <summary>Converte CurrentPage + parâmetro em Visibility.</summary>
public sealed class PageToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string page && parameter is string target)
            return page == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
