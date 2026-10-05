using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

/// <summary>Tela que precisa saber quando aparece e quando sai de vista (carregar dados, parar medidores).</summary>
public interface IScreen
{
    Task ActivateAsync();
    void Deactivate();
}

public sealed record NavItem(string Key, string Label, string Glyph);

public partial class MainViewModel : ObservableObject
{
    // Glifos da fonte Segoe Fluent Icons (Segoe MDL2 Assets no Windows 10).
    public static IReadOnlyList<NavItem> NavItems { get; } =
    [
        new("Home", "Início", ""),
        new("Optimizations", "Otimizações", ""),
        new("Games", "Jogos", ""),
        new("Cleanup", "Limpeza", ""),
        new("Startup", "Inicialização", ""),
        new("Monitor", "Monitor", ""),
        new("History", "Histórico", ""),
        new("Settings", "Configurações", ""),
    ];

    private readonly Dictionary<string, Lazy<object>> _screens;
    private string? _activeKey;

    [ObservableProperty]
    private string _currentPage = "Home";

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _statusMessage = "Pronto";

    /// <summary>Uma gravação em andamento. Bloqueia outras gravações; a navegação continua livre.</summary>
    [ObservableProperty]
    private bool _isBusy;

    private Action? _cancelOperation;

    /// <summary>Pergunta sim/não ao usuário (título, texto). A janela principal troca por uma caixa de diálogo; nos testes responde sim.</summary>
    public Func<string, string, bool> Confirm { get; set; } = (_, _) => true;

    public bool IsScreenCreated(string key) => _screens.TryGetValue(key, out var screen) && screen.IsValueCreated;

    public bool CanCancelOperation => IsBusy && _cancelOperation is not null;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private SystemInfo? _systemInfo;

    public HomeViewModel Home => (HomeViewModel)_screens["Home"].Value;
    public OptimizationsViewModel Optimizations => (OptimizationsViewModel)_screens["Optimizations"].Value;
    public GamesViewModel Games => (GamesViewModel)_screens["Games"].Value;
    public CleanupViewModel Cleanup => (CleanupViewModel)_screens["Cleanup"].Value;
    public StartupViewModel StartupVm => (StartupViewModel)_screens["Startup"].Value;
    public MonitorViewModel Monitor => (MonitorViewModel)_screens["Monitor"].Value;
    public HistoryViewModel History => (HistoryViewModel)_screens["History"].Value;
    public SettingsViewModel Settings => (SettingsViewModel)_screens["Settings"].Value;

    public MainViewModel()
    {
        // Cada tela só é construída na primeira navegação até ela.
        _screens = new()
        {
            ["Home"] = new(() => new HomeViewModel(this)),
            ["Optimizations"] = new(() => new OptimizationsViewModel(this)),
            ["Games"] = new(() => new GamesViewModel(this)),
            ["Cleanup"] = new(() => new CleanupViewModel(this)),
            ["Startup"] = new(() => new StartupViewModel(this)),
            ["Monitor"] = new(() => new MonitorViewModel(this)),
            ["History"] = new(() => new HistoryViewModel(this)),
            ["Settings"] = new(() => new SettingsViewModel(this)),
        };
        CurrentViewModel = _screens["Home"].Value;
        _activeKey = "Home";

        IsAdmin = SystemInfoService.IsAdministrator();
        _ = Task.Run(() =>
        {
            var info = SystemInfoService.GetSystemInfo();
            System.Windows.Application.Current?.Dispatcher.Invoke(() => SystemInfo = info);
        });
    }

    public async Task InitializeAsync()
    {
        if (_screens["Home"].Value is IScreen home) await home.ActivateAsync();
        _ = CheckForUpdateAsync();
    }

    /// <summary>Só avisa; nunca baixa nem instala. Falha de rede é silenciosa.</summary>
    private async Task CheckForUpdateAsync()
    {
        if (!AppSettings.Current.CheckForUpdates) return;
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(3, 0, 0);
        var update = await UpdateService.CheckAsync(version);
        if (update is not null) StatusMessage = $"Há uma versão nova ({update.Version}): {update.Url}";
    }

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    // A lista de navegação altera CurrentPage diretamente; tudo passa por aqui.
    partial void OnCurrentPageChanged(string value)
    {
        if (!_screens.TryGetValue(value, out var target) || value == _activeKey) return;
        if (_activeKey is not null && _screens[_activeKey].IsValueCreated && _screens[_activeKey].Value is IScreen previous)
            previous.Deactivate();
        _activeKey = value;
        CurrentViewModel = target.Value;
        StatusMessage = "Pronto";
        if (target.Value is IScreen screen) _ = ActivateAsync(screen);
    }

    private async Task ActivateAsync(IScreen screen)
    {
        try { await screen.ActivateAsync(); }
        catch (Exception ex) { StatusMessage = $"Falha ao carregar a tela: {ex.Message}"; }
    }

    public void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;
        if (message is not null) StatusMessage = message;
        OnPropertyChanged(nameof(CanCancelOperation));
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    public void SetCancellation(Action? cancel)
    {
        _cancelOperation = cancel;
        OnPropertyChanged(nameof(CanCancelOperation));
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        _cancelOperation?.Invoke();
        StatusMessage = "Cancelando; aguardando a operação encerrar…";
        SetCancellation(null);
    }

    public async Task RunOperationAsync(string message, Func<Task> operation)
    {
        if (IsBusy) return;
        SetBusy(true, message);
        try { await operation(); }
        catch (OperationCanceledException) { StatusMessage = "Operação cancelada"; }
        catch (Exception ex) { StatusMessage = $"Falha: {ex.Message}"; }
        finally
        {
            SetCancellation(null);
            SetBusy(false);
        }
    }

    public async Task RequireRestorePointAsync(string description)
    {
        if (!AppSettings.Current.RequireRestorePoint) return;
        StatusMessage = "Criando e verificando ponto de restauração…";
        if (!await RestorePointService.CreateAsync(description))
            throw new InvalidOperationException("Operação interrompida: o ponto de restauração solicitado não foi criado. Verifique a Proteção do Sistema ou desative a exigência em Configurações para continuar sem ponto.");
    }
}
