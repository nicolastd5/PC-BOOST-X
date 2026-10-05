using System.Collections.ObjectModel;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPage = "Dashboard";

    [ObservableProperty]
    private string _statusMessage = "Pronto";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    private bool _isBusy;

    private Action? _cancelOperation;

    public bool CanCancelOperation => IsBusy && _cancelOperation is not null;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private SystemInfo? _systemInfo;

    public DashboardViewModel Dashboard { get; }
    public GamingViewModel Gaming { get; }
    public GameProfilesViewModel GameProfiles { get; }
    public CleanupViewModel Cleanup { get; }
    public DebloatViewModel Debloat { get; }
    public StartupViewModel StartupVm { get; }
    public MonitorViewModel Monitor { get; }

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel(this);
        Gaming = new GamingViewModel(this);
        GameProfiles = new GameProfilesViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Debloat = new DebloatViewModel(this);
        StartupVm = new StartupViewModel(this);
        Monitor = new MonitorViewModel(this);

        IsAdmin = SystemInfoService.IsAdministrator();
        _ = Task.Run(() =>
        {
            var info = SystemInfoService.GetSystemInfo();
            System.Windows.Application.Current?.Dispatcher.Invoke(() => SystemInfo = info);
        });
    }

    public async Task InitializeAsync()
    {
        await Dashboard.InitializeAsync();
        await Gaming.InitializeAsync();
    }

    private bool CanNavigate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Navigate(string page)
    {
        if (IsBusy) return;
        CurrentPage = page;
        StatusMessage = page switch
        {
            "Dashboard" => "Visão geral do sistema",
            "Gaming" => "Otimizações de jogo",
            "GameProfiles" => "Perfis por jogo",
            "Cleanup" => "Limpeza de disco",
            "Debloat" => "Serviços e debloat",
            "Startup" => "Programas de inicialização",
            "Monitor" => "Medidor de desempenho",
            _ => "Pronto"
        };

        if (page == "Cleanup") _ = Cleanup.ScanCommand.ExecuteAsync(null);
        if (page == "Startup") _ = StartupVm.LoadCommand.ExecuteAsync(null);
        if (page == "Debloat" && Debloat.Services.Count == 0) _ = Debloat.LoadCommand.ExecuteAsync(null);
        if (page == "GameProfiles" && GameProfiles.Games.Count == 0) _ = GameProfiles.DetectCommand.ExecuteAsync(null);
        if (page == "Monitor") Monitor.Start();
        else Monitor.Stop();
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
        if (!Dashboard.RestorePointEnabled) return;
        StatusMessage = "Criando e verificando ponto de restauração…";
        if (!await RestorePointService.CreateAsync(description))
            throw new InvalidOperationException("Operação interrompida: o ponto de restauração solicitado não foi criado. Verifique a Proteção do Sistema ou desmarque explicitamente essa opção para continuar sem ponto.");
    }
}
