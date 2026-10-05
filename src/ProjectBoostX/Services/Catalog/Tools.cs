using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string DeviceGuardHvci = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string DirectXPrefs = @"Software\Microsoft\DirectX";
    private const string SwapEffect = "SwapEffectUpgradeEnable=1;";

    private static string? OnlyWindows11(SystemInfo pc) => pc.IsWindows11 ? null : "Disponível só no Windows 11.";

    /// <summary>Acrescenta o campo sem apagar os outros que o usuário já tenha em DirectXUserGlobalSettings.</summary>
    internal static string MergeGlobalSettings(string? existing)
    {
        var fields = (existing ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => !f.StartsWith("SwapEffectUpgradeEnable=", StringComparison.OrdinalIgnoreCase)).ToList();
        fields.Add("SwapEffectUpgradeEnable=1");
        return string.Join(';', fields) + ";";
    }

    private static string? GlobalSettings()
    {
        using var key = HKCU.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: false);
        return key?.GetValue("DirectXUserGlobalSettings") as string;
    }

    private static IEnumerable<OptimizationItem> ToolItems() =>
    [
        new()
        {
            Id = "tools.standby", Name = "Limpar memória em espera",
            Description = "Esvazia a lista de espera (standby) da RAM, como o RAMMap. Útil antes de abrir um jogo pesado.",
            Category = OptimizationCategory.Memory, Risk = RiskLevel.Safe, Impact = "Libera RAM usada como cache",
            Caution = "O Windows reconstrói o cache aos poucos; programas abertos podem demorar um instante para recarregar dados.",
            IsAction = true,
            ApplyExtra = async id =>
            {
                var before = await Task.Run(MemoryNative.StandbyMb);
                await Task.Run(MemoryNative.PurgeStandbyList);
                var after = await Task.Run(MemoryNative.StandbyMb);
                ActionLog.Write(id, "detalhe", true, $"RAM em espera: {before} MB → {after} MB");
            }
        },
        new()
        {
            Id = "tools.trim", Name = "Executar TRIM nos SSDs",
            Description = "Avisa os SSDs sobre os blocos livres (Optimize-Volume -ReTrim).",
            Category = OptimizationCategory.System, Risk = RiskLevel.Safe, Impact = "Escrita sustentada do SSD mais estável",
            IsAction = true,
            ApplyExtra = _ => ProcessRunner.RunPowerShellAsync(
                "Get-Partition | Where-Object { $_.DriveLetter } | ForEach-Object { " +
                "$p = Get-Disk -Number $_.DiskNumber | Get-PhysicalDisk; " +
                "if ($p.MediaType -eq 'SSD') { Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim } }", 600_000)
        },
    ];

    private static IEnumerable<OptimizationItem> ExtraItems() =>
    [
        new()
        {
            Id = "gpu.windowed", Name = "Otimizações para jogos em janela",
            Description = "Liga a atualização do modelo de apresentação (SwapEffectUpgrade): jogos em janela e sem borda usam o caminho de baixa latência do Windows 11.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Menos latência em jogos em janela",
            NotRecommended = OnlyWindows11,
            ApplyExtra = owner => Task.Run(() => RegistryBackupService.SetString(HKCU,
                @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", MergeGlobalSettings(GlobalSettings()), owner)),
            IsAppliedExtra = () => (GlobalSettings() ?? "").Split(';').Contains("SwapEffectUpgradeEnable=1")
        },
        new()
        {
            Id = "net.deliveryopt", Name = "Desativar Otimização de Entrega",
            Description = "O Windows deixa de enviar partes de atualizações para outros PCs pela sua conexão.",
            Category = OptimizationCategory.Network, Risk = RiskLevel.Safe, Impact = "Sem upload de atualizações em segundo plano",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)]
        },
        new()
        {
            Id = "ui.bingsearch", Name = "Busca do Windows sem resultados da web",
            Description = "A pesquisa do menu Iniciar procura só no PC, sem sugestões do Bing.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Pesquisa mais rápida e local",
            Registry = [Dw(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)]
        },
        new()
        {
            Id = "ui.widgets", Name = "Desativar Widgets e Notícias",
            Description = "Desliga o painel de Widgets e o processo que o mantém.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Menos processos em segundo plano",
            NotRecommended = OnlyWindows11,
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)]
        },
        new()
        {
            Id = "edge.background", Name = "Edge sem execução em segundo plano",
            Description = "Impede o Edge de iniciar com o Windows e de continuar aberto depois de fechado.",
            Category = OptimizationCategory.Services, Risk = RiskLevel.Safe, Impact = "Menos RAM em repouso",
            Registry =
            [
                Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0),
                Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0)
            ]
        },
        new()
        {
            Id = "sec.memoryintegrity", Name = "Desativar Integridade de Memória (HVCI)",
            Description = "Desliga o isolamento de núcleo baseado em virtualização, que custa alguns por cento de desempenho em jogos.",
            Category = OptimizationCategory.System, Risk = RiskLevel.Advanced, Impact = "Alguns por cento a mais em jogos",
            Caution = "Reduz a proteção contra drivers maliciosos. Só desligue se souber o que está fazendo. Exige reinício.",
            RequiresRestart = true,
            NotRecommended = _ => RegistryBackupService.GetDword(HKLM, DeviceGuardHvci, "Locked") == 1
                ? "Travado pelo firmware; o Windows ignora a alteração." : null,
            Registry = [Dw(HKLM, DeviceGuardHvci, "Enabled", 0)]
        },
    ];
}
