using System.IO;
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Catálogo de ajustes; mudanças persistentes exigem backup e ações pontuais não são reversíveis.
/// </summary>
public static class OptimizationCatalog
{
    public static IReadOnlyList<OptimizationItem> CreateAll() =>
    [
        // ── Energia ──────────────────────────────────────────
        new()
        {
            Id = "power.high",
            Name = "Plano de energia de alto desempenho",
            Description = "Ativa Alto Desempenho (ou Desempenho Máximo) para evitar throttle da CPU.",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Safe,
            Impact = "CPU estável em carga"
        },
        new()
        {
            Id = "power.sleep",
            Name = "Desativar suspensão (AC)",
            Description = "Impede suspensão/hibernação enquanto o PC está na tomada.",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Safe,
            Impact = "Menos interrupções"
        },
        new()
        {
            Id = "power.cpu.max",
            Name = "CPU a 100% no plano ativo",
            Description = "Força processador mínimo/máximo em 100% (AC) no plano atual.",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Safe,
            Impact = "Sem downclock agressivo"
        },
        new()
        {
            Id = "power.usb.suspend",
            Name = "Desativar suspensão USB seletiva",
            Description = "Mantém mouse/teclado USB sempre ativos (reduz input lag esporádico).",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Safe,
            Impact = "Input mais consistente"
        },
        new()
        {
            Id = "power.pcie.aspm",
            Name = "Desativar ASPM do PCI Express",
            Description = "Remove Link State Power Management que pode engasgar a GPU.",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Moderate,
            Impact = "GPU mais estável"
        },
        new()
        {
            Id = "power.wireless.max",
            Name = "Wi-Fi em máximo desempenho",
            Description = "Impede o adaptador de economizar energia (menos lag online).",
            Category = OptimizationCategory.Power,
            Risk = RiskLevel.Safe,
            Impact = "Ping mais estável"
        },

        // ── Jogos ────────────────────────────────────────────
        new()
        {
            Id = "game.mode",
            Name = "Game Mode do Windows",
            Description = "Ativa Game Mode e reduz overlays do Game Bar.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Safe,
            Impact = "Mais prioridade pro jogo"
        },
        new()
        {
            Id = "game.fso",
            Name = "Desativar otimizações de tela cheia",
            Description = "Tela cheia clássica (FSO off) — menos input lag em muitos jogos.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Moderate,
            Impact = "Input lag menor"
        },
        new()
        {
            Id = "game.dvr",
            Name = "Desativar Game DVR / gravação",
            Description = "Desliga gravação em segundo plano do Xbox Game Bar.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Safe,
            Impact = "Menos overhead de captura"
        },
        new()
        {
            Id = "game.gpu",
            Name = "Agendamento de GPU por hardware",
            Description = "Ativa HAGS quando suportado pelo driver.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Moderate,
            Impact = "Pode reduzir latência CPU→GPU"
        },
        new()
        {
            Id = "game.mmprofile",
            Name = "Perfil multimedia Games",
            Description = "Scheduling High + desativa Network Throttling no MMCSS.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Moderate,
            Impact = "Rede e scheduling agressivos"
        },
        new()
        {
            Id = "game.notifications",
            Name = "Desativar notificações do Windows",
            Description = "Desativa notificações globalmente até você reverter o ajuste.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Safe,
            Impact = "Notificações desativadas"
        },
        new()
        {
            Id = "game.bartips",
            Name = "Desativar dicas do Game Bar",
            Description = "Remove painel de dicas e gravação sugerida do Xbox Game Bar.",
            Category = OptimizationCategory.Gaming,
            Risk = RiskLevel.Safe,
            Impact = "Overlay mais leve"
        },

        // ── Mouse / Input ────────────────────────────────────
        new()
        {
            Id = "input.mouseaccel",
            Name = "Desativar aceleração do mouse",
            Description = "Desliga 'Aumentar precisão do ponteiro' para aim 1:1.",
            Category = OptimizationCategory.Input,
            Risk = RiskLevel.Safe,
            Impact = "Muscle memory consistente"
        },
        new()
        {
            Id = "input.mousespeed",
            Name = "Mouse 6/11 no registro",
            Description = "Força velocidade 6/11 (padrão 1:1 sem escala do Windows).",
            Category = OptimizationCategory.Input,
            Risk = RiskLevel.Safe,
            Impact = "Sensibilidade raw"
        },
        new()
        {
            Id = "input.keyboard",
            Name = "Teclado sem delay inicial",
            Description = "Repeat Delay=0 e Repeat Rate alto — taps mais responsivos.",
            Category = OptimizationCategory.Input,
            Risk = RiskLevel.Safe,
            Impact = "A/D strafe mais nítido"
        },
        new()
        {
            Id = "input.menudelay",
            Name = "Delay de menus em 0",
            Description = "MenuShowDelay=0 abre menus instantaneamente.",
            Category = OptimizationCategory.Input,
            Risk = RiskLevel.Safe,
            Impact = "UI mais ágil"
        },

        // ── Rede ─────────────────────────────────────────────
        new()
        {
            Id = "net.tcp",
            Name = "TCP autotuning normal",
            Description = "Garante autotuninglevel=normal (estável para jogos).",
            Category = OptimizationCategory.Network,
            Risk = RiskLevel.Safe,
            Impact = "Rede estável"
        },
        new()
        {
            Id = "net.nagle",
            Name = "Desativar Nagle (TcpAckFrequency)",
            Description = "Reduz atraso de envio de pacotes pequenos em alguns jogos.",
            Category = OptimizationCategory.Network,
            Risk = RiskLevel.Moderate,
            Impact = "Latência de rede menor"
        },
        new()
        {
            Id = "net.dns.flush",
            Name = "Limpar cache DNS",
            Description = "Flush DNS — útil após trocar rede/VPN.",
            Category = OptimizationCategory.Network,
            Risk = RiskLevel.Safe,
            Impact = "Resolução limpa"
        },
        new()
        {
            Id = "net.throttle.off",
            Name = "Network Throttling desativado",
            Description = "NetworkThrottlingIndex=0xFFFFFFFF globalmente.",
            Category = OptimizationCategory.Network,
            Risk = RiskLevel.Moderate,
            Impact = "Menos throttle de pacotes"
        },

        // ── Visual ───────────────────────────────────────────
        new()
        {
            Id = "visual.effects",
            Name = "Efeitos visuais para desempenho",
            Description = "Reduz animações (mantém sombras de fonte).",
            Category = OptimizationCategory.Visual,
            Risk = RiskLevel.Safe,
            Impact = "UI mais leve"
        },
        new()
        {
            Id = "visual.transparency",
            Name = "Desativar transparência",
            Description = "Desliga acrylic/transparência do Windows 10/11.",
            Category = OptimizationCategory.Visual,
            Risk = RiskLevel.Safe,
            Impact = "Menos composição GPU"
        },
        new()
        {
            Id = "visual.animations",
            Name = "Desativar animações de janela",
            Description = "MinAnimate=0 e efeitos de maximizar/minimizar off.",
            Category = OptimizationCategory.Visual,
            Risk = RiskLevel.Safe,
            Impact = "Janelas instantâneas"
        },

        // ── Telemetria / Background ──────────────────────────
        new()
        {
            Id = "telemetry.off",
            Name = "Reduzir telemetria",
            Description = "AllowTelemetry=0 e tasks CEIP desativadas.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Safe,
            Impact = "Menos background/rede"
        },
        new()
        {
            Id = "telemetry.tips",
            Name = "Desativar dicas e sugestões",
            Description = "Corta sugestões do Windows, conteúdo e Get tips.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Safe,
            Impact = "Menos notificações"
        },
        new()
        {
            Id = "telemetry.activity",
            Name = "Desativar histórico de atividades",
            Description = "Desliga coleta de timeline / Activity History.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Safe,
            Impact = "Menos gravação em disco"
        },
        new()
        {
            Id = "telemetry.wer",
            Name = "Desativar Windows Error Reporting",
            Description = "Corta WER e upload de crash dumps.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Safe,
            Impact = "Menos I/O em crash"
        },
        new()
        {
            Id = "telemetry.backgroundapps",
            Name = "Restringir apps em background",
            Description = "Desativa execução em background de apps da Store.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Moderate,
            Impact = "Menos RAM/CPU ocioso"
        },
        new()
        {
            Id = "telemetry.cortana",
            Name = "Desativar Cortana",
            Description = "Desliga Cortana via política (não afeta busca básica).",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Safe,
            Impact = "Menos processo residente"
        },
        new()
        {
            Id = "telemetry.location",
            Name = "Desativar localização",
            Description = "Corta serviço de localização do Windows.",
            Category = OptimizationCategory.Telemetry,
            Risk = RiskLevel.Moderate,
            Impact = "Menos GPS/WiFi scan"
        },

        // ── Serviços ─────────────────────────────────────────
        new()
        {
            Id = "services.diagtrack",
            Name = "Serviço DiagTrack",
            Description = "Desativa Connected User Experiences and Telemetry.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Safe,
            Impact = "Menos coleta"
        },
        new()
        {
            Id = "services.sysmain",
            Name = "SysMain (Superfetch)",
            Description = "Em SSD, pré-fetch gera I/O extra — desativa.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Moderate,
            Impact = "Menos I/O em SSD"
        },
        new()
        {
            Id = "services.search",
            Name = "Indexação Windows Search",
            Description = "Desativa indexer se você não busca arquivos sempre.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Moderate,
            Impact = "Menos CPU/disco idle"
        },
        new()
        {
            Id = "services.fax",
            Name = "Serviço Fax",
            Description = "Desativa Fax (desnecessário em 99% dos PCs).",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Safe,
            Impact = "Serviço a menos"
        },
        new()
        {
            Id = "services.maps",
            Name = "Downloaded Maps Manager",
            Description = "Desativa mapas offline.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Safe,
            Impact = "Serviço a menos"
        },
        new()
        {
            Id = "services.remoteregistry",
            Name = "Remote Registry",
            Description = "Desativa edição remota de registro (segurança + recursos).",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Safe,
            Impact = "Superfície de ataque menor"
        },
        new()
        {
            Id = "services.xbox",
            Name = "Serviços Xbox Live",
            Description = "Desativa Xbox Live Auth/GameSave/Networking se não usa.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Moderate,
            Impact = "4 serviços a menos"
        },
        new()
        {
            Id = "services.printer",
            Name = "Print Spooler",
            Description = "Desativa spooler se você não imprime nesta máquina.",
            Category = OptimizationCategory.Services,
            Risk = RiskLevel.Advanced,
            Impact = "Menos processo idle"
        },

        // ── Memória / Disco ──────────────────────────────────
        new()
        {
            Id = "memory.large",
            Name = "Large System Cache desativado",
            Description = "Prioriza aplicativos em vez do cache do sistema.",
            Category = OptimizationCategory.Memory,
            Risk = RiskLevel.Safe,
            Impact = "Mais RAM pro jogo"
        },
        new()
        {
            Id = "memory.ntfs",
            Name = "NTFS sem last access + sem 8.3",
            Description = "Desativa NtfsDisableLastAccessUpdate e nomes 8.3.",
            Category = OptimizationCategory.Memory,
            Risk = RiskLevel.Safe,
            Impact = "Menos escrita em metadata"
        },
        new()
        {
            Id = "memory.standby",
            Name = "Limpar lista standby (soft)",
            Description = "Força working set de processos grandes a liberar RAM.",
            Category = OptimizationCategory.Memory,
            Risk = RiskLevel.Moderate,
            Impact = "RAM liberada agora"
        },
        new()
        {
            Id = "memory.pagedpool",
            Name = "Paged/NonPaged pool sane",
            Description = "Mantém valores padrão otimizados de session manager.",
            Category = OptimizationCategory.Memory,
            Risk = RiskLevel.Safe,
            Impact = "Estabilidade de pool"
        },

        // ── Sistema ──────────────────────────────────────────
        new()
        {
            Id = "sys.startupdelay",
            Name = "Remover delay de inicialização",
            Description = "Zera StartupDelayInMSec.",
            Category = OptimizationCategory.System,
            Risk = RiskLevel.Safe,
            Impact = "Boot aparente mais rápido"
        },
        new()
        {
            Id = "sys.hibernation",
            Name = "Desativar hibernação",
            Description = "powercfg /hibernate off — libera hiberfil.sys.",
            Category = OptimizationCategory.System,
            Risk = RiskLevel.Moderate,
            Impact = "Libera GBs em disco"
        },
        new()
        {
            Id = "sys.corepark",
            Name = "Desativar Core Parking",
            Description = "Impede o Windows de 'dormir' núcleos da CPU.",
            Category = OptimizationCategory.System,
            Risk = RiskLevel.Moderate,
            Impact = "Todos núcleos ativos"
        },
        new()
        {
            Id = "sys.priority.sep",
            Name = "Priority Separation = 38",
            Description = "Ajusta quantum/priority separation para foreground apps.",
            Category = OptimizationCategory.System,
            Risk = RiskLevel.Moderate,
            Impact = "Jogo ganha mais quantum"
        },
        new()
        {
            Id = "sys.flushdns",
            Name = "Limpar memória standby + DNS",
            Description = "Ação pontual: flush DNS e working sets grandes.",
            Category = OptimizationCategory.System,
            Risk = RiskLevel.Safe,
            Impact = "Alívio imediato"
        },
    ];

