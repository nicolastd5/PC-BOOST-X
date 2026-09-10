using System.Collections.ObjectModel;
using System.IO;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private string _powerPlan = "â€¦";

    [ObservableProperty]
    private string _gameModeStatus = "â€¦";

    [ObservableProperty]
    private int _appliedCount;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _lastAction = "Nenhuma aÃ§Ã£o executada ainda";

    [ObservableProperty]
    private bool _restorePointEnabled = true;

    public ObservableCollection<OptimizationItem> QuickWins { get; } = [];

    public DashboardViewModel(MainViewModel main)
    {
        _main = main;

        // Quick wins seguros â€” nÃ£o bloqueia a UI na construÃ§Ã£o
        foreach (var id in new[] { "power.high", "game.mode", "game.dvr", "visual.transparency", "telemetry.off", "sys.startupdelay" })
        {
            var item = OptimizationCatalog.CreateAll().FirstOrDefault(o => o.Id == id);
            if (item is not null) QuickWins.Add(item);
        }
        SelectedCount = QuickWins.Count(i => i.IsSelected);
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => OptimizationStateDetector.ApplyTo(QuickWins)).ConfigureAwait(false);
        await RefreshInfoAsync().ConfigureAwait(false);
    }

    public void RefreshInfo()
    {
        _ = RefreshInfoAsync();
    }

    private async Task RefreshInfoAsync()
    {
        try
        {
            var plan = await SystemInfoService.GetCurrentPowerPlanAsync().ConfigureAwait(false);
            var gameMode = await Task.Run(() => GameOptimizationService.DescribeCurrentGameMode()).ConfigureAwait(false);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                PowerPlan = plan;
                GameModeStatus = gameMode;
            });
        }
        catch
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                PowerPlan = "Desconhecido";
            });
        }
    }

    [RelayCommand]
    private async Task BoostAsync()
    {
        var selected = QuickWins.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Selecione pelo menos uma otimizaÃ§Ã£o");
            return;
        }

        _main.SetBusy(true, "Criando ponto de restauraÃ§Ã£oâ€¦");
        try
        {
            if (RestorePointEnabled)
                await RestorePointService.CreateAsync("Project Boost X - Boost rÃ¡pido");
        }
        catch
        {
            // ponto de restauraÃ§Ã£o pode falhar em alguns setups
        }

        int ok = 0, fail = 0;
        var appliedIds = new List<string>();
        foreach (var item in selected)
        {
            _main.SetBusy(true, $"Aplicando: {item.Name}");
            var state = await OptimizationCatalog.ApplyAsync(item);
            item.State = state;
            if (state == ApplyState.Applied)
            {
                ok++;
                appliedIds.Add(item.Id);
            }
            else fail++;
        }

        if (appliedIds.Count > 0)
            OptimizationStateStore.MarkMany(appliedIds);

        // Desmarca o que jÃ¡ ficou aplicado pra nÃ£o pedir de novo
        foreach (var item in QuickWins.Where(i => i.State == ApplyState.Applied))
            item.IsSelected = false;

        AppliedCount += ok;
        LastAction = $"Boost rÃ¡pido: {ok} aplicadas, {fail} falhas â€” {DateTime.Now:HH:mm}";
        _main.SetBusy(false, LastAction);
        RefreshInfo();
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        _main.SetBusy(true, "Criando ponto de restauraÃ§Ã£oâ€¦");
        var ok = await RestorePointService.CreateAsync();
        _main.SetBusy(false, ok ? "Ponto de restauraÃ§Ã£o criado" : "NÃ£o foi possÃ­vel criar o ponto (verifique ProteÃ§Ã£o do Sistema)");
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        _main.SetBusy(true, "Revertendo todas as otimizaÃ§Ãµesâ€¦");
        try
        {
            var progress = new Progress<string>(s => _main.SetBusy(true, s));
            var result = await RevertAllService.RevertEverythingAsync(progress);
            await RevertAllService.RevertServiceStartTypesAsync();

            foreach (var item in QuickWins)
            {
                item.State = ApplyState.NotApplied;
                item.IsSelected = true;
            }

            LastAction = result.Message;
            _main.SetBusy(false, result.Message);
            RefreshInfo();
        }
        catch (Exception ex)
        {
            _main.SetBusy(false, $"Falha ao reverter: {ex.Message}");
        }
    }
}

