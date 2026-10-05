using System.Collections.ObjectModel;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class StartupViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;

    public ObservableCollection<StartupItem> Items { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _summary = "";

    public StartupViewModel(MainViewModel main) => _main = main;

    public async Task ActivateAsync()
    {
        if (Items.Count == 0 && !IsLoading) await LoadAsync();
    }

    public void Deactivate() { }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var list = (await StartupService.GetItemsAsync()).Concat(await StartupTasks.GetAsync());
            Items.Clear();
            foreach (var i in list.OrderBy(i => i.Name)) Items.Add(i);
            Summary = $"{Items.Count} itens · {Items.Count(i => i.IsEnabled)} ativos";
        }
        catch (Exception ex) { Summary = "Falha ao carregar: " + ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private Task DisableAsync(StartupItem? item) => _main.RunOperationAsync("Desativando inicialização…", async () =>
    {
        if (item is null) return;
        await _main.RequireRestorePointAsync("Project Boost X - Inicialização");
        _main.StatusMessage = $"Desativando {item.Name}…";
        if (item.Source == StartupTasks.Source) await StartupTasks.DisableAsync(item);
        else await StartupService.DisableAsync(item);
        item.IsEnabled = false;
        ActionLog.Write("startup." + item.Name, "desativar", true);
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
        ActionLog.Write("startup." + item.Name, "reativar", true);
        _main.StatusMessage = $"{item.Name} reativado";
    });
}
