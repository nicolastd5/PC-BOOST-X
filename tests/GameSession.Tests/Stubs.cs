// Tabela de processos em memória no lugar do Windows real.
namespace BoostParaPc.Services;

public static class AppPaths
{
    public static string DataDir { get; set; } = "";
}

public static class MemoryNative
{
    public static int Purges { get; set; }
    public static void PurgeStandbyList() => Purges++;
}

public static class ProcessNative
{
    public const int Idle = 0x40, BelowNormal = 0x4000, Normal = 0x20, High = 0x80;

    public sealed class Proc(string name, DateTime start, int priority)
    {
        public string Name { get; } = name;
        public DateTime Start { get; set; } = start;
        public int Priority { get; set; } = priority;
        public bool Efficiency { get; set; }
        public bool FailSet { get; set; }
    }

    public static Dictionary<int, Proc> Table { get; } = [];
    public static List<int> Touched { get; } = [];

    public static void Reset() { Table.Clear(); Touched.Clear(); }

    public static DateTime? StartTime(int pid) => Table.TryGetValue(pid, out var p) ? p.Start : null;
    public static int[] FindByName(string exeName) => Table.Where(p => p.Value.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray();
    public static int? GetPriority(int pid) => Table.TryGetValue(pid, out var p) ? p.Priority : null;

    public static bool SetPriority(int pid, int priorityClass)
    {
        if (!Table.TryGetValue(pid, out var p) || p.FailSet) return false;
        Touched.Add(pid);
        p.Priority = priorityClass;
        return true;
    }

    public static bool SetEfficiency(int pid, bool on)
    {
        if (!Table.TryGetValue(pid, out var p) || p.FailSet) return false;
        p.Efficiency = on;
        return true;
    }
}
