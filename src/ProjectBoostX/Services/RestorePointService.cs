using System.Text;

namespace BoostParaPc.Services;

public static class RestorePointService
{
    private const string SuccessToken = "PROJECT_BOOST_X_RESTORE_CREATED";

    public static Task<bool> CreateAsync(string description = "Project Boost X - antes de otimizar")
        => CreateAsync(description, (script, timeout) => ProcessRunner.RunPowerShellAsync(script, timeout));

    internal static async Task<bool> CreateAsync(string description, Func<string, int, Task<string>> execute)
    {
        try
        {
            var result = await execute(BuildCreateScript(description), 60_000).ConfigureAwait(false);
            return string.Equals(result.Trim(), SuccessToken, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    internal static string BuildCreateScript(string description)
    {
        // A descrição pode vir do nome de um executável. Codificar em Base64 evita
        // que aspas, acentos, quebras de linha ou metacaracteres sejam interpretados
        // pelo PowerShell como parte do script.
        var encodedDescription = Convert.ToBase64String(Encoding.UTF8.GetBytes(description ?? string.Empty));
        return $$"""
        $ErrorActionPreference = 'Stop'
        $description = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedDescription}}'))
        $drive = $env:SystemDrive.TrimEnd('\') + '\'
        Enable-ComputerRestore -Drive $drive | Out-Null
        $before = @(Get-ComputerRestorePoint | Select-Object -ExpandProperty SequenceNumber)
        Checkpoint-Computer -Description $description -RestorePointType 'MODIFY_SETTINGS' | Out-Null
        $created = @(Get-ComputerRestorePoint | Where-Object {
            $_.SequenceNumber -notin $before -and $_.Description -ceq $description
        })
        if ($created.Count -eq 0) { throw 'Não foi possível verificar um novo ponto de restauração.' }
        '{{SuccessToken}}'
        """;
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
