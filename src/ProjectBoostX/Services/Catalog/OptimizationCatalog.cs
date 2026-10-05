using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Catálogo de ajustes. Cada item declara uma vez o que grava; aplicar, detectar e reverter
/// saem dessa declaração (ver <see cref="OptimizationEngine"/>).
/// </summary>
public static partial class OptimizationCatalog
{
    /// <summary>Instâncias únicas: todas as telas observam os mesmos itens.</summary>
    public static IReadOnlyList<OptimizationItem> All { get; } =
    [
        .. PowerItems(), .. GamingItems(), .. InputVisualItems(),
        .. PrivacyItems(), .. ServiceItems(), .. SystemNetworkItems()
    ];

    /// <summary>Itens que um clique pode aplicar. Avançados e contraindicados para o PC ficam sempre de fora.</summary>
    public static IEnumerable<OptimizationItem> Preset(RiskLevel maxRisk) => All.Where(i =>
        !i.IsAction && i.IsRecommended && i.Risk != RiskLevel.Advanced && i.Risk <= maxRisk);

    private static RegistryKey HKCU => Registry.CurrentUser;
    private static RegistryKey HKLM => Registry.LocalMachine;

    private static RegValue Dw(RegistryKey root, string key, string name, int value) =>
        new(root, key, name, value, RegistryValueKind.DWord);

    private static RegValue Sz(RegistryKey root, string key, string name, string value) =>
        new(root, key, name, value, RegistryValueKind.String);

    private static string? Laptop(SystemInfo pc) =>
        pc.IsLaptop ? "Em notebook aumenta consumo, temperatura e ruído." : null;
}
