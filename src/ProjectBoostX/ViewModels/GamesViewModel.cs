using System.Collections.ObjectModel;
using System.IO;
using BoostParaPc.Models;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class GamesViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;
    private bool _loadedOnce;

    public ObservableCollection<GameProfile> Games { get; } = [];

    [ObservableProperty]
    private string _lastResult = "Clique em Detectar jogos para varrer Steam, Epic, Riot e pastas comuns.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    private GameProfile? _selectedGame;

    public bool HasSelectedGame => SelectedGame is not null;

    public GamesViewModel(MainViewModel main) => _main = main;

    public async Task ActivateAsync()
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        await DetectAsync();
    }

    public void Deactivate() { }

    /// <summary>Os cartões aparecem logo com a capa provisória; as capas reais chegam depois, 4 por vez, sem travar a tela.</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        LastResult = "Procurando jogos (Steam, Epic, registro, discos)…";
        try
        {
            Games.Clear();
            SelectedGame = null;
            var list = await Task.Run(() => GameProfileService.DetectGames());
            foreach (var g in list)
            {
                g.LoadRecommendationInfo();
                g.CoverPath = await Task.Run(() => CoverService.EnsureFallbackCoverFile(g.Name));
                Games.Add(g);
            }
            if (list.Count > 0) SelectedGame = Games[0];
            LastResult = list.Count == 0
                ? "Nenhum jogo encontrado. Tente rodar como administrador ou ter jogos em pastas padrão."
                : $"{list.Count} jogos · buscando capas…";

            var real = 0;
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (game, ct) =>
            {
                try
                {
                    var path = await CoverService.ResolveCoverPathAsync(game, ct);
                    await dispatcher.InvokeAsync(() => game.CoverPath = path);
                    if (!Path.GetFileName(path).StartsWith("fb_", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref real);
                }
                catch { /* fica com a capa provisória */ }
            });
            if (list.Count > 0) LastResult = $"{list.Count} jogos · {real} capas reais · clique numa capa para ver as otimizações";
        }
        catch (Exception ex) { LastResult = "Falha ao detectar jogos: " + ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenGame(GameProfile? game)
    {
        if (game is null) return;
        foreach (var g in Games) g.IsDetailOpen = false;
        game.IsDetailOpen = true;
        SelectedGame = game;
        // Não força IsSelected: inspecionar não entra no lote por engano.
    }

    [RelayCommand]
    private Task ApplyRecommendedForSelectedAsync() => RunForSelectedAsync(useRecommendation: true);

    /// <summary>As quatro opções manuais (tela cheia, DPI, prioridade, GPU) ficam no painel do jogo.</summary>
    [RelayCommand]
    private Task ApplyManualForSelectedAsync() => RunForSelectedAsync(useRecommendation: false);

    private Task RunForSelectedAsync(bool useRecommendation) => _main.RunOperationAsync("Aplicando perfil…", async () =>
    {
        var g = SelectedGame;
        if (g is null) { _main.StatusMessage = "Selecione um jogo"; return; }
        await _main.RequireRestorePointAsync($"Project Boost X - {g.Name}");
        _main.StatusMessage = $"Aplicando: {g.Name}";
        await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation));
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
    private Task ApplyRecommendedSelectedAsync() => RunProfilesAsync(revert: false);

    [RelayCommand]
    private Task RevertSelectedAsync() => RunProfilesAsync(revert: true);

    private Task RunProfilesAsync(bool revert) => _main.RunOperationAsync(
        revert ? "Revertendo perfis…" : "Aplicando perfis…", async () =>
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0) { _main.StatusMessage = "Nenhum jogo marcado"; return; }
        if (!revert) await _main.RequireRestorePointAsync("Project Boost X - Perfis de jogo");

        var ok = 0;
        var failures = new List<string>();
        foreach (var g in selected)
        {
            _main.StatusMessage = $"{(revert ? "Revertendo" : "Aplicando")}: {g.Name}";
            try
            {
                if (revert) await Task.Run(() => GameProfileService.RevertProfile(g));
                else await Task.Run(() => GameProfileService.ApplyProfile(g, useRecommendation: true));
                ok++;
            }
            catch (Exception ex) { failures.Add($"{g.Name}: {ex.Message}"); }
        }
        LastResult = $"{ok}/{selected.Count} perfis {(revert ? "revertidos" : "aplicados")} · {failures.Count} falhas. {string.Join("; ", failures)}".Trim();
        _main.StatusMessage = LastResult;
    });

    [RelayCommand]
    private Task BoostRunningAsync() => _main.RunOperationAsync("Aumentando a prioridade dos jogos em execução…", async () =>
    {
        var msg = await Task.Run(GameProfileService.BoostRunningGames);
        LastResult = msg;
        _main.StatusMessage = msg;
    });

    [RelayCommand] private void SelectAll() { foreach (var g in Games) g.IsSelected = true; }
    [RelayCommand] private void SelectNone() { foreach (var g in Games) g.IsSelected = false; }
}
