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
    private string _powerPlan = "…";

    [ObservableProperty]
    private string _gameModeStatus = "…";

    [ObservableProperty]
    private int _appliedCount;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _lastAction = "Nenhuma ação executada ainda";

    [ObservableProperty]
    private bool _restorePointEnabled = true;

    public ObservableCollection<OptimizationItem> QuickWins { get; } = [];

    private static readonly string[] QuickWinIds =
        ["power.high", "game.mode", "game.dvr", "visual.transparency", "telemetry.off", "sys.startupdelay"];

    public DashboardViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var item in OptimizationCatalog.All.Where(i => QuickWinIds.Contains(i.Id)))
            QuickWins.Add(item);
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => OptimizationEngine.Refresh(QuickWins));
        foreach (var item in QuickWins)
            item.IsSelected = item.State != ApplyState.Applied && item.IsRecommended;
        AppliedCount = QuickWins.Count(i => i.State == ApplyState.Applied);
        SelectedCount = QuickWins.Count(i => i.IsSelected);
        await RefreshInfoAsync();
    }

    public void RefreshInfo()
    {
        _ = RefreshInfoAsync();
    }

    private async Task RefreshInfoAsync()
    {
        try
        {
            PowerPlan = await Task.Run(PowerNative.ActiveSchemeName);
            GameModeStatus = await Task.Run(() =>
                RegistryBackupService.GetDword(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") == 1
                    ? "Ativado" : "Desativado / padrão");
        }
        catch
        {
            PowerPlan = "Desconhecido";
        }
    }

    [RelayCommand]
    private Task BoostAsync() => _main.RunOperationAsync("Aplicando Boost rápido…", async () =>
    {
        var selected = QuickWins.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Selecione pelo menos uma otimização";
            return;
        }

        await _main.RequireRestorePointAsync("Project Boost X - Boost rápido");

        int ok = 0, fail = 0;
        foreach (var item in selected)
        {
            _main.StatusMessage = $"Aplicando: {item.Name}";
            if (await OptimizationEngine.ApplyAsync(item) == ApplyState.Applied) ok++;
            else fail++;
        }

        // Desmarca o que já ficou aplicado pra não pedir de novo
        foreach (var item in QuickWins.Where(i => i.State == ApplyState.Applied))
            item.IsSelected = false;

        AppliedCount = QuickWins.Count(i => i.State == ApplyState.Applied);
        LastAction = $"Boost rápido: {ok} aplicadas, {fail} falhas — {DateTime.Now:HH:mm}";
        _main.StatusMessage = LastAction;
        await RefreshInfoAsync();
    });

    [RelayCommand]
    private Task CreateRestorePointAsync() => _main.RunOperationAsync("Criando ponto de restauração…", async () =>
    {
        var ok = await RestorePointService.CreateAsync();
        _main.StatusMessage = ok ? "Ponto de restauração criado e verificado" : "Não foi possível criar o ponto (verifique Proteção do Sistema)";
    });

    [RelayCommand]
    private Task RevertAllAsync() => _main.RunOperationAsync("Revertendo todas as otimizações…", async () =>
    {
        var progress = new Progress<string>(s => { if (_main.IsBusy) _main.StatusMessage = s; });
        var result = await RevertAllService.RevertEverythingAsync(progress);

        if (result.Success)
        {
            await Task.Run(() => OptimizationEngine.Refresh(OptimizationCatalog.All));
            await InitializeAsync();
            await _main.Gaming.InitializeAsync();
            foreach (var item in QuickWins)
                item.IsSelected = item.State != ApplyState.Applied;
        }

        LastAction = result.Message;
        _main.StatusMessage = result.Message;
    });
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
        foreach (var item in OptimizationCatalog.All) Items.Add(item);
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => OptimizationEngine.Refresh(Items));
        SelectRecommended();
    }

    /// <summary>A seleção automática só marca o que é seguro e indicado para este PC.</summary>
    private void SelectRecommended()
    {
        foreach (var item in Items)
            item.IsSelected = SelectAll && !item.IsAction && item.IsRecommended
                              && item.Risk == RiskLevel.Safe && item.State != ApplyState.Applied;
    }

    [RelayCommand]
    private void ToggleSelectAll() => SelectRecommended();

    [RelayCommand]
    private Task ApplySelectedAsync() => _main.RunOperationAsync("Aplicando otimizações…", async () =>
    {
        var selected = Items.Where(i => i.IsSelected && !i.IsAction).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Nada selecionado";
            return;
        }

        await _main.RequireRestorePointAsync("Project Boost X - Otimizações");

        var ok = 0;
        foreach (var item in selected)
        {
            _main.StatusMessage = $"Aplicando: {item.Name}";
            if (await OptimizationEngine.ApplyAsync(item) != ApplyState.Applied) continue;
            ok++;
            item.IsSelected = false;
        }

        _main.StatusMessage = $"{ok}/{selected.Count} otimizações aplicadas · {selected.Count - ok} falhas";
    });

    [RelayCommand]
    private Task ApplyItemAsync(OptimizationItem? item) => _main.RunOperationAsync("Aplicando…", async () =>
    {
        if (item is null) return;
        if (!item.IsAction) await _main.RequireRestorePointAsync($"Project Boost X - {item.Name}");
        var state = await OptimizationEngine.ApplyAsync(item);
        item.IsSelected = false;
        _main.StatusMessage = state == ApplyState.Failed
            ? $"{item.Name}: {item.StatusMessage}"
            : item.IsAction ? $"{item.Name}: concluído" : $"{item.Name}: aplicado";
    });

    [RelayCommand]
    private Task RevertItemAsync(OptimizationItem? item) => _main.RunOperationAsync("Revertendo…", async () =>
    {
        if (item is null) return;
        var ok = await OptimizationEngine.RevertAsync(item);
        _main.StatusMessage = ok ? $"{item.Name}: revertido" : $"{item.Name}: {item.StatusMessage}";
    });
}

