using System.Diagnostics;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class SettingsViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;
    private readonly AppSettings _settings = AppSettings.Current;
    private bool _loading = true;

    [ObservableProperty] private bool _requireRestorePoint;
    [ObservableProperty] private bool _autoGameMode;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private string _backgroundApps = "";
    [ObservableProperty] private string _updateStatus = "";

    public string Version { get; } = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.0";
    public string DataFolder => AppPaths.DataDir;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        RequireRestorePoint = _settings.RequireRestorePoint;
        AutoGameMode = _settings.AutoGameMode;
        StartWithWindows = _settings.StartWithWindows;
        CheckForUpdates = _settings.CheckForUpdates;
        BackgroundApps = string.Join(Environment.NewLine, _settings.BackgroundApps);
        _loading = false;
    }

    public Task ActivateAsync() => Task.CompletedTask;
    public void Deactivate() { }

    // O TextBox só atualiza ao perder o foco; salvar aqui cobre fechar o programa dentro da tela.
    partial void OnBackgroundAppsChanged(string value) => SaveBackgroundApps();

    private void Save(Action change)
    {
        if (_loading) return;
        change();
        try { _settings.Save(); }
        catch (Exception ex) { _main.StatusMessage = "Não foi possível salvar as configurações: " + ex.Message; }
    }

    partial void OnRequireRestorePointChanged(bool value) => Save(() => _settings.RequireRestorePoint = value);
    partial void OnCheckForUpdatesChanged(bool value) => Save(() => _settings.CheckForUpdates = value);

    partial void OnAutoGameModeChanged(bool value)
    {
        Save(() => _settings.AutoGameMode = value);
        if (!_loading && System.Windows.Application.Current is App app) app.ApplyAutoGameMode();
    }

    async partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try
        {
            if (value) await StartupTaskService.EnableAsync(); else await StartupTaskService.DisableAsync();
            Save(() => _settings.StartWithWindows = value);
        }
        catch (Exception ex)
        {
            _main.StatusMessage = "Não foi possível alterar a inicialização com o Windows: " + ex.Message;
            _loading = true; StartWithWindows = !value; _loading = false;
        }
    }

    public void SaveBackgroundApps() => Save(() => _settings.BackgroundApps = BackgroundApps
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !GameSessionService.IsProtected(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true }); }
        catch (Exception ex) { _main.StatusMessage = "Não foi possível abrir a pasta: " + ex.Message; }
    }

    [RelayCommand]
    private Task CreateRestorePointAsync() => _main.Home.CreateRestorePointCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task RevertAllAsync() => _main.Home.RevertAllCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateStatus = "Verificando…";
        var update = await UpdateService.CheckAsync(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(3, 0, 0));
        UpdateStatus = UpdateService.Repository.Length == 0 ? "A verificação será ativada quando o projeto tiver um repositório público."
            : update is null ? "Você está na versão mais recente (ou a verificação não conseguiu conectar)."
            : $"Nova versão {update.Version}: {update.Url}";
    }
}
