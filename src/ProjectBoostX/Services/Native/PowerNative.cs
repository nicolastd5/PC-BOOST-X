using System.Runtime.InteropServices;
using System.Text;

namespace BoostParaPc.Services;

/// <summary>
/// Leitura de planos de energia pela API do Windows: não inicia processos,
/// não depende do idioma do sistema e enxerga ajustes ocultos.
/// </summary>
public static class PowerNative
{
    private static readonly Dictionary<string, Guid> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sub_sleep"] = new("238c9fa8-0aad-41ed-83f4-97be242c8f20"),
        ["standbyidle"] = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"),
        ["hibernateidle"] = new("9d7815a6-7ee4-497e-8888-515a05f02364"),
        ["sub_video"] = new("7516b95f-f776-4464-8c53-06167f40cc99"),
        ["videoidle"] = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"),
        ["sub_processor"] = new("54533251-82be-4824-96c1-47b60b740d00"),
        ["PROCTHROTTLEMAX"] = new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        ["PROCTHROTTLEMIN"] = new("893dee8e-2bef-41e0-89c6-b55d0929964c"),
    };

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootKey, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootKey, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootKey, in Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static string ActiveSchemeName()
    {
        if (ActiveScheme() is not { } scheme) return "Desconhecido";
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return "Desconhecido";
        var buffer = new byte[size];
        return PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0')
            : "Desconhecido";
    }

    public static uint? ReadAc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting, plan, ac: true);

    public static uint? ReadDc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting, plan, ac: false);

    private static uint? Read(string subgroup, string setting, string? plan, bool ac)
    {
        Guid? scheme = plan is null ? ActiveScheme() : Guid.TryParse(plan, out var parsed) ? parsed : null;
        if (scheme is not { } s || !TryGuid(subgroup, out var sub) || !TryGuid(setting, out var set)) return null;
        uint value;
        var status = ac
            ? PowerReadACValueIndex(IntPtr.Zero, s, sub, set, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, s, sub, set, out value);
        return status == 0 ? value : null;
    }

    private static bool TryGuid(string token, out Guid guid) =>
        Aliases.TryGetValue(token, out guid) || Guid.TryParse(token, out guid);
}