public partial class GamingViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<OptimizationItem> Items { get; } = [];

    [ObservableProperty]
    private bool _selectAll = true;

    public GamingViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var item in OptimizationCatalog.CreateAll()
                     .Where(i => i.Category is OptimizationCategory.Gaming
                         or OptimizationCategory.Power
                         or OptimizationCategory.Network
                         or OptimizationCategory.Input))
        {
            Items.Add(item);
        }
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            OptimizationStateDetector.ApplyTo(Items);
            foreach (var item in Items.Where(i => i.State == ApplyState.Applied))
                item.IsSelected = false;
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Nada selecionado");
            return;
        }

        _main.SetBusy(true, "Aplicando otimizaÃ§Ãµes de jogoâ€¦");
        try { await RestorePointService.CreateAsync("Project Boost X - Gaming"); } catch { }

        int ok = 0;
        var appliedIds = new List<string>();
        foreach (var item in selected)
        {
            _main.SetBusy(true, $"Aplicando: {item.Name}");
            var state = await OptimizationCatalog.ApplyAsync(item);
            item.State = state;
            if (state == ApplyState.Applied)
            {
                ok++;
                appliedIds.Add(item.Id);
                item.IsSelected = false;
            }
        }

        if (appliedIds.Count > 0)
            OptimizationStateStore.MarkMany(appliedIds);

        _main.SetBusy(false, $"{ok}/{selected.Count} otimizaÃ§Ãµes de jogo aplicadas");
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        // Seleciona sÃ³ o que ainda nÃ£o foi aplicado
        foreach (var i in Items)
            i.IsSelected = SelectAll && i.State != ApplyState.Applied;
    }
}

public partial class CleanupViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CleanupTarget> Targets { get; } = [];

    [ObservableProperty]
    private string _totalSizeDisplay = "â€”";

    [ObservableProperty]
    private string _lastResult = "";

    [ObservableProperty]
    private string _scanStatus = "Clique em Analisar para varrer temporÃ¡rios.";

    private long _totalBytes;

    public CleanupViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _main.SetBusy(true, "Analisando arquivos temporÃ¡riosâ€¦");
        ScanStatus = "Varrendo pastas (pode levar alguns segundos)â€¦";
        Targets.Clear();
        RecalcTotal();

        try
        {
            var progress = new Progress<string>(s => ScanStatus = s);
            var found = await CleanupService.ScanAsync(progress, ct);

            foreach (var t in found)
            {
                t.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(CleanupTarget.Selected))
                        RecalcTotal();
                };
                Targets.Add(t);
            }
            RecalcTotal();
            ScanStatus = $"Pronto Â· {Targets.Count} alvos";
            _main.SetBusy(false, $"Encontrados {Targets.Count} alvos â€” {TotalSizeDisplay}");
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "AnÃ¡lise cancelada.";
            _main.SetBusy(false, "AnÃ¡lise cancelada");
        }
        catch (Exception ex)
        {
            ScanStatus = "Falha na anÃ¡lise.";
            _main.SetBusy(false, $"Erro na anÃ¡lise: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var selected = Targets.Where(t => t.Selected).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Nada selecionado para limpar");
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _main.SetBusy(true, "Limpandoâ€¦");
        ScanStatus = "Removendo arquivosâ€¦";

        try
        {
            var progress = new Progress<string>(s =>
            {
                ScanStatus = s;
                _main.SetBusy(true, s);
            });
            var result = await CleanupService.CleanAsync(selected, progress, ct);
            LastResult = $"Liberado {CleanupService.FormatBytes(result.BytesFreed)} Â· {result.FilesRemoved} arquivos Â· {result.Errors} erros";
            ScanStatus = LastResult;
            _main.SetBusy(false, LastResult);
            // Reanalisar Ã© Ãºtil, mas nÃ£o bloqueia se demorar
            await ScanCommand.ExecuteAsync(null);
        }
        catch (OperationCanceledException)
        {
            _main.SetBusy(false, "Limpeza cancelada");
        }
        catch (Exception ex)
        {
            _main.SetBusy(false, $"Erro na limpeza: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        _main.SetBusy(false, "Cancelado");
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in Targets) t.Selected = true;
        RecalcTotal();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var t in Targets) t.Selected = false;
        RecalcTotal();
    }

    private void RecalcTotal()
    {
        _totalBytes = Targets.Where(t => t.Selected).Sum(t => t.SizeBytes);
        TotalSizeDisplay = CleanupService.FormatBytes(_totalBytes);
    }
}

