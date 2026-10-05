using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Data;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public sealed record FilterOption<T>(string Label, T? Value) where T : struct;

public sealed class BloatChoice(AppxService.Bloat app) : ObservableObject
{
    public AppxService.Bloat App { get; } = app;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public partial class OptimizationsViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;
    private bool _loaded;

    public ObservableCollection<OptimizationItem> Items { get; } = [];
    public ICollectionView View { get; }

    public IReadOnlyList<FilterOption<OptimizationCategory>> Categories { get; } =
    [
        new("Todas as categorias", null),
        .. Enum.GetValues<OptimizationCategory>().Select(c => new FilterOption<OptimizationCategory>(CategoryName(c), c))
    ];

    public IReadOnlyList<FilterOption<RiskLevel>> Risks { get; } =
    [
        new("Todos os riscos", null),
        new("Seguro", RiskLevel.Safe),
        new("Moderado", RiskLevel.Moderate),
        new("Avançado", RiskLevel.Advanced),
    ];

    [ObservableProperty] private FilterOption<OptimizationCategory> _selectedCategory;
    [ObservableProperty] private FilterOption<RiskLevel> _selectedRisk;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isLoading;

    // Apps pré-instalados
    public ObservableCollection<BloatChoice> Bloat { get; } = [];
    [ObservableProperty] private string _bloatStatus = "Clique em Procurar para ver quais apps pré-instalados estão neste PC.";

    // DNS
    public ObservableCollection<DnsBenchmark.Result> DnsResults { get; } = [];
    [ObservableProperty] private string _dnsStatus = "Mede a latência de Cloudflare, Google e Quad9 e do seu DNS atual.";

    public OptimizationsViewModel(MainViewModel main)
    {
        _main = main;
        _selectedCategory = Categories[0];
        _selectedRisk = Risks[0];
        foreach (var item in OptimizationCatalog.All.Where(i => !i.IsAction)) Items.Add(item);
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is OptimizationItem i && OptimizationFilter.Matches(i, SelectedCategory.Value, SelectedRisk.Value, Search);
        foreach (var item in Items) item.PropertyChanged += OnItemChanged;
    }

