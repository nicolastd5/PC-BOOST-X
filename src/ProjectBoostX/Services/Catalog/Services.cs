using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    /// <summary>Tipo de inicialização do serviço (4 = desativado), ou <c>null</c> se ele não existe nesta edição do Windows.</summary>
    private static int? ServiceStart(string name)
    {
        using var key = HKLM.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: false);
        return key?.GetValue("Start") as int?;
    }

    private static OptimizationItem Service(string id, string name, string description, RiskLevel risk, string impact,
        string caution, Func<SystemInfo, string?>? notRecommended, params string[] services) => new()
    {
        Id = id, Name = name, Description = description, Category = OptimizationCategory.Services, Risk = risk,
        Impact = impact, Caution = caution, NotRecommended = notRecommended,
        ApplyExtra = async owner =>
        {
            // Serviço ausente ou já desativado: nada a fazer.
            foreach (var service in services.Where(s => ServiceStart(s) is int start && start != 4))
                await SystemSettingsBackupService.DisableServiceAsync(service, owner);
        },
        IsAppliedExtra = () => services.All(s => ServiceStart(s) is null or 4)
    };

    private static IEnumerable<OptimizationItem> ServiceItems() =>
    [
        Service("services.diagtrack", "Serviços de telemetria",
            "Desativa 'Experiências do Usuário Conectado e Telemetria' e o roteador de mensagens WAP.",
            RiskLevel.Safe, "Menos coleta em segundo plano", "", null, "DiagTrack", "dmwappushservice"),
        Service("services.sysmain", "SysMain (Superfetch)",
            "Desativa o pré-carregamento de programas na memória.",
            RiskLevel.Moderate, "Menos leitura de disco em repouso",
            "Programas que você abre sempre podem demorar um pouco mais na primeira abertura.",
            pc => pc.DiskType == "HD" ? "Em HD o SysMain acelera bastante a abertura de programas."
                : pc.DiskType == "SSD" ? null : "Tipo de disco não identificado; mantenha o SysMain ligado.",
            "SysMain"),
        Service("services.search", "Indexação do Windows Search",
            "Desativa o indexador de arquivos.",
            RiskLevel.Moderate, "Menos CPU e disco em repouso",
            "A busca do menu Iniciar, do Explorador e do Outlook fica bem mais lenta.", null, "WSearch"),
        Service("services.fax", "Serviço de Fax", "Desativa o serviço de fax.",
            RiskLevel.Safe, "Um serviço a menos", "", null, "Fax"),
        Service("services.maps", "Gerenciador de mapas baixados", "Desativa a atualização de mapas offline.",
            RiskLevel.Safe, "Um serviço a menos", "", null, "MapsBroker"),
        Service("services.xbox", "Serviços Xbox Live",
            "Desativa autenticação, salvamento em nuvem, rede e acessórios do Xbox.",
            RiskLevel.Moderate, "Quatro serviços a menos",
            "Game Pass, app Xbox, jogos da Microsoft Store e controles Xbox sem fio dependem destes serviços.", null,
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"),
        Service("services.printer", "Spooler de impressão", "Desativa o serviço de impressão.",
            RiskLevel.Advanced, "Um processo residente a menos",
            "Nenhuma impressora funciona, nem 'Imprimir em PDF'.", null, "Spooler"),
    ];
}
