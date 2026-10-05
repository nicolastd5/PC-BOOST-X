using System.Collections.ObjectModel;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public sealed record HistoryRow(ActionLog.Entry Entry, string Name, bool CanRevert)
{
    public string When => Entry.Utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public string Result => Entry.Ok ? "Concluído" : "Falhou";
    public string Action => Entry.Action;
}

public partial class HistoryViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    /// <summary>0 = tudo, 1 = só sucessos, 2 = só falhas.</summary>
    [ObservableProperty] private int _filter;
    [ObservableProperty] private string _summary = "";

    public HistoryViewModel(MainViewModel main) => _main = main;

    public Task ActivateAsync() => LoadAsync();
    public void Deactivate() { }

    partial void OnFilterChanged(int value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var entries = await Task.Run(ActionLog.Read);
        Rows.Clear();
        foreach (var entry in entries.Where(e => Filter == 0 || (Filter == 1) == e.Ok).Take(500))
        {
            var item = OptimizationCatalog.All.FirstOrDefault(i => i.Id == entry.ItemId);
            Rows.Add(new HistoryRow(entry, item?.Name ?? entry.ItemId,
                CanRevert: item is { IsAction: false, State: ApplyState.Applied } && entry.Action == "aplicar" && entry.Ok));
        }
        Summary = Rows.Count == 0 ? "Nenhuma ação registrada ainda." : $"{Rows.Count} registros";
    }

    [RelayCommand]
    private Task RevertAsync(HistoryRow? row) => _main.RunOperationAsync("Revertendo…", async () =>
    {
        var item = OptimizationCatalog.All.FirstOrDefault(i => i.Id == row?.Entry.ItemId);
        if (item is null) return;
        var ok = await OptimizationEngine.RevertAsync(item);
        _main.StatusMessage = ok ? $"{item.Name}: revertido" : $"{item.Name}: {item.StatusMessage}";
        await LoadAsync();
    });

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ProjectBoostX-suporte-{DateTime.Now:yyyyMMdd}.zip",
            Filter = "Arquivo zip (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var info = _main.SystemInfo;
            var hardware = info is null ? "Informações do PC indisponíveis"
                : $"{info.OsName} {info.OsVersion}\nCPU: {info.CpuName} ({info.CpuCores} núcleos / {info.CpuLogical} threads)\nRAM: {info.TotalRamGb} GB\nGPU: {info.GpuName}\nDisco: {info.DiskType}, {info.DiskFreeGb}/{info.DiskTotalGb} GB livres\nNotebook: {info.IsLaptop}\nProject Boost X {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";
            await Task.Run(() => SupportExport.Create(dialog.FileName, hardware));
            _main.StatusMessage = "Arquivo de suporte criado. Nada foi enviado: você decide para quem mandar.";
        }
        catch (Exception ex) { _main.StatusMessage = "Não foi possível exportar: " + ex.Message; }
    }
}
