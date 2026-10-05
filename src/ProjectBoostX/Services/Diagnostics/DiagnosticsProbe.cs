using System.Management;
using System.Runtime.InteropServices;
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>Coleta os dados do diagnóstico. Fala com o Windows (WMI, Registro, API de vídeo); a avaliação é pura, em <see cref="Diagnostics"/>.</summary>
public static class DiagnosticsProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    public static async Task<DiagnosticInputs> CollectAsync(IEnumerable<GameProfile>? games = null, SystemInfo? info = null)
    {
        var tcp = await Task.Run(TcpAutoTuningDisabled).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var (cur, max) = Display();
            var (speed, type, count) = Memory();
            var (driverAge, gpu) = Gpu();
            return new DiagnosticInputs
            {
                DisplayCurrentHz = cur,
                DisplayMaxHz = max,
                RamSpeedMhz = speed,
                RamType = type,
                RamModuleCount = count,
                GpuDriverAgeDays = driverAge,
                GpuName = gpu,
                TrimDisabled = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotification") is { } t ? t != 0 : null,
                SystemFreePercent = info is { DiskTotalGb: > 0 } ? info.DiskFreeGb / info.DiskTotalGb * 100 : null,
                PagingDisabled = PagingDisabled(),
                RebootPending = RebootPending(),
                OnBattery = OnBattery(),
                GamesOnHdd = games is null ? null : GamesOnHdd(games),
                TcpAutoTuningDisabled = tcp,
                MemoryIntegrityOn = Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled") is { } m ? m == 1 : null,
            };
        }).ConfigureAwait(false);
    }

    private static int? Dword(RegistryKey root, string path, string name)
    {
        try { using var key = root.OpenSubKey(path); return key?.GetValue(name) as int?; }
        catch { return null; }
    }

    private static (int? Current, int? Max) Display()
    {
        try
        {
            var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(null, -1, ref mode)) return (null, null);
            int cur = mode.dmDisplayFrequency, max = cur;
            for (var i = 0; ; i++)
            {
                var m = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                if (!EnumDisplaySettings(null, i, ref m)) break;
                if (m.dmPelsWidth == mode.dmPelsWidth && m.dmPelsHeight == mode.dmPelsHeight && m.dmDisplayFrequency > max)
                    max = m.dmDisplayFrequency;
            }
            return (cur, max);
        }
        catch { return (null, null); }
    }

    private static (int? Speed, string? Type, int? Count) Memory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            var modules = searcher.Get().Cast<ManagementObject>().ToList();
            if (modules.Count == 0) return (null, null, null);
            var speed = modules.Select(m => m["ConfiguredClockSpeed"] is null ? 0 : Convert.ToInt32(m["ConfiguredClockSpeed"])).Where(s => s > 0).DefaultIfEmpty(0).Min();
            var type = modules[0]["SMBIOSMemoryType"] is { } t ? Convert.ToInt32(t) switch { 26 => "DDR4", 34 => "DDR5", _ => "Outro" } : null;
            return (speed > 0 ? speed : null, type, modules.Count);
        }
        catch { return (null, null, null); }
    }

    private static (int? AgeDays, string? Name) Gpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverDate FROM Win32_VideoController");
            foreach (ManagementObject o in searcher.Get())
            {
                var name = o["Name"]?.ToString();
                if (name is null || name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)) continue;
                if (o["DriverDate"] is string raw)
                    return ((int)(DateTime.Now - ManagementDateTimeConverter.ToDateTime(raw)).TotalDays, name);
                return (null, name);
            }
        }
        catch { /* sem dado */ }
        return (null, null);
    }

    private static bool? PagingDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            if (key?.GetValue("PagingFiles") is not string[] files) return null;
            return files.All(string.IsNullOrWhiteSpace);
        }
        catch { return null; }
    }

    private static bool? RebootPending()
    {
        try
        {
            using var a = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var b = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var c = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return a is not null || b is not null || c?.GetValue("PendingFileRenameOperations") is not null;
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AC; public byte Flag; public byte Percent; public byte Saver; public int Life; public int Full; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private static bool? OnBattery() => GetSystemPowerStatus(out var s) ? s.AC == 0 : null;

    private static List<string> GamesOnHdd(IEnumerable<GameProfile> games) =>
        games.Where(g => g.ExecutablePath.Length > 1 && g.ExecutablePath[1] == ':')
             .GroupBy(g => char.ToUpperInvariant(g.ExecutablePath[0]))
             .Where(grp => SystemInfoService.GetDiskType(grp.Key) == "HD")
             .SelectMany(grp => grp.Select(g => g.Name)).ToList();

    private static async Task<bool?> TcpAutoTuningDisabled()
    {
        try
        {
            var text = await ProcessRunner.RunPowerShellAsync("(Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal", 10_000).ConfigureAwait(false);
            var level = text.Trim();
            return level.Length == 0 ? null : level.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        }
        catch { return null; }
    }
}
