using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>Aplica, detecta e reverte itens do catálogo a partir da declaração de cada um.</summary>
public static class OptimizationEngine
{
    /// <summary>Nunca lança: o resultado fica em <see cref="OptimizationItem.State"/> e <see cref="OptimizationItem.StatusMessage"/>.</summary>
    public static async Task<ApplyState> ApplyAsync(OptimizationItem item)
    {
        item.StatusMessage = null;
        try
        {
            await Task.Run(() =>
            {
                foreach (var v in item.Registry)
                    RegistryBackupService.Set(v.Root, v.Key, v.Name, v.Value, v.Kind, item.Id);
            });
            if (item.ApplyExtra is not null) await item.ApplyExtra(item.Id);

            ActionLog.Write(item.Id, "aplicar", true);
            if (item.IsAction)
            {
                item.StatusMessage = $"Executado às {DateTime.Now:HH:mm}";
                return item.State = ApplyState.NotApplied;
            }
            OptimizationStateStore.MarkApplied(item.Id);
            return item.State = ApplyState.Applied;
        }
        catch (Exception ex)
        {
            // Desfaz o que já foi gravado. Se nem isso for possível, os backups continuam pendentes.
            var message = ex.Message;
            try { await RevertCoreAsync(item.Id); }
            catch { message += " A reversão automática falhou; use Reverter para tentar de novo."; }
            item.StatusMessage = message;
            ActionLog.Write(item.Id, "aplicar", false, message);
            return item.State = ApplyState.Failed;
        }
    }

    /// <summary>Nunca lança. Devolve <c>false</c> se algum backup não pôde ser restaurado (ele continua pendente).</summary>
    public static async Task<bool> RevertAsync(OptimizationItem item)
    {
        try
        {
            await RevertCoreAsync(item.Id);
            item.StatusMessage = null;
            item.State = IsApplied(item) ? ApplyState.Applied : ApplyState.NotApplied;
            ActionLog.Write(item.Id, "reverter", true);
            return true;
        }
        catch (Exception ex)
        {
            item.StatusMessage = $"Reversão incompleta: {ex.Message}";
            ActionLog.Write(item.Id, "reverter", false, ex.Message);
            return false;
        }
    }

    public static bool IsApplied(OptimizationItem item)
    {
        if (item.IsAction) return false;
        // Sem nada para ler no sistema (ex.: plano de energia duplicado): vale o que o programa registrou.
        if (item.Registry.Count == 0 && item.IsAppliedExtra is null)
            return OptimizationStateStore.Load().Contains(item.Id);
        try { return RegistryMatches(item) && (item.IsAppliedExtra?.Invoke() ?? true); }
        catch { return false; }
    }

    public static void Refresh(IEnumerable<OptimizationItem> items)
    {
        foreach (var item in items)
            item.State = IsApplied(item) ? ApplyState.Applied : ApplyState.NotApplied;
    }

    public static void ApplyHardwareRules(IEnumerable<OptimizationItem> items, SystemInfo info)
    {
        foreach (var item in items)
            item.NotRecommendedReason = item.NotRecommended?.Invoke(info);
    }

    internal static bool RegistryMatches(OptimizationItem item) => item.Registry.All(v =>
    {
        using var key = v.Root.OpenSubKey(v.Key, writable: false);
        return Equals(key?.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), v.Value);
    });

    private static async Task RevertCoreAsync(string owner)
    {
        await Task.Run(() => RegistryBackupService.RestoreMatchingBackups(owner));
        await SystemSettingsBackupService.RevertAsync(owner);
        OptimizationStateStore.Clear(owner);
    }
}
