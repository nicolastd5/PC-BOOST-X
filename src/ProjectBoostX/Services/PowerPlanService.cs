namespace BoostParaPc.Services;

public static class PowerPlanService
{
    // GUIDs padrão do Windows
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public static async Task<bool> ActivateHighPerformanceAsync()
    {
        try
        {
            var list = await ProcessRunner.RunCheckedAsync("powercfg.exe", "/list", 8_000).ConfigureAwait(false);
            var target = list.StdOut.Contains(HighPerformance, StringComparison.OrdinalIgnoreCase) ? HighPerformance
                : list.StdOut.Contains(UltimatePerformance, StringComparison.OrdinalIgnoreCase) ? UltimatePerformance : null;
            await SystemSettingsBackupService.ActivatePowerPlanAsync(target ?? UltimatePerformance, duplicate: target is null).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> ActivateBalancedAsync()
    {
        try { await SystemSettingsBackupService.ActivatePowerPlanAsync(Balanced).ConfigureAwait(false); return true; }
        catch { return false; }
    }

    public static async Task DisableSleepTimeoutsAsync()
    {
        await SystemSettingsBackupService.SetPowerValueAsync("sub_sleep", "standbyidle", 0).ConfigureAwait(false);
        await SystemSettingsBackupService.SetPowerValueAsync("sub_sleep", "hibernateidle", 0).ConfigureAwait(false);
        await SystemSettingsBackupService.SetPowerValueAsync("sub_video", "videoidle", 0).ConfigureAwait(false);
    }

    public static async Task SetGpuPreferenceAsync()
    {
        await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "PROCTHROTTLEMAX", 100).ConfigureAwait(false);
        await SystemSettingsBackupService.SetPowerValueAsync("sub_processor", "PROCTHROTTLEMAX", 100, ac: false).ConfigureAwait(false);
    }
}
