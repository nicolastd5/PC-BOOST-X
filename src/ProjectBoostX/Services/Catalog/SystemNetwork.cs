using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    /// <summary>Chaves das interfaces de rede que têm endereço IP.</summary>
    private static IEnumerable<string> ActiveInterfaces()
    {
        using var interfaces = HKLM.OpenSubKey(Interfaces, writable: false);
        if (interfaces is null) yield break;
        foreach (var name in interfaces.GetSubKeyNames())
        {
            using var key = interfaces.OpenSubKey(name, writable: false);
            if (key?.GetValue("DhcpIPAddress") is not null || key?.GetValue("IPAddress") is not null)
                yield return $@"{Interfaces}\{name}";
        }
    }

    private static IEnumerable<OptimizationItem> SystemNetworkItems() =>
    [
        new()
        {
            Id = "net.nagle", Name = "Desativar algoritmo de Nagle",
            Description = "Envia pacotes TCP pequenos sem esperar, nas interfaces de rede ativas. Só afeta jogos que usam TCP; a maioria usa UDP e não muda.",
            Category = OptimizationCategory.Network, Risk = RiskLevel.Moderate, Impact = "Latência menor em jogos TCP",
            ApplyExtra = owner => Task.Run(() =>
            {
                var paths = ActiveInterfaces().ToList();
                if (paths.Count == 0)
                    throw new InvalidOperationException("Nenhuma interface de rede ativa foi encontrada.");
                foreach (var path in paths)
                {
                    RegistryBackupService.SetDword(HKLM, path, "TcpAckFrequency", 1, owner);
                    RegistryBackupService.SetDword(HKLM, path, "TcpNoDelay", 1, owner);
                }
            }),
            IsAppliedExtra = () => ActiveInterfaces().ToList() is { Count: > 0 } paths && paths.All(p =>
                RegistryBackupService.GetDword(HKLM, p, "TcpAckFrequency") == 1 &&
                RegistryBackupService.GetDword(HKLM, p, "TcpNoDelay") == 1)
        },
        new()
        {
            Id = "tools.dnsflush", Name = "Limpar cache de DNS",
            Description = "Esvazia o cache de nomes. Útil depois de trocar de rede, VPN ou servidor DNS.",
            Category = OptimizationCategory.Network, Risk = RiskLevel.Safe, Impact = "Resolução de nomes limpa",
            IsAction = true,
            ApplyExtra = _ => ProcessRunner.RunCheckedAsync("ipconfig", "/flushdns")
        },
        new()
        {
            Id = "memory.ntfs", Name = "NTFS sem registro de último acesso",
            Description = "O sistema de arquivos deixa de gravar a data de último acesso a cada leitura.",
            Category = OptimizationCategory.Memory, Risk = RiskLevel.Safe, Impact = "Menos escrita em disco",
            RequiresRestart = true,
            // 0x80000001 = desativado, gerenciado pelo usuário.
            Registry = [Dw(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001))]
        },
        new()
        {
            Id = "sys.startupdelay", Name = "Remover atraso dos programas de inicialização",
            Description = "Os programas que abrem com o Windows deixam de esperar cerca de 10 segundos após o login.",
            Category = OptimizationCategory.System, Risk = RiskLevel.Safe, Impact = "Área de trabalho pronta mais cedo",
            Registry = [Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)]
        },
        new()
        {
            Id = "sys.hibernation", Name = "Desativar hibernação",
            Description = "Desliga a hibernação e apaga o arquivo hiberfil.sys.",
            Category = OptimizationCategory.System, Risk = RiskLevel.Moderate, Impact = "Libera vários GB no disco do sistema",
            Caution = "A Inicialização Rápida do Windows também é desligada; o boot a frio pode ficar um pouco mais lento.",
            NotRecommended = pc => pc.IsLaptop ? "Notebook perde a hibernação ao fechar a tampa ou com bateria fraca." : null,
            ApplyExtra = owner => SystemSettingsBackupService.SetHibernationAsync(false, owner),
            IsAppliedExtra = () => RegistryBackupService.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0
        },
    ];
}
