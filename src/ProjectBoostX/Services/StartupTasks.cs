using System.Text.Json;
using System.Text.RegularExpressions;
using BoostParaPc.Models;

namespace BoostParaPc.Services;

/// <summary>Tarefas agendadas que rodam no logon (fora de \Microsoft\). Desativar e reativar passa pelo journal, com dono startup.task.&lt;nome&gt;.</summary>
public static partial class StartupTasks
{
    public const string Source = "Tarefa agendada";

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+\z")]
    private static partial Regex SafeName();

    public static string Owner(StartupItem item) => "startup.task." + item.Name;

    public static async Task<IReadOnlyList<StartupItem>> GetAsync()
    {
        try
        {
            var json = await ProcessRunner.RunPowerShellAsync(
                "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' -and $_.TaskName -ne 'ProjectBoostX' -and " +
                "($_.Triggers.CimClass.CimClassName -contains 'MSFT_TaskLogonTrigger') } | " +
                "Select-Object TaskName,TaskPath,State | ConvertTo-Json -Compress", 30_000).ConfigureAwait(false);
            return Parse(json);
        }
        catch { return []; }
    }

    internal static IReadOnlyList<StartupItem> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        var items = new List<StartupItem>();
        foreach (var e in elements)
        {
            var name = e.TryGetProperty("TaskName", out var n) ? n.GetString() : null;
            var path = e.TryGetProperty("TaskPath", out var p) ? p.GetString() : null;
            if (name is null || path is null || !SafeName().IsMatch(name)) continue;   // nome fora do padrão: não é oferecido
            var disabled = e.TryGetProperty("State", out var s) &&
                (s.ValueKind == JsonValueKind.Number ? s.GetInt32() == 1 : string.Equals(s.GetString(), "Disabled", StringComparison.OrdinalIgnoreCase));
            items.Add(new StartupItem { Name = name, Command = path + name, Source = Source, Location = path, IsEnabled = !disabled });
        }
        return items;
    }

    public static Task DisableAsync(StartupItem item) =>
        SystemSettingsBackupService.DisableScheduledTaskAsync(item.Location, item.Name, Owner(item));

    public static Task EnableAsync(StartupItem item) => SystemSettingsBackupService.RevertAsync(Owner(item));
}
