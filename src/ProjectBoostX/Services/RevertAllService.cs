using System.IO;
using System.Text.RegularExpressions;

namespace BoostParaPc.Services;

public static class RevertAllService
{
    public sealed record RevertResult(int BackupsRestored, int BackupsFailed, string Message)
    {
        public bool Success => BackupsFailed == 0;
    }
    public static async Task<RevertResult> RevertEverythingAsync(IProgress<string>? progress = null)
    {
        int restored = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in RegistryBackupService.PendingBackups()
                             .Where(f => !Path.GetFileName(f).StartsWith("startup_", StringComparison.Ordinal)))
                {
                    progress?.Report($"Restaurando {Path.GetFileName(file)}…");
                    RegistryBackupService.RestoreAndArchive(file);
                    restored++;
                }
            }).ConfigureAwait(false);
            progress?.Report("Restaurando energia, serviços e inicialização…");
            await SystemSettingsBackupService.RevertAllAsync().ConfigureAwait(false);
            await StartupService.RevertAllAsync().ConfigureAwait(false);
            await RevertServiceStartTypesAsync().ConfigureAwait(false);
            OptimizationStateStore.ClearAll();
            return new(restored, 0, "Reversão concluída. Restaurados apenas os ajustes com backup. Alguns ajustes exigem reiniciar o Windows.");
        }
        catch (Exception ex)
        {
            return new(restored, 1, $"Reversão incompleta: {ex.Message}. Backups pendentes e histórico preservados.");
        }
    }
    /// <summary>Compatibilidade com dumps antigos; não inventa estado de execução ausente no backup.</summary>
    public static async Task RevertServiceStartTypesAsync()
    {
        foreach (var file in Directory.GetFiles(AppPaths.BackupDir, "service_*.txt").OrderByDescending(Path.GetFileName))
        {
            var match = Regex.Match(Path.GetFileName(file), @"\Aservice_(.+)_\d{8}_\d{6}\.txt\z");
            if (!match.Success) throw new InvalidDataException($"Nome de backup de serviço inválido: {Path.GetFileName(file)}");
            var name = match.Groups[1].Value;
            if (!Regex.IsMatch(name, @"\A[A-Za-z0-9_.-]+\z")) throw new InvalidDataException("Nome de serviço inválido");
            var dump = File.ReadAllText(file);
            string? start = dump.Contains("AUTO_START", StringComparison.Ordinal) ? "auto"
                : dump.Contains("DEMAND_START", StringComparison.Ordinal) ? "demand"
                : dump.Contains("DISABLED", StringComparison.Ordinal) ? "disabled" : null;
            if (start is null) throw new InvalidDataException($"Backup antigo de {name} não contém o tipo de inicialização");
            await ProcessRunner.RunCheckedAsync("sc.exe", $"config \"{name}\" start= {start}", timeoutMs: 8_000).ConfigureAwait(false);
            RegistryBackupService.Archive(file);
        }
    }
}
