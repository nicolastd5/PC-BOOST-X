using System.Diagnostics;

namespace BoostParaPc.Services;

public static class RestorePointService
{
    public static async Task<bool> CreateAsync(string description = "Project Boost X - antes de otimizar")
    {
        // Ativa System Restore na unidade C: se necessário e cria ponto
        var script = """
            $ErrorActionPreference = 'SilentlyContinue'
            Enable-ComputerRestore -Drive "C:\" | Out-Null
            Checkpoint-Computer -Description "ARGS_DESC" -RestorePointType "MODIFY_SETTINGS"
            "OK"
            """;
        script = script.Replace("ARGS_DESC", description.Replace("\"", "'"));

        var result = await ProcessRunner.RunPowerShellAsync(script, timeoutMs: 60_000);
        return result.Contains("OK", StringComparison.OrdinalIgnoreCase)
               || result.Contains("sucesso", StringComparison.OrdinalIgnoreCase)
               || string.IsNullOrWhiteSpace(result);
    }

    public static async Task<IReadOnlyList<string>> ListAsync()
    {
        var output = await ProcessRunner.RunPowerShellAsync(
            "Get-ComputerRestorePoint | Select-Object -Last 10 SequenceNumber, Description, CreationTime | Format-Table -AutoSize | Out-String",
            timeoutMs: 20_000);

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();
    }
}
