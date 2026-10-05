using System.Windows;
using BoostParaPc.Services;

namespace BoostParaPc;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon? _tray;
    private GameWatcher? _watcher;
    private bool _exiting;

    public static bool StartInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureMigrated();
        StartInTray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);

        DispatcherUnhandledException += (_, args) =>
        {
            // O log vem primeiro: se a mensagem falhar, o erro continua registrado.
            ActionLog.Write("app", "erro-nao-tratado", false, args.Exception.ToString());
            MessageBox.Show(
                $"Erro inesperado:\n{args.Exception.Message}",
                "Project Boost X",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        _ = StartGameModeAsync();
    }

    private async Task StartGameModeAsync()
    {
        // Antes de qualquer coisa: se o programa fechou no meio de uma sessão, devolve o sistema ao que era.
        try { await GameSessionService.RecoverAsync(); }
        catch (Exception ex) { ActionLog.Write("session", "recuperar", false, ex.Message); }

        if (AppSettings.Current.AutoGameMode || StartInTray) ApplyAutoGameMode();
    }

    /// <summary>Liga ou desliga o vigia de jogos e o ícone de bandeja conforme a configuração.</summary>
    public void ApplyAutoGameMode()
    {
        Dispatcher.Invoke(() =>
        {
            var on = AppSettings.Current.AutoGameMode;
            if (on && _watcher is null)
            {
                var games = new Lazy<IReadOnlyCollection<string>>(() => GameProfileService.DetectGames()
                    .Select(g => g.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase));
                _watcher = new GameWatcher();
                _watcher.Message += text => _tray?.ShowBalloonTip(2000, "Project Boost X", text, System.Windows.Forms.ToolTipIcon.Info);
                try { _watcher.Start(() => games.Value); }
                catch (Exception ex) { ActionLog.Write("session", "vigiar", false, ex.Message); }
            }
            else if (!on)
            {
                _watcher?.Dispose();
                _watcher = null;
            }

            if ((on || StartInTray) && _tray is null) CreateTray();
            else if (!on && !StartInTray && _tray is not null) { _tray.Dispose(); _tray = null; }
        });
    }

    private void CreateTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowMain());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Project Boost X",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    private void ShowMain()
    {
        if (MainWindow is null) { MainWindow = new MainWindow(); }
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>Fechar a janela só minimiza para a bandeja quando o Modo Jogo automático está ligado.</summary>
    public bool TryHideToTray()
    {
        if (_exiting || _tray is null || !AppSettings.Current.AutoGameMode) return false;
        MainWindow?.Hide();
        return true;
    }

    private void ExitApp()
    {
        _exiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        // O programa fechou no meio de uma sessão: desfaz agora, sem esperar a próxima abertura.
        try { GameSessionService.EndAsync().GetAwaiter().GetResult(); } catch { /* RecoverAsync cuida na próxima abertura */ }
        base.OnExit(e);
    }
}
