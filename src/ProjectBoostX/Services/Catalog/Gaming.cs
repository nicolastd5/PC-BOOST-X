using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string SystemProfile = @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private static IEnumerable<OptimizationItem> GamingItems() =>
    [
        new()
        {
            Id = "game.mode", Name = "Modo de Jogo do Windows",
            Description = "Liga o Modo de Jogo: o Windows prioriza o jogo e adia atualizações durante a partida.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Mais prioridade para o jogo",
            Registry = [Dw(HKCU, GameBar, "AllowAutoGameMode", 1), Dw(HKCU, GameBar, "AutoGameModeEnabled", 1)]
        },
        new()
        {
            Id = "game.bartips", Name = "Desativar painel e atalhos da Xbox Game Bar",
            Description = "Remove o painel de boas-vindas e a abertura da Game Bar pelo controle.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Menos sobreposição durante o jogo",
            Registry = [Dw(HKCU, GameBar, "ShowStartupPanel", 0), Dw(HKCU, GameBar, "UseNexusForGameBarEnabled", 0)]
        },
        new()
        {
            Id = "game.dvr", Name = "Desativar gravação em segundo plano",
            Description = "Desliga a captura automática e a gravação do que já aconteceu (Game DVR).",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Sem custo de captura",
            Caution = "Você deixa de poder salvar os últimos segundos de jogo com Win+Alt+G.",
            Registry =
            [
                Dw(HKCU, GameDvr, "AppCaptureEnabled", 0), Dw(HKCU, GameDvr, "HistoricalCaptureEnabled", 0),
                Dw(HKCU, GameConfigStore, "GameDVR_Enabled", 0)
            ]
        },
        new()
        {
            Id = "game.fso", Name = "Desativar otimizações de tela cheia (global)",
            Description = "Força tela cheia exclusiva clássica em todos os jogos.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Menos atraso de entrada em jogos antigos",
            Caution = "No Windows 11, jogos em janela sem borda podem perder VRR e HDR automático. Prefira o ajuste por jogo em Perfis.",
            Registry =
            [
                Dw(HKCU, GameConfigStore, "GameDVR_FSEBehaviorMode", 2),
                Dw(HKCU, GameConfigStore, "GameDVR_HonorUserFSEBehaviorMode", 1),
                Dw(HKCU, GameConfigStore, "GameDVR_FSEBehavior", 2),
                Dw(HKCU, GameConfigStore, "GameDVR_DXGIHonorFSEWindowsCompatible", 1),
                Dw(HKCU, GameConfigStore, "GameDVR_EFSEFeatureFlags", 0)
            ]
        },
        new()
        {
            Id = "game.gpu", Name = "Agendamento de GPU acelerado por hardware",
            Description = "Liga o HAGS. Necessário para geração de quadros do DLSS; exige driver compatível.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Menos latência entre CPU e GPU",
            RequiresRestart = true,
            Registry = [Dw(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2)]
        },
        new()
        {
            Id = "game.mmprofile", Name = "Rede e multimídia sem limitação",
            Description = "Remove o limite de pacotes de rede durante reprodução de mídia e reserva o mínimo de CPU para tarefas de fundo.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Rede sem estrangulamento com áudio/vídeo tocando",
            // SystemResponsiveness: 10 é o mínimo que o Windows aceita (0 é tratado como 10).
            Registry = [Dw(HKLM, SystemProfile, "NetworkThrottlingIndex", -1), Dw(HKLM, SystemProfile, "SystemResponsiveness", 10)]
        },
    ];
}
