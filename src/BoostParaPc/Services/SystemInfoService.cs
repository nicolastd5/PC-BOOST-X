using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static class SystemInfoService
{
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static SystemInfo GetSystemInfo()
    {
        var os = Environment.OSVersion.Version;
        var isWin11 = os.Build >= 22000;

        var osName = isWin11 ? "Windows 11" : "Windows 10";
        var cpuName = GetCpuName();
        var (cores, logical) = GetCpuCounts();
        var totalRam = GetTotalRamGb();
        var gpu = GetGpuName();
        var (diskType, diskTotal, diskFree) = GetDiskInfo();
        var isLaptop = DetectLaptop();

        return new SystemInfo(
            OsName: osName,
            OsVersion: $"{os.Major}.{os.Minor}.{os.Build}",
            CpuName: cpuName,
            CpuCores: cores,
            CpuLogical: logical,
            TotalRamGb: totalRam,
            GpuName: gpu,
            DiskType: diskType,
            DiskTotalGb: diskTotal,
            DiskFreeGb: diskFree,
            IsLaptop: isLaptop,
            IsWindows11: isWin11,
            ComputerName: Environment.MachineName);
    }

    public static string GetCurrentPowerPlan()
    {
        try
        {
            var result = ProcessRunner.RunAsync("powercfg", "/getactivescheme", timeoutMs: 5_000)
                .GetAwaiter().GetResult();
            // Example: "Plano de energia ativo GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)"
            var line = result.StdOut.Trim();
            var start = line.IndexOf('(');
            var end = line.IndexOf(')');
            if (start >= 0 && end > start)
                return line[(start + 1)..end];
            return line.Length > 0 ? line : "Desconhecido";
        }
        catch
        {
            return "Desconhecido";
        }
    }

    public static async Task<string> GetCurrentPowerPlanAsync()
    {
        try
        {
            var result = await ProcessRunner.RunAsync("powercfg", "/getactivescheme", timeoutMs: 5_000).ConfigureAwait(false);
            var line = result.StdOut.Trim();
            var start = line.IndexOf('(');
            var end = line.IndexOf(')');
            if (start >= 0 && end > start)
                return line[(start + 1)..end];
            return line.Length > 0 ? line : "Desconhecido";
        }
        catch
        {
            return "Desconhecido";
        }
    }

    private static string GetCpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in searcher.Get())
                return obj["Name"]?.ToString()?.Trim() ?? Environment.ProcessorCount + " núcleos";
        }
        catch { /* fall through */ }
        return $"{Environment.ProcessorCount} núcleos lógicos";
    }

    private static (int cores, int logical) GetCpuCounts()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                var logical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                return (cores, logical);
            }
        }
        catch { /* fall through */ }
        return (Environment.ProcessorCount, Environment.ProcessorCount);
    }

    private static double GetTotalRamGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var bytes = Convert.ToDouble(obj["TotalPhysicalMemory"]);
                return Math.Round(bytes / (1024.0 * 1024.0 * 1024.0), 1);
            }
        }
        catch { /* fall through */ }
        return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0), 1);
    }

    private static string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { /* fall through */ }
        return "Desconhecido";
    }

    private static (string type, double totalGb, double freeGb) GetDiskInfo()
    {
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
            if (drive is null) return ("Desconhecido", 0, 0);

            var total = Math.Round(drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
            var free = Math.Round(drive.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);

            var type = "Desconhecido";
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Model, MediaType FROM Win32_DiskDrive WHERE InterfaceType != 'USB'");
                foreach (var obj in searcher.Get())
                {
                    var media = obj["MediaType"]?.ToString() ?? "";
                    type = media.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD" : "HD";
                    break;
                }
            }
            catch { type = "Desconhecido"; }

            return (type, total, free);
        }
        catch
        {
            return ("Desconhecido", 0, 0);
        }
    }

    private static bool DetectLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (var obj in searcher.Get())
            {
                if (obj["ChassisTypes"] is ushort[] types)
                {
                    // 8=Portable, 9=Laptop, 10=Notebook, 11=Hand Held, 12=Docking Station, 14=Sub Notebook
                    return types.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14);
                }
            }
        }
        catch { /* ignore */ }

        try
        {
            return GetSystemPowerStatus(out var sps) && sps.BatteryFlag != 128 && sps.BatteryFlag != 255;
        }
        catch
        {
            return false;
        }
    }
}
