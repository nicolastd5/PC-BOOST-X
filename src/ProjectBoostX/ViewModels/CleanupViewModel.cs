using System.Collections.ObjectModel;
using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostParaPc.ViewModels;

public partial class CleanupViewModel : ObservableObject, IScreen
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CleanupTarget> Targets { get; } = [];

    [ObservableProperty] private string _totalSizeDisplay = "—";
    [ObservableProperty] private string _lastResult = "";
    [ObservableProperty] private string _scanStatus = "Clique em Analisar para varrer temporários.";

    /// <summary>A análise só bloqueia esta tela, não a janela inteira: dá para navegar enquanto ela roda.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CancelCommand))]
    private bool _isLoading;

    private long _totalBytes;

    public CleanupViewModel(MainViewModel main) => _main = main;

    public async Task ActivateAsync()
    {
        if (Targets.Count == 0 && !IsLoading) await ScanAsync();
    }

    public void Deactivate() { }

    private bool CanScan() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsLoading = true;
        try
        {
            await ScanTargetsAsync(cts.Token);
        }
        catch (OperationCanceledException) { ScanStatus = "Análise cancelada."; }
        catch (Exception ex) { ScanStatus = "Falha na análise: " + ex.Message; }
        finally
        {
            _cts = null;
            IsLoading = false;
        }
    }

    private async Task ScanTargetsAsync(CancellationToken ct)
    {
        ScanStatus = "Varrendo pastas (pode levar alguns segundos)…";
        Targets.Clear();
        RecalcTotal();
        var progress = new Progress<string>(s => { if (!ct.IsCancellationRequested && IsLoading) ScanStatus = s; });
        var found = await CleanupService.ScanAsync(progress, ct);
        ct.ThrowIfCancellationRequested();
        foreach (var t in found)
        {
            t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CleanupTarget.Selected)) RecalcTotal(); };
            Targets.Add(t);
        }
        RecalcTotal();
        ScanStatus = $"Pronto · {Targets.Count} alvos";
    }

    [RelayCommand]
    private Task CleanAsync() => _main.RunOperationAsync("Limpando…", async () =>
    {
        var selected = Targets.Where(t => t.Selected).ToList();
        if (selected.Count == 0) { _main.StatusMessage = "Nada selecionado para limpar"; return; }

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
            // Arquivos em uso são pulados e contam como "erros"; a limpeza só falhou se nada saiu.
            ActionLog.Write("cleanup", "limpar", result.Errors == 0 || result.FilesRemoved > 0, LastResult);
            ct.ThrowIfCancellationRequested();
            await ScanTargetsAsync(ct);
            _main.StatusMessage = LastResult;
        }
        catch (OperationCanceledException) { ScanStatus = "Limpeza cancelada."; throw; }
        catch { ScanStatus = "Falha na limpeza."; throw; }
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
        ScanStatus = "Cancelando…";
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
