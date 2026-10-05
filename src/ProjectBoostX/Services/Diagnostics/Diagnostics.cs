namespace BoostParaPc.Services;

public enum Severity { Info, Warning, Critical }

public sealed record Finding(string Id, Severity Severity, string Title, string Detail, string? FixLabel = null, string? FixUri = null);

/// <summary>Dados coletados do PC. Campo nulo = não foi possível ler; nunca vira achado.</summary>
public sealed record DiagnosticInputs
{
    public int? DisplayCurrentHz { get; init; }
    public int? DisplayMaxHz { get; init; }
    public int? RamSpeedMhz { get; init; }
    /// <summary>"DDR4", "DDR5" ou outro texto.</summary>
    public string? RamType { get; init; }
    public int? RamModuleCount { get; init; }
    public int? GpuDriverAgeDays { get; init; }
    public string? GpuName { get; init; }
    public bool? TrimDisabled { get; init; }
    public double? SystemFreePercent { get; init; }
    public bool? PagingDisabled { get; init; }
    public bool? RebootPending { get; init; }
    public bool? OnBattery { get; init; }
    public IReadOnlyList<string>? GamesOnHdd { get; init; }
    public bool? TcpAutoTuningDisabled { get; init; }
    public bool? MemoryIntegrityOn { get; init; }
}

public static class Diagnostics
{
    public static IReadOnlyList<Finding> Evaluate(DiagnosticInputs d)
    {
        var f = new List<Finding>();

        if (d.DisplayCurrentHz is { } cur && d.DisplayMaxHz is { } max && cur < max)
            f.Add(new("display.refresh", Severity.Warning, "Monitor abaixo da taxa máxima",
                $"O monitor está em {cur} Hz, mas a mesma resolução aceita {max} Hz.",
                "Abrir configurações de tela", "ms-settings:display-advanced"));

        if (d.RamSpeedMhz is { } speed && d.RamType is { } type &&
            ((type == "DDR4" && speed <= 2666) || (type == "DDR5" && speed <= 4800)))
            f.Add(new("ram.basespeed", Severity.Info, "Memória pode estar na velocidade base",
                $"Sua {type} roda a {speed} MHz. Pode ser que o perfil XMP/EXPO esteja desligado na BIOS (é uma estimativa, não uma certeza).",
                "Como ativar XMP/EXPO", "https://www.google.com/search?q=como+ativar+XMP+EXPO+na+BIOS"));

        if (d.RamModuleCount == 1)
            f.Add(new("ram.single", Severity.Warning, "Memória em canal único",
                "Há um único módulo de RAM. Dois módulos iguais (canal duplo) dão mais desempenho em jogos e no vídeo integrado."));

        if (d.GpuDriverAgeDays is > 180)
            f.Add(new("gpu.driver", Severity.Warning, "Driver de vídeo antigo",
                $"O driver da placa de vídeo tem {d.GpuDriverAgeDays} dias.",
                "Procurar driver", "https://www.google.com/search?q=" + Uri.EscapeDataString((d.GpuName ?? "placa de vídeo") + " driver")));

        if (d.TrimDisabled == true)
            f.Add(new("disk.trim", Severity.Warning, "TRIM desligado",
                "O Windows não avisa o SSD sobre blocos livres; a escrita pode ficar mais lenta com o tempo.",
                "Abrir Otimizações", "app:optimizations"));

        if (d.SystemFreePercent is < 15)
            f.Add(new("disk.free", Severity.Critical, "C: quase cheio",
                $"Só {d.SystemFreePercent:0}% do disco do sistema está livre.", "Abrir Limpeza", "app:cleanup"));

        if (d.PagingDisabled == true)
            f.Add(new("ram.paging", Severity.Warning, "Arquivo de paginação desativado",
                "Sem paginação, programas e jogos podem fechar quando a RAM acaba.",
                "Abrir desempenho", "app:systemperformance"));

        if (d.RebootPending == true)
            f.Add(new("system.reboot", Severity.Info, "Reinício pendente",
                "O Windows está esperando um reinício para concluir alterações."));

        if (d.OnBattery == true)
            f.Add(new("power.battery", Severity.Info, "Notebook na bateria",
                "Na bateria o Windows limita o desempenho. Ligue na tomada para jogar."));

        if (d.GamesOnHdd is { Count: > 0 } games)
            f.Add(new("games.hdd", Severity.Warning, "Jogos instalados em HD",
                "Estes jogos estão em disco mecânico e carregam mais devagar: " + string.Join(", ", games) + "."));

        if (d.TcpAutoTuningDisabled == true)
            f.Add(new("net.tcp", Severity.Warning, "Ajuste automático de TCP desligado",
                "O ajuste automático da janela de recepção está desativado e pode limitar a velocidade de download.",
                "Restaurar para Normal", "fix:tcp"));

        if (d.MemoryIntegrityOn == true)
            f.Add(new("sec.memoryintegrity", Severity.Info, "Integridade de Memória ligada",
                "É um recurso de segurança que custa alguns por cento de desempenho em jogos. Desligar é opcional e reduz a proteção.",
                "Ver item", "app:optimizations"));

        return f;
    }

    /// <summary>100 − 15×críticos − 7×avisos, mínimo 0.</summary>
    public static int Score(IEnumerable<Finding> findings)
    {
        var list = findings.ToList();
        return Math.Max(0, 100 - 15 * list.Count(x => x.Severity == Severity.Critical) - 7 * list.Count(x => x.Severity == Severity.Warning));
    }
}