public partial class DebloatViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<WindowsServiceItem> Services { get; } = [];

    [ObservableProperty]
    private string _filter = "";

    public DebloatViewModel(MainViewModel main)
    {
        _main = main;
        // Carrega sob demanda na primeira visita Ã  aba â€” nÃ£o bloqueia o startup
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _main.SetBusy(true, "Listando serviÃ§osâ€¦");
        var list = await Task.Run(GetKnownServices);
        Services.Clear();
        foreach (var s in list) Services.Add(s);
        _main.SetBusy(false, $"{Services.Count} serviÃ§os candidatos listados");
    }

    [RelayCommand]
    private async Task DisableSelectedAsync()
    {
        var selected = Services.Where(s => s.IsSelected && s.CanDisable).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Nenhum serviÃ§o selecionado");
            return;
        }

        _main.SetBusy(true, "Desativando serviÃ§osâ€¦");
        try { await RestorePointService.CreateAsync("Project Boost X - ServiÃ§os"); } catch { }

        int ok = 0;
        foreach (var s in selected)
        {
            _main.SetBusy(true, $"Desativando {s.Name}");
            await ProcessRunner.RunAsync("sc", $"config {s.Name} start= disabled");
            await ProcessRunner.RunAsync("sc", $"stop {s.Name}");
            ok++;
        }
        _main.SetBusy(false, $"{ok} serviÃ§os desativados (reinicie para efeito total)");
        await LoadCommand.ExecuteAsync(null);
    }

    private static List<WindowsServiceItem> GetKnownServices()
    {
        // Lista conservadora de serviÃ§os comuns de telemetria/background
        string[] candidates =
        [
            "DiagTrack|Connected User Experiences and Telemetry|Telemetria da Microsoft|Safe",
            "dmwappushservice|WAP Push Message Routing|Telemetria mobile|Safe",
            "SysMain|SysMain (Superfetch)|PrÃ©-carregamento em RAM|Moderate",
            "WSearch|Windows Search|IndexaÃ§Ã£o de arquivos|Moderate",
            "Fax|Fax|ServiÃ§o de fax (desnecessÃ¡rio)|Safe",
            "MapsBroker|Downloaded Maps Manager|Mapas offline|Safe",
            "RetailDemo|Retail Demo Service|Modo loja|Safe",
            "XblAuthManager|Xbox Live Auth Manager|Xbox (se nÃ£o usa)|Moderate",
            "XblGameSave|Xbox Live Game Save|Xbox (se nÃ£o usa)|Moderate",
            "XboxNetApiSvc|Xbox Live Networking|Xbox (se nÃ£o usa)|Moderate",
            "XboxGipSvc|Xbox Accessory Management|Xbox (se nÃ£o usa)|Moderate",
        ];

        var result = new List<WindowsServiceItem>();
        foreach (var line in candidates)
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            var name = parts[0];

            var qc = ProcessRunner.RunAsync("sc", $"qc {name}").GetAwaiter().GetResult();
            if (qc.ExitCode != 0) continue; // nÃ£o existe neste sistema

            var startType = "unknown";
            var status = "stopped";
            foreach (var l in qc.StdOut.Split('\n'))
            {
                if (l.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase))
                {
                    if (l.Contains("AUTO_START")) startType = "Automatic";
                    else if (l.Contains("DEMAND_START")) startType = "Manual";
                    else if (l.Contains("DISABLED")) startType = "Disabled";
                }
            }

            var query = ProcessRunner.RunAsync("sc", $"query {name}").GetAwaiter().GetResult();
            if (query.StdOut.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                status = "running";

            if (startType == "Disabled") continue;

            result.Add(new WindowsServiceItem
            {
                Name = name,
                DisplayName = parts[1],
                Description = parts[2],
                StartType = startType,
                Status = status,
                Risk = parts[3] == "Safe" ? RiskLevel.Safe : RiskLevel.Moderate,
                CanDisable = true
            });
        }
        return result;
    }
}

public partial class StartupViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<StartupItem> Items { get; } = [];

    public StartupViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task LoadAsync()
    {
        _main.SetBusy(true, "Carregando itens de inicializaÃ§Ã£oâ€¦");
        var list = await StartupService.GetItemsAsync();
        Items.Clear();
        foreach (var i in list) Items.Add(i);
        _main.SetBusy(false, $"{Items.Count} itens de inicializaÃ§Ã£o");
    }

    [RelayCommand]
    private async Task DisableAsync(StartupItem? item)
    {
        if (item is null) return;
        _main.SetBusy(true, $"Desativando {item.Name}â€¦");
        await StartupService.DisableAsync(item);
        item.IsEnabled = false;
        _main.SetBusy(false, $"{item.Name} desativado (backup salvo)");
    }

    [RelayCommand]
    private async Task EnableAsync(StartupItem? item)
    {
        if (item is null) return;
        _main.SetBusy(true, $"Reativando {item.Name}â€¦");
        await StartupService.EnableAsync(item);
        item.IsEnabled = true;
        _main.SetBusy(false, $"{item.Name} reativado");
    }
}

