using System.Collections.ObjectModel;
using System.Diagnostics;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class HomeViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;
    private readonly PerformanceMonitorService _monitor = new();
    private bool _loaded;

    [ObservableProperty] private string _powerPlan = "…";
    [ObservableProperty] private string _gameModeStatus = "…";
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private string _lastAction = "Nenhuma ação executada ainda";

    [ObservableProperty] private int _healthScore = 100;
    [ObservableProperty] private string _healthText = "Analisando…";
    [ObservableProperty] private bool _isDiagnosing;

    [ObservableProperty] private string _comparisonNote = "Medindo o estado atual do PC…";
    [ObservableProperty] private bool _hasComparison;

    [ObservableProperty] private string _liveCpu = "—";
    [ObservableProperty] private string _liveRam = "—";
    [ObservableProperty] private string _liveDisk = "—";
    [ObservableProperty] private string _liveNet = "—";

    public int TotalCount => OptimizationCatalog.All.Count(i => !i.IsAction);

    public ObservableCollection<Finding> Findings { get; } = [];
    public ObservableCollection<SystemSnapshot.Delta> Comparison { get; } = [];

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        _monitor.SampleUpdated += OnSample;
    }

    public async Task ActivateAsync()
    {
        _monitor.Start();
        if (_loaded) { await RefreshCountsAsync(); return; }
        _loaded = true;
        await RefreshCountsAsync();
        // O primeiro retrato é salvo antes de qualquer otimização feita por este programa.
        _ = RunDiagnosticsAsync();
        _ = TakeSnapshotAsync();
    }

    public void Deactivate() => _monitor.Stop();

    private async Task RefreshCountsAsync()
    {
        await Task.Run(() => OptimizationEngine.Refresh(OptimizationCatalog.All));
        AppliedCount = OptimizationCatalog.All.Count(i => !i.IsAction && i.State == ApplyState.Applied);
        try
        {
            PowerPlan = await Task.Run(PowerNative.ActiveSchemeName);
            GameModeStatus = await Task.Run(() =>
                RegistryBackupService.GetDword(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") == 1
                    ? "Ativado" : "Desativado / padrão");
        }
        catch { PowerPlan = "Desconhecido"; }
    }

    private void OnSample(object? sender, PerformanceSample s) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        LiveCpu = $"{s.CpuPercent:0}%";
        LiveRam = $"{s.RamPercent:0}%";
        LiveDisk = $"{s.DiskPercent:0}%";
        LiveNet = s.NetworkKbps >= 1024 ? $"{s.NetworkKbps / 1024:0.0} MB/s" : $"{s.NetworkKbps:0} KB/s";
    });

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        if (IsDiagnosing) return;
        IsDiagnosing = true;
        HealthText = "Analisando…";
        try
        {
            var games = _main.IsScreenCreated("Games") ? _main.Games.Games.ToList() : null;
            var inputs = await DiagnosticsProbe.CollectAsync(games, _main.SystemInfo);
            var findings = Diagnostics.Evaluate(inputs);
            Findings.Clear();
            foreach (var f in findings.OrderByDescending(f => f.Severity)) Findings.Add(f);
            HealthScore = Diagnostics.Score(findings);
            HealthText = findings.Count == 0 ? "Nenhum problema encontrado" : $"{findings.Count} pontos de atenção";
        }
        catch (Exception ex) { HealthText = "Não foi possível analisar: " + ex.Message; }
        finally { IsDiagnosing = false; }
    }

    [RelayCommand]
    private async Task FixAsync(Finding? finding)
    {
        if (finding?.FixUri is not { } uri) return;
        try
        {
            if (uri.StartsWith("app:", StringComparison.Ordinal))
            {
                var target = uri[4..];
                if (target == "systemperformance") Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true });
                else _main.NavigateCommand.Execute(target switch { "optimizations" => "Optimizations", "cleanup" => "Cleanup", _ => "Home" });
            }
            else if (uri == "fix:tcp")
            {
                await _main.RunOperationAsync("Restaurando o ajuste de TCP…", async () =>
                {
                    await SystemSettingsBackupService.SetTcpAutoTuningAsync("normal", "diag.tcp");
                    _main.StatusMessage = "Ajuste de TCP restaurado para Normal";
                });
                await RunDiagnosticsAsync();
            }
            else Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) { _main.StatusMessage = "Não foi possível abrir: " + ex.Message; }
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        try
        {
            var current = await SnapshotProbe.TakeAsync();
            var baseline = SystemSnapshot.SaveBaselineIfMissing(current);
            Comparison.Clear();
            foreach (var row in SystemSnapshot.Compare(baseline, current)) Comparison.Add(row);
            HasComparison = true;
            ComparisonNote = ReferenceEquals(baseline, current) || baseline == current
                ? "Este é o retrato inicial do seu PC. Depois de otimizar, atualize para comparar. O tempo de boot só muda depois de reiniciar."
                : $"Comparado com o retrato de {baseline.TakenUtc.ToLocalTime():dd/MM/yyyy HH:mm}. O tempo de boot só muda depois de reiniciar.";
        }
        catch (Exception ex) { ComparisonNote = "Não foi possível medir: " + ex.Message; }
    }

    [RelayCommand]
    private Task OptimizeNowAsync() => _main.RunOperationAsync("Aplicando o preset Seguro…", async () =>
    {
        var plan = OptimizationCatalog.Preset(RiskLevel.Safe).Where(i => i.State != ApplyState.Applied).ToList();
        if (plan.Count == 0)
        {
            _main.StatusMessage = "Tudo do preset Seguro já está aplicado";
            return;
        }
        var summary = string.Join("\n", plan.Select(i => "• " + i.Name));
        if (!_main.Confirm("Otimizar agora", $"Estas {plan.Count} otimizações de risco Seguro serão aplicadas, cada uma com backup e reversão:\n\n{summary}\n\nContinuar?"))
        {
            _main.StatusMessage = "Nada foi alterado";
            return;
        }

        await _main.RequireRestorePointAsync("Project Boost X - Otimizar agora");
        int ok = 0, fail = 0;
        foreach (var item in plan)
        {
            _main.StatusMessage = $"Aplicando: {item.Name}";
            if (await OptimizationEngine.ApplyAsync(item) == ApplyState.Applied) ok++;
            else fail++;
        }
        AppliedCount = OptimizationCatalog.All.Count(i => !i.IsAction && i.State == ApplyState.Applied);
        LastAction = $"Otimizar agora: {ok} aplicadas, {fail} falhas — {DateTime.Now:HH:mm}";
        _main.StatusMessage = LastAction;
        await RefreshCountsAsync();
    });

    [RelayCommand]
    private Task CreateRestorePointAsync() => _main.RunOperationAsync("Criando ponto de restauração…", async () =>
    {
        var ok = await RestorePointService.CreateAsync();
        _main.StatusMessage = ok ? "Ponto de restauração criado e verificado" : "Não foi possível criar o ponto (verifique Proteção do Sistema)";
    });

    [RelayCommand]
    public Task RevertAllAsync() => _main.RunOperationAsync("Revertendo todas as otimizações…", async () =>
    {
        if (!_main.Confirm("Reverter tudo", "Todas as alterações feitas por este programa serão desfeitas. Continuar?"))
        {
            _main.StatusMessage = "Nada foi alterado";
            return;
        }
        var progress = new Progress<string>(s => { if (_main.IsBusy) _main.StatusMessage = s; });
        var result = await RevertAllService.RevertEverythingAsync(progress);
        if (result.Success)
        {
            await Task.Run(() => OptimizationEngine.Refresh(OptimizationCatalog.All));
            await RefreshCountsAsync();
            if (_main.IsScreenCreated("Optimizations")) await _main.Optimizations.ActivateAsync();
        }
        LastAction = result.Message;
        _main.StatusMessage = result.Message;
    });
}