    /// <summary>Itens de ação pontual (limpar DNS, limpar memória, TRIM).</summary>
    public IReadOnlyList<OptimizationItem> Tools { get; } = OptimizationCatalog.All.Where(i => i.IsAction).ToList();

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OptimizationItem.IsSelected)) SelectedCount = Items.Count(i => i.IsSelected);
    }

    partial void OnSelectedCategoryChanged(FilterOption<OptimizationCategory> value) => View.Refresh();
    partial void OnSelectedRiskChanged(FilterOption<RiskLevel> value) => View.Refresh();
    partial void OnSearchChanged(string value) => View.Refresh();

    public async Task ActivateAsync()
    {
        IsLoading = true;
        try { await Task.Run(() => OptimizationEngine.Refresh(OptimizationCatalog.All)); }
        finally { IsLoading = false; }
        if (!_loaded)
        {
            _loaded = true;
            foreach (var item in Tools) item.PropertyChanged += OnItemChanged;
        }
        SelectedCount = Items.Count(i => i.IsSelected);
    }

    public void Deactivate() { }

    /// <summary>Marca (sem aplicar) os itens do preset que ainda não estão aplicados. Avançados e contraindicados nunca entram.</summary>
    private void Mark(RiskLevel maxRisk)
    {
        var preset = OptimizationCatalog.Preset(maxRisk).ToHashSet();
        foreach (var item in Items) item.IsSelected = preset.Contains(item) && item.State != ApplyState.Applied;
        _main.StatusMessage = $"{Items.Count(i => i.IsSelected)} otimizações marcadas pelo preset. Revise e clique em Aplicar selecionadas.";
    }

    [RelayCommand] private void PresetSafe() => Mark(RiskLevel.Safe);
    [RelayCommand] private void PresetBalanced() => Mark(RiskLevel.Moderate);
    [RelayCommand] private void ClearSelection() { foreach (var item in Items) item.IsSelected = false; }

    private bool ConfirmRisk(OptimizationItem item)
    {
        if (item.Risk == RiskLevel.Advanced)
            return _main.Confirm("Item avançado", $"{item.Name}\n\n{item.Caution}\n\nDeseja aplicar mesmo assim?");
        if (!item.IsRecommended)
            return _main.Confirm("Não recomendado para este PC", $"{item.Name}\n\n{item.NotRecommendedReason}\n\nDeseja aplicar mesmo assim?");
        return true;
    }

    [RelayCommand]
    private Task ApplySelectedAsync() => _main.RunOperationAsync("Aplicando otimizações…", async () =>
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) { _main.StatusMessage = "Nada selecionado"; return; }
        var risky = selected.Where(i => i.Risk != RiskLevel.Safe || !i.IsRecommended).ToList();
        if (risky.Count > 0 && !_main.Confirm("Revisar antes de aplicar",
                "Entre os itens marcados há ajustes que exigem atenção:\n\n" + string.Join("\n", risky.Select(i => $"• {i.Name} ({i.RiskDisplay})")) + "\n\nAplicar todos?"))
        {
            _main.StatusMessage = "Nada foi alterado";
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
        if (!item.IsAction && !ConfirmRisk(item)) { _main.StatusMessage = "Nada foi alterado"; return; }
        if (!item.IsAction) await _main.RequireRestorePointAsync($"Project Boost X - {item.Name}");
        var state = await OptimizationEngine.ApplyAsync(item);
        item.IsSelected = false;
        _main.StatusMessage = state == ApplyState.Failed
            ? $"{item.Name}: {item.StatusMessage}"
            : item.IsAction ? $"{item.Name}: concluído" : $"{item.Name}: aplicado" + (item.RequiresRestart ? " (reinicie para valer)" : "");
    });

    [RelayCommand]
    private Task RevertItemAsync(OptimizationItem? item) => _main.RunOperationAsync("Revertendo…", async () =>
    {
        if (item is null) return;
        var ok = await OptimizationEngine.RevertAsync(item);
        _main.StatusMessage = ok ? $"{item.Name}: revertido" : $"{item.Name}: {item.StatusMessage}";
    });

    // ---- Apps pré-instalados ----

    [RelayCommand]
    private async Task FindBloatAsync()
    {
        IsLoading = true;
        BloatStatus = "Procurando…";
        try
        {
            var installed = await AppxService.InstalledAsync();
            Bloat.Clear();
            foreach (var app in installed) Bloat.Add(new BloatChoice(app));
            BloatStatus = installed.Count == 0 ? "Nenhum dos apps da lista está instalado." : $"{installed.Count} apps encontrados. A remoção não tem reversão; reinstale pela Microsoft Store.";
        }
        catch (Exception ex) { BloatStatus = "Não foi possível listar: " + ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task RemoveBloatAsync() => _main.RunOperationAsync("Removendo apps…", async () =>
    {
        var chosen = Bloat.Where(b => b.IsSelected).ToList();
        if (chosen.Count == 0) { _main.StatusMessage = "Nenhum app marcado"; return; }
        if (!_main.Confirm("Remover apps", "Estes apps serão removidos para o seu usuário. Não há reversão automática (reinstale pela Microsoft Store):\n\n" +
                string.Join("\n", chosen.Select(c => "• " + c.App.Name)) + "\n\nContinuar?"))
        {
            _main.StatusMessage = "Nada foi removido";
            return;
        }
        try
        {
            await AppxService.RemoveAsync(chosen.Select(c => c.App.Package));
            ActionLog.Write("appx.remove", "remover", true, string.Join(", ", chosen.Select(c => c.App.Package)));
            _main.StatusMessage = $"{chosen.Count} apps removidos";
        }
        catch (Exception ex)
        {
            ActionLog.Write("appx.remove", "remover", false, ex.Message);
            _main.StatusMessage = "Falha ao remover: " + ex.Message;
        }
        await FindBloatAsync();
    });

    // ---- DNS ----

    private const string DnsOwner = "tools.dns";

    private static async Task<int> ActiveInterfaceIndexAsync()
    {
        var text = await ProcessRunner.RunPowerShellAsync(
            "(Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1).InterfaceIndex", 15_000);
        return int.TryParse(text.Trim(), out var index) ? index : throw new InvalidOperationException("Nenhuma conexão ativa foi encontrada.");
    }

    [RelayCommand]
    private async Task TestDnsAsync()
    {
        IsLoading = true;
        DnsStatus = "Medindo (cerca de 1 minuto)…";
        try
        {
            string? current = null;
            try
            {
                var index = await ActiveInterfaceIndexAsync();
                current = (await ProcessRunner.RunPowerShellAsync($"(Get-DnsClientServerAddress -InterfaceIndex {index} -AddressFamily IPv4).ServerAddresses | Select-Object -First 1", 15_000)).Trim();
            }
            catch { /* sem DNS atual: só os públicos */ }
            var results = await DnsBenchmark.RunAsync(current);
            DnsResults.Clear();
            foreach (var r in results) DnsResults.Add(r);
            DnsStatus = results.FirstOrDefault()?.MedianMs is { } best ? $"Mais rápido: {results[0].Name} ({best:0} ms)" : "Nenhum servidor respondeu.";
        }
        catch (Exception ex) { DnsStatus = "Falha no teste: " + ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task UseDnsAsync(DnsBenchmark.Result? result) => _main.RunOperationAsync("Trocando o DNS…", async () =>
    {
        if (result is null || result.Name == "Atual") return;
        if (!_main.Confirm("Trocar DNS", $"O DNS da conexão ativa passará a ser {result.Name} ({result.Primary}). Dá para voltar ao anterior a qualquer momento. Continuar?"))
        {
            _main.StatusMessage = "Nada foi alterado";
            return;
        }
        var index = await ActiveInterfaceIndexAsync();
        string[] servers = result.Secondary.Length > 0 ? [result.Primary, result.Secondary] : [result.Primary];
        await SystemSettingsBackupService.SetDnsAsync(index, servers, DnsOwner);
        ActionLog.Write(DnsOwner, "aplicar", true, string.Join(",", servers));
        _main.StatusMessage = $"DNS trocado para {result.Name}";
    });

    [RelayCommand]
    private Task RestoreDnsAsync() => _main.RunOperationAsync("Restaurando o DNS…", async () =>
    {
        await SystemSettingsBackupService.RevertAsync(DnsOwner);
        ActionLog.Write(DnsOwner, "reverter", true);
        _main.StatusMessage = "DNS restaurado ao que era antes";
    });

    private static string CategoryName(OptimizationCategory c) => c switch
    {
        OptimizationCategory.Power => "Energia",
        OptimizationCategory.Gaming => "Jogos",
        OptimizationCategory.Input => "Entrada",
        OptimizationCategory.Visual => "Visual",
        OptimizationCategory.Telemetry => "Privacidade",
        OptimizationCategory.Services => "Serviços",
        OptimizationCategory.Network => "Rede",
        OptimizationCategory.Memory => "Memória",
        OptimizationCategory.System => "Sistema",
        _ => c.ToString()
    };
}
