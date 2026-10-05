using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string PolicySystem = @"SOFTWARE\Policies\Microsoft\Windows\System";

    private static IEnumerable<OptimizationItem> PrivacyItems() =>
    [
        new()
        {
            Id = "telemetry.off", Name = "Reduzir telemetria",
            Description = "Define a coleta de diagnóstico no mínimo e desativa as tarefas do Programa de Aperfeiçoamento. Nas edições Home e Pro o mínimo é 'obrigatório', não zero.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Menos atividade de fundo e de rede",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)],
            ApplyExtra = owner => SystemSettingsBackupService.DisableTelemetryTasksAsync(owner)
        },
        new()
        {
            Id = "telemetry.tips", Name = "Desativar dicas, sugestões e apps instalados sozinhos",
            Description = "Corta sugestões no Iniciar e nas Configurações e a instalação silenciosa de apps promovidos.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Sem apps e anúncios surgindo sozinhos",
            Registry =
            [
                Dw(HKCU, Cdm, "SubscribedContent-338389Enabled", 0), Dw(HKCU, Cdm, "SubscribedContent-338388Enabled", 0),
                Dw(HKCU, Cdm, "SystemPaneSuggestionsEnabled", 0), Dw(HKCU, Cdm, "SilentInstalledAppsEnabled", 0)
            ]
        },
        new()
        {
            Id = "telemetry.activity", Name = "Desativar histórico de atividades",
            Description = "O Windows deixa de registrar e enviar o histórico de apps e arquivos abertos.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Menos gravação em disco",
            Registry =
            [
                Dw(HKLM, PolicySystem, "EnableActivityFeed", 0), Dw(HKLM, PolicySystem, "PublishUserActivities", 0),
                Dw(HKLM, PolicySystem, "UploadUserActivities", 0)
            ]
        },
        new()
        {
            Id = "telemetry.wer", Name = "Desativar Relatório de Erros do Windows",
            Description = "Não coleta nem envia relatórios quando um programa trava.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Sem disco e rede usados após um travamento",
            Caution = "Dificulta o diagnóstico se você costuma enviar relatórios de falha a um suporte.",
            Registry =
            [
                Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1),
                Dw(HKCU, @"Software\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)
            ]
        },
        new()
        {
            Id = "telemetry.backgroundapps", Name = "Bloquear apps da Store em segundo plano",
            Description = "Apps da Microsoft Store deixam de rodar quando estão fechados.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Moderate, Impact = "Menos RAM e CPU em repouso",
            Caution = "Apps da Store param de notificar e sincronizar fechados (Email, Calendário, WhatsApp da Store).",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2)]
        },
        new()
        {
            Id = "telemetry.location", Name = "Desativar localização",
            Description = "Desliga o serviço de localização do Windows para todos os apps.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Moderate, Impact = "Sem varredura de Wi-Fi/GPS",
            Caution = "Clima, mapas, fuso horário automático e 'Localizar meu dispositivo' deixam de funcionar.",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1)]
        },
    ];
}
