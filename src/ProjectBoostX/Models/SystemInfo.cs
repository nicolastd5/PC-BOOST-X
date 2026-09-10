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
    string ComputerName);
