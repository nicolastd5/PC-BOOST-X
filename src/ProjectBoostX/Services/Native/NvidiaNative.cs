using System.Runtime.InteropServices;

namespace BoostParaPc.Services;

/// <summary>
/// Modo de gerenciamento de energia global do driver NVIDIA ("Preferir desempenho máximo" no
/// Painel de Controle), pela NVAPI que o próprio driver instala. Sem driver NVIDIA, a leitura devolve <c>null</c>.
/// </summary>
public static class NvidiaNative
{
    public const uint PreferMaxPerformance = 1;
    public const uint MaxPowerMode = 5;

    private const uint PreferredPState = 0x1057EB71;
    private const int SettingNotFound = -160;

    // NVDRS_SETTING_V1 (nvapi.h, #pragma pack 4): versão, nome[2048 UTF-16], id, tipo, origem,
    // dois sinalizadores e duas uniões de 4100 bytes (valor predefinido e valor atual).
    private const int SettingSize = 12320;
    private const uint SettingVersion = SettingSize | (1 << 16);
    private const int SettingIdOffset = 4100;
    private const int IsCurrentPredefinedOffset = 4112;
    private const int CurrentValueOffset = 8220;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSessionFn(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionFn(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBaseProfileFn(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, [In, Out] byte[] setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetSettingFn(IntPtr session, IntPtr profile, [In, Out] byte[] setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint id);

    private static readonly Lazy<bool> Initialized = new(() =>
    {
        try { return Call<InitializeFn>(0x0150E828)() == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException) { return false; }
    });

    public static bool IsAvailable => Initialized.Value;

    /// <summary>Modo atual e se foi escolhido pelo usuário. <c>null</c> quando não há driver NVIDIA.</summary>
    public static (uint Value, bool UserSet)? GetPowerMode()
    {
        if (!Initialized.Value) return null;
        return Session<(uint, bool)>((session, profile) =>
        {
            var setting = NewSetting();
            var status = Call<GetSettingFn>(0x73BF8338)(session, profile, PreferredPState, setting);
            if (status == SettingNotFound) return (MaxPowerMode, false);
            Check(status);
            return (BitConverter.ToUInt32(setting, CurrentValueOffset), BitConverter.ToUInt32(setting, IsCurrentPredefinedOffset) == 0);
        });
    }

    public static void SetPowerMode(uint value) => Change((session, profile) =>
    {
        var setting = NewSetting();
        BitConverter.TryWriteBytes(setting.AsSpan(SettingIdOffset), PreferredPState);
        BitConverter.TryWriteBytes(setting.AsSpan(CurrentValueOffset), value);
        Check(Call<SetSettingFn>(0x577DD202)(session, profile, setting));
    });

    /// <summary>Remove a escolha do usuário: volta a valer o padrão do driver.</summary>
    public static void ClearPowerMode() => Change((session, profile) =>
    {
        var status = Call<DeleteSettingFn>(0xE4A26362)(session, profile, PreferredPState);
        if (status != SettingNotFound) Check(status);
    });

    private static void Change(Action<IntPtr, IntPtr> change)
    {
        if (!Initialized.Value) throw new InvalidOperationException("Nenhum driver NVIDIA foi encontrado neste PC.");
        Session((session, profile) =>
        {
            change(session, profile);
            Check(Call<SessionFn>(0xFCBC7E14)(session)); // SaveSettings
            return true;
        });
    }

    private static T Session<T>(Func<IntPtr, IntPtr, T> body)
    {
        Check(Call<CreateSessionFn>(0x0694D52E)(out var session));
        try
        {
            Check(Call<SessionFn>(0x375DBD6B)(session)); // LoadSettings
            Check(Call<GetBaseProfileFn>(0xDA8466A0)(session, out var profile));
            return body(session, profile);
        }
        finally { Call<SessionFn>(0xDAD9CFF8)(session); } // DestroySession
    }

    private static byte[] NewSetting()
    {
        var setting = new byte[SettingSize];
        BitConverter.TryWriteBytes(setting, SettingVersion);
        return setting;
    }

    private static T Call<T>(uint id) where T : Delegate
    {
        var pointer = QueryInterface(id);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Este driver NVIDIA não oferece a função necessária.");
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"O driver NVIDIA recusou a operação (código {status}).");
    }
}
