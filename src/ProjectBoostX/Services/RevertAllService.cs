using System.IO;
using BoostParaPc.Services;

namespace BoostParaPc.Services;

/// <summary>
/// Reverte otimizações aplicadas: restaura backups de registro e limpa o estado salvo.
/// </summary>
public static class RevertAllService
{
    public sealed record RevertResult(int BackupsRestored, int BackupsFailed, string Message);

    public static async Task<RevertResult> RevertEverythingAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            var dir = AppPaths.BackupDir;

            int ok = 0, fail = 0;

            if (Directory.Exists(dir))
            {
                // Mais antigo primeiro — restaura na ordem original
                var files = Directory.GetFiles(dir, "*.json")
                    .OrderBy(File.GetCreationTimeUtc)
                    .ToList();

                foreach (var file in files)
                {
                    progress?.Report($"Restaurando {Path.GetFileName(file)}…");
                    try
                    {
                        var n = RegistryBackupService.RestoreBackup(file);
                        if (n > 0) ok++;
                        else fail++;
                    }
                    catch
                    {
                        fail++;
                    }
                }
            }

            progress?.Report("Limpando histórico de otimizações…");
            OptimizationStateStore.ClearAll();

            // Alguns ajustes que não ficam em backup de registro
            try
            {
                ProcessRunner.RunAsync("powercfg", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e", timeoutMs: 8_000)
                    .GetAwaiter().GetResult(); // Equilibrado
            }
            catch { }

            try
            {
                ProcessRunner.RunAsync("powercfg", "/hibernate on", timeoutMs: 8_000).GetAwaiter().GetResult();
            }
            catch { }

            var msg = ok > 0
                ? $"{ok} backups restaurados, {fail} falhas. Plano Equilibrado reativado."
                : fail > 0
                    ? $"Nenhum backup restaurado ({fail} falhas). Pode ser preciso reiniciar o PC."
                    : "Nenhum backup encontrado — nada para reverter no registro.";

            return new RevertResult(ok, fail, msg);
        }).ConfigureAwait(false);
    }

    public static async Task RevertServiceStartTypesAsync()
    {
        await Task.Run(() =>
        {
            var dir = AppPaths.BackupDir;
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "service_*.txt"))
            {
                try
                {
                    var name = Path.GetFileName(file)
                        .Replace("service_", "")
                        .Split('_')[0];

                    // Extrai START_TYPE do dump do sc qc
                    var text = File.ReadAllText(file);
                    string start = "demand";
                    if (text.Contains("AUTO_START")) start = "auto";
                    else if (text.Contains("DEMAND_START")) start = "demand";
                    else if (text.Contains("DISABLED")) start = "disabled";

                    ProcessRunner.RunAsync("sc", $"config {name} start= {start}", timeoutMs: 8_000)
                        .GetAwaiter().GetResult();
                    ProcessRunner.RunAsync("sc", $"start {name}", timeoutMs: 8_000)
                        .GetAwaiter().GetResult();
                }
                catch { }
            }
        }).ConfigureAwait(false);
    }
}
