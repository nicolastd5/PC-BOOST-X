using System.Windows;

namespace BoostParaPc;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services.AppPaths.EnsureMigrated();

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erro inesperado:\n{args.Exception.Message}",
                "Project Boost X",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
