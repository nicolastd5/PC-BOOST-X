using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoostParaPc.Services;

/// <summary>Prioridade e modo de eficiência de processos de outros programas. Nenhum método lança: devolve null/false se não for possível.</summary>
public static class ProcessNative
{
    public const int Idle = 0x40, BelowNormal = 0x4000, Normal = 0x20, High = 0x80;

    private const uint QueryLimited = 0x1000, SetInformation = 0x200;
    private const int ProcessPowerThrottling = 4;
    private const uint ThrottlingVersion = 1, ExecutionSpeed = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetPriorityClass(IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    public static DateTime? StartTime(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.StartTime.ToUniversalTime(); }
        catch { return null; }
    }

    public static int[] FindByName(string exeName)
    {
        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(exeName);
            return Process.GetProcessesByName(name).Select(p => { using (p) return p.Id; }).ToArray();
        }
        catch { return []; }
    }

    public static int? GetPriority(int pid) => With(pid, QueryLimited, h => GetPriorityClass(h) is var c && c != 0 ? (int?)c : null);

    public static bool SetPriority(int pid, int priorityClass) =>
        With(pid, SetInformation, h => SetPriorityClass(h, (uint)priorityClass) ? (bool?)true : null) == true;

    public static bool SetEfficiency(int pid, bool on) => With(pid, SetInformation, h =>
    {
        var state = new PowerThrottlingState { Version = ThrottlingVersion, ControlMask = ExecutionSpeed, StateMask = on ? ExecutionSpeed : 0 };
        return SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>()) ? (bool?)true : null;
    }) == true;

    private static T? With<T>(int pid, uint access, Func<IntPtr, T?> action) where T : struct
    {
        var handle = OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero) return null;
        try { return action(handle); }
        finally { CloseHandle(handle); }
    }
}
