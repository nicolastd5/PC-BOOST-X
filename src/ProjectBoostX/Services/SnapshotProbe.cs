using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace BoostParaPc.Services;

/// <summary>Coleta o retrato atual. Cada medida que falhar fica nula/zero, sem derrubar as outras.</summary>
public static class SnapshotProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

    public static Task<SystemSnapshot> TakeAsync() => Task.Run(async () =>
    {
        var startup = 0;
        try { startup = (await StartupService.GetItemsAsync()).Count(i => i.IsEnabled); } catch { }
        return new SystemSnapshot(DateTime.UtcNow, BootMs(), RamUsedMb(), Processes(), RunningServices(), startup);
    });

    private static int RamUsedMb()
    {
        var status = new MemoryStatus();
        return GlobalMemoryStatusEx(status) ? (int)((status.TotalPhys - status.AvailPhys) / (1024 * 1024)) : 0;
    }

    private static int Processes()
    {
        try { return Process.GetProcesses().Length; } catch { return 0; }
    }

    private static int RunningServices()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Service WHERE State = 'Running'");
            return searcher.Get().Count;
        }
        catch { return 0; }
    }

    /// <summary>Último evento 100 do log Diagnostics-Performance; campo BootTime em ms. Só muda depois de reiniciar.</summary>
    private static int? BootMs()
    {
        try
        {
            var query = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record is null) return null;
            var data = XDocument.Parse(record.ToXml()).Descendants().FirstOrDefault(e => e.Name.LocalName == "Data" && (string?)e.Attribute("Name") == "BootTime");
            return int.TryParse(data?.Value, out var ms) ? ms : null;
        }
        catch { return null; }
    }
}
