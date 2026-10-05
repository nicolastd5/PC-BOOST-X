namespace BoostParaPc.Models;

public sealed record SystemInfo(
    string OsName,
    string OsVersion,
    string CpuName,
    int CpuCores,
    int CpuLogical,
    double TotalRamGb,
    string GpuName,
    string DiskType,
    double DiskTotalGb,
    double DiskFreeGb,
    bool IsLaptop,
    bool IsWindows11,
    string ComputerName)
{
    public bool IsX3D => CpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase);

    // ponytail: heurística por nome e contagem de núcleos; trocar por GetLogicalProcessorInformationEx
    // (EfficiencyClass) se aparecer falso positivo que importe. Um falso positivo só rotula core parking
    // como "não recomendado"; o usuário ainda pode aplicar.
    public bool IsHybridCpu =>
        CpuName.Contains("Core(TM) Ultra", StringComparison.OrdinalIgnoreCase)
        || CpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase)
        || (CpuCores > 0 && CpuLogical != CpuCores && CpuLogical != CpuCores * 2);
}