    public static async Task<ApplyState> ApplyAsync(OptimizationItem item)
    {
        item.StatusMessage = null;
        try
        {
            switch (item.Id)
            {
                case "power.high":
                    return await PowerPlanService.ActivateHighPerformanceAsync() ? ApplyState.Applied : ApplyState.Failed;

                case "power.sleep":
                    await PowerPlanService.DisableSleepTimeoutsAsync();
                    return ApplyState.Applied;

                case "power.cpu.max":
                    await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "PROCTHROTTLEMAX", 100);
                    await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "PROCTHROTTLEMAX", 100, ac: false);
                    await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "PROCTHROTTLEMIN", 100);
                    return ApplyState.Applied;

                case "power.usb.suspend":
                    await SystemSettingsBackupService.SetPowerValueAsync("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0);
                    return ApplyState.Applied;

                case "power.pcie.aspm":
                    await SystemSettingsBackupService.SetPowerValueAsync("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0);
                    return ApplyState.Applied;

                case "power.wireless.max":
                    await SystemSettingsBackupService.SetPowerValueAsync("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0);
                    return ApplyState.Applied;

                case "game.mode":
                    BackupGameKeys();
                    await GameOptimizationService.ApplyGameModeAsync();
                    return ApplyState.Applied;

                case "game.fso":
                    BackupGameKeys();
                    await GameOptimizationService.ApplyFullscreenOptimizationsAsync();
                    return ApplyState.Applied;

                case "game.dvr":
                    BackupGameKeys();
                    await GameOptimizationService.DisableGameDvrBroadcastAsync();
                    return ApplyState.Applied;

                case "game.gpu":
                    BackupGameKeys();
                    await GameOptimizationService.ApplyGpuSchedulingAsync();
                    return ApplyState.Applied;

                case "game.mmprofile":
                    BackupGameKeys();
                    await GameOptimizationService.ApplyMultimediaGameProfileAsync();
                    return ApplyState.Applied;

                case "game.notifications":
                    BackupExtra("gamenotif",
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled"));
                    RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0);
                    return ApplyState.Applied;

                case "game.bartips":
                    BackupExtra("bartips",
                        (Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel"),
                        (Registry.CurrentUser, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled"),
                        (Registry.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode"));
                    RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0);
                    RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0);
                    return ApplyState.Applied;

                case "input.mouseaccel":
                    BackupExtra("mouse",
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity"));
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0");
                    await ProcessRunner.RunCheckedAsync("rundll32.exe", "user32.dll,UpdatePerUserSystemParameters");
                    return ApplyState.Applied;

                case "input.mousespeed":
                    BackupExtra("mouse611",
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1"),
                        (Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2"));
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity", "10");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0");
                    return ApplyState.Applied;

                case "input.keyboard":
                    BackupExtra("keyboard",
                        (Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay"),
                        (Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed"));
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed", "31");
                    return ApplyState.Applied;

                case "input.menudelay":
                    BackupExtra("menudelay",
                        (Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay"));
                    RegistryBackupService.SetString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", "0");
                    return ApplyState.Applied;

                case "net.tcp":
                    await SystemSettingsBackupService.SetTcpAutoTuningAsync("normal");
                    return ApplyState.Applied;

                case "net.nagle":
                    await ApplyNagleOffAsync();
                    return ApplyState.Applied;

                case "net.dns.flush":
                    await ProcessRunner.RunCheckedAsync("ipconfig", "/flushdns");
                    return ApplyState.Applied;

                case "net.throttle.off":
                    BackupExtra("throttle",
                        (Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
                        (Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                        "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                        "SystemResponsiveness", 0);
                    return ApplyState.Applied;

                case "visual.effects":
                    BackupVisualKeys();
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 0);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Control Panel\Desktop", "DragFullWindows", 0);
                    RegistryBackupService.SetString(Registry.CurrentUser,
                        @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
                    return ApplyState.Applied;

                case "visual.transparency":
                    BackupVisualKeys();
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
                    return ApplyState.Applied;

                case "visual.animations":
                    BackupExtra("anim",
                        (Registry.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"),
                        (Registry.CurrentUser, @"Control Panel\Desktop", "UserPreferencesMask"),
                        (Registry.CurrentUser, @"Control Panel\Desktop", "DragFullWindows"));
                    RegistryBackupService.SetString(Registry.CurrentUser,
                        @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0");
                    RegistryBackupService.SetString(Registry.CurrentUser,
                        @"Control Panel\Desktop", "DragFullWindows", "0");
                    return ApplyState.Applied;

                case "telemetry.off":
                    BackupTelemetry();
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
                    await SystemSettingsBackupService.DisableTelemetryTasksAsync();
                    return ApplyState.Applied;

                case "telemetry.tips":
                    BackupExtra("tips",
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled"),
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled"),
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled"));
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0);
                    return ApplyState.Applied;

                case "telemetry.activity":
                    BackupExtra("activity",
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed"),
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities"),
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities"));
                    RegistryBackupService.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0);
                    RegistryBackupService.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0);
                    RegistryBackupService.SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0);
                    return ApplyState.Applied;

                case "telemetry.wer":
                    BackupExtra("wer",
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled"),
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\Windows Error Reporting", "Disabled"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
                    return ApplyState.Applied;

                case "telemetry.backgroundapps":
                    BackupExtra("bgapps",
                        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"));
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
                    return ApplyState.Applied;

                case "telemetry.cortana":
                    BackupExtra("cortana",
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0);
                    return ApplyState.Applied;

                case "telemetry.location":
                    BackupExtra("location",
                        (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation"),
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration", "Status"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1);
                    return ApplyState.Applied;

                case "services.diagtrack":
                    await SystemSettingsBackupService.DisableServiceAsync("DiagTrack");
                    return ApplyState.Applied;

                case "services.sysmain":
                    if (!IsSsd()) return ApplyState.Unsupported;
                    await SystemSettingsBackupService.DisableServiceAsync("SysMain");
                    return ApplyState.Applied;

                case "services.search":
                    await SystemSettingsBackupService.DisableServiceAsync("WSearch");
                    return ApplyState.Applied;

                case "services.fax":
                    await SystemSettingsBackupService.DisableServiceAsync("Fax");
                    return ApplyState.Applied;

                case "services.maps":
                    await SystemSettingsBackupService.DisableServiceAsync("MapsBroker");
                    return ApplyState.Applied;

                case "services.remoteregistry":
                    await SystemSettingsBackupService.DisableServiceAsync("RemoteRegistry");
                    return ApplyState.Applied;

                case "services.xbox":
                    foreach (var s in new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" })
                    {
                        await SystemSettingsBackupService.DisableServiceAsync(s);
                    }
                    return ApplyState.Applied;

                case "services.printer":
                    await SystemSettingsBackupService.DisableServiceAsync("Spooler");
                    return ApplyState.Applied;

                case "memory.large":
                    BackupMemoryKeys();
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache", 0);
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1);
                    return ApplyState.Applied;

                case "memory.ntfs":
                    BackupExtra("ntfs",
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate"),
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1);
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1);
                    return ApplyState.Applied;

                case "memory.standby":
                case "sys.flushdns":
                    await ProcessRunner.RunCheckedAsync("ipconfig", "/flushdns");
                    // EmptyWorkingSet em processos grandes (soft reclaim)
                    await ProcessRunner.RunPowerShellAsync("""
                        $sig = '[DllImport("psapi.dll")] public static extern int EmptyWorkingSet(IntPtr hwSnap);'
                        $type = Add-Type -MemberDefinition $sig -Name 'PSAPI' -Namespace 'Win32' -PassThru
                        Get-Process | Where-Object { $_.WorkingSet64 -gt 200MB } | ForEach-Object {
                          try { [void]$type::EmptyWorkingSet($_.Handle) } catch { }
                        }
                        """, timeoutMs: 20_000);
                    return ApplyState.Applied;

                case "memory.pagedpool":
                    BackupExtra("pool",
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "NonPagedPoolSize"),
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "PagedPoolSize"),
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "SystemPages"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "NonPagedPoolSize", 0);
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "PagedPoolSize", 0);
                    return ApplyState.Applied;

                case "sys.startupdelay":
                    RegistryBackupService.SetDword(Registry.CurrentUser,
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
                    return ApplyState.Applied;

                case "sys.hibernation":
                    await SystemSettingsBackupService.SetHibernationAsync(false);
                    return ApplyState.Applied;

                case "sys.corepark":
                    await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "CPMINCORES", 100);
                    return ApplyState.Applied;

                case "sys.priority.sep":
                    BackupExtra("prio",
                        (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation"));
                    RegistryBackupService.SetDword(Registry.LocalMachine,
                        @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38);
                    return ApplyState.Applied;

                default:
                    return ApplyState.Unsupported;
            }
        }
        catch (Exception ex)
        {
            item.StatusMessage = ex.Message;
            return ApplyState.Failed;
        }
    }

    private static async Task ApplyNagleOffAsync()
    {
        // Aplica TcpAckFrequency=1 e TcpNoDelay=1 nas interfaces
        var changed = await Task.Run(() =>
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", writable: false)
                ?? throw new InvalidOperationException("As interfaces de rede não puderam ser acessadas.");

            var count = 0;
            var failures = 0;
            foreach (var name in interfaces.GetSubKeyNames())
            {
                try
                {
                    using var iface = interfaces.OpenSubKey(name, writable: true);
                    if (iface is null) continue;
                    if (iface.GetValue("DhcpIPAddress") is null && iface.GetValue("IPAddress") is null) continue;

                    BackupExtra("nagle_" + name,
                        (Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{name}", "TcpAckFrequency"),
                        (Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{name}", "TcpNoDelay"));

                    iface.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    iface.SetValue("TcpNoDelay", 1, RegistryValueKind.DWord);
                    count++;
                }
                catch (UnauthorizedAccessException) { failures++; }
                catch (System.Security.SecurityException) { failures++; }
                catch (IOException) { failures++; }
            }
            return (count, failures);
        });
        if (changed.count == 0)
            throw new InvalidOperationException("Nenhuma interface de rede ativa foi encontrada para aplicar o ajuste.");
        if (changed.failures > 0)
            throw new IOException($"Não foi possível alterar {changed.failures} interface(s) de rede.");
    }

    public static async Task<bool> RevertAsync(string backupFile)
        => await Task.Run(() => RegistryBackupService.RestoreBackup(backupFile) > 0);

    private static void BackupExtra(string id, params (RegistryKey root, string keyPath, string valueName)[] entries)
        => RegistryBackupService.CreateBackup(id, entries);

    private static void BackupGameKeys()
    {
        BackupExtra("game",
            (Registry.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode"),
            (Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
            (Registry.CurrentUser, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled"),
            (Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel"),
            (Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode"),
            (Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode"),
            (Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehavior"),
            (Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible"),
            (Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
            (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
            (Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"));
    }

    private static void BackupVisualKeys()
    {
        BackupExtra("visual",
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect"),
            (Registry.CurrentUser, @"Control Panel\Desktop", "DragFullWindows"),
            (Registry.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency"));
    }

    private static void BackupTelemetry()
    {
        BackupExtra("telemetry",
            (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"));
    }

    private static void BackupMemoryKeys()
    {
        BackupExtra("memory",
            (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache"),
            (Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"));
    }

    private static bool IsSsd()
    {
        try
        {
            var result = ProcessRunner.RunPowerShellAsync(
                "Get-PhysicalDisk | Select-Object -First 1 -ExpandProperty MediaType",
                timeoutMs: 8_000).GetAwaiter().GetResult();
            return result.Contains("SSD", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Se o tipo do disco não puder ser confirmado, não desative o
            // SysMain por suposição: em HD ele pode ser útil.
            return false;
        }
    }
}