public partial class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly PerformanceMonitorService _service = new();
    private readonly MainViewModel _main;

    [ObservableProperty]
    private float _cpu;

    [ObservableProperty]
    private float _ram;

    [ObservableProperty]
    private float _disk;

    [ObservableProperty]
    private float _networkKbps;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private string _cpuDisplay = "0%";

    [ObservableProperty]
    private string _ramDisplay = "0%";

    [ObservableProperty]
    private string _diskDisplay = "0%";

    [ObservableProperty]
    private string _netDisplay = "0 KB/s";

    public MonitorViewModel(MainViewModel main)
    {
        _main = main;
        _service.SampleUpdated += OnSample;
    }

    private void OnSample(object? sender, PerformanceSample s)
    {
        // Atualiza na thread da UI
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Cpu = s.CpuPercent;
            Ram = s.RamPercent;
            Disk = s.DiskPercent;
            NetworkKbps = s.NetworkKbps;
            ProcessCount = s.ProcessCount;
            CpuDisplay = $"{s.CpuPercent:0}%";
            RamDisplay = $"{s.RamPercent:0}%";
            DiskDisplay = $"{s.DiskPercent:0}%";
            NetDisplay = s.NetworkKbps >= 1024
                ? $"{s.NetworkKbps / 1024:0.0} MB/s"
                : $"{s.NetworkKbps:0} KB/s";
        });
    }

    public void Start() => _service.Start(1000);
    public void Stop() => _service.Stop();

    public void Dispose() => _service.Dispose();
}

public partial class GameProfilesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<GameProfile> Games { get; } = [];

    [ObservableProperty]
    private string _lastResult = "Clique em Detectar jogos para varrer Steam/Epic/Riot e pastas comuns.";

    [ObservableProperty]
    private bool _selectAllFso = true;

    [ObservableProperty]
    private bool _selectAllDpi;

    [ObservableProperty]
    private bool _selectAllPriority = true;

    [ObservableProperty]
    private bool _selectAllGpu = true;

    public GameProfilesViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private async Task DetectAsync()
    {
        _main.SetBusy(true, "Procurando jogos (Steam ACF, Epic manifests, registro, discos)â€¦");
        Games.Clear();
        var list = await Task.Run(() => GameProfileService.DetectGames());
        foreach (var g in list)
        {
            g.LoadRecommendationInfo();
            Games.Add(g);
        }
        _main.SetBusy(false, list.Count == 0
            ? "Nenhum jogo conhecido encontrado â€” verifique pastas de Steam/Epic/Games"
            : $"{list.Count} jogos detectados");
        LastResult = list.Count == 0
            ? "Nenhum jogo encontrado. Tente rodar como admin ou ter jogos em pastas padrÃ£o."
            : $"{list.Count} jogos prontos. Use Â«Recomendado por jogoÂ» para aplicar o perfil ideal de cada um.";
    }

    [RelayCommand]
    private async Task ApplySelectedAsync()
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Nenhum jogo selecionado");
            return;
        }

        _main.SetBusy(true, "Aplicando perfisâ€¦");
        try { await RestorePointService.CreateAsync("Project Boost X - Perfis de jogo"); } catch { }

        foreach (var g in selected)
        {
            g.IsFullscreenOptDisabled = SelectAllFso;
            g.IsHighDpiOverridden = SelectAllDpi;
            g.IsHighPriority = SelectAllPriority;
            g.IsGpuPreferred = SelectAllGpu;
            _main.SetBusy(true, $"Perfil: {g.Name}");
            await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation: false));
        }

        LastResult = $"{selected.Count} perfis aplicados (FSO clÃ¡ssico, DPI, prioridade, GPU).";
        _main.SetBusy(false, LastResult);
    }

    [RelayCommand]
    private async Task ApplyRecommendedAsync()
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.SetBusy(false, "Nenhum jogo selecionado");
            return;
        }

        _main.SetBusy(true, "Aplicando recomendaÃ§Ãµes por jogoâ€¦");
        try { await RestorePointService.CreateAsync("Project Boost X - Recomendado por jogo"); } catch { }

        foreach (var g in selected)
        {
            _main.SetBusy(true, $"Recomendado: {g.Name}");
            await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation: true));
        }

        LastResult = $"{selected.Count} jogos com perfil recomendado aplicado. Confira as dicas na lista.";
        _main.SetBusy(false, LastResult);
    }

    [RelayCommand]
    private async Task RevertSelectedAsync()
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        _main.SetBusy(true, "Revertendo perfisâ€¦");
        foreach (var g in selected)
            await Task.Run(() => GameProfileService.RevertProfile(g));
        LastResult = $"{selected.Count} perfis revertidos.";
        _main.SetBusy(false, LastResult);
    }

    [RelayCommand]
    private void BoostRunning()
    {
        _main.SetBusy(true, "Aumentando prioridade de jogos em execuÃ§Ã£oâ€¦");
        var msg = GameProfileService.BoostRunningGames();
        LastResult = msg;
        _main.SetBusy(false, msg);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var g in Games) g.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var g in Games) g.IsSelected = false;
    }
}
