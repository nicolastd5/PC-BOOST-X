using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoostParaPc.Services;

/// <summary>Esvazia a lista de espera (standby) da memória, como faz o RAMMap. Exige administrador.</summary>
public static class MemoryNative
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8, SePrivilegeEnabled = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returned);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static void PurgeStandbyList()
    {
        EnablePrivilege("SeProfileSingleProcessPrivilege");
        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
            throw new InvalidOperationException($"O Windows recusou limpar a memória em espera (código {status:x}). Execute como administrador.");
    }

    /// <summary>RAM em espera (cache) em MB, somando as três prioridades.</summary>
    public static long StandbyMb()
    {
        string[] names = ["Standby Cache Normal Priority Bytes", "Standby Cache Reserve Bytes", "Standby Cache Core Bytes"];
        long bytes = 0;
        foreach (var name in names)
        {
            using var counter = new PerformanceCounter("Memory", name);
            bytes += (long)counter.NextValue();
        }
        return bytes / (1024 * 1024);
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            throw new InvalidOperationException("Não foi possível abrir o token do processo.");
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                throw new InvalidOperationException("Privilégio desconhecido: " + name);
            var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                throw new InvalidOperationException("Sem permissão para limpar a memória em espera. Execute como administrador.");
        }
        finally { CloseHandle(token); }
    }
}