public partial class CleanupViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CleanupTarget> Targets { get; } = [];

    [ObservableProperty]
    private string _totalSizeDisplay = "—";

    [ObservableProperty]
    private string _lastResult = "";

    [ObservableProperty]
    private string _scanStatus = "Clique em Analisar para varrer temporários.";

    private long _totalBytes;

    public CleanupViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private Task ScanAsync() => _main.RunOperationAsync("Analisando arquivos temporários…", async () =>
    {
        using var cts = new CancellationTokenSource();
        _cts = cts;
        CancelCommand.NotifyCanExecuteChanged();
        _main.SetCancellation(() => RequestCancel(cts));
        try
        {
            await ScanTargetsAsync(cts.Token);
            _main.StatusMessage = $"Encontrados {Targets.Count} alvos — {TotalSizeDisplay}";
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Análise cancelada.";
            throw;
        }
        catch
        {
            ScanStatus = "Falha na análise.";
            throw;
        }
        finally
        {
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    });

    private async Task ScanTargetsAsync(CancellationToken ct)
    {
        ScanStatus = "Varrendo pastas (pode levar alguns segundos)…";
        Targets.Clear();
        RecalcTotal();
        var progress = new Progress<string>(s => { if (!ct.IsCancellationRequested && _main.IsBusy) ScanStatus = s; });
        var found = await CleanupService.ScanAsync(progress, ct);
        ct.ThrowIfCancellationRequested();
        foreach (var t in found)
        {
            t.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupTarget.Selected)) RecalcTotal();
            };
            Targets.Add(t);
        }
        RecalcTotal();
        ScanStatus = $"Pronto · {Targets.Count} alvos";
    }

    [RelayCommand]
    private Task CleanAsync() => _main.RunOperationAsync("Limpando…", async () =>
    {
        var selected = Targets.Where(t => t.Selected).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Nada selecionado para limpar";
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        CancelCommand.NotifyCanExecuteChanged();
        _main.SetCancellation(() => RequestCancel(cts));
        var ct = cts.Token;

        ScanStatus = "Removendo arquivos…";

        try
        {
            var progress = new Progress<string>(s =>
            {
                if (ct.IsCancellationRequested || !_main.IsBusy) return;
                ScanStatus = s;
                _main.StatusMessage = s;
            });
            var result = await CleanupService.CleanAsync(selected, progress, ct);
            LastResult = $"Liberado {CleanupService.FormatBytes(result.BytesFreed)} · {result.FilesRemoved} arquivos · {result.Errors} erros";
            ScanStatus = LastResult;
            ct.ThrowIfCancellationRequested();
            await ScanTargetsAsync(ct);
            _main.StatusMessage = LastResult;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Limpeza cancelada.";
            throw;
        }
        catch
        {
            ScanStatus = "Falha na limpeza.";
            throw;
        }
        finally
        {
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    });

    private bool CanCancel() => _cts is not null;

    private void RequestCancel(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_cts, cts))
        {
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
        cts.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        var cts = _cts;
        if (cts is null) return;
        RequestCancel(cts);
        _main.SetCancellation(null);
        _main.StatusMessage = "Cancelando; aguardando a operação encerrar…";
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
        // Carrega sob demanda na primeira visita à aba — não bloqueia o startup
    }

    [RelayCommand]
    private Task LoadAsync() => _main.RunOperationAsync("Listando serviços…", async () =>
    {
        await ReloadServicesAsync();
        _main.StatusMessage = $"{Services.Count} serviços candidatos listados";
    });

    private async Task ReloadServicesAsync()
    {
        var list = await Task.Run(GetKnownServices);
        Services.Clear();
        foreach (var s in list) Services.Add(s);
    }

    [RelayCommand]
    private Task DisableSelectedAsync() => _main.RunOperationAsync("Desativando serviços…", async () =>
    {
        var selected = Services.Where(s => s.IsSelected && s.CanDisable).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Nenhum serviço selecionado";
            return;
        }

        await _main.RequireRestorePointAsync("Project Boost X - Serviços");

        int ok = 0;
        var failures = new List<string>();
        foreach (var s in selected)
        {
            _main.StatusMessage = $"Desativando {s.Name}";
            try
            {
                await SystemSettingsBackupService.DisableServiceAsync(s.Name);
                s.IsSelected = false;
                ok++;
            }
            catch (Exception ex) { failures.Add($"{s.Name}: {ex.Message}"); }
        }
        await ReloadServicesAsync();
        _main.StatusMessage = $"{ok}/{selected.Count} serviços desativados · {failures.Count} falhas. {string.Join("; ", failures)}".Trim();
    });

    private static List<WindowsServiceItem> GetKnownServices()
    {
        // Lista conservadora de serviços comuns de telemetria/background
        string[] candidates =
        [
            "DiagTrack|Connected User Experiences and Telemetry|Telemetria da Microsoft|Safe",
            "dmwappushservice|WAP Push Message Routing|Telemetria mobile|Safe",
            "SysMain|SysMain (Superfetch)|Pré-carregamento em RAM|Moderate",
            "WSearch|Windows Search|Indexação de arquivos|Moderate",
            "Fax|Fax|Serviço de fax (desnecessário)|Safe",
            "MapsBroker|Downloaded Maps Manager|Mapas offline|Safe",
            "RetailDemo|Retail Demo Service|Modo loja|Safe",
            "XblAuthManager|Xbox Live Auth Manager|Xbox (se não usa)|Moderate",
            "XblGameSave|Xbox Live Game Save|Xbox (se não usa)|Moderate",
            "XboxNetApiSvc|Xbox Live Networking|Xbox (se não usa)|Moderate",
            "XboxGipSvc|Xbox Accessory Management|Xbox (se não usa)|Moderate",
        ];

        var result = new List<WindowsServiceItem>();
        foreach (var line in candidates)
        {
            var parts = line.Split('|');
            if (parts.Length < 4) continue;
            var name = parts[0];

            var qc = ProcessRunner.RunAsync("sc", $"qc {name}").GetAwaiter().GetResult();
            if (qc.ExitCode != 0) continue; // não existe neste sistema

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
    private Task LoadAsync() => _main.RunOperationAsync("Carregando itens de inicialização…", async () =>
    {
        var list = (await StartupService.GetItemsAsync()).Concat(await StartupTasks.GetAsync());
        Items.Clear();
        foreach (var i in list.OrderBy(i => i.Name)) Items.Add(i);
        _main.StatusMessage = $"{Items.Count} itens de inicialização";
    });

    [RelayCommand]
    private Task DisableAsync(StartupItem? item) => _main.RunOperationAsync("Desativando inicialização…", async () =>
    {
        if (item is null) return;
        await _main.RequireRestorePointAsync("Project Boost X - Inicialização");
        _main.StatusMessage = $"Desativando {item.Name}…";
        if (item.Source == StartupTasks.Source) await StartupTasks.DisableAsync(item);
        else await StartupService.DisableAsync(item);
        item.IsEnabled = false;
        _main.StatusMessage = $"{item.Name} desativado (backup salvo)";
    });

    [RelayCommand]
    private Task EnableAsync(StartupItem? item) => _main.RunOperationAsync("Reativando inicialização…", async () =>
    {
        if (item is null) return;
        _main.StatusMessage = $"Reativando {item.Name}…";
        if (item.Source == StartupTasks.Source) await StartupTasks.EnableAsync(item);
        else await StartupService.EnableAsync(item);
        item.IsEnabled = true;
        _main.StatusMessage = $"{item.Name} reativado";
    });
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    private GameProfile? _selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;

    public GameProfilesViewModel(MainViewModel main) => _main = main;

    [RelayCommand]
    private Task DetectAsync() => _main.RunOperationAsync("Procurando jogos (Steam ACF, Epic manifests, registro, discos)…", async () =>
    {
        Games.Clear();
        SelectedGame = null;
        var list = await Task.Run(() => GameProfileService.DetectGames());

        foreach (var g in list)
            g.LoadRecommendationInfo();

        // Resolve capas ANTES de entrar na UI — evita card preso no gradiente
        var ok = 0;
        var i = 0;
        foreach (var g in list)
        {
            i++;
            _main.StatusMessage = $"Capas {i}/{list.Count}: {g.Name}…";
            try
            {
                var path = await CoverService.ResolveCoverPathAsync(g);
                g.CoverPath = path;
                if (!Path.GetFileName(path).StartsWith("fb_", StringComparison.OrdinalIgnoreCase))
                    ok++;
            }
            catch
            {
                g.CoverPath = CoverService.EnsureFallbackCoverFile(g.Name);
            }
            Games.Add(g);
        }

        if (list.Count > 0)
            SelectedGame = Games[0];

        LastResult = list.Count == 0
            ? "Nenhum jogo encontrado. Tente rodar como admin ou ter jogos em pastas padrão."
            : $"{list.Count} jogos · {ok} capas reais · clique numa capa para ver as otimizações";
        _main.StatusMessage = LastResult;
    });

    [RelayCommand]
    private void OpenGame(GameProfile? game)
    {
        if (game is null) return;
        foreach (var g in Games) g.IsDetailOpen = false;
        game.IsDetailOpen = true;
        SelectedGame = game;
        // Não força IsSelected — inspecionar não entra no lote por engano
    }

    [RelayCommand]
    private Task ApplyRecommendedForSelectedAsync() => _main.RunOperationAsync("Aplicando perfil recomendado…", async () =>
    {
        var g = SelectedGame;
        if (g is null)
        {
            _main.StatusMessage = "Selecione um jogo na galeria";
            return;
        }

        await _main.RequireRestorePointAsync($"Project Boost X - {g.Name}");
        _main.StatusMessage = $"Aplicando recomendado: {g.Name}";
        await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation: true));
        LastResult = $"{g.Name}: {g.Status}";
        _main.StatusMessage = LastResult;
    });

    [RelayCommand]
    private Task RevertForSelectedAsync() => _main.RunOperationAsync("Revertendo perfil…", async () =>
    {
        var g = SelectedGame;
        if (g is null) return;
        _main.StatusMessage = $"Revertendo {g.Name}…";
        await Task.Run(() => GameProfileService.RevertProfile(g));
        LastResult = $"{g.Name} revertido";
        _main.StatusMessage = LastResult;
    });

    [RelayCommand]
    private Task ApplySelectedAsync() => RunProfilesAsync(useRecommendation: false, revert: false);

    [RelayCommand]
    private Task ApplyRecommendedAsync() => RunProfilesAsync(useRecommendation: true, revert: false);

    [RelayCommand]
    private Task RevertSelectedAsync() => RunProfilesAsync(useRecommendation: false, revert: true);

    private Task RunProfilesAsync(bool useRecommendation, bool revert) => _main.RunOperationAsync(
        revert ? "Revertendo perfis…" : "Aplicando perfis…", async () =>
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Nenhum jogo selecionado";
            return;
        }

        if (!revert) await _main.RequireRestorePointAsync("Project Boost X - Perfis de jogo");

        var ok = 0;
        var failures = new List<string>();
        foreach (var g in selected)
        {
            _main.StatusMessage = $"{(revert ? "Revertendo" : "Aplicando")}: {g.Name}";
            try
            {
                if (revert) await Task.Run(() => GameProfileService.RevertProfile(g));
                else
                {
                    if (!useRecommendation)
                    {
                        g.IsFullscreenOptDisabled = SelectAllFso;
                        g.IsHighDpiOverridden = SelectAllDpi;
                        g.IsHighPriority = SelectAllPriority;
                        g.IsGpuPreferred = SelectAllGpu;
                    }
                    await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation));
                }
                ok++;
            }
            catch (Exception ex) { failures.Add($"{g.Name}: {ex.Message}"); }
        }

        LastResult = $"{ok}/{selected.Count} perfis {(revert ? "revertidos" : "aplicados")} · {failures.Count} falhas. {string.Join("; ", failures)}".Trim();
        _main.StatusMessage = LastResult;
    });

    [RelayCommand]
    private Task BoostRunningAsync() => _main.RunOperationAsync("Aumentando prioridade de jogos em execução…", async () =>
    {
        var msg = await Task.Run(GameProfileService.BoostRunningGames);
        LastResult = msg;
        _main.StatusMessage = msg;
    });

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
