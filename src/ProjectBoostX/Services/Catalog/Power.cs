using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string SubProcessor = "sub_processor";

    private static OptimizationItem PowerValue(string id, string name, string description, RiskLevel risk, string impact,
        Func<SystemInfo, string?>? notRecommended, params (string Subgroup, string Setting, uint Value)[] values) => new()
    {
        Id = id, Name = name, Description = description, Category = OptimizationCategory.Power, Risk = risk,
        Impact = impact, NotRecommended = notRecommended,
        ApplyExtra = async owner =>
        {
            foreach (var v in values)
                await SystemSettingsBackupService.SetPowerValueAsync(v.Subgroup, v.Setting, v.Value, owner: owner);
        },
        IsAppliedExtra = () => values.All(v => PowerNative.ReadAc(v.Subgroup, v.Setting) == v.Value)
    };

    private static IEnumerable<OptimizationItem> PowerItems() =>
    [
        new()
        {
            Id = "power.high",
            Name = "Plano de energia de alto desempenho",
            Description = "Ativa o plano Alto Desempenho (ou Desempenho Máximo, se for o disponível).",
            Category = OptimizationCategory.Power, Risk = RiskLevel.Safe, Impact = "CPU não reduz a frequência sob carga",
            NotRecommended = Laptop,
            ApplyExtra = async owner =>
            {
                if (!await PowerPlanService.ActivateHighPerformanceAsync(owner))
                    throw new InvalidOperationException("Não foi possível ativar o plano de alto desempenho.");
            }
        },
        PowerValue("power.sleep", "Desativar suspensão na tomada",
            "Impede que o PC suspenda, hiberne ou apague a tela sozinho enquanto está na tomada.",
            RiskLevel.Safe, "Sem interrupções em sessões longas",
            pc => pc.IsLaptop ? "Em notebook a tela e o PC ficam ligados indefinidamente na tomada." : null,
            ("sub_sleep", "standbyidle", 0), ("sub_sleep", "hibernateidle", 0), ("sub_video", "videoidle", 0)),
        PowerValue("power.cpu.max", "CPU sempre em 100%",
            "Fixa o estado mínimo e máximo do processador em 100% no plano ativo (na tomada).",
            RiskLevel.Moderate, "Sem redução de frequência; mais consumo em repouso", Laptop,
            (SubProcessor, "PROCTHROTTLEMAX", 100), (SubProcessor, "PROCTHROTTLEMIN", 100)),
        PowerValue("power.usb.suspend", "Desativar suspensão seletiva de USB",
            "Mantém mouse, teclado e headset USB sempre energizados.",
            RiskLevel.Safe, "Sem engasgos de periférico ao voltar de pausa", null,
            ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0)),
        PowerValue("power.pcie.aspm", "Desativar economia do PCI Express (ASPM)",
            "Mantém o link da placa de vídeo e do SSD NVMe sempre ativo.",
            RiskLevel.Moderate, "Menos microtravamentos em GPU e NVMe", Laptop,
            ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0)),
        PowerValue("power.wireless.max", "Wi-Fi em desempenho máximo",
            "Impede o adaptador Wi-Fi de economizar energia.",
            RiskLevel.Safe, "Ping mais estável no Wi-Fi", null,
            ("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0)),
        // GUID em vez do apelido CPMINCORES: o apelido não existe em todas as máquinas.
        PowerValue("sys.corepark", "Desativar core parking",
            "Impede o Windows de estacionar núcleos da CPU (mínimo de núcleos ativos em 100%).",
            RiskLevel.Moderate, "Todos os núcleos disponíveis de imediato",
            pc => pc.IsLaptop ? "Em notebook aumenta consumo, temperatura e ruído."
                : pc.IsX3D ? "Processadores X3D dependem do core parking para direcionar o jogo aos núcleos certos."
                : pc.IsHybridCpu ? "Em CPU com núcleos P e E, o Windows usa o core parking para escalonar corretamente."
                : null,
            ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100)),
    ];
}
