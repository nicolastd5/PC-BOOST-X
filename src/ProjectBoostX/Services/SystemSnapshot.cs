using System.IO;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>Retrato do estado do PC, para comparar antes e depois das otimizações.</summary>
public sealed record SystemSnapshot(DateTime TakenUtc, int? BootMs, int RamUsedMb, int Processes, int RunningServices, int StartupItems)
{
    public sealed record Delta(string Label, string Before, string After, int? Change);

    public static string BaselineFile => Path.Combine(AppPaths.DataDir, "baseline.json");

    /// <summary>Salva o primeiro retrato. Nunca sobrescreve um existente. Devolve o retrato que vale como base.</summary>
    public static SystemSnapshot SaveBaselineIfMissing(SystemSnapshot current)
    {
        if (LoadBaseline() is { } existing) return existing;
        Directory.CreateDirectory(Path.GetDirectoryName(BaselineFile)!);
        File.WriteAllText(BaselineFile, JsonSerializer.Serialize(current));
        return current;
    }

    public static SystemSnapshot? LoadBaseline()
    {
        try { return File.Exists(BaselineFile) ? JsonSerializer.Deserialize<SystemSnapshot>(File.ReadAllText(BaselineFile)) : null; }
        catch { return null; }
    }

    /// <summary>Compara linha a linha. Campo ausente em qualquer lado aparece como "—" e sem variação.</summary>
    public static IReadOnlyList<Delta> Compare(SystemSnapshot before, SystemSnapshot after) =>
    [
        Row("Tempo de boot (s)", before.BootMs / 1000, after.BootMs / 1000),
        Row("RAM em uso (MB)", before.RamUsedMb, after.RamUsedMb),
        Row("Processos", before.Processes, after.Processes),
        Row("Serviços em execução", before.RunningServices, after.RunningServices),
        Row("Itens de inicialização", before.StartupItems, after.StartupItems),
    ];

    private static Delta Row(string label, int? before, int? after) =>
        new(label, before?.ToString() ?? "—", after?.ToString() ?? "—", before is { } b && after is { } a ? a - b : null);
}
