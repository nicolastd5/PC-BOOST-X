// O motor e o catálogo reais são compilados neste projeto. Só o que toca o Windows
// (pastas do app, comandos, energia e serviços) é substituído por estado em memória.
using Microsoft.Win32;

namespace BoostParaPc.Services;

public static class AppPaths
{
    public static string DataDir { get; set; } = "";
    public static string BackupDir
    {
        get { var dir = Path.Combine(DataDir, "backups"); Directory.CreateDirectory(dir); return dir; }
    }
    public static string AppliedFile => Path.Combine(DataDir, "applied.json");
}

public static class ProcessRunner
{
    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
    public static List<string> Calls { get; } = [];

    public static Task<CommandResult> RunCheckedAsync(string fileName, string arguments, int timeoutMs = 30_000,
        bool elevated = false, CancellationToken cancellationToken = default)
    {
        Calls.Add(fileName + " " + arguments);
        return Task.FromResult(new CommandResult(0, "", "", false));
    }

    public static Task<string> RunPowerShellAsync(string script, int timeoutMs = 30_000, CancellationToken cancellationToken = default)
    {
        Calls.Add("powershell " + script);
        return Task.FromResult("");
    }
}

public static class MemoryNative
{
    public static long StandbyMb() => 1024;
    public static void PurgeStandbyList() => ProcessRunner.Calls.Add("purge-standby");
}

public static class PowerNative
{
    public static Dictionary<string, uint> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static uint? ReadAc(string subgroup, string setting, string? plan = null) =>
        Values.TryGetValue(subgroup + " " + setting, out var value) ? value : null;
}

/// <summary>Driver NVIDIA em memória; <c>null</c> = PC sem driver NVIDIA.</summary>
public static class NvidiaNative
{
    public const uint PreferMaxPerformance = 1;
    public static (uint Value, bool UserSet)? Mode { get; set; } = (5, false);
    public static bool IsAvailable => Mode is not null;
    public static (uint Value, bool UserSet)? GetPowerMode() => Mode;
}

public static class PowerPlanService
{
    public static Task<bool> ActivateHighPerformanceAsync(string? owner = null) => Task.FromResult(true);
}

/// <summary>Journal em memória: cada alteração guarda como desfazê-la e quem é o dono.</summary>
public static class SystemSettingsBackupService
{
    private static readonly List<(string? Owner, Action Undo)> Journal = [];

    public static void Reset()
    {
        Journal.Clear();
        PowerNative.Values.Clear();
        ProcessRunner.Calls.Clear();
        NvidiaNative.Mode = (5, false);
    }

    public static Task SetNvidiaPowerModeAsync(uint mode, string? owner = null)
    {
        var previous = NvidiaNative.Mode ?? throw new InvalidOperationException("Nenhum driver NVIDIA foi encontrado neste PC.");
        Journal.Add((owner, () => NvidiaNative.Mode = previous));
        NvidiaNative.Mode = (mode, true);
        return Task.CompletedTask;
    }

    public static Task SetPowerValueAsync(string subgroup, string setting, uint value, bool ac = true, string? owner = null)
    {
        var key = subgroup + " " + setting;
        var existed = PowerNative.Values.TryGetValue(key, out var previous);
        Journal.Add((owner, () => { if (existed) PowerNative.Values[key] = previous; else PowerNative.Values.Remove(key); }));
        PowerNative.Values[key] = value;
        return Task.CompletedTask;
    }

    public static Task DisableServiceAsync(string name, string? owner = null) =>
        SetDword($@"SYSTEM\CurrentControlSet\Services\{name}", "Start", 4, owner);

    public static Task SetHibernationAsync(bool enabled, string? owner = null) =>
        SetDword(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", enabled ? 1 : 0, owner);

    public static Task DisableTelemetryTasksAsync(string? owner = null) => Task.CompletedTask;

    public static Task RevertAsync(string? owner)
    {
        for (var i = Journal.Count - 1; i >= 0; i--)
        {
            if (owner is not null && Journal[i].Owner != owner) continue;
            Journal[i].Undo();
            Journal.RemoveAt(i);
        }
        return Task.CompletedTask;
    }

    private static Task SetDword(string path, string name, int value, string? owner)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path);
        var previous = (int)key.GetValue(name)!;
        Journal.Add((owner, () =>
        {
            using var restore = Registry.LocalMachine.CreateSubKey(path);
            restore.SetValue(name, previous, RegistryValueKind.DWord);
        }));
        key.SetValue(name, value, RegistryValueKind.DWord);
        return Task.CompletedTask;
    }
}
