using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BoostParaPc.Services;

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
            // Tempo entre o início do processo e a janela pronta, no log (medição da inicialização).
            var elapsed = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            ActionLog.Write("app", "inicio", true, $"janela pronta em {elapsed:0} ms");

            if (DataContext is not ViewModels.MainViewModel vm) return;
            vm.Confirm = (title, text) => MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.0";
            VersionText.Text = $"v{version} · Windows 10/11";
            AdminBadge.Text = vm.IsAdmin ? "Modo administrador" : "Sem admin: alguns ajustes falharão";
            AdminBadge.Foreground = vm.IsAdmin
                ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                : (System.Windows.Media.Brush)FindResource("WarningBrush");

            if (!AppSettings.Current.FirstRunAcknowledged && !App.StartInTray)
            {
                if (new Views.FirstRunDialog { Owner = this }.ShowDialog() == true)
                {
                    AppSettings.Current.FirstRunAcknowledged = true;
                    try { AppSettings.Current.Save(); } catch { /* sem permissão: mostra de novo na próxima vez */ }
                }
            }
            await vm.InitializeAsync();
        };
    }
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
