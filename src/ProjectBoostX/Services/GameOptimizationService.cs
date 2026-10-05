using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Otimizações de jogo: Game Mode, fullscreen otimizado, prioridade de processos,
/// agendamento de GPU, telemetria de jogos.
/// </summary>
public static class GameOptimizationService
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string MultimediaProfileKey = @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GameConfigStoreKey = @"System\GameConfigStore";

    public static async Task ApplyGameModeAsync()
    {
        // Game Mode ON
        RegistryBackupService.SetDword(Registry.CurrentUser, GameBarKey, "AllowAutoGameMode", 1);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled", 1);

        // Desativa Game Bar overlay (reduz overhead; não é anti-cheat)
        RegistryBackupService.SetDword(Registry.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled", 0);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameBarKey, "ShowStartupPanel", 0);

        await Task.CompletedTask;
    }

    public static async Task ApplyFullscreenOptimizationsAsync()
    {
        // Desativa FSO globalmente (tela cheia clássica = menos input lag em muitos jogos)
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", 2);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", 1);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_FSEBehavior", 2);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_MonitorMatchTimeInSeconds", 0);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_EFSEFeatureFlags", 0);

        // Desativa Game DVR (gravação em segundo plano)
        RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
        RegistryBackupService.SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);

        await Task.CompletedTask;
    }

    public static async Task ApplyGpuSchedulingAsync()
    {
        // Hardware-accelerated GPU scheduling (HAGS) — pode ajudar em GPUs modernas
        RegistryBackupService.SetDword(Registry.LocalMachine, GraphicsDriversKey, "HwSchMode", 2);
        await Task.CompletedTask;
    }

    public static async Task ApplyMultimediaGameProfileAsync()
    {
        // SystemProfile: NetworkingCategory / Responsiveness para jogos
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "Affinity", 0);
        RegistryBackupService.SetString(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "Background Only", "False");
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "Clock Rate", 10000);
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "GPU Priority", 8);
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "Priority", 6);
        RegistryBackupService.SetString(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "Scheduling Category", "High");
        RegistryBackupService.SetString(Registry.LocalMachine, MultimediaProfileKey + @"\Tasks\Games", "SFIO Priority", "High");

        // NetworkThrottlingIndex desativado (0xFFFFFFFF) para latência
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
        RegistryBackupService.SetDword(Registry.LocalMachine, MultimediaProfileKey, "SystemResponsiveness", 0);

        await Task.CompletedTask;
    }

    public static async Task DisableGameDvrBroadcastAsync()
    {
        RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0);
        RegistryBackupService.SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
        RegistryBackupService.SetDword(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", 0);
        await Task.CompletedTask;
    }

    public static async Task ApplyAllGamingOptimizationsAsync()
    {
        var targets = new (RegistryKey root, string key, string value)[]
        {
            (Registry.CurrentUser, GameBarKey, "AllowAutoGameMode"),
            (Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled"),
            (Registry.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled"),
            (Registry.CurrentUser, GameBarKey, "ShowStartupPanel"),
            (Registry.CurrentUser, GameConfigStoreKey, "GameDVR_FSEBehaviorMode"),
            (Registry.CurrentUser, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode"),
            (Registry.CurrentUser, GameConfigStoreKey, "GameDVR_FSEBehavior"),
            (Registry.CurrentUser, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible"),
            (Registry.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
            (Registry.LocalMachine, GraphicsDriversKey, "HwSchMode"),
            (Registry.LocalMachine, MultimediaProfileKey, "NetworkThrottlingIndex"),
            (Registry.LocalMachine, MultimediaProfileKey, "SystemResponsiveness"),
        };

        RegistryBackupService.CreateBackup("gaming", targets);

        await ApplyGameModeAsync();
        await ApplyFullscreenOptimizationsAsync();
        await ApplyGpuSchedulingAsync();
        await ApplyMultimediaGameProfileAsync();
        await DisableGameDvrBroadcastAsync();
    }

    public static string DescribeCurrentGameMode()
    {
        var enabled = RegistryBackupService.GetDword(Registry.CurrentUser, GameBarKey, "AutoGameModeEnabled");
        return enabled == 1 ? "Ativado" : "Desativado / padrão";
    }
}
