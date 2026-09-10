using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Lê o estado real do sistema para marcar otimizações já aplicadas.
/// </summary>
public static class OptimizationStateDetector
{
    public static bool IsLikelyApplied(string id)
    {
        try
        {
            return id switch
            {
                "power.high" => IsHighPerformancePlan(),
                "power.sleep" => true, // difícil de ler de forma confiável; confia no store
                "game.mode" => GetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") == 1,
                "game.fso" => GetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehavior") == 2,
                "game.dvr" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") == 0
                              || GetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") == 0,
                "game.gpu" => GetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") == 2,
                "game.mmprofile" => GetDword(Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex") == unchecked((int)0xFFFFFFFF),
                "game.notifications" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled") == 0,
                "game.bartips" => GetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "ShowStartupPanel") == 0,
                "input.mouseaccel" => GetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
                "input.mousespeed" => GetString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity") == "10",
                "input.keyboard" => GetString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay") == "0",
                "input.menudelay" => GetString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay") == "0",
                "visual.effects" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == 3,
                "visual.transparency" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") == 0,
                "telemetry.off" => GetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == 0,
                "telemetry.tips" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled") == 0,
                "telemetry.activity" => GetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed") == 0,
                "telemetry.wer" => GetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled") == 1,
                "telemetry.backgroundapps" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == 1,
                "telemetry.cortana" => GetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana") == 0,
                "memory.large" => GetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "LargeSystemCache") == 0,
                "memory.ntfs" => GetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate") == 1,
                "sys.startupdelay" => GetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec") == 0,
                "sys.priority.sep" => GetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation") == 38,
                "net.throttle.off" => GetDword(Registry.LocalMachine, @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex") == unchecked((int)0xFFFFFFFF),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Aplica o estado salvo + detecção do sistema em uma lista de itens.</summary>
    public static void ApplyTo(IEnumerable<Models.OptimizationItem> items)
    {
        var saved = OptimizationStateStore.Load();
        foreach (var item in items)
        {
            if (saved.Contains(item.Id) || IsLikelyApplied(item.Id))
            {
                if (item.State == Models.ApplyState.NotApplied)
                    item.State = Models.ApplyState.Applied;
            }
        }
    }

    private static bool IsHighPerformancePlan()
    {
        try
        {
            var result = ProcessRunner.RunAsync("powercfg", "/getactivescheme", timeoutMs: 5_000)
                .GetAwaiter().GetResult();
            var stdout = result.StdOut.ToLowerInvariant();
            return stdout.Contains("8c5e7fda") // High Performance
                   || stdout.Contains("e9a42b02") // Ultimate
                   || stdout.Contains("alto desempenho")
                   || stdout.Contains("high performance")
                   || stdout.Contains("máximo desempenho")
                   || stdout.Contains("ultimate");
        }
        catch
        {
            return false;
        }
    }

    private static int? GetDword(RegistryKey root, string key, string name)
        => RegistryBackupService.GetDword(root, key, name);

    private static string? GetString(RegistryKey root, string key, string name)
    {
        try
        {
            using var k = root.OpenSubKey(key, writable: false);
            return k?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
