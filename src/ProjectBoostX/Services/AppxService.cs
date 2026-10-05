using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoostParaPc.Services;

/// <summary>Apps pré-instalados que podem ser removidos para o usuário atual. A remoção não tem reversão.</summary>
public static partial class AppxService
{
    public sealed record Bloat(string Package, string Name);

    public static IReadOnlyList<Bloat> Known { get; } =
    [
        new("Microsoft.BingNews", "Notícias"),
        new("Microsoft.BingWeather", "Clima"),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
        new("Microsoft.GetHelp", "Obter Ajuda"),
        new("Microsoft.Getstarted", "Dicas"),
        new("Microsoft.WindowsFeedbackHub", "Hub de Comentários"),
        new("Microsoft.ZuneVideo", "Filmes e TV"),
        new("Microsoft.ZuneMusic", "Groove Música"),
        new("Microsoft.MicrosoftOfficeHub", "Office Hub"),
        new("Clipchamp.Clipchamp", "Clipchamp"),
        new("Microsoft.PowerAutomateDesktop", "Power Automate"),
        new("Microsoft.Todos", "Microsoft To Do"),
        new("Microsoft.549981C3F5F10", "Cortana"),
    ];

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9.\-]{0,127}\z")]
    private static partial Regex PackagePattern();

    /// <summary>O nome entra num comando do PowerShell; só aceita o que está na lista fixa e tem formato de pacote.</summary>
    public static bool IsValidPackageName(string name) =>
        PackagePattern().IsMatch(name) && Known.Any(k => k.Package.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string BuildRemoveScript(IEnumerable<string> packages)
    {
        var list = packages.ToList();
        var bad = list.FirstOrDefault(p => !IsValidPackageName(p));
        if (bad is not null) throw new ArgumentException($"Pacote não permitido: {bad}");
        return string.Join("; ", list.Select(p => $"Get-AppxPackage -Name '{p}' | Remove-AppxPackage"));
    }

    public static async Task<IReadOnlyList<Bloat>> InstalledAsync()
    {
        var names = string.Join(",", Known.Select(k => $"'{k.Package}'"));
        var json = await ProcessRunner.RunPowerShellAsync(
            $"Get-AppxPackage | Where-Object {{ @({names}) -contains $_.Name }} | Select-Object -ExpandProperty Name | ConvertTo-Json -Compress", 60_000);
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        var installed = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToHashSet()
            : [doc.RootElement.GetString()];
        return Known.Where(k => installed.Contains(k.Package)).ToList();
    }

    public static Task RemoveAsync(IEnumerable<string> packages) =>
        ProcessRunner.RunPowerShellAsync(BuildRemoveScript(packages), 300_000);
}
